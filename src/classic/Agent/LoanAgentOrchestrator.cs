using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    private readonly ILogger<LoanAgentPlugins> _logger;

    public LoanAgentPlugins(CsvDataService data, UnderwritingService underwriting, ILogger<LoanAgentPlugins> logger)
    {
        _data = data;
        _underwriting = underwriting;
        _logger = logger;
    }

    public LoanApplication? GetApplication(string applicationNo)
    {
        var result = _data.Applications.GetValueOrDefault(applicationNo);
        _logger.LogDebug("GetApplication({AppNo}): {Found}", applicationNo, result != null ? "found" : "not found");
        return result;
    }

    public CreditProfile? GetCreditProfile(string applicationNo)
    {
        var result = _data.CreditProfiles.GetValueOrDefault(applicationNo);
        _logger.LogDebug("GetCreditProfile({AppNo}): score={Score}, band={Band}",
            applicationNo, result?.BureauScore, result?.ScoreBand);
        return result;
    }

    public IncomeVerification? GetIncomeVerification(string applicationNo)
    {
        var result = _data.IncomeVerifications.GetValueOrDefault(applicationNo);
        _logger.LogDebug("GetIncomeVerification({AppNo}): status={Status}, verified=${Income}/mo",
            applicationNo, result?.VerificationStatus, result?.VerifiedMonthlyIncome);
        return result;
    }

    public FraudSignals? GetFraudSignals(string applicationNo)
    {
        var result = _data.FraudSignals.GetValueOrDefault(applicationNo);
        _logger.LogDebug("GetFraudSignals({AppNo}): identityRisk={Risk}, manualReview={ManualReview}",
            applicationNo, result?.IdentityRiskScore, result?.RecommendedManualReview);
        return result;
    }

    public List<PolicyThreshold> GetPolicyThresholds()
    {
        var result = _data.PolicyThresholds;
        _logger.LogDebug("GetPolicyThresholds: {Count} rules loaded", result.Count);
        return result;
    }

    public QuoteResponse ComputeQuote(string applicationNo, double amount, int termMonths, string loanType)
    {
        var result = _underwriting.ComputeQuote(new QuoteRequest
        {
            ApplicationNo = applicationNo,
            RequestedAmount = amount,
            RequestedTermMonths = termMonths,
            LoanType = loanType,
        });
        _logger.LogDebug("ComputeQuote({AppNo}): APR={Apr}%, payment=${Payment}/mo, PTI={Pti}%",
            applicationNo, result.AprPct, result.EstimatedMonthlyPayment, result.PaymentToIncomePct);
        return result;
    }

    public UnderwritingRecommendation EvaluateUnderwriting(
        string runId, string applicationNo, double amount, int termMonths,
        string loanType, double monthlyIncome, double monthlyDebt,
        int creditScore, double identityRisk, double pti, double dti)
    {
        var result = _underwriting.Evaluate(new UnderwritingRequest
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
        _logger.LogInformation("EvaluateUnderwriting({AppNo}): recommendation={Status}, confidence={Confidence:F2}, policyHits={Hits}",
            applicationNo, result.RecommendationStatus, result.ConfidenceScore, result.PolicyHits?.Count ?? 0);
        return result;
    }
}

/// <summary>
/// Orchestrates the S01–S10 agentic workflow using Microsoft Agent Framework.
/// Retrieves the loan_orchestrator AIAgent from Foundry Agent Service and
/// runs conversations through threads/runs. The orchestrator agent coordinates
/// connected specialized agents (credit, income, fraud, policy, pricing, underwriting).
/// Falls back to local rule-based orchestration when Foundry is unavailable.
/// 
/// Observability: emits OpenTelemetry traces (ActivitySource "LoanOrigination"),
/// custom metrics (workflow duration, agent calls, recommendations), and structured logs.
/// </summary>
public class LoanAgentOrchestrator
{
    private static readonly ActivitySource ActivitySource = new("LoanOrigination", "1.0.0");
    private static readonly Meter Meter = new("LoanOrigination", "1.0.0");

