using LoanOriginationDemo.Models;
using LoanOriginationDemo.Services;
using Microsoft.AspNetCore.Mvc;

namespace LoanOriginationDemo.Controllers;

[ApiController]
[Route("api/v1")]
public class LoanApiController : ControllerBase
{
    private readonly CsvDataService _data;
    private readonly UnderwritingService _underwriting;
    private readonly PdfParsingService _pdfParser;

    public LoanApiController(CsvDataService data, UnderwritingService underwriting, PdfParsingService pdfParser)
    {
        _data = data;
        _underwriting = underwriting;
        _pdfParser = pdfParser;
    }

    // ── Application endpoints ──
    [HttpGet("applications")]
    public IActionResult ListApplications()
    {
        var apps = _data.Applications.Values.Select(a => new
        {
            a.ApplicationNo,
            a.ApplicantName,
            a.LoanAmountRequested,
            a.LoanPurpose,
            a.ApplicationDate,
            a.RequestedTermMonths,
            a.LoanType,
        });
        return Ok(new { applications = apps });
    }

    [HttpGet("applications/{applicationNo}")]
    public IActionResult GetApplication(string applicationNo)
    {
        if (!_data.Applications.TryGetValue(applicationNo, out var app))
            return NotFound(new { error_code = "NOT_FOUND", message = "Application not found" });
        return Ok(app);
    }

    [HttpPost("applications/upload")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadApplication(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error_code = "NO_FILE", message = "No file uploaded" });

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error_code = "INVALID_FORMAT", message = "Only PDF files are accepted" });

        using var stream = file.OpenReadStream();
        var app = _pdfParser.ParsePdf(stream);

        if (string.IsNullOrWhiteSpace(app.ApplicationNo))
            app.ApplicationNo = $"UPL-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(10000, 99999)}";

        _data.Applications[app.ApplicationNo] = app;

        return Ok(app);
    }

    // ── Enrichment endpoints ──
    [HttpGet("credit-profile")]
    public IActionResult GetCreditProfile([FromQuery(Name = "application_no")] string applicationNo)
    {
        if (!_data.CreditProfiles.TryGetValue(applicationNo, out var cp))
            return NotFound(new { error_code = "NOT_FOUND", message = $"No credit profile for {applicationNo}" });
        return Ok(cp);
    }

    [HttpGet("income-verification")]
    public IActionResult GetIncomeVerification([FromQuery(Name = "application_no")] string applicationNo)
    {
        if (!_data.IncomeVerifications.TryGetValue(applicationNo, out var iv))
            return NotFound(new { error_code = "NOT_FOUND", message = $"No income data for {applicationNo}" });
        return Ok(iv);
    }

    [HttpGet("fraud-signals")]
    public IActionResult GetFraudSignals([FromQuery(Name = "application_no")] string applicationNo)
    {
        if (!_data.FraudSignals.TryGetValue(applicationNo, out var fs))
            return NotFound(new { error_code = "NOT_FOUND", message = $"No fraud data for {applicationNo}" });
        return Ok(fs);
    }

    // ── Reference endpoints ──
    [HttpGet("policy/thresholds")]
    public IActionResult GetPolicyThresholds()
        => Ok(new { rules = _data.PolicyThresholds });

    [HttpGet("reference/loan-purposes")]
    public IActionResult GetLoanPurposes()
        => Ok(new { purposes = new[] { "HOME_IMPROVEMENT", "DEBT_CONSOLIDATION", "MEDICAL_EXPENSES", "VEHICLE_PURCHASE", "EDUCATION", "OTHER" } });

    // ── Decisioning endpoints ──
    [HttpPost("loan-products/quote")]
    public IActionResult GetQuote([FromBody] QuoteRequest req)
        => Ok(_underwriting.ComputeQuote(req));

    [HttpPost("underwriting/recommendation")]
    public IActionResult GetRecommendation([FromBody] UnderwritingRequest req)
        => Ok(_underwriting.Evaluate(req));
}
