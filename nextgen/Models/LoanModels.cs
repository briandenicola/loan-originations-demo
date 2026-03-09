namespace LoanOriginationDemo.Models;

// ── Application ──
public class LoanApplication
{
    public string ApplicationNo { get; set; } = "";
    public string ApplicationDate { get; set; } = "";
    public string ApplicantName { get; set; } = "";
    public string Dob { get; set; } = "";
    public string SsnLast4 { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string CurrentAddress { get; set; } = "";
    public string CityStateZip { get; set; } = "";
    public double LoanAmountRequested { get; set; }
    public string LoanPurpose { get; set; } = "";
    public int RequestedTermMonths { get; set; }
    public string LoanType { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public double GrossAnnualIncome { get; set; }
    public double MonthlyNetIncome { get; set; }
    public double OtherIncomeMonthly { get; set; }
    public double TotalMonthlyDebtPayments { get; set; }
    public string HousingStatus { get; set; } = "";
    public double HousingPaymentMonthly { get; set; }
    public double DeclaredDtiPct { get; set; }
    public double EstimatedSavings { get; set; }
    public double RetirementInvestments { get; set; }
}

// ── Credit Profile ──
public class CreditProfile
{
    public string ApplicationNo { get; set; } = "";
    public int BureauScore { get; set; }
    public string ScoreBand { get; set; } = "";
    public int Delinquencies24m { get; set; }
    public double UtilizationPct { get; set; }
    public int HardInquiries6m { get; set; }
    public string BankruptcyFlag { get; set; } = "N";
    public int OldestTradeLineMonths { get; set; }
    public int TotalOpenTradelines { get; set; }
}

// ── Income Verification ──
public class IncomeVerification
{
    public string ApplicationNo { get; set; } = "";
    public double VerifiedMonthlyIncome { get; set; }
    public string VerificationStatus { get; set; } = "";
    public double EmployerMatchPct { get; set; }
    public int PayrollRecordsMonths { get; set; }
    public double IncomeVariancePct { get; set; }
}

// ── Fraud Signals ──
public class FraudSignals
{
    public string ApplicationNo { get; set; } = "";
    public double IdentityRiskScore { get; set; }
    public double DeviceRiskScore { get; set; }
    public string AddressMismatchFlag { get; set; } = "N";
    public string SyntheticIdFlag { get; set; } = "N";
    public string WatchlistHitFlag { get; set; } = "N";
    public string RecommendedManualReview { get; set; } = "N";
}

// ── Policy Threshold ──
public class PolicyThreshold
{
    public string RuleId { get; set; } = "";
    public string Metric { get; set; } = "";
    public string Operator { get; set; } = "";
    public string Threshold { get; set; } = "";
    public string Severity { get; set; } = "";
    public string DecisionEffect { get; set; } = "";
    public string Description { get; set; } = "";
}

// ── Product Pricing ──
public class ProductPricing
{
    public string RiskTier { get; set; } = "";
    public string LoanType { get; set; } = "";
    public int TermMonths { get; set; }
    public double MinAmount { get; set; }
    public double MaxAmount { get; set; }
    public int MinCreditScore { get; set; }
    public double MaxDtiPct { get; set; }
    public double AprPct { get; set; }
    public string PricingRuleId { get; set; } = "";
}

// ── Quote ──
public class QuoteRequest
{
    public string ApplicationNo { get; set; } = "";
    public double RequestedAmount { get; set; }
    public int RequestedTermMonths { get; set; }
    public string LoanType { get; set; } = "";
    public string PaymentMethod { get; set; } = "AUTO_DEBIT";
}

public class QuoteResponse
{
    public string ApplicationNo { get; set; } = "";
    public string RiskTier { get; set; } = "";
    public double AprPct { get; set; }
    public double EstimatedMonthlyPayment { get; set; }
    public double TotalRepayableAmount { get; set; }
    public double PaymentToIncomePct { get; set; }
    public string PricingRuleId { get; set; } = "";
}

// ── Underwriting ──
public class UnderwritingRequest
{
    public string RunId { get; set; } = "";
    public string ApplicationNo { get; set; } = "";
    public double RequestedAmount { get; set; }
    public int RequestedTermMonths { get; set; }
    public string LoanType { get; set; } = "";
    public double MonthlyIncome { get; set; }
    public double MonthlyDebtPayments { get; set; }
    public int CreditScore { get; set; }
    public double IdentityRiskScore { get; set; }
    public double PaymentToIncomePct { get; set; }
    public double VerifiedDtiPct { get; set; }
    public List<string> PolicyOverrides { get; set; } = new();
}

public class RecommendationFactor
{
    public string FactorName { get; set; } = "";
    public string Direction { get; set; } = "";
    public double ImpactWeight { get; set; }
    public string Explanation { get; set; } = "";
}

public class PolicyHit
{
    public string RuleId { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Message { get; set; } = "";
}

public class UnderwritingRecommendation
{
    public string RecommendationStatus { get; set; } = "";
    public double ConfidenceScore { get; set; }
    public string RationaleSummary { get; set; } = "";
    public List<RecommendationFactor> KeyFactors { get; set; } = new();
    public List<string> Conditions { get; set; } = new();
    public List<PolicyHit> PolicyHits { get; set; } = new();
}

// ── Workflow ──
public class WorkflowStep
{
    public string StepId { get; set; } = "";
    public string StepName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string? Detail { get; set; }
}

// ── Decision ──
public class DecisionRequest
{
    public string RunId { get; set; } = "";
    public string ApplicationNo { get; set; } = "";
    public string ReviewerId { get; set; } = "reviewer-001";
    public string Decision { get; set; } = "";
    public double? AdjustedAmount { get; set; }
    public int? AdjustedTermMonths { get; set; }
    public double? AdjustedRate { get; set; }
    public string Notes { get; set; } = "";
    public object? RecommendationSnapshot { get; set; }
}

// ── Agent Run Response ──
public class AgentRunResponse
{
    public string RunId { get; set; } = "";
    public string ApplicationNo { get; set; } = "";
    public object? Prepared { get; set; }
    public object? WorkflowLog { get; set; }
    public object? Recommendation { get; set; }
}
