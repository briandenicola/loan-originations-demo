using System.Diagnostics;
using Azure.Identity;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Agents.AI.Workflows.Declarative.Events;

namespace LoanOriginationDemo.Agent.Workflow;

/// <summary>
/// Builds and executes the Loan Origination workflow using a declarative YAML definition.
/// 
/// The workflow is defined in LoanOrigination.yaml and executed via DeclarativeWorkflowBuilder.
/// The YAML file defines sequential agent invocations:
///   credit-profile-agent → income-verification-agent → fraud-screening-agent
///   → policy-evaluation-agent → pricing-agent → underwriting-recommendation-agent
///
/// All agents are resolved by name from Azure AI Foundry Agent Service.
/// </summary>
public class LoanWorkflowRunner
{
    private const string WorkflowYamlFile = "Agent/Workflow/LoanOrigination.yaml";
    private static readonly ActivitySource ActivitySource = new("LoanOrigination.Workflow");

    private readonly Uri _foundryEndpoint;
    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;

    public LoanWorkflowRunner(
        Uri foundryEndpoint,
        ILogger logger,
        ILoggerFactory loggerFactory)
    {
        _foundryEndpoint = foundryEndpoint;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Creates a Workflow instance from the declarative YAML definition.
    /// </summary>
    private Microsoft.Agents.AI.Workflows.Workflow CreateWorkflow()
    {
        var agentProvider = new AzureAgentProvider(
            _foundryEndpoint,
            new ChainedTokenCredential(
                new AzureCliCredential(),
                new EnvironmentCredential(),
                new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)));

        var options = new DeclarativeWorkflowOptions(agentProvider)
        {
            LoggerFactory = _loggerFactory,
        };

        var workflowPath = Path.Combine(AppContext.BaseDirectory, WorkflowYamlFile);
        _logger.LogInformation("📄 Loading declarative workflow from: {Path}", workflowPath);

        if (!File.Exists(workflowPath))
        {
            throw new FileNotFoundException(
                $"Workflow YAML file not found at '{workflowPath}'. Ensure LoanOrigination.yaml is copied to output.", workflowPath);
        }

        return DeclarativeWorkflowBuilder.Build<string>(workflowPath, options);
    }

    /// <summary>
    /// Executes the declarative loan origination workflow.
    /// The input is the enriched application data JSON string.
    /// Returns the AI-generated underwriting rationale.
    /// </summary>
    public async Task<WorkflowResult> ExecuteAsync(string enrichedApplicationData)
    {
        using var activity = ActivitySource.StartActivity("ExecuteDeclarativeWorkflow", ActivityKind.Server);
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("🔄 Creating declarative workflow from YAML...");
        var workflow = CreateWorkflow();

        _logger.LogInformation("🚀 Executing declarative workflow via InProcessExecution...");
        var executionSw = Stopwatch.StartNew();

        var rationale = new System.Text.StringBuilder();

        var checkpointManager = CheckpointManager.CreateInMemory();
        await using var run = await InProcessExecution
            .RunStreamingAsync(workflow, enrichedApplicationData, checkpointManager)
            .ConfigureAwait(false);

        await foreach (var evt in run.WatchStreamAsync().ConfigureAwait(false))
        {
            switch (evt)
            {
                case DeclarativeActionInvokedEvent actionInvoked:
                    _logger.LogInformation("[Workflow] ▶ Action: {Id} [{Type}]",
                        actionInvoked.ActionId, actionInvoked.ActionType);
                    break;

                case DeclarativeActionCompletedEvent actionComplete:
                    _logger.LogInformation("[Workflow] ✓ Action completed: {Id} [{Type}]",
                        actionComplete.ActionId, actionComplete.ActionType);
                    break;

                case MessageActivityEvent activityEvent:
                    _logger.LogInformation("[Workflow] 📢 {Message}", activityEvent.Message?.Trim());
                    break;

                case AgentResponseUpdateEvent agentUpdate:
                    var text = agentUpdate.Update?.Text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        rationale.Append(text);
                        _logger.LogTrace("[Workflow] Agent streaming: {Fragment}",
                            text.Length > 80 ? text[..80] + "..." : text);
                    }
                    break;

                case AgentResponseEvent responseEvent:
                    _logger.LogInformation("[Workflow] 💬 Agent response complete. Tokens: {Total} (in={In}, out={Out})",
                        responseEvent.Response?.Usage?.TotalTokenCount,
                        responseEvent.Response?.Usage?.InputTokenCount,
                        responseEvent.Response?.Usage?.OutputTokenCount);
                    break;

                case ExecutorCompletedEvent completedEvt:
                    _logger.LogInformation("[Workflow] ✅ Executor completed: {Id}", completedEvt.ExecutorId);
                    break;

                case ExecutorFailedEvent failedEvt:
                    _logger.LogError("[Workflow] ❌ Executor failed: {Id} — {Error}",
                        failedEvt.ExecutorId, failedEvt.Data?.Message ?? "Unknown");
                    break;

                case WorkflowErrorEvent errorEvt:
                    var ex = errorEvt.Data as Exception;
                    _logger.LogError(ex, "[Workflow] ❌ Workflow error: {Error}", ex?.Message ?? "Unknown");
                    throw ex ?? new InvalidOperationException("Workflow failed with unknown error.");

                case SuperStepCompletedEvent checkpoint:
                    _logger.LogDebug("[Workflow] Checkpoint #{Step} [{Id}]",
                        checkpoint.StepNumber, checkpoint.CompletionInfo?.Checkpoint?.CheckpointId ?? "(none)");
                    break;
            }
        }

        executionSw.Stop();
        sw.Stop();

        var result = rationale.ToString().Trim();
        _logger.LogInformation("✅ Declarative workflow complete in {Duration}ms (total: {TotalDuration}ms). Output: {Len} chars",
            executionSw.ElapsedMilliseconds, sw.ElapsedMilliseconds, result.Length);

        activity?.SetTag("loan.workflow.duration_ms", sw.ElapsedMilliseconds);
        activity?.SetTag("loan.workflow.output_chars", result.Length);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return new WorkflowResult
        {
            Rationale = result.Length > 0 ? result : "No rationale generated by workflow.",
            DurationMs = sw.ElapsedMilliseconds,
        };
    }
}

public class WorkflowResult
{
    public string Rationale { get; set; } = "";
    public long DurationMs { get; set; }
    public string? ThreadId { get; set; }
    public string? FoundryRunId { get; set; }
}
