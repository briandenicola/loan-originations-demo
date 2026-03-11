using System.Globalization;
using System.Text.RegularExpressions;
using LoanOriginationDemo.Models;
using UglyToad.PdfPig;

namespace LoanOriginationDemo.Services;

public class PdfParsingService
{
    /// <summary>
    /// Parse a loan application PDF (Central Bank of Asheville format) into a LoanApplication object.
    /// </summary>
    public LoanApplication ParsePdf(Stream pdfStream)
    {
        string fullText;
        using (var document = PdfDocument.Open(pdfStream))
        {
            fullText = string.Join("\n", document.GetPages().Select(p => p.Text));
        }

        var app = new LoanApplication
        {
            ApplicationNo = Extract(fullText, @"Application No\.?:?\s*([A-Z]{2,4}-\d{4}-\d{4,6})"),
            ApplicationDate = NormalizeDate(Extract(fullText, @"Application Date:?\s*(.+?)(?:\n|Application)")),
            ApplicantName = Extract(fullText, @"Full Legal Name\s*(.+?)(?:\n|Date of Birth)"),
            Dob = NormalizeDate(Extract(fullText, @"Date of Birth\s*(\d{2}/\d{2}/\d{4})")),
            SsnLast4 = Extract(fullText, @"SSN.*?(\d{4})\s*(?:\n|Phone)"),
            Phone = Extract(fullText, @"Phone Number\s*(\(\d{3}\)\s*\d{3}-\d{4})"),
            Email = Extract(fullText, @"Email Address\s*([\w.\-]+@[\w.\-]+\.\w+)"),
            CurrentAddress = Extract(fullText, @"Current Address\s*(.+?)(?:\n|City)"),
            CityStateZip = Extract(fullText, @"City\s*/\s*State\s*/\s*ZIP\s*(.+?)(?:\n|Time)"),
            LoanAmountRequested = ParseCurrency(Extract(fullText, @"Loan Amount Requested\s*\$?([\d,]+(?:\.\d{2})?)")),
            LoanPurpose = NormalizePurpose(Extract(fullText, @"Loan Purpose\s*(.+?)(?:\n|Requested Term)")),
            RequestedTermMonths = ParseInt(Extract(fullText, @"Requested Term\s*(\d+)\s*months")),
            LoanType = ExtractLoanType(fullText),
            PaymentMethod = ExtractPaymentMethod(fullText),
            GrossAnnualIncome = ParseCurrency(Extract(fullText, @"Gross Annual Income\s*\$?([\d,]+(?:\.\d{2})?)")),
            MonthlyNetIncome = ParseCurrency(Extract(fullText, @"Monthly Net Income\s*\$?([\d,]+(?:\.\d{2})?)")),
            OtherIncomeMonthly = ParseCurrency(Extract(fullText, @"Other Income \(monthly\)\s*\$?([\d,]+(?:\.\d{2})?)")),
            TotalMonthlyDebtPayments = ParseCurrency(Extract(fullText, @"Total Monthly Debt Payments\s*\$?([\d,]+(?:\.\d{2})?)")),
            HousingStatus = ExtractHousingStatus(fullText),
            HousingPaymentMonthly = ParseCurrency(Extract(fullText, @"Monthly Payment\s*\$?([\d,]+(?:\.\d{2})?)")),
            DeclaredDtiPct = ParsePercent(Extract(fullText, @"Debt-to-Income Ratio\s*([\d.]+)%?")),
            EstimatedSavings = ParseCurrency(Extract(fullText, @"Estimated Total Savings\s*\$?([\d,]+(?:\.\d{2})?)")),
            RetirementInvestments = ParseCurrency(Extract(fullText, @"Retirement.*?Accts?\s*\$?([\d,]+(?:\.\d{2})?)")),
        };

        return app;
    }

    private static string Extract(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static string ExtractLoanType(string text)
    {
        if (Regex.IsMatch(text, @"X\s*Unsecured Personal Loan", RegexOptions.IgnoreCase))
            return "UNSECURED";
        if (Regex.IsMatch(text, @"X\s*Secured Personal Loan", RegexOptions.IgnoreCase))
            return "SECURED";
        if (Regex.IsMatch(text, @"X\s*Line of Credit", RegexOptions.IgnoreCase))
            return "LINE_OF_CREDIT";
        return "UNSECURED";
    }

    private static string ExtractPaymentMethod(string text)
    {
        if (Regex.IsMatch(text, @"X\s*Auto-debit", RegexOptions.IgnoreCase))
            return "AUTO_DEBIT";
        if (Regex.IsMatch(text, @"X\s*Manual monthly", RegexOptions.IgnoreCase))
            return "MANUAL";
        return "AUTO_DEBIT";
    }

    private static string ExtractHousingStatus(string text)
    {
        // Look for the housing status section specifically
        var section = Regex.Match(text, @"Housing Status\s*(.*?)Monthly Payment", RegexOptions.Singleline);
        if (!section.Success) return "RENT";
        var s = section.Groups[1].Value;
        if (Regex.IsMatch(s, @"X\s*Own")) return "OWN";
        if (Regex.IsMatch(s, @"X\s*Rent")) return "RENT";
        return "RENT";
    }

    private static string NormalizePurpose(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return raw.Trim().ToUpperInvariant().Replace(" ", "_");
    }

    private static string NormalizeDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        raw = raw.Trim();
        // Try MM/dd/yyyy
        if (DateTime.TryParseExact(raw, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
            return d1.ToString("yyyy-MM-dd");
        // Try "Month dd, yyyy" (e.g., "February 10, 2026")
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
            return d2.ToString("yyyy-MM-dd");
        return raw;
    }

    private static double ParseCurrency(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        var cleaned = raw.Replace(",", "").Replace("$", "").Trim();
        return double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static int ParseInt(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        return int.TryParse(raw.Trim(), out var v) ? v : 0;
    }

    private static double ParsePercent(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        return double.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v / 100.0 : 0;
    }
}
