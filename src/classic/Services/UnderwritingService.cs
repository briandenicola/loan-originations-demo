using LoanOriginationDemo.Models;

namespace LoanOriginationDemo.Services;

/// <summary>
/// Computes loan pricing quotes and underwriting recommendations.
/// </summary>
public class UnderwritingService
{
    private readonly CsvDataService _data;
    public UnderwritingService(CsvDataService data) => _data = data;

    public QuoteResponse ComputeQuote(QuoteRequest req)
    {
        var credit = _data.CreditProfiles.GetValueOrDefault(req.ApplicationNo);
        int score = credit?.BureauScore ?? 650;
        string tier = score >= 740 ? "A" : score >= 680 ? "B" : "C";

        var rule = _data.PricingMatrix
            .Where(p => p.RiskTier == tier && p.LoanType == req.LoanType
                     && p.TermMonths == req.RequestedTermMonths
                     && p.MinAmount <= req.RequestedAmount && req.RequestedAmount <= p.MaxAmount)
            .FirstOrDefault()
            ?? _data.PricingMatrix.FirstOrDefault(p => p.RiskTier == tier && p.LoanType == req.LoanType)
            ?? _data.PricingMatrix.FirstOrDefault(p => p.RiskTier == tier)
            ?? _data.PricingMatrix.First();

        double apr = rule.AprPct;
        double monthlyRate = apr / 100.0 / 12.0;
        int n = req.RequestedTermMonths;
        double payment = monthlyRate > 0
            ? req.RequestedAmount * (monthlyRate * Math.Pow(1 + monthlyRate, n)) / (Math.Pow(1 + monthlyRate, n) - 1)
            : req.RequestedAmount / n;
        payment = Math.Round(payment, 2);
        double total = Math.Round(payment * n, 2);

        var income = _data.IncomeVerifications.GetValueOrDefault(req.ApplicationNo);
        double monthlyIncome = income?.VerifiedMonthlyIncome ?? 5000;
        double pti = Math.Round(payment / monthlyIncome, 4);

        return new QuoteResponse
        {
            ApplicationNo = req.ApplicationNo,
            RiskTier = tier,
            AprPct = apr,
            EstimatedMonthlyPayment = payment,
            TotalRepayableAmount = total,
            PaymentToIncomePct = pti,
            PricingRuleId = rule.PricingRuleId,
        };
    }

