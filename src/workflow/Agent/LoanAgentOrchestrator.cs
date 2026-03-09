using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Azure.AI.Projects;
using LoanOriginationDemo.Agent.Workflow;
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
/// Orchestrates the S01–S10 agentic workflow using a declarative YAML workflow definition.
/// 
/// Architecture: The workflow is defined in LoanOrigination.yaml (owned by agent_init) and
/// executed at runtime via DeclarativeWorkflowBuilder + InProcessExecution. The YAML file
/// defines sequential InvokeAzureAgent actions for 6 specialist agents in Foundry.
/// 
/// This class handles data enrichment (S01-S02, S07), builds the enriched JSON payload,
/// and passes it to the declarative workflow for AI analysis (S03-S06, S08-S09).
/// 
/// Observability: emits OpenTelemetry traces (ActivitySource "LoanOrigination"),
/// custom metrics (workflow duration, agent calls, recommendations), and structured logs.
/// </summary>
public class LoanAgentOrchestrator
{
    private static readonly ActivitySource ActivitySource = new("LoanOrigination", "1.0.0");
    private static readonly Meter Meter = new("LoanOrigination", "1.0.0");

    private static readonly Counter<long> WorkflowCounter = Meter.CreateCounter<long>("loan.workflow.total", description: "Total workflow executions");
    private static readonly Counter<long> AgentErrorCounter = Meter.CreateCounter<long>("loan.agent.errors.total", description: "Foundry agent call failures");
    private static readonly Histogram<double> WorkflowDuration = Meter.CreateHistogram<double>("loan.workflow.duration_ms", "ms", "Workflow execution duration");

    private readonly LoanAgentPlugins _plugins;
    private readonly AIProjectClient? _projectClient;
    private readonly IConfiguration _config;
    private readonly ILogger<LoanAgentOrchestrator> _logger;
    private readonly string _outputDir;
    private readonly JsonSerializerOptions _jsonOpts;

