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

    /// <summary>Run the full S01–S10 agent workflow with real-time SSE step updates.</summary>
    [HttpGet("run-stream")]
    public async Task RunWorkflowStream([FromQuery] string application_no)
    {
        if (string.IsNullOrEmpty(application_no))
        {
            Response.StatusCode = 400;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        var writer = Response.BodyWriter;

        async Task SendEvent(string eventType, string data)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes($"event: {eventType}\ndata: {data}\n\n");
            await Response.Body.WriteAsync(bytes);
            await Response.Body.FlushAsync();
        }

        try
        {
            _logger.LogInformation("API: SSE /api/v1/agent/run-stream — application_no={AppNo}", application_no);

            var result = await _agent.RunWorkflowAsync(application_no, (stepId, status, detail) =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new { stepId, status, detail });
                SendEvent("step", json).GetAwaiter().GetResult();
            });

            var resultJson = System.Text.Json.JsonSerializer.Serialize(result,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            await SendEvent("complete", resultJson);

            _logger.LogInformation("API: SSE completed for {AppNo}, runId={RunId}", application_no, result.RunId);
        }
        catch (ArgumentException ex)
        {
            await SendEvent("error", System.Text.Json.JsonSerializer.Serialize(new { error_code = "NOT_FOUND", message = ex.Message }));
        }
        catch (InvalidOperationException ex)
        {
            await SendEvent("error", System.Text.Json.JsonSerializer.Serialize(new { error_code = "AGENT_UNAVAILABLE", message = ex.Message }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: SSE error for {AppNo}: {Message}", application_no, ex.Message);
            await SendEvent("error", System.Text.Json.JsonSerializer.Serialize(new { error_code = "INTERNAL_ERROR", message = "Unexpected error during workflow." }));
        }
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

    /// <summary>Recompute recommendation with adjusted terms via AI agents.</summary>
    [HttpPost("recompute")]
    public async Task<IActionResult> Recompute([FromBody] Dictionary<string, System.Text.Json.JsonElement> body)
    {
        var appNo = body.GetValueOrDefault("application_no").ToString();
        _logger.LogInformation("API: POST /api/v1/agent/recompute — application_no={AppNo}", appNo);

        double amount = body.ContainsKey("requested_amount") ? body["requested_amount"].GetDouble() : 0;
        int term = body.ContainsKey("requested_term_months") ? body["requested_term_months"].GetInt32() : 0;
        var loanType = body.ContainsKey("loan_type") ? body["loan_type"].GetString() ?? "" : "";
        var runId = body.ContainsKey("run_id") ? body["run_id"].GetString() ?? "RECOMPUTE" : "RECOMPUTE";

        try
        {
            var result = await _agent.RecomputeWithAgentAsync(appNo!, amount, term, loanType, runId);
            _logger.LogInformation("API: Recompute complete for {AppNo}", appNo);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("API: Recompute — invalid application_no: {AppNo} — {Message}", appNo, ex.Message);
            return BadRequest(new { error_code = "BAD_REQUEST", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "API: Recompute agent error for {AppNo}: {Message}", appNo, ex.Message);
            return StatusCode(503, new { error_code = "AGENT_UNAVAILABLE", message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Recompute unexpected error for {AppNo}: {Message}", appNo, ex.Message);
            return StatusCode(500, new { error_code = "INTERNAL_ERROR", message = "Recompute failed." });
        }
    }
}
