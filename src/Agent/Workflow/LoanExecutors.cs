using System.Diagnostics;
using System.Text.Json;
using LoanOriginationDemo.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace LoanOriginationDemo.Agent.Workflow;

// ══════════════════════════════════════════════════════════════════════════════
// Custom Executors for the Loan Origination Workflow
//
// Executors handle deterministic data operations (plugin calls, computations).
// Agents handle AI-powered analysis (LLM inference via Foundry).
// The WorkflowBuilder wires them into a graph with edges and fan-out/fan-in.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// S01-S02: Intake — loads application data and gathers all enrichment data.
/// Outputs a formatted context string for the downstream agents.
/// </summary>
internal sealed class IntakeExecutor(LoanAgentPlugins plugins, ILogger logger)
    : Executor<string, string>("IntakeExecutor")
{
    public override ValueTask<string> HandleAsync(string applicationNo, IWorkflowContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("[IntakeExecutor] Processing application {AppNo}", applicationNo);

        var app = plugins.GetApplication(applicationNo)
            ?? throw new ArgumentException($"Application {applicationNo} not found");

        var credit = plugins.GetCreditProfile(applicationNo);
        var income = plugins.GetIncomeVerification(applicationNo);
        var fraud = plugins.GetFraudSignals(applicationNo);
        var thresholds = plugins.GetPolicyThresholds();
        var quote = plugins.ComputeQuote(applicationNo, app.LoanAmountRequested, app.RequestedTermMonths, app.LoanType);

        double verifiedDti = income?.VerifiedMonthlyIncome > 0
            ? Math.Round(app.TotalMonthlyDebtPayments / income.VerifiedMonthlyIncome, 4) : 999;

        var rec = plugins.EvaluateUnderwriting(
            $"RUN-WF", applicationNo, app.LoanAmountRequested, app.RequestedTermMonths,
            app.LoanType, income?.VerifiedMonthlyIncome ?? 0, app.TotalMonthlyDebtPayments,
            credit?.BureauScore ?? 0, fraud?.IdentityRiskScore ?? 1.0,
            quote.PaymentToIncomePct, verifiedDti);

        // Store state for later use by the result executor
        var state = new LoanWorkflowState
        {
            ApplicationNo = applicationNo,
            Application = app,
            CreditProfile = credit,
            IncomeVerification = income,
            FraudSignals = fraud,
            PolicyThresholds = thresholds,
            Quote = quote,
            VerifiedDti = verifiedDti,
            Recommendation = rec,
        };

        state.LogStep("S01", "Application Intake", "COMPLETE", $"Application {applicationNo} received");
        state.LogStep("S02", "Data Enrichment", "COMPLETE", "All enrichment APIs called");
        state.LogStep("S03", "Credit Profile", "COMPLETE", $"Bureau score: {credit?.BureauScore} ({credit?.ScoreBand})");
        state.LogStep("S04", "Income Verification", "COMPLETE", $"Status: {income?.VerificationStatus}, Verified: ${income?.VerifiedMonthlyIncome}/mo");
        state.LogStep("S05", "Fraud Screening", "COMPLETE", $"Identity risk: {fraud?.IdentityRiskScore}, Manual review: {fraud?.RecommendedManualReview}");
        state.LogStep("S06", "Policy Evaluation", "COMPLETE", $"{thresholds.Count} rules evaluated");
        state.LogStep("S07", "DTI & Affordability", "COMPLETE", $"Verified DTI: {verifiedDti:P1}");
        state.LogStep("S08", "Pricing", "COMPLETE", $"APR: {quote.AprPct}%, Payment: ${quote.EstimatedMonthlyPayment}/mo");

        // Serialize the full context as the workflow message for downstream agents
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

        // Store state as workflow variable for the result executor
        // (passed through the message flow rather than workflow state)

        sw.Stop();
        logger.LogInformation("[IntakeExecutor] Complete in {Duration}ms — {ContextSize} chars of enriched context",
            sw.ElapsedMilliseconds, enrichedContext.Length);

        return ValueTask.FromResult(enrichedContext);
    }
}

