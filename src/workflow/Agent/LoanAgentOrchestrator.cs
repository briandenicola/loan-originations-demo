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

    public async Task<AgentRunResponse> RunWorkflowAsync(string applicationNo, Action<string, string, string>? onStepUpdate = null)
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

        // Require Foundry configuration
        if (_projectClient == null)
        {
            _logger.LogError("❌ AIProjectClient is not configured. Cannot run workflow.");
            throw new InvalidOperationException("Azure AI Foundry is not configured.");
        }

        activity?.SetTag("loan.execution_mode", "code_coordinator");
        WorkflowCounter.Add(1, new KeyValuePair<string, object?>("mode", "code_coordinator"));

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

        onStepUpdate?.Invoke("S01", "COMPLETE", $"Application {applicationNo} accepted");
        onStepUpdate?.Invoke("S02", "COMPLETE", "All enrichment data loaded");

        activity?.SetTag("loan.execution_mode", "code_coordinator");
        var runId = $"RUN-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
        activity?.SetTag("loan.run_id", runId);

        // ═══════════════════════════════════════════════════════════════
        // Code-Based Coordinator Workflow
        // Calls each specialist agent individually via AIProjectClient,
        // gathers responses, compiles a comprehensive brief, and sends
        // it to the underwriting agent for the final recommendation.
        // ═══════════════════════════════════════════════════════════════

        if (_projectClient == null)
            throw new InvalidOperationException("AIProjectClient not configured.");

        // Resolve all agents from Foundry
        var agentMap = new Dictionary<string, string>(); // name → id
        await foreach (var a in _projectClient.Agents.GetAgentsAsync())
            agentMap[a.Name] = a.Id;

        _logger.LogInformation("[{RunId}] Resolved {Count} agents from Foundry", runId, agentMap.Count);

        // Helper: call a single specialist agent by name
        async Task<(string response, string? threadId, string? runIdStr, long durationMs)> CallAgentAsync(
            string agentName, string prompt, string stepId)
        {
            if (!agentMap.TryGetValue(agentName, out var agentId))
            {
                _logger.LogWarning("[{RunId}] Agent '{Agent}' not found in Foundry — skipping", runId, agentName);
                return ($"Agent '{agentName}' not found in Foundry.", null, null, 0);
            }

            using var agentActivity = ActivitySource.StartActivity($"InvokeAgent_{stepId}", ActivityKind.Client);
            agentActivity?.SetTag("gen_ai.agent.name", agentName);
            var agentSw = System.Diagnostics.Stopwatch.StartNew();

            _logger.LogInformation("[{RunId}] {Step}: Calling agent '{Agent}'...", runId, stepId, agentName);

            var agent = await _projectClient!.GetAIAgentAsync(agentId);
            var response = await agent.RunAsync(prompt);

            agentSw.Stop();
            var text = response.Text ?? "(empty response)";
            _logger.LogInformation("[{RunId}] {Step}: Agent '{Agent}' responded in {Duration}ms ({Chars} chars)",
                runId, stepId, agentName, agentSw.ElapsedMilliseconds, text.Length);

            agentActivity?.SetTag("agent.response.length", text.Length);
            agentActivity?.SetTag("agent.call.duration_ms", agentSw.ElapsedMilliseconds);
            agentActivity?.SetStatus(ActivityStatusCode.Ok);

            return (text,
                response.AdditionalProperties?.GetValueOrDefault("threadId")?.ToString(),
                response.ResponseId,
                agentSw.ElapsedMilliseconds);
        }

        // ── S03–S08: Call specialist agents ─────────────────────────────

        var specialistResults = new Dictionary<string, string>();
        var agentTraces = new List<object>();

        var specialists = new (string stepId, string agentName, string prompt)[]
        {
            ("S03", "credit-profile-agent",
                $"Analyze the credit profile for this loan application. Provide a structured risk assessment covering bureau score, delinquencies, utilization, and credit age.\n\nAPPLICATION DATA:\n{enrichedData}"),
            ("S04", "income-verification-agent",
                $"Verify the income data for this loan application. Assess verification confidence, employer match, income stability, and affordability.\n\nAPPLICATION DATA:\n{enrichedData}"),
            ("S05", "fraud-screening-agent",
                $"Screen this loan application for fraud signals. Classify the fraud risk level, check identity verification, device signals, and watchlist matches.\n\nAPPLICATION DATA:\n{enrichedData}"),
            ("S06", "policy-evaluation-agent",
                $"Evaluate this loan application against all underwriting policy rules POL-001 through POL-010. Provide per-rule PASS/FAIL assessment with reasoning.\n\nAPPLICATION DATA:\n{enrichedData}"),
            ("S08", "pricing-agent",
                $"Review the pricing data in this loan application. Validate the risk tier assignment, quoted APR, and monthly payment calculations.\n\nAPPLICATION DATA:\n{enrichedData}"),
        };

        foreach (var (stepId, agentName, prompt) in specialists)
        {
            onStepUpdate?.Invoke(stepId, "RUNNING", $"Calling {agentName}...");
            try
            {
                var (response, tid, rid, dur) = await CallAgentAsync(agentName, prompt, stepId);
                specialistResults[stepId] = response;
                agentTraces.Add(new { step = stepId, agent = agentName, response_length = response.Length, duration_ms = dur, thread_id = tid, run_id = rid });
                onStepUpdate?.Invoke(stepId, "COMPLETE", $"{agentName} done ({dur}ms, {response.Length} chars)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RunId}] {Step}: Agent '{Agent}' failed: {Error}", runId, stepId, agentName, ex.Message);
                specialistResults[stepId] = $"ERROR: {ex.Message}";
                agentTraces.Add(new { step = stepId, agent = agentName, error = ex.Message });
                onStepUpdate?.Invoke(stepId, "FAILED", $"{agentName} error: {ex.Message}");
            }
        }

        // ── Compile comprehensive brief ─────────────────────────────────

        onStepUpdate?.Invoke("S07", "COMPLETE", $"Verified DTI: {verifiedDti:P1}");

        var briefBuilder = new System.Text.StringBuilder();
        briefBuilder.AppendLine("You are the final underwriting recommendation agent. Below is the ORIGINAL APPLICATION DATA followed by COMPLETE ANALYSIS from each specialist agent. Use ALL of this information to produce your final recommendation.");
        briefBuilder.AppendLine();
        briefBuilder.AppendLine("═══════════════════════════════════════════");
        briefBuilder.AppendLine("SECTION 1: ORIGINAL APPLICATION DATA");
        briefBuilder.AppendLine("═══════════════════════════════════════════");
        briefBuilder.AppendLine(enrichedData);
        briefBuilder.AppendLine();

        var sectionNames = new Dictionary<string, string>
        {
            ["S03"] = "CREDIT PROFILE ANALYSIS (credit-profile-agent)",
            ["S04"] = "INCOME VERIFICATION ANALYSIS (income-verification-agent)",
            ["S05"] = "FRAUD SCREENING ANALYSIS (fraud-screening-agent)",
            ["S06"] = "POLICY EVALUATION (policy-evaluation-agent)",
            ["S08"] = "PRICING ANALYSIS (pricing-agent)",
        };

        int sectionNum = 2;
        foreach (var (stepId, sectionName) in sectionNames)
        {
            briefBuilder.AppendLine("═══════════════════════════════════════════");
            briefBuilder.AppendLine($"SECTION {sectionNum}: {sectionName}");
            briefBuilder.AppendLine("═══════════════════════════════════════════");
            briefBuilder.AppendLine(specialistResults.GetValueOrDefault(stepId, "(no response)"));
            briefBuilder.AppendLine();
            sectionNum++;
        }

        briefBuilder.AppendLine("═══════════════════════════════════════════");
        briefBuilder.AppendLine("YOUR TASK — FINAL UNDERWRITING RECOMMENDATION");
        briefBuilder.AppendLine("═══════════════════════════════════════════");
        briefBuilder.AppendLine("Based on ALL of the above specialist analyses and the original application data, produce your FINAL UNDERWRITING RECOMMENDATION including:");
        briefBuilder.AppendLine("1. Recommendation status: APPROVE, CONDITIONAL, or DECLINE");
        briefBuilder.AppendLine("2. Confidence score (0.0 to 1.0)");
        briefBuilder.AppendLine("3. Key risk factors and mitigating factors");
        briefBuilder.AppendLine("4. Conditions (if CONDITIONAL)");
        briefBuilder.AppendLine("5. Professional rationale summary for a human reviewer");

        var comprehensiveBrief = briefBuilder.ToString();
        _logger.LogInformation("[{RunId}] Compiled comprehensive brief: {Size} chars (from {SpecialistCount} specialist agents)",
            runId, comprehensiveBrief.Length, specialistResults.Count);

        // ── S09: Call underwriting recommendation agent ──────────────────

        onStepUpdate?.Invoke("S09", "RUNNING", "Calling underwriting-recommendation-agent...");

        string agentRationale;
        string? lastThreadId = null;
        string? lastRunId = null;
        long underwritingDurationMs = 0;

        try
        {
            var (response, tid, rid, dur) = await CallAgentAsync(
                "underwriting-recommendation-agent", comprehensiveBrief, "S09");
            agentRationale = response;
            lastThreadId = tid;
            lastRunId = rid;
            underwritingDurationMs = dur;
            agentTraces.Add(new { step = "S09", agent = "underwriting-recommendation-agent", response_length = response.Length, duration_ms = dur, thread_id = tid, run_id = rid });
            onStepUpdate?.Invoke("S09", "COMPLETE", $"Recommendation ready ({dur}ms)");
        }
        catch (Exception ex)
        {
            AgentErrorCounter.Add(1);
            _logger.LogError(ex, "[{RunId}] ❌ Underwriting agent failed: {Error}", runId, ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw new InvalidOperationException($"Underwriting agent failed: {ex.Message}", ex);
        }

        // Override the rule-based rationale with the AI-generated one
        rec.RationaleSummary = agentRationale;

        // Save agent workflow trace
        var agentTrace = new Dictionary<string, object>
        {
            ["run_id"] = runId,
            ["application_no"] = applicationNo,
            ["workflow_type"] = "Code-Based Coordinator Workflow",
            ["specialist_agents_called"] = specialistResults.Count,
            ["specialist_traces"] = agentTraces,
            ["comprehensive_brief_chars"] = comprehensiveBrief.Length,
            ["underwriting_response"] = agentRationale,
            ["underwriting_response_chars"] = agentRationale.Length,
            ["underwriting_duration_ms"] = underwritingDurationMs,
            ["enriched_context"] = JsonSerializer.Deserialize<object>(enrichedData,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!,
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
        };
        if (lastThreadId != null) agentTrace["foundry_thread_id"] = lastThreadId;
        if (lastRunId != null) agentTrace["foundry_run_id"] = lastRunId;
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
            new() { StepId = "S09", StepName = "Underwriting Recommendation", Status = "COMPLETE", Timestamp = DateTime.UtcNow.ToString("o"), Detail = $"Code-based coordinator — {specialistResults.Count} specialists + underwriter [{underwritingDurationMs}ms]", AgentName = "underwriting-recommendation-agent" },
            new() { StepId = "S10", StepName = "Human Review Ready", Status = "PENDING", Timestamp = DateTime.UtcNow.ToString("o"), Detail = "Awaiting reviewer decision" },
        };

        var result = await BuildAndSaveResponse(runId, applicationNo, app, credit, income, fraud,
            quote, rec, verifiedDti, steps, lastThreadId, lastRunId);

        onStepUpdate?.Invoke("S10", "COMPLETE", "Ready for human review");

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

    /// <summary>
    /// Recomputes the underwriting recommendation with adjusted loan terms by calling
    /// the pricing and underwriting AI agents with the new amounts.
    /// </summary>
    public async Task<object> RecomputeWithAgentAsync(
        string applicationNo, double requestedAmount, int requestedTermMonths, string loanType, string originalRunId)
    {
        using var activity = ActivitySource.StartActivity("RecomputeWithAgent", ActivityKind.Server);
        activity?.SetTag("loan.application_no", applicationNo);
        activity?.SetTag("loan.requested_amount", requestedAmount);
        activity?.SetTag("loan.requested_term_months", requestedTermMonths);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var app = _plugins.GetApplication(applicationNo)
            ?? throw new ArgumentException($"Application {applicationNo} not found");

        if (_projectClient == null)
            throw new InvalidOperationException("Azure AI Foundry is not configured.");

        // Re-enrich with adjusted terms
        var credit = _plugins.GetCreditProfile(applicationNo);
        var income = _plugins.GetIncomeVerification(applicationNo);
        var fraud = _plugins.GetFraudSignals(applicationNo);
        var thresholds = _plugins.GetPolicyThresholds();
        var quote = _plugins.ComputeQuote(applicationNo, requestedAmount, requestedTermMonths, loanType);
        double verifiedDti = income?.VerifiedMonthlyIncome > 0
            ? Math.Round(app.TotalMonthlyDebtPayments / income.VerifiedMonthlyIncome, 4) : 999;

        var enrichedData = JsonSerializer.Serialize(new
        {
            application = new
            {
                application_no = applicationNo,
                applicant_name = app.ApplicantName,
                loan_amount = requestedAmount,
                loan_purpose = app.LoanPurpose,
                term_months = requestedTermMonths,
                loan_type = loanType,
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
            verified_dti_pct = verifiedDti,
            recompute_context = new
            {
                original_run_id = originalRunId,
                original_amount = app.LoanAmountRequested,
                adjusted_amount = requestedAmount,
                original_term = app.RequestedTermMonths,
                adjusted_term = requestedTermMonths,
            },
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = false });

        // Resolve agents
        var agentMap = new Dictionary<string, string>();
        await foreach (var a in _projectClient.Agents.GetAgentsAsync())
            agentMap[a.Name] = a.Id;

        // Call pricing agent with adjusted terms
        string pricingAnalysis = "";
        if (agentMap.TryGetValue("pricing-agent", out var pricingId))
        {
            _logger.LogInformation("[RECOMPUTE] Calling pricing-agent with adjusted terms: ${Amount}, {Term}mo", requestedAmount, requestedTermMonths);
            var pricingAgent = await _projectClient.GetAIAgentAsync(pricingId);
            var pricingResponse = await pricingAgent.RunAsync(
                $"Review the pricing for this ADJUSTED loan application. The borrower is requesting a change from the original terms. Validate the risk tier, APR, and monthly payment for the new terms.\n\nADJUSTED APPLICATION DATA:\n{enrichedData}");
            pricingAnalysis = pricingResponse.Text ?? "";
            _logger.LogInformation("[RECOMPUTE] pricing-agent responded: {Chars} chars", pricingAnalysis.Length);
        }

        // Build brief for underwriting agent
        var brief = new System.Text.StringBuilder();
        brief.AppendLine("You are the underwriting recommendation agent. A loan officer has ADJUSTED the loan terms and is requesting a re-evaluation.");
        brief.AppendLine($"ORIGINAL loan amount: ${app.LoanAmountRequested:N0}, term: {app.RequestedTermMonths} months");
        brief.AppendLine($"ADJUSTED loan amount: ${requestedAmount:N0}, term: {requestedTermMonths} months");
        brief.AppendLine();
        brief.AppendLine("APPLICATION DATA (with adjusted terms):");
        brief.AppendLine(enrichedData);
        brief.AppendLine();
        if (!string.IsNullOrEmpty(pricingAnalysis))
        {
            brief.AppendLine("PRICING ANALYSIS (for adjusted terms):");
            brief.AppendLine(pricingAnalysis);
            brief.AppendLine();
        }
        brief.AppendLine("Produce your FINAL UNDERWRITING RECOMMENDATION for the ADJUSTED terms. Include:");
        brief.AppendLine("1. Recommendation status: APPROVE, CONDITIONAL, or DECLINE");
        brief.AppendLine("2. Confidence score (0.0 to 1.0)");
        brief.AppendLine("3. How the term/amount change affects risk assessment");
        brief.AppendLine("4. Key factors and conditions");
        brief.AppendLine("5. Professional rationale for the human reviewer");

        // Call underwriting agent
        if (!agentMap.TryGetValue("underwriting-recommendation-agent", out var uwId))
            throw new InvalidOperationException("underwriting-recommendation-agent not found in Foundry.");

        _logger.LogInformation("[RECOMPUTE] Calling underwriting-recommendation-agent...");
        var uwAgent = await _projectClient.GetAIAgentAsync(uwId);
        var uwResponse = await uwAgent.RunAsync(brief.ToString());
        var rationale = uwResponse.Text ?? "(empty response)";

        sw.Stop();
        _logger.LogInformation("[RECOMPUTE] Complete for {AppNo}: {Chars} chars in {Duration}ms",
            applicationNo, rationale.Length, sw.ElapsedMilliseconds);

        activity?.SetTag("loan.recompute.duration_ms", sw.ElapsedMilliseconds);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return new
        {
            quote = new
            {
                apr_pct = quote.AprPct,
                estimated_monthly_payment = quote.EstimatedMonthlyPayment,
                payment_to_income_pct = quote.PaymentToIncomePct,
                total_repayable_amount = quote.TotalRepayableAmount,
                risk_tier = quote.RiskTier,
                loan_amount = requestedAmount,
                term_months = requestedTermMonths,
            },
            recommendation = new
            {
                recommendation_status = "RECOMPUTED",
                confidence_score = -1.0,
                rationale_summary = rationale,
                key_factors = new List<object>(),
                conditions = new List<string>(),
                policy_hits = new List<object>(),
                agent_enhanced = true,
                recomputed_at = DateTime.UtcNow.ToString("o"),
            },
            verified_dti_pct = verifiedDti,
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