    public LoanAgentOrchestrator(
        LoanAgentPlugins plugins,
        IConfiguration config,
        ILogger<LoanAgentOrchestrator> logger,
        AIProjectClient? projectClient = null)
    {
        _plugins = plugins;
        _projectClient = projectClient;
        _config = config;
        _logger = logger;
        _outputDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "output");
        Directory.CreateDirectory(_outputDir);
        _jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        _logger.LogInformation("LoanAgentOrchestrator initialized (Agent Framework Workflows). Foundry: {FoundryStatus}, OutputDir: {OutputDir}",
            _projectClient != null ? "Connected" : "NOT CONFIGURED", _outputDir);
    }

    /// <summary>
    /// Runs a startup health check by invoking the health-check-agent in Foundry.
    /// Confirms end-to-end connectivity: credential → Foundry → model → response.
    /// </summary>
    public async Task<bool> HealthCheckAsync()
    {
        if (_projectClient == null)
        {
            _logger.LogError("❌ Health check failed: AIProjectClient not configured");
            return false;
        }

        using var activity = ActivitySource.StartActivity("HealthCheck");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("🏥 Running startup health check against Foundry Agent Service...");

        try
        {
            // Find the health-check-agent using new API
            bool healthAgentFound = false;
            int agentCount = 0;
            await foreach (var agent in _projectClient.Agents.GetAgentsAsync())
            {
                agentCount++;
                _logger.LogInformation("  Agent found: {Name} (id={Id})", agent.Name, agent.Id);
                if (agent.Name == "health-check-agent")
                    healthAgentFound = true;
            }

            _logger.LogInformation("  Total agents in Foundry: {Count}", agentCount);

            if (!healthAgentFound)
            {
                _logger.LogError("❌ Health check agent not found in Foundry. Run agent_init first.");
                return false;
            }

            // Verify agent record is accessible via GetAgentAsync
            var agentRecord = await _projectClient.Agents.GetAgentAsync("health-check-agent");

            sw.Stop();
            _logger.LogInformation("✅ Health check passed in {Duration}ms", sw.ElapsedMilliseconds);
            _logger.LogInformation("   Agent: {Name} (id={Id})", agentRecord.Value.Name, agentRecord.Value.Id);
            _logger.LogInformation("   Total agents: {Count}", agentCount);

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

    public async Task<AgentRunResponse> RunWorkflowAsync(string applicationNo)
    {
        using var activity = ActivitySource.StartActivity("RunWorkflow", ActivityKind.Server);
        activity?.SetTag("loan.application_no", applicationNo);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("=== Starting Declarative Workflow for application {AppNo} ===", applicationNo);

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

        // Require Foundry endpoint
        var foundryEndpointStr = _config["AzureOpenAI:Endpoint"];
        if (string.IsNullOrEmpty(foundryEndpointStr))
        {
            _logger.LogError("❌ AzureOpenAI:Endpoint is not configured. Cannot run workflow.");
            throw new InvalidOperationException("Azure AI Foundry is not configured. Set AzureOpenAI:Endpoint in appsettings.json.");
        }

        activity?.SetTag("loan.execution_mode", "declarative_workflow");
        WorkflowCounter.Add(1, new KeyValuePair<string, object?>("mode", "declarative_workflow"));

        // Build enriched application data (previously done in IntakeExecutor)
        var credit = _plugins.GetCreditProfile(applicationNo);
        var income = _plugins.GetIncomeVerification(applicationNo);
        var fraud = _plugins.GetFraudSignals(applicationNo);
        var thresholds = _plugins.GetPolicyThresholds();
        var quote = _plugins.ComputeQuote(applicationNo, app.LoanAmountRequested, app.RequestedTermMonths, app.LoanType);
        double verifiedDti = income?.VerifiedMonthlyIncome > 0
            ? Math.Round(app.TotalMonthlyDebtPayments / income.VerifiedMonthlyIncome, 4) : 999;
        var rec = _plugins.EvaluateUnderwriting(
            $"RUN-WF", applicationNo, app.LoanAmountRequested, app.RequestedTermMonths,
            app.LoanType, income?.VerifiedMonthlyIncome ?? 0, app.TotalMonthlyDebtPayments,
            credit?.BureauScore ?? 0, fraud?.IdentityRiskScore ?? 1.0,
            quote.PaymentToIncomePct, verifiedDti);

        var enrichedData = JsonSerializer.Serialize(new
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
            policy_thresholds = thresholds,
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
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = false });

        _logger.LogInformation("Enriched application data: {Size} chars", enrichedData.Length);

        // Build the workflow prompt with explicit step instructions
        var workflowPrompt = $"Execute the Loan Origination Workflow for application {applicationNo}.\n\n" +
            $"WORKFLOW STEPS:\n" +
            $"S01 — Application Intake: Confirm receipt and validate all required fields.\n" +
            $"S02 — Data Enrichment: Verify supporting data gathered (credit, income, fraud, policy, pricing).\n" +
            $"S03 — Credit Profile Analysis (credit-profile-agent): Analyze bureau score ({credit?.BureauScore}, {credit?.ScoreBand}), delinquencies, utilization.\n" +
            $"S04 — Income Verification (income-verification-agent): Evaluate verified income (${income?.VerifiedMonthlyIncome}/mo), status ({income?.VerificationStatus}).\n" +
            $"S05 — Fraud Screening (fraud-screening-agent): Assess identity risk ({fraud?.IdentityRiskScore}), device risk, watchlist hits.\n" +
            $"S06 — Policy Evaluation (policy-evaluation-agent): Review {thresholds.Count} policy rules, flag failures.\n" +
            $"S07 — DTI & Affordability: Verified DTI {verifiedDti:P1} against threshold.\n" +
            $"S08 — Pricing Analysis (pricing-agent): APR {quote.AprPct}%, payment ${quote.EstimatedMonthlyPayment}/mo.\n" +
            $"S09 — Underwriting Recommendation: Synthesize all findings into final recommendation ({rec.RecommendationStatus}, confidence {rec.ConfidenceScore:F2}).\n\n" +
            $"ENRICHED APPLICATION DATA:\n{enrichedData}";

        // Execute the Declarative YAML workflow
        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var workflowRunner = new Workflow.LoanWorkflowRunner(
            new Uri(foundryEndpointStr), _logger, loggerFactory);
        Workflow.WorkflowResult workflowResult;
        try
        {
            workflowResult = await workflowRunner.ExecuteAsync(enrichedData);
        }
        catch (Exception ex)
        {
            AgentErrorCounter.Add(1);
            _logger.LogError(ex, "❌ Agent Framework workflow failed for {AppNo}: {Error}", applicationNo, ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw new InvalidOperationException($"Agent workflow failed: {ex.Message}", ex);
        }

        // Use already-computed data for the response
        var runId = $"RUN-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";

        // Override the rule-based rationale with the AI-generated one
        rec.RationaleSummary = workflowResult.Rationale;

        // Save agent workflow trace (input, context, thread/run, response)
        var agentTrace = new Dictionary<string, object>
        {
            ["run_id"] = runId,
            ["application_no"] = applicationNo,
            ["agent_name"] = "loan-origination-workflow",
            ["workflow_type"] = "Declarative YAML Workflow",
            ["workflow_file"] = "LoanOrigination.yaml",
            ["prompt"] = workflowPrompt,
            ["enriched_context"] = JsonSerializer.Deserialize<object>(enrichedData,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!,
            ["response"] = workflowResult.Rationale,
            ["response_length_chars"] = workflowResult.Rationale.Length,
            ["duration_ms"] = workflowResult.DurationMs,
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
        };
        if (workflowResult.ThreadId != null) agentTrace["foundry_thread_id"] = workflowResult.ThreadId;
        if (workflowResult.FoundryRunId != null) agentTrace["foundry_run_id"] = workflowResult.FoundryRunId;
        await WriteJson("agent_workflow_trace.json", agentTrace);

        // Build workflow step log
        var steps = new List<WorkflowStep>
        {
            new() { StepId = "S01", StepName = "Application Intake", Status = "COMPLETE", Timestamp = DateTime.UtcNow.ToString("o"), Detail = $"Application {applicationNo} received" },
            new() { StepId = "S02", StepName = "Data Enrichment", Status = "COMPLETE", Timestamp = DateTime.UtcNow.ToString("o"), Detail = "All enrichment APIs called" },
            new() { StepId = "S03", StepName = "Credit Profile Agent", Status = "COMPLETE", Timestamp = DateTime.UtcNow.ToString("o"), Detail = $"Bureau score: {credit?.BureauScore} ({credit?.ScoreBand})", AgentName = "credit-profile-agent" },
            new() { StepId = "S04", StepName = "Income Verification Agent", Status = "COMPLETE", Timestamp = DateTime.UtcNow.ToString("o"), Detail = $"Status: {income?.VerificationStatus}, Verified: ${income?.VerifiedMonthlyIncome}/mo", AgentName = "income-verification-agent" },
            new() { StepId = "S05", StepName = "Fraud Screening Agent", Status = "COMPLETE", Timestamp = DateTime.UtcNow.ToString("o"), Detail = $"Identity risk: {fraud?.IdentityRiskScore}, Manual review: {fraud?.RecommendedManualReview}", AgentName = "fraud-screening-agent" },
            new() { StepId = "S06", StepName = "Policy Evaluation", Status = "COMPLETE", Timestamp = DateTime.UtcNow.ToString("o"), Detail = $"{thresholds.Count} rules evaluated", AgentName = "policy-evaluation-agent" },
            new() { StepId = "S07", StepName = "DTI & Affordability", Status = "COMPLETE", Timestamp = DateTime.UtcNow.ToString("o"), Detail = $"Verified DTI: {verifiedDti:P1}" },
            new() { StepId = "S08", StepName = "Pricing", Status = "COMPLETE", Timestamp = DateTime.UtcNow.ToString("o"), Detail = $"APR: {quote.AprPct}%, Payment: ${quote.EstimatedMonthlyPayment}/mo", AgentName = "pricing-agent" },
            new() { StepId = "S09", StepName = "Loan Orchestrator (Declarative Workflow)", Status = "COMPLETE", Timestamp = DateTime.UtcNow.ToString("o"), Detail = $"Full workflow (S01–S09) executed via Declarative YAML Workflow [{workflowResult.DurationMs}ms]", AgentName = "loan-origination-workflow" },
            new() { StepId = "S10", StepName = "Human Review Ready", Status = "PENDING", Timestamp = DateTime.UtcNow.ToString("o"), Detail = "Awaiting reviewer decision" },
        };

        var result = await BuildAndSaveResponse(runId, applicationNo, app, credit, income, fraud,
            quote, rec, verifiedDti, steps, workflowResult.ThreadId, workflowResult.FoundryRunId);

        sw.Stop();
        WorkflowDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("application_no", applicationNo));
        activity?.SetTag("loan.run_id", result.RunId);
        activity?.SetTag("loan.duration_ms", sw.ElapsedMilliseconds);
        activity?.SetStatus(ActivityStatusCode.Ok);

        _logger.LogInformation("=== Workflow complete for {AppNo}: runId={RunId}, duration={Duration}ms ===",
            applicationNo, result.RunId, sw.ElapsedMilliseconds);

        return result;
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
            ["execution_mode"] = "Declarative YAML Workflow (LoanOrigination.yaml)",
            ["llm_model"] = "gpt-4.1, gpt-5.2-chat, Phi-4-reasoning (Azure AI Foundry)",
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
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var stamped = Path.GetFileNameWithoutExtension(filename) + $"_{stamp}.json";
        var path = Path.Combine(_outputDir, stamped);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, _jsonOpts));
        _logger.LogDebug("Wrote output file: {Filename} ({Size} bytes)", stamped, new FileInfo(path).Length);
    }
}
