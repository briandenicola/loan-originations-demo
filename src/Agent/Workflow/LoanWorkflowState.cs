using LoanOriginationDemo.Models;

namespace LoanOriginationDemo.Agent.Workflow;

/// <summary>
/// Shared state that flows through the loan origination workflow.
/// Executors populate data, agents add AI analysis.
/// </summary>
public class LoanWorkflowState
{
    public string RunId { get; set; } = "";
    public string ApplicationNo { get; set; } = "";
    public LoanApplication Application { get; set; } = null!;
    public CreditProfile? CreditProfile { get; set; }
    public IncomeVerification? IncomeVerification { get; set; }
    public FraudSignals? FraudSignals { get; set; }
    public List<PolicyThreshold> PolicyThresholds { get; set; } = [];
    public QuoteResponse? Quote { get; set; }
    public double VerifiedDti { get; set; }
    public UnderwritingRecommendation? Recommendation { get; set; }

    // AI analysis results from agents
    public string? CreditAnalysis { get; set; }
    public string? IncomeAnalysis { get; set; }
    public string? FraudAnalysis { get; set; }
    public string? PolicyAnalysis { get; set; }
    public string? PricingAnalysis { get; set; }
    public string? UnderwritingRationale { get; set; }

    // Foundry metadata
    public string? ThreadId { get; set; }
    public string? FoundryRunId { get; set; }

    public List<WorkflowStep> Steps { get; set; } = [];

    public void LogStep(string id, string name, string status, string? detail = null)
    {
        Steps.Add(new WorkflowStep
        {
            StepId = id,
            StepName = name,
            Status = status,
            Timestamp = DateTime.UtcNow.ToString("o"),
            Detail = detail
        });
    }
}
