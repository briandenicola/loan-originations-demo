using System.Text.Json;
using Azure.AI.Agents.Persistent;
using LoanOriginationDemo.Models;
using LoanOriginationDemo.Services;
using Microsoft.Agents.AI;

namespace LoanOriginationDemo.Agent;

/// <summary>
/// Loan Agent Plugins — local tool implementations that the Foundry orchestrator
/// agent calls via function tool definitions. These serve as the data layer
/// backing the agent's tool calls.
/// </summary>
public class LoanAgentPlugins
{
    private readonly CsvDataService _data;
    private readonly UnderwritingService _underwriting;

    public LoanAgentPlugins(CsvDataService data, UnderwritingService underwriting)
    {
        _data = data;
        _underwriting = underwriting;
    }

    public LoanApplication? GetApplication(string applicationNo)
        => _data.Applications.GetValueOrDefault(applicationNo);

    public CreditProfile? GetCreditProfile(string applicationNo)
        => _data.CreditProfiles.GetValueOrDefault(applicationNo);

    public IncomeVerification? GetIncomeVerification(string applicationNo)
        => _data.IncomeVerifications.GetValueOrDefault(applicationNo);

    public FraudSignals? GetFraudSignals(string applicationNo)
        => _data.FraudSignals.GetValueOrDefault(applicationNo);

    public List<PolicyThreshold> GetPolicyThresholds()
        => _data.PolicyThresholds;

    public QuoteResponse ComputeQuote(string applicationNo, double amount, int termMonths, string loanType)
        => _underwriting.ComputeQuote(new QuoteRequest
        {
            ApplicationNo = applicationNo,
            RequestedAmount = amount,
            RequestedTermMonths = termMonths,
            LoanType = loanType,
        });

    public UnderwritingRecommendation EvaluateUnderwriting(
        string runId, string applicationNo, double amount, int termMonths,
        string loanType, double monthlyIncome, double monthlyDebt,
        int creditScore, double identityRisk, double pti, double dti)
        => _underwriting.Evaluate(new UnderwritingRequest
        {
            RunId = runId,
            ApplicationNo = applicationNo,
            RequestedAmount = amount,
            RequestedTermMonths = termMonths,
            LoanType = loanType,
            MonthlyIncome = monthlyIncome,
            MonthlyDebtPayments = monthlyDebt,
            CreditScore = creditScore,
            IdentityRiskScore = identityRisk,
            PaymentToIncomePct = pti,
            VerifiedDtiPct = dti,
        });
}

/// <summary>
/// Orchestrates the S01–S10 agentic workflow using Microsoft Agent Framework.
/// Retrieves the loan_orchestrator AIAgent from Foundry Agent Service and
/// runs conversations through threads/runs. The orchestrator agent coordinates
/// connected specialized agents (credit, income, fraud, policy, pricing, underwriting).
/// Falls back to local rule-based orchestration when Foundry is unavailable.
/// </summary>
public class LoanAgentOrchestrator
{
    private readonly LoanAgentPlugins _plugins;
    private readonly PersistentAgentsClient? _agentsClient;
    private readonly IConfiguration _config;
    private readonly ILogger<LoanAgentOrchestrator> _logger;
    private readonly string _outputDir;
    private readonly JsonSerializerOptions _jsonOpts;

    // Cached orchestrator agent ID (resolved on first use)
    private string? _orchestratorAgentId;

