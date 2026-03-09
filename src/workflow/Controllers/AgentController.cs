using LoanOriginationDemo.Agent;
using LoanOriginationDemo.Models;
using Microsoft.AspNetCore.Mvc;

namespace LoanOriginationDemo.Controllers;

[ApiController]
[Route("api/v1/agent")]
public class AgentController : ControllerBase
{
    private readonly LoanAgentOrchestrator _agent;
    private readonly LoanAgentPlugins _plugins;
    private readonly Services.UnderwritingService _underwriting;
    private readonly ILogger<AgentController> _logger;

    public AgentController(LoanAgentOrchestrator agent, LoanAgentPlugins plugins,
        Services.UnderwritingService underwriting, ILogger<AgentController> logger)
    {
        _agent = agent;
        _plugins = plugins;
        _underwriting = underwriting;
        _logger = logger;
    }

    /// <summary>Run the full S01–S10 agent workflow.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> RunWorkflow([FromBody] Dictionary<string, string> body)
    {
        if (!body.TryGetValue("application_no", out var appNo) || string.IsNullOrEmpty(appNo))
        {
            _logger.LogWarning("RunWorkflow called with missing application_no");
            return BadRequest(new { error_code = "BAD_REQUEST", message = "application_no is required" });
        }
        try
        {
            _logger.LogInformation("API: POST /api/v1/agent/run — application_no={AppNo}", appNo);
            var result = await _agent.RunWorkflowAsync(appNo);
            _logger.LogInformation("API: Workflow completed for {AppNo}, runId={RunId}", appNo, result.RunId);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("API: Application not found: {AppNo} — {Message}", appNo, ex.Message);
            return NotFound(new { error_code = "NOT_FOUND", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "API: Agent service error for {AppNo}: {Message}", appNo, ex.Message);
            return StatusCode(503, new { error_code = "AGENT_UNAVAILABLE", message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Unexpected error for {AppNo}: {Message}", appNo, ex.Message);
            return StatusCode(500, new { error_code = "INTERNAL_ERROR", message = "An unexpected error occurred during workflow execution." });
        }
    }

    /// <summary>Record human reviewer decision.</summary>
    [HttpPost("decision")]
    public async Task<IActionResult> RecordDecision([FromBody] DecisionRequest req)
    {
        _logger.LogInformation("API: POST /api/v1/agent/decision — application_no={AppNo}, decision={Decision}",
            req.ApplicationNo, req.Decision);
        var record = await _agent.RecordDecisionAsync(req);
        return Ok(record);
    }

    /// <summary>Recompute recommendation with adjusted terms.</summary>
    [HttpPost("recompute")]
    public IActionResult Recompute([FromBody] Dictionary<string, System.Text.Json.JsonElement> body)
    {
        var appNo = body.GetValueOrDefault("application_no").ToString();
        _logger.LogInformation("API: POST /api/v1/agent/recompute — application_no={AppNo}", appNo);

        double amount = body.ContainsKey("requested_amount") ? body["requested_amount"].GetDouble() : 0;
        int term = body.ContainsKey("requested_term_months") ? body["requested_term_months"].GetInt32() : 0;
        var loanType = body.ContainsKey("loan_type") ? body["loan_type"].GetString() ?? "" : "";
        var runId = body.ContainsKey("run_id") ? body["run_id"].GetString() ?? "RECOMPUTE" : "RECOMPUTE";

        var app = _plugins.GetApplication(appNo);
        if (app == null)
        {
            _logger.LogWarning("API: Recompute — invalid application_no: {AppNo}", appNo);
            return BadRequest(new { error_code = "BAD_REQUEST", message = "Invalid application_no" });
        }

        if (amount <= 0) amount = app.LoanAmountRequested;
        if (term <= 0) term = app.RequestedTermMonths;
        if (string.IsNullOrEmpty(loanType)) loanType = app.LoanType;

        var quote = _plugins.ComputeQuote(appNo, amount, term, loanType);
        var credit = _plugins.GetCreditProfile(appNo);
        var income = _plugins.GetIncomeVerification(appNo);
        var fraud = _plugins.GetFraudSignals(appNo);

        double verifiedDti = income!.VerifiedMonthlyIncome > 0
            ? Math.Round(app.TotalMonthlyDebtPayments / income.VerifiedMonthlyIncome, 4) : 999;

        var rec = _plugins.EvaluateUnderwriting(runId, appNo, amount, term, loanType,
            income.VerifiedMonthlyIncome, app.TotalMonthlyDebtPayments,
            credit!.BureauScore, fraud!.IdentityRiskScore,
            quote.PaymentToIncomePct, verifiedDti);

        _logger.LogInformation("API: Recompute complete for {AppNo}: recommendation={Status}", appNo, rec.RecommendationStatus);
        return Ok(new { quote, recommendation = rec });
    }
}