/// <summary>
/// Bridge executor: converts the string output from IntakeExecutor into ChatMessages 
/// and a TurnToken so the downstream AI agent can process it.
/// </summary>
internal sealed class AgentBridgeExecutor(string id, string agentPromptPrefix, ILogger logger)
    : Executor<string>(id)
{
    public override async ValueTask HandleAsync(string enrichedContext, IWorkflowContext context, CancellationToken ct = default)
    {
        logger.LogInformation("[{Id}] Bridging {Size} chars of context to AI agent", Id, enrichedContext.Length);

        var prompt = $"{agentPromptPrefix}\n\nENRICHED APPLICATION DATA:\n{enrichedContext}";
        await context.SendMessageAsync(new ChatMessage(ChatRole.User, prompt), cancellationToken: ct);
        await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: ct);
    }
}

/// <summary>
/// Collects the AI agent's response messages and forwards them as a string
/// to the next stage in the workflow.
/// </summary>
internal sealed class AgentResponseCollector(string id, string stepName, ILogger logger)
    : Executor<List<ChatMessage>, string>(id)
{
    public override ValueTask<string> HandleAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken ct = default)
    {
        var response = string.Join("\n", messages.Select(m => m.Text?.Trim() ?? "")).Trim();
        logger.LogInformation("[{Id}] {StepName} agent response: {Length} chars", Id, stepName, response.Length);
        logger.LogDebug("[{Id}] Response preview: {Preview}...", Id, response.Length > 200 ? response[..200] : response);

        return ValueTask.FromResult(response);
    }
}

/// <summary>
/// Fan-in executor that collects concurrent agent responses and combines them
/// with a prompt for the underwriting agent.
/// </summary>
internal sealed class AnalysisAggregationExecutor(ILogger logger)
    : Executor<List<ChatMessage>>("AnalysisAggregation")
{
    private readonly List<string> _analyses = [];
    private int _expectedCount;
    private string? _originalContext;

    public void SetExpectedCount(int count) => _expectedCount = count;
    public void SetOriginalContext(string context) => _originalContext = context;

    public override async ValueTask HandleAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken ct = default)
    {
        var text = string.Join("\n", messages.Select(m => m.Text?.Trim() ?? "")).Trim();
        lock (_analyses)
        {
            _analyses.Add(text);
            logger.LogInformation("[AnalysisAggregation] Collected {Count}/{Expected} agent analyses",
                _analyses.Count, _expectedCount);
        }

        if (_analyses.Count >= _expectedCount)
        {
            var combined = string.Join("\n\n---\n\n", _analyses);
            logger.LogInformation("[AnalysisAggregation] All {Count} analyses collected, forwarding to underwriting",
                _analyses.Count);

            var underwritingPrompt =
                $"You have received the following specialist agent analyses for a loan application.\n\n" +
                $"SPECIALIST ANALYSES:\n{combined}\n\n" +
                $"Based on ALL analyses above, produce your final underwriting recommendation.";

            await context.SendMessageAsync(new ChatMessage(ChatRole.User, underwritingPrompt), cancellationToken: ct);
            await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: ct);
        }
    }
}

/// <summary>
/// Final executor: collects the underwriting agent response and yields the workflow output.
/// </summary>
internal sealed class WorkflowOutputExecutor(ILogger logger)
    : Executor<List<ChatMessage>, string>("WorkflowOutput")
{
    public override async ValueTask<string> HandleAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken ct = default)
    {
        var rationale = string.Join("\n", messages.Select(m => m.Text?.Trim() ?? "")).Trim();
        logger.LogInformation("[WorkflowOutput] Final underwriting rationale: {Length} chars", rationale.Length);

        await context.YieldOutputAsync(rationale, ct);
        return rationale;
    }
}