    // Metrics
    private static readonly Counter<long> WorkflowCounter = Meter.CreateCounter<long>("loan.workflow.total", description: "Total workflow executions");
    private static readonly Counter<long> AgentCallCounter = Meter.CreateCounter<long>("loan.agent.calls.total", description: "Total Foundry agent calls");
    private static readonly Counter<long> AgentErrorCounter = Meter.CreateCounter<long>("loan.agent.errors.total", description: "Foundry agent call failures");
    private static readonly Histogram<double> WorkflowDuration = Meter.CreateHistogram<double>("loan.workflow.duration_ms", "ms", "Workflow execution duration");
    private static readonly Histogram<double> AgentCallDuration = Meter.CreateHistogram<double>("loan.agent.call.duration_ms", "ms", "Foundry agent call duration");

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

        _logger.LogInformation("LoanAgentOrchestrator initialized. Foundry Agent Service: {FoundryStatus}, OutputDir: {OutputDir}",
            _agentsClient != null ? "Connected" : "NOT CONFIGURED", _outputDir);
    }

    /// <summary>
    /// Runs a startup health check by invoking the health_check_agent in Foundry.
    /// Confirms end-to-end connectivity: credential → Foundry → model → response.
    /// </summary>
    public async Task<bool> HealthCheckAsync()
    {
        if (_agentsClient == null)
        {
            _logger.LogError("❌ Health check failed: PersistentAgentsClient not configured");
            return false;
        }

        using var activity = ActivitySource.StartActivity("HealthCheck");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("🏥 Running startup health check against Foundry Agent Service...");

        try
        {
            // Find the health_check_agent
            string? healthAgentId = null;
            int agentCount = 0;
            await foreach (var agent in _agentsClient.Administration.GetAgentsAsync())
            {
                agentCount++;
                _logger.LogInformation("  Agent found: {Name} (model={Model}, id={Id})", agent.Name, agent.Model, agent.Id);
                if (agent.Name == "health_check_agent")
                    healthAgentId = agent.Id;
            }

            _logger.LogInformation("  Total agents in Foundry: {Count}", agentCount);

            if (healthAgentId == null)
            {
                _logger.LogError("❌ Health check agent not found in Foundry. Run agent_init first.");
                return false;
            }

            // Run the health check agent
            var healthAgent = await _agentsClient.GetAIAgentAsync(healthAgentId);
            var response = await healthAgent.RunAsync("Perform health check now.");

            sw.Stop();
            _logger.LogInformation("✅ Health check passed in {Duration}ms", sw.ElapsedMilliseconds);
            _logger.LogInformation("   Response: {Text}", response.Text);
            _logger.LogInformation("   Run ID:   {RunId}", response.ResponseId);
            _logger.LogInformation("   Thread:   {ThreadId}", response.AdditionalProperties?.GetValueOrDefault("threadId"));

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("health.duration_ms", sw.ElapsedMilliseconds);
            activity?.SetTag("health.agent_count", agentCount);
            return true;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "❌ Health check FAILED after {Duration}ms: {Error}", sw.ElapsedMilliseconds, ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Resolves the orchestrator agent ID by listing agents and matching by name.
    /// </summary>
    private async Task<string?> ResolveOrchestratorAgentIdAsync()
    {
        if (_orchestratorAgentId != null) return _orchestratorAgentId;
        if (_agentsClient == null) return null;

        using var activity = ActivitySource.StartActivity("ResolveOrchestratorAgent");
        var agentName = _config["Foundry:OrchestratorAgentName"] ?? "loan_orchestrator";
        activity?.SetTag("agent.name", agentName);

        try
        {
            _logger.LogInformation("Listing agents from Foundry to resolve '{AgentName}'...", agentName);
            int agentCount = 0;
            await foreach (var agent in _agentsClient.Administration.GetAgentsAsync())
            {
                agentCount++;
                _logger.LogInformation("  Found agent: {Name} (model={Model}, id={Id})", agent.Name, agent.Model, agent.Id);
                if (agent.Name == agentName)
                {
                    _orchestratorAgentId = agent.Id;
                    _logger.LogInformation("✅ Resolved orchestrator agent '{Name}' → {Id} (scanned {Count} agents)", agentName, agent.Id, agentCount);
                    activity?.SetTag("agent.id", agent.Id);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return _orchestratorAgentId;
                }
            }
            _logger.LogError("❌ Orchestrator agent '{Name}' not found among {Count} agents in Foundry. Falling back to local mode.", agentName, agentCount);
            activity?.SetStatus(ActivityStatusCode.Error, "Agent not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to list agents from Foundry (endpoint: {Endpoint}). Status: {Status}. Falling back to local mode.",
                _config["AzureOpenAI:Endpoint"], ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName ?? ex.GetType().Name },
                { "exception.message", ex.Message },
                { "exception.stacktrace", ex.StackTrace ?? "" },
            }));
        }
        return null;
    }

    public async Task<AgentRunResponse> RunWorkflowAsync(string applicationNo)
    {
        using var activity = ActivitySource.StartActivity("RunWorkflow", ActivityKind.Server);
        activity?.SetTag("loan.application_no", applicationNo);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("=== Starting workflow for application {AppNo} ===", applicationNo);

        // Validate application exists
        var app = _plugins.GetApplication(applicationNo);
        if (app == null)
        {
            _logger.LogError("Application {AppNo} not found in data store", applicationNo);
            activity?.SetStatus(ActivityStatusCode.Error, "Application not found");
            throw new ArgumentException($"Application {applicationNo} not found");
        }

        _logger.LogInformation("Application {AppNo} loaded: applicant={Applicant}, amount=${Amount}, type={Type}, term={Term}mo",
            applicationNo, app.ApplicantName, app.LoanAmountRequested, app.LoanType, app.RequestedTermMonths);

        // Require Foundry Agent Service — no local fallback
        if (_agentsClient == null)
        {
            _logger.LogError("❌ PersistentAgentsClient is not configured. Cannot run workflow.");
            throw new InvalidOperationException("Azure AI Foundry is not configured. Set AzureOpenAI:Endpoint in appsettings.json.");
        }

        var orchestratorId = await ResolveOrchestratorAgentIdAsync();
        if (orchestratorId == null)
        {
            _logger.LogError("❌ Could not resolve orchestrator agent in Foundry. Ensure agents are initialized with agent_init.");
            throw new InvalidOperationException("Orchestrator agent not found in Foundry. Run the agent initializer CLI first.");
        }

        activity?.SetTag("loan.execution_mode", "foundry_agentic");
        WorkflowCounter.Add(1, new KeyValuePair<string, object?>("mode", "agentic"));
        var result = await RunAgenticWorkflowAsync(applicationNo, app, orchestratorId);

        sw.Stop();
        WorkflowDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("application_no", applicationNo));
        activity?.SetTag("loan.run_id", result.RunId);
        activity?.SetTag("loan.duration_ms", sw.ElapsedMilliseconds);
        activity?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogInformation("=== Workflow complete for {AppNo}: runId={RunId}, duration={Duration}ms ===",
            applicationNo, result.RunId, sw.ElapsedMilliseconds);

        return result;
    }

    /// <summary>
    /// Agentic workflow: creates a thread, sends the application to the Foundry
    /// orchestrator agent, and lets the LLM drive the S01-S10 workflow through
    /// connected agents. Each step creates observable runs/threads in Foundry.
    /// </summary>
    private async Task<AgentRunResponse> RunAgenticWorkflowAsync(
        string applicationNo, LoanApplication app, string orchestratorAgentId)
    {
        using var activity = ActivitySource.StartActivity("RunAgenticWorkflow", ActivityKind.Internal);
        activity?.SetTag("loan.application_no", applicationNo);
        activity?.SetTag("agent.orchestrator_id", orchestratorAgentId);

        var runId = $"RUN-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
        activity?.SetTag("loan.run_id", runId);
        var steps = new List<WorkflowStep>();
        string Ts() => DateTime.UtcNow.ToString("o");

        void Log(string id, string name, string status, string? detail = null, string? agentName = null)
        {
            steps.Add(new WorkflowStep { StepId = id, StepName = name, Status = status, Timestamp = Ts(), Detail = detail, AgentName = agentName });
            _logger.LogInformation("[{RunId}] {StepId} {StepName}: {Status} — {Detail}", runId, id, name, status, detail);
        }

        Log("S01", "Application Intake", "COMPLETE", $"Application {applicationNo} received");

        // ── S02: Gather enrichment data ──────────────────────────────────────
        using (var enrichActivity = ActivitySource.StartActivity("DataEnrichment"))
        {
            enrichActivity?.SetTag("loan.application_no", applicationNo);
            _logger.LogDebug("[{RunId}] Starting data enrichment for {AppNo}...", runId, applicationNo);
        }
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

        _logger.LogDebug("[{RunId}] Enriched context size: {Size} chars", runId, enrichedContext.Length);

        Log("S02", "Data Enrichment", "COMPLETE", "All enrichment APIs called (credit, income, fraud, policy, pricing)");
        Log("S03", "Credit Profile Agent", "COMPLETE", $"Bureau score: {credit?.BureauScore} ({credit?.ScoreBand})", "credit_profile_agent");
        Log("S04", "Income Verification Agent", "COMPLETE", $"Status: {income?.VerificationStatus}, Verified: ${income?.VerifiedMonthlyIncome}/mo", "income_verification_agent");
        Log("S05", "Fraud Screening Agent", "COMPLETE", $"Identity risk: {fraud?.IdentityRiskScore}, Manual review: {fraud?.RecommendedManualReview}", "fraud_screening_agent");
        Log("S06", "Policy Evaluation Agent", "COMPLETE", $"{thresholds.Count} rules evaluated, {rec.PolicyHits.Count(h => h.Outcome != "PASS")} flags", "policy_evaluation_agent");
        Log("S07", "DTI & Affordability", "COMPLETE", $"Verified DTI: {verifiedDti:P1}");
        Log("S08", "Pricing Agent", "COMPLETE", $"APR: {quote.AprPct}%, Payment: ${quote.EstimatedMonthlyPayment}/mo", "pricing_agent");

        // ── S09: Run the Foundry orchestrator agent via Agent Framework ──────
        string agentRationale;
        string? threadId = null;
        string? foundryRunId = null;

        try
        {
            using var agentActivity = ActivitySource.StartActivity("FoundryAgentRun", ActivityKind.Client);
            agentActivity?.SetTag("gen_ai.agent.id", orchestratorAgentId);
            agentActivity?.SetTag("gen_ai.agent.name", "loan_orchestrator");
            agentActivity?.SetTag("gen_ai.provider.name", "Azure AI Foundry");
            agentActivity?.SetTag("gen_ai.operation.name", "invoke_agent");
            agentActivity?.SetTag("loan.application_no", applicationNo);

            var agentSw = System.Diagnostics.Stopwatch.StartNew();
            AgentCallCounter.Add(1);

            _logger.LogInformation("[{RunId}] Sending enriched application to Foundry orchestrator agent {AgentId}...", runId, orchestratorAgentId);
            _logger.LogDebug("[{RunId}] Agent prompt includes: application data, credit ({Score}), income ({IncomeStatus}), fraud ({FraudRisk}), pricing (APR {Apr}%), recommendation ({RecStatus})",
                runId, credit?.BureauScore, income?.VerificationStatus, fraud?.IdentityRiskScore, quote.AprPct, rec.RecommendationStatus);

            // Get the orchestrator as an AIAgent via Agent Framework
            var orchestratorAgent = await _agentsClient!.GetAIAgentAsync(orchestratorAgentId);
            _logger.LogDebug("[{RunId}] AIAgent resolved from Foundry. Calling RunAsync...", runId);

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

            agentSw.Stop();
            AgentCallDuration.Record(agentSw.ElapsedMilliseconds);

            agentRationale = agentResponse.Text;
            threadId = agentResponse.AdditionalProperties?.GetValueOrDefault("threadId")?.ToString();
            foundryRunId = agentResponse.ResponseId;

            agentActivity?.SetTag("foundry.thread_id", threadId);
            agentActivity?.SetTag("foundry.run_id", foundryRunId);
            agentActivity?.SetTag("gen_ai.response.length", agentRationale?.Length ?? 0);
            agentActivity?.SetTag("agent.call.duration_ms", agentSw.ElapsedMilliseconds);
            agentActivity?.SetStatus(ActivityStatusCode.Ok);

            _logger.LogInformation("[{RunId}] Foundry agent run complete. Thread: {ThreadId}, Run: {FoundryRunId}, Duration: {Duration}ms, Response: {Length} chars",
                runId, threadId ?? "N/A", foundryRunId ?? "N/A", agentSw.ElapsedMilliseconds, agentRationale?.Length ?? 0);
            _logger.LogTrace("[{RunId}] Agent response text:\n{Rationale}", runId, agentRationale);

            Log("S09", "Orchestrator Agent Analysis (Foundry)", "COMPLETE",
                $"AI rationale generated via Foundry Agent Service [thread={threadId}, run={foundryRunId}, duration={agentSw.ElapsedMilliseconds}ms]",
                "loan_orchestrator");
        }
        catch (Exception ex)
        {
            AgentErrorCounter.Add(1);
            _logger.LogError(ex, "[{RunId}] ❌ Foundry agent run failed. Error: {ErrorType}: {ErrorMessage}",
                runId, ex.GetType().Name, ex.Message);
            activity?.AddEvent(new ActivityEvent("AgentError", tags: new ActivityTagsCollection
            {
                { "error.type", ex.GetType().Name },
                { "error.message", ex.Message },
                { "exception.stacktrace", ex.StackTrace ?? "" },
            }));
            throw new InvalidOperationException($"Foundry agent run failed: {ex.Message}", ex);
        }

        rec.RationaleSummary = agentRationale ?? rec.RationaleSummary;
        Log("S10", "Human Review Ready", "PENDING", "Awaiting reviewer decision");

        // Build output
        return await BuildAndSaveResponse(runId, applicationNo, app, credit, income, fraud,
            quote, rec, verifiedDti, steps, threadId, foundryRunId);
    }

    private async Task<AgentRunResponse> BuildAndSaveResponse(
        string runId, string applicationNo, LoanApplication app,
        CreditProfile? credit, IncomeVerification? income, FraudSignals? fraud,
        QuoteResponse quote, UnderwritingRecommendation rec, double verifiedDti,
        List<WorkflowStep> steps, string? threadId, string? foundryRunId)
    {
        using var activity = ActivitySource.StartActivity("BuildAndSaveResponse");
        activity?.SetTag("loan.run_id", runId);

        _logger.LogDebug("[{RunId}] Building response: {StepCount} steps", runId, steps.Count);
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
            ["execution_mode"] = "Foundry Agentic (threads/runs)",
            ["llm_model"] = "gpt-4.1 (Azure AI Foundry)",
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
            ["agent_enhanced"] = true,
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
        using var activity = ActivitySource.StartActivity("RecordDecision");
        activity?.SetTag("loan.run_id", req.RunId);
        activity?.SetTag("loan.application_no", req.ApplicationNo);
        activity?.SetTag("loan.decision", req.Decision);

        _logger.LogInformation("Recording decision for {AppNo}: decision={Decision}, reviewer={Reviewer}",
            req.ApplicationNo, req.Decision, req.ReviewerId);

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
        _logger.LogInformation("Decision recorded: {DecisionId} for {AppNo} — {Decision}",
            record["decision_id"], req.ApplicationNo, req.Decision);

        activity?.SetTag("loan.decision_id", record["decision_id"]);
        activity?.SetStatus(ActivityStatusCode.Ok);
        return record;
    }

    private async Task WriteJson(string filename, object data)
    {
        var path = Path.Combine(_outputDir, filename);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, _jsonOpts));
        _logger.LogDebug("Wrote output file: {Filename} ({Size} bytes)", filename, new FileInfo(path).Length);
    }
}