    public UnderwritingRecommendation Evaluate(UnderwritingRequest req)
    {
        var fraud = _data.FraudSignals.GetValueOrDefault(req.ApplicationNo);
        var income = _data.IncomeVerifications.GetValueOrDefault(req.ApplicationNo);

        // Build metrics dictionary for policy evaluation
        var metrics = new Dictionary<string, object>
        {
            ["bureau_score"] = req.CreditScore,
            ["verified_dti_pct"] = req.VerifiedDtiPct,
            ["identity_risk_score"] = req.IdentityRiskScore,
            ["loan_amount_requested"] = req.RequestedAmount,
            ["payment_to_income_pct"] = req.PaymentToIncomePct,
            ["declared_dti_pct"] = req.VerifiedDtiPct,
            ["watchlist_hit_flag"] = fraud?.WatchlistHitFlag ?? "N",
            ["income_verification_status"] = income?.VerificationStatus ?? "UNVERIFIED",
        };

        var policyHits = new List<PolicyHit>();
        bool hasDecline = false, hasConditional = false;

        foreach (var rule in _data.PolicyThresholds)
        {
            if (!metrics.TryGetValue(rule.Metric, out var rawVal)) continue;

            bool hit = EvaluateRule(rawVal, rule.Operator, rule.Threshold);
            string outcome = hit && rule.DecisionEffect == "DECLINE" ? "FAIL" : hit ? "WARN" : "PASS";
            if (hit && rule.DecisionEffect == "DECLINE") hasDecline = true;
            if (hit && rule.DecisionEffect == "CONDITIONAL") hasConditional = true;

            policyHits.Add(new PolicyHit { RuleId = rule.RuleId, Outcome = outcome, Message = rule.Description });
        }

        // Key factors
        var factors = new List<RecommendationFactor>();
        if (req.CreditScore >= 740)
            factors.Add(new() { FactorName = "Bureau score", Direction = "positive", ImpactWeight = 0.29, Explanation = "Credit score is above prime threshold." });
        else if (req.CreditScore >= 680)
            factors.Add(new() { FactorName = "Bureau score", Direction = "neutral", ImpactWeight = 0.22, Explanation = "Credit score is in good range but below prime." });
        else
            factors.Add(new() { FactorName = "Bureau score", Direction = "negative", ImpactWeight = 0.30, Explanation = "Credit score is below preferred lending threshold." });

        if (req.VerifiedDtiPct <= 0.30)
            factors.Add(new() { FactorName = "Verified DTI", Direction = "positive", ImpactWeight = 0.27, Explanation = "Debt-to-income is well within policy limits." });
        else if (req.VerifiedDtiPct <= 0.40)
            factors.Add(new() { FactorName = "Verified DTI", Direction = "neutral", ImpactWeight = 0.24, Explanation = "Debt-to-income is within limits but approaching ceiling." });
        else
            factors.Add(new() { FactorName = "Verified DTI", Direction = "negative", ImpactWeight = 0.30, Explanation = "Debt-to-income exceeds policy limit." });

        if (req.IdentityRiskScore <= 0.10)
            factors.Add(new() { FactorName = "Identity risk", Direction = "positive", ImpactWeight = 0.15, Explanation = "Very low identity fraud risk." });
        else if (req.IdentityRiskScore <= 0.20)
            factors.Add(new() { FactorName = "Identity risk", Direction = "neutral", ImpactWeight = 0.12, Explanation = "Identity risk is within acceptable range." });
        else
            factors.Add(new() { FactorName = "Identity risk", Direction = "negative", ImpactWeight = 0.20, Explanation = "Elevated identity risk requires manual review." });

        if (req.PaymentToIncomePct <= 0.10)
            factors.Add(new() { FactorName = "Payment burden", Direction = "positive", ImpactWeight = 0.15, Explanation = "Estimated payment is a small share of income." });
        else if (req.PaymentToIncomePct <= 0.18)
            factors.Add(new() { FactorName = "Payment burden", Direction = "neutral", ImpactWeight = 0.12, Explanation = "Payment burden is moderate." });
        else
            factors.Add(new() { FactorName = "Payment burden", Direction = "negative", ImpactWeight = 0.18, Explanation = "Payment burden is elevated relative to income." });

        if (req.RequestedAmount > 20000)
            factors.Add(new() { FactorName = "Requested amount", Direction = "negative", ImpactWeight = 0.14, Explanation = "Requested amount exceeds high-ticket review threshold." });

        double posWeight = factors.Where(f => f.Direction == "positive").Sum(f => f.ImpactWeight);
        double negWeight = factors.Where(f => f.Direction == "negative").Sum(f => f.ImpactWeight);
        double confidence = Math.Round(Math.Min(0.97, Math.Max(0.35, 0.5 + posWeight - negWeight)), 2);

        string status;
        if (hasDecline) { status = "DECLINE"; confidence = Math.Round(Math.Min(confidence, 0.65), 2); }
        else if (hasConditional) status = "CONDITIONAL";
        else status = "APPROVE";

        var conditions = new List<string>();
        if (hasConditional)
        {
            if (req.IdentityRiskScore > 0.20) conditions.Add("Complete enhanced identity verification before final approval.");
            if (req.RequestedAmount > 20000) conditions.Add("Validate collateral evidence before approval.");
            if (req.PaymentToIncomePct > 0.18) conditions.Add("Confirm applicant capacity for quoted payment.");
            if (income?.VerificationStatus != "VERIFIED") conditions.Add("Obtain additional income documentation for verification.");
            if (conditions.Count == 0) conditions.Add("Reviewer confirmation required before proceeding.");
        }

        var posParts = factors.Where(f => f.Direction == "positive").Select(f => f.Explanation);
        var negParts = factors.Where(f => f.Direction == "negative").Select(f => f.Explanation);
        var rationale = new List<string>();
        if (posParts.Any()) rationale.Add("Strengths: " + string.Join("; ", posParts));
        if (negParts.Any()) rationale.Add("Concerns: " + string.Join("; ", negParts));

        return new UnderwritingRecommendation
        {
            RecommendationStatus = status,
            ConfidenceScore = confidence,
            RationaleSummary = rationale.Any() ? string.Join(" | ", rationale) : "Application meets standard criteria.",
            KeyFactors = factors,
            Conditions = conditions,
            PolicyHits = policyHits,
        };
    }

    private static bool EvaluateRule(object rawVal, string op, string threshold)
    {
        if (rawVal is string sVal)
        {
            return op switch
            {
                "=" => sVal == threshold,
                "!=" => sVal != threshold,
                _ => false
            };
        }
        double val = Convert.ToDouble(rawVal);
        double thresh = double.Parse(threshold, System.Globalization.CultureInfo.InvariantCulture);
        return op switch
        {
            ">" => val > thresh,
            ">=" => val >= thresh,
            "<" => val < thresh,
            "<=" => val <= thresh,
            "=" => Math.Abs(val - thresh) < 0.0001,
            "!=" => Math.Abs(val - thresh) >= 0.0001,
            _ => false
        };
    }
}
