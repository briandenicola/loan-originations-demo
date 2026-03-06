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

    public AgentController(LoanAgentOrchestrator agent, LoanAgentPlugins plugins, Services.UnderwritingService underwriting)
    {
        _agent = agent;
        _plugins = plugins;
        _underwriting = underwriting;
    }

    /// <summary>Run the full S01–S10 agent workflow.</summary>
    [HttpPost("run")]
    public async Task<IActionResult> RunWorkflow([FromBody] Dictionary<string, string> body)
    {
        if (!body.TryGetValue("application_no", out var appNo) || string.IsNullOrEmpty(appNo))
            return BadRequest(new { error_code = "BAD_REQUEST", message = "application_no is required" });
        try
        {
            var result = await _agent.RunWorkflowAsync(appNo);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error_code = "NOT_FOUND", message = ex.Message });
        }
    }

    /// <summary>Record human reviewer decision.</summary>
    [HttpPost("decision")]
    public async Task<IActionResult> RecordDecision([FromBody] DecisionRequest req)
    {
        var record = await _agent.RecordDecisionAsync(req);
        return Ok(record);
    }

    /// <summary>Recompute recommendation with adjusted terms.</summary>
    [HttpPost("recompute")]
    public IActionResult Recompute([FromBody] Dictionary<string, object> body)
    {
        var appNo = body.GetValueOrDefault("application_no")?.ToString() ?? "";
        var amount = body.ContainsKey("requested_amount") ? Convert.ToDouble(body["requested_amount"]) : 0;
        var term = body.ContainsKey("requested_term_months") ? Convert.ToInt32(body["requested_term_months"].ToString()) : 0;
        var loanType = body.GetValueOrDefault("loan_type")?.ToString() ?? "";
        var runId = body.GetValueOrDefault("run_id")?.ToString() ?? "RECOMPUTE";

        var app = _plugins.GetApplication(appNo);
        if (app == null) return BadRequest(new { error_code = "BAD_REQUEST", message = "Invalid application_no" });

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

        return Ok(new { quote, recommendation = rec });
    }
}
