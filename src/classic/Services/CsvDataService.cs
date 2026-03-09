using System.Globalization;
using LoanOriginationDemo.Models;

namespace LoanOriginationDemo.Services;

/// <summary>
/// Loads and serves CSV data for all mock API endpoints.
/// </summary>
public class CsvDataService
{
    private readonly string _dataDir;

    public Dictionary<string, LoanApplication> Applications { get; } = new();
    public Dictionary<string, CreditProfile> CreditProfiles { get; } = new();
    public Dictionary<string, IncomeVerification> IncomeVerifications { get; } = new();
    public Dictionary<string, FraudSignals> FraudSignals { get; } = new();
    public List<ProductPricing> PricingMatrix { get; } = new();
    public List<PolicyThreshold> PolicyThresholds { get; } = new();

    public CsvDataService(IConfiguration config)
    {
        _dataDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "materials", "data");
        LoadAll();
    }

    private void LoadAll()
    {
        foreach (var row in ReadCsv("loan_application_register.csv"))
        {
            var app = new LoanApplication
            {
                ApplicationNo = row["application_no"],
                ApplicationDate = row["application_date"],
                ApplicantName = row["applicant_name"],
                Dob = row["dob"],
                SsnLast4 = row["ssn_last4"],
                Phone = row["phone"],
                Email = row["email"],
                CurrentAddress = row["current_address"],
                CityStateZip = row["city_state_zip"],
                LoanAmountRequested = Dbl(row["loan_amount_requested"]),
                LoanPurpose = row["loan_purpose"],
                RequestedTermMonths = Int(row["requested_term_months"]),
                LoanType = row["loan_type"],
                PaymentMethod = row["payment_method"],
                GrossAnnualIncome = Dbl(row["gross_annual_income"]),
                MonthlyNetIncome = Dbl(row["monthly_net_income"]),
                OtherIncomeMonthly = Dbl(row["other_income_monthly"]),
                TotalMonthlyDebtPayments = Dbl(row["total_monthly_debt_payments"]),
                HousingStatus = row["housing_status"],
                HousingPaymentMonthly = Dbl(row["housing_payment_monthly"]),
                DeclaredDtiPct = Dbl(row["declared_dti_pct"]),
                EstimatedSavings = Dbl(row["estimated_savings"]),
                RetirementInvestments = Dbl(row["retirement_investments"]),
            };
            Applications[app.ApplicationNo] = app;
        }

        foreach (var row in ReadCsv("credit_bureau_extract.csv"))
        {
            CreditProfiles[row["application_no"]] = new CreditProfile
            {
                ApplicationNo = row["application_no"],
                BureauScore = Int(row["bureau_score"]),
                ScoreBand = row["score_band"],
                Delinquencies24m = Int(row["delinquencies_24m"]),
                UtilizationPct = Dbl(row["utilization_pct"]),
                HardInquiries6m = Int(row["hard_inquiries_6m"]),
                BankruptcyFlag = row["bankruptcy_flag"],
                OldestTradeLineMonths = Int(row["oldest_trade_line_months"]),
                TotalOpenTradelines = Int(row["total_open_tradelines"]),
            };
        }

        foreach (var row in ReadCsv("income_verification_extract.csv"))
        {
            IncomeVerifications[row["application_no"]] = new IncomeVerification
            {
                ApplicationNo = row["application_no"],
                VerifiedMonthlyIncome = Dbl(row["verified_monthly_income"]),
                VerificationStatus = row["verification_status"],
                EmployerMatchPct = Dbl(row["employer_match_pct"]),
                PayrollRecordsMonths = Int(row["payroll_records_months"]),
                IncomeVariancePct = Dbl(row["income_variance_pct"]),
            };
        }

        foreach (var row in ReadCsv("fraud_screening_extract.csv"))
        {
            FraudSignals[row["application_no"]] = new FraudSignals
            {
                ApplicationNo = row["application_no"],
                IdentityRiskScore = Dbl(row["identity_risk_score"]),
                DeviceRiskScore = Dbl(row["device_risk_score"]),
                AddressMismatchFlag = row["address_mismatch_flag"],
                SyntheticIdFlag = row["synthetic_id_flag"],
                WatchlistHitFlag = row["watchlist_hit_flag"],
                RecommendedManualReview = row["recommended_manual_review"],
            };
        }

        foreach (var row in ReadCsv("product_pricing_matrix.csv"))
        {
            PricingMatrix.Add(new ProductPricing
            {
                RiskTier = row["risk_tier"],
                LoanType = row["loan_type"],
                TermMonths = Int(row["term_months"]),
                MinAmount = Dbl(row["min_amount"]),
                MaxAmount = Dbl(row["max_amount"]),
                MinCreditScore = Int(row["min_credit_score"]),
                MaxDtiPct = Dbl(row["max_dti_pct"]),
                AprPct = Dbl(row["apr_pct"]),
                PricingRuleId = row["pricing_rule_id"],
            });
        }

        foreach (var row in ReadCsv("policy_thresholds.csv"))
        {
            PolicyThresholds.Add(new PolicyThreshold
            {
                RuleId = row["rule_id"],
                Metric = row["metric"],
                Operator = row["operator"],
                Threshold = row["threshold"],
                Severity = row["severity"],
                DecisionEffect = row["decision_effect"],
                Description = row["description"],
            });
        }
    }

    private List<Dictionary<string, string>> ReadCsv(string filename)
    {
        var path = Path.Combine(_dataDir, filename);
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) return new();
        var headers = lines[0].Split(',');
        var results = new List<Dictionary<string, string>>();
        for (int i = 1; i < lines.Length; i++)
        {
            var values = ParseCsvLine(lines[i]);
            var dict = new Dictionary<string, string>();
            for (int j = 0; j < headers.Length && j < values.Count; j++)
                dict[headers[j].Trim()] = values[j].Trim();
            results.Add(dict);
        }
        return results;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();
        foreach (char c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); continue; }
            current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }

    private static double Dbl(string s) => double.TryParse(s, CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static int Int(string s) => int.TryParse(s, out var v) ? v : 0;
}