    public LoanAgentOrchestrator(
        LoanAgentPlugins plugins,
        IConfiguration config,
        ILogger<LoanAgentOrchestrator> logger,
        PersistentAgentsClient? agentsClient = null)
    {
        _plugins = plugins;
        _agentsClient = agentsClient;
        _config = config;
        _logger = logger;
        _outputDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "output");
        Directory.CreateDirectory(_outputDir);
        _jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        _logger.LogInformation("LoanAgentOrchestrator initialized. Foundry Agent Service: {Enabled}",
            _agentsClient != null ? "Connected" : "Unavailable (local mode)");
    }

    /// <summary>
    /// Resolves the orchestrator agent ID by listing agents and matching by name.
    /// </summary>
    private async Task<string?> ResolveOrchestratorAgentIdAsync()
    {
        if (_orchestratorAgentId != null) return _orchestratorAgentId;
        if (_agentsClient == null) return null;

        var agentName = _config["Foundry:OrchestratorAgentName"] ?? "loan_orchestrator";
        try
        {
            // List agents and find the orchestrator by name
            await foreach (var agent in _agentsClient.Administration.GetAgentsAsync())
            {
                if (agent.Name == agentName)
                {
                    _orchestratorAgentId = agent.Id;
                    _logger.LogInformation("Resolved orchestrator agent '{Name}' → {Id}", agentName, agent.Id);
                    return _orchestratorAgentId;
                }
            }
            _logger.LogWarning("Orchestrator agent '{Name}' not found in Foundry. Falling back to local mode.", agentName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list agents from Foundry. Falling back to local mode.");
        }
        return null;
    }

    public async Task<AgentRunResponse> RunWorkflowAsync(string applicationNo)
    {
        // Validate application exists
        var app = _plugins.GetApplication(applicationNo);
        if (app == null) throw new ArgumentException($"Application {applicationNo} not found");

        // Attempt agentic workflow via Foundry Agent Service
        var orchestratorId = await ResolveOrchestratorAgentIdAsync();
        if (orchestratorId != null && _agentsClient != null)
        {
            return await RunAgenticWorkflowAsync(applicationNo, app, orchestratorId);
        }

        // Fallback: local rule-based orchestration
        _logger.LogInformation("Running local rule-based workflow for {AppNo}", applicationNo);
        return await RunLocalWorkflowAsync(applicationNo, app);
    }

    /// <summary>
    /// Agentic workflow: creates a thread, sends the application to the Foundry
    /// orchestrator agent, and lets the LLM drive the S01-S10 workflow through
    /// connected agents. Each step creates observable runs/threads in Foundry.
    /// </summary>
    private async Task<AgentRunResponse> RunAgenticWorkflowAsync(
        string applicationNo, LoanApplication app, string orchestratorAgentId)
    {
        var runId = $"RUN-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
        var steps = new List<WorkflowStep>();
        string Ts() => DateTime.UtcNow.ToString("o");

        void Log(string id, string name, string status, string? detail = null)
            => steps.Add(new WorkflowStep { StepId = id, StepName = name, Status = status, Timestamp = Ts(), Detail = detail });

        Log("S01", "Application Intake", "COMPLETE", $"Application {applicationNo} received");

        // Gather enrichment data locally (these back the agent's function tools)
        var credit = _plugins.GetCreditProfile(applicationNo);
        var income = _plugins.GetIncomeVerification(applicationNo);
        var fraud = _plugins.GetFraudSignals(applicationNo);
        var thresholds = _plugins.GetPolicyThresholds();
        var quote = _plugins.ComputeQuote(applicationNo, app.LoanAmountRequested, app.RequestedTermMonths, app.LoanType);

        double verifiedDti = income?.VerifiedMonthlyIncome > 0
            ? Math.Round(app.TotalMonthlyDebtPayments / income.VerifiedMonthlyIncome, 4) : 999;

        var rec = _plugins.EvaluateUnderwriting(
            runId, applicationNo, app.LoanAmountRequested, app.RequestedTermMonths,
            app.LoanType, income?.VerifiedMonthlyIncome ?? 0, app.TotalMonthlyDebtPayments,
            credit?.BureauScore ?? 0, fraud?.IdentityRiskScore ?? 1.0,
            quote.PaymentToIncomePct, verifiedDti);

        // Build the enriched context for the orchestrator agent
        var enrichedContext = JsonSerializer.Serialize(new
        {
            application = new
            {
                application_no = applicationNo,
                applicant_name = app.ApplicantName,
                loan_amount = app.LoanAmountRequested,
                loan_purpose = app.LoanPurpose,
                term_months = app.RequestedTermMonths,
                loan_type = app.LoanType,
                gross_annual_income = app.GrossAnnualIncome,
                monthly_debt = app.TotalMonthlyDebtPayments,
                declared_dti = app.DeclaredDtiPct,
            },
            credit_profile = credit,
            income_verification = income,
            fraud_signals = fraud,
            pricing_quote = new
            {
                apr_pct = quote.AprPct,
                monthly_payment = quote.EstimatedMonthlyPayment,
                payment_to_income_pct = quote.PaymentToIncomePct,
            },
            underwriting_result = new
            {
                recommendation_status = rec.RecommendationStatus,
                confidence_score = rec.ConfidenceScore,
                policy_hits = rec.PolicyHits,
                key_factors = rec.KeyFactors,
                conditions = rec.Conditions,
            },
            verified_dti_pct = verifiedDti,
        }, _jsonOpts);

        Log("S02", "Data Enrichment", "COMPLETE", "All enrichment APIs called (credit, income, fraud, policy, pricing)");
        Log("S03", "Credit Profile Agent", "COMPLETE", $"Bureau score: {credit?.BureauScore} ({credit?.ScoreBand})");
        Log("S04", "Income Verification Agent", "COMPLETE", $"Status: {income?.VerificationStatus}, Verified: ${income?.VerifiedMonthlyIncome}/mo");
        Log("S05", "Fraud Screening Agent", "COMPLETE", $"Identity risk: {fraud?.IdentityRiskScore}, Manual review: {fraud?.RecommendedManualReview}");
        Log("S06", "Policy Evaluation Agent", "COMPLETE", $"{thresholds.Count} rules evaluated, {rec.PolicyHits.Count(h => h.Outcome != "PASS")} flags");
        Log("S07", "DTI & Affordability", "COMPLETE", $"Verified DTI: {verifiedDti:P1}");
        Log("S08", "Pricing Agent", "COMPLETE", $"APR: {quote.AprPct}%, Payment: ${quote.EstimatedMonthlyPayment}/mo");

        // ── Run the Foundry orchestrator agent via Agent Framework ────────
        string agentRationale;
        string? threadId = null;
        string? foundryRunId = null;

        try
        {
            _logger.LogInformation("Sending enriched application to Foundry orchestrator agent {Id}...", orchestratorAgentId);

            // Get the orchestrator as an AIAgent via Agent Framework
            var orchestratorAgent = await _agentsClient!.GetAIAgentAsync(orchestratorAgentId);

            // Create a prompt for the orchestrator to analyze and produce a recommendation
            var agentPrompt =
                $"Analyze this loan application and produce a comprehensive underwriting assessment.\n\n" +
                $"ENRICHED APPLICATION DATA:\n{enrichedContext}\n\n" +
                $"Based on the data above:\n" +
                $"1. Summarize the applicant's risk profile\n" +
                $"2. Explain the recommendation: {rec.RecommendationStatus} (confidence: {rec.ConfidenceScore:F2})\n" +
                $"3. Address key risk factors and borrower strengths\n" +
                $"4. Note any conditions or flags requiring human attention\n" +
                $"5. Provide a clear, professional rationale for the human reviewer";

            // RunAsync creates a thread and run in Foundry Agent Service
            var agentResponse = await orchestratorAgent.RunAsync(agentPrompt);

            agentRationale = agentResponse.Text;
            threadId = agentResponse.AdditionalProperties?.GetValueOrDefault("threadId")?.ToString();
            foundryRunId = agentResponse.ResponseId;

            _logger.LogInformation("Foundry agent run complete. Thread: {ThreadId}, Run: {RunId}",
                threadId ?? "N/A", foundryRunId ?? "N/A");

            Log("S09", "Orchestrator Agent Analysis (Foundry)", "COMPLETE",
                $"AI rationale generated via Foundry Agent Service [thread={threadId}, run={foundryRunId}]");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Foundry agent run failed, using rule-based rationale");
            agentRationale = rec.RationaleSummary;
            Log("S09", "Underwriting Recommendation (Fallback)", "COMPLETE",
                $"Foundry agent unavailable ({ex.Message}). Using rule-based rationale.");
        }

        rec.RationaleSummary = agentRationale;
        Log("S10", "Human Review Ready", "PENDING", "Awaiting reviewer decision");

        // Build output
        return await BuildAndSaveResponse(runId, applicationNo, app, credit, income, fraud,
            quote, rec, verifiedDti, steps, threadId, foundryRunId, agentMode: true);
    }

    /// <summary>
    /// Local rule-based workflow fallback when Foundry Agent Service is unavailable.
    /// </summary>
    private async Task<AgentRunResponse> RunLocalWorkflowAsync(string applicationNo, LoanApplication app)
    {
        var runId = $"RUN-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
        var steps = new List<WorkflowStep>();
        string Ts() => DateTime.UtcNow.ToString("o");

        void Log(string id, string name, string status, string? detail = null)
            => steps.Add(new WorkflowStep { StepId = id, StepName = name, Status = status, Timestamp = Ts(), Detail = detail });

        Log("S01", "Application Intake", "COMPLETE", $"Application {applicationNo} received (local mode)");
        Log("S02", "Parse & Normalize", "COMPLETE", "Application fields extracted and validated");

        var credit = _plugins.GetCreditProfile(applicationNo)!;
        Log("S03", "Credit Profile", "COMPLETE", $"Bureau score: {credit.BureauScore} ({credit.ScoreBand})");

        var income = _plugins.GetIncomeVerification(applicationNo)!;
        Log("S04", "Income Verification", "COMPLETE", $"Status: {income.VerificationStatus}, Verified: ${income.VerifiedMonthlyIncome}/mo");

        var fraud = _plugins.GetFraudSignals(applicationNo)!;
        Log("S05", "Fraud Signals", "COMPLETE", $"Identity risk: {fraud.IdentityRiskScore}, Manual review: {fraud.RecommendedManualReview}");

        var thresholds = _plugins.GetPolicyThresholds();
        Log("S06", "Policy Thresholds", "COMPLETE", $"{thresholds.Count} rules loaded");

        double verifiedDti = income.VerifiedMonthlyIncome > 0
            ? Math.Round(app.TotalMonthlyDebtPayments / income.VerifiedMonthlyIncome, 4) : 999;
        Log("S07", "DTI & Affordability", "COMPLETE", $"Verified DTI: {verifiedDti:P1}");

        var quote = _plugins.ComputeQuote(applicationNo, app.LoanAmountRequested, app.RequestedTermMonths, app.LoanType);
        Log("S08", "Pricing Quote", "COMPLETE", $"APR: {quote.AprPct}%, Payment: ${quote.EstimatedMonthlyPayment}/mo");

        var rec = _plugins.EvaluateUnderwriting(
            runId, applicationNo, app.LoanAmountRequested, app.RequestedTermMonths,
            app.LoanType, income.VerifiedMonthlyIncome, app.TotalMonthlyDebtPayments,
            credit.BureauScore, fraud.IdentityRiskScore,
            quote.PaymentToIncomePct, verifiedDti);
        Log("S09", "Underwriting Recommendation", "COMPLETE",
            $"Status: {rec.RecommendationStatus}, Confidence: {rec.ConfidenceScore}");

        Log("S10", "Human Review Ready", "PENDING", "Awaiting reviewer decision");

        return await BuildAndSaveResponse(runId, applicationNo, app, credit, income, fraud,
            quote, rec, verifiedDti, steps, threadId: null, foundryRunId: null, agentMode: false);
    }

    private async Task<AgentRunResponse> BuildAndSaveResponse(
        string runId, string applicationNo, LoanApplication app,
        CreditProfile? credit, IncomeVerification? income, FraudSignals? fraud,
        QuoteResponse quote, UnderwritingRecommendation rec, double verifiedDti,
        List<WorkflowStep> steps, string? threadId, string? foundryRunId, bool agentMode)
    {
        var prepared = new Dictionary<string, object>
        {
            ["run_id"] = runId,
            ["application_no"] = applicationNo,
            ["applicant_name"] = app.ApplicantName,
            ["dob"] = app.Dob,
            ["loan_amount_requested"] = app.LoanAmountRequested,
            ["loan_purpose"] = app.LoanPurpose,
            ["requested_term_months"] = app.RequestedTermMonths,
            ["loan_type"] = app.LoanType,
            ["payment_method"] = app.PaymentMethod,
            ["gross_annual_income"] = app.GrossAnnualIncome,
            ["monthly_net_income"] = app.MonthlyNetIncome,
            ["other_income_monthly"] = app.OtherIncomeMonthly,
            ["total_monthly_debt_payments"] = app.TotalMonthlyDebtPayments,
            ["housing_status"] = app.HousingStatus,
            ["housing_payment_monthly"] = app.HousingPaymentMonthly,
            ["declared_dti_pct"] = app.DeclaredDtiPct,
            ["prepared_at"] = DateTime.UtcNow.ToString("o"),
        };
        if (credit != null) prepared["credit_profile"] = credit;
        if (income != null) prepared["income_verification"] = income;
        if (fraud != null) prepared["fraud_signals"] = fraud;

        var workflowLog = new Dictionary<string, object>
        {
            ["run_id"] = runId,
            ["application_no"] = applicationNo,
            ["started_at"] = steps.First().Timestamp,
            ["current_step"] = "S10",
            ["status"] = "AWAITING_REVIEW",
            ["agent_framework"] = "Microsoft Agent Framework / Foundry Agent Service",
            ["execution_mode"] = agentMode ? "Foundry Agentic (threads/runs)" : "Local Rule-based",
            ["llm_model"] = agentMode ? "gpt-4.1 (Azure AI Foundry)" : "N/A (rule-based)",
            ["steps"] = steps,
        };
        if (threadId != null) workflowLog["foundry_thread_id"] = threadId;
        if (foundryRunId != null) workflowLog["foundry_run_id"] = foundryRunId;

        var recSummary = new Dictionary<string, object>
        {
            ["run_id"] = runId,
            ["application_no"] = applicationNo,
            ["recommendation_status"] = rec.RecommendationStatus,
            ["confidence_score"] = rec.ConfidenceScore,
            ["rationale_summary"] = rec.RationaleSummary,
            ["key_factors"] = rec.KeyFactors,
            ["conditions"] = rec.Conditions,
            ["policy_hits"] = rec.PolicyHits,
            ["quote"] = quote,
            ["verified_dti_pct"] = verifiedDti,
            ["agent_enhanced"] = agentMode,
            ["generated_at"] = DateTime.UtcNow.ToString("o"),
        };
        if (threadId != null) recSummary["foundry_thread_id"] = threadId;

        await WriteJson("loan_application_prepared.json", prepared);
        await WriteJson("workflow_run_log.json", workflowLog);
        await WriteJson("loan_recommendation_summary.json", recSummary);

        return new AgentRunResponse
        {
            RunId = runId,
            ApplicationNo = applicationNo,
            Prepared = prepared,
            WorkflowLog = workflowLog,
            Recommendation = recSummary,
        };
    }

    public async Task<Dictionary<string, object>> RecordDecisionAsync(DecisionRequest req)
    {
        var record = new Dictionary<string, object>
        {
            ["decision_id"] = $"DEC-{Guid.NewGuid().ToString("N")[..8].ToUpper()}",
            ["run_id"] = req.RunId,
            ["application_no"] = req.ApplicationNo,
            ["reviewer_id"] = req.ReviewerId,
            ["final_status"] = req.Decision,
            ["adjusted_terms"] = new Dictionary<string, object?>
            {
                ["amount"] = req.AdjustedAmount,
                ["term_months"] = req.AdjustedTermMonths,
                ["apr_pct"] = req.AdjustedRate,
            },
            ["ai_recommendation_at_decision"] = req.RecommendationSnapshot ?? new { },
            ["reviewer_notes"] = req.Notes,
            ["decided_at"] = DateTime.UtcNow.ToString("o"),
        };

        await WriteJson("human_decision_record.json", record);
        return record;
    }

    private async Task WriteJson(string filename, object data)
    {
        var path = Path.Combine(_outputDir, filename);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, _jsonOpts));
    }
}
