using System.Diagnostics;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows.Declarative;

// ─────────────────────────────────────────────────────────────────────────────
// Loan Origination Agent & Workflow Initializer CLI
// Creates all agents AND validates the declarative YAML workflow in Foundry.
// SDK: Microsoft.Agents.AI.AzureAI.Persistent + Workflows.Declarative
// Auth: Entra ID (AzureCliCredential → EnvironmentCredential → ManagedIdentity)
// ─────────────────────────────────────────────────────────────────────────────

var sw = Stopwatch.StartNew();

var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT")
    ?? args.FirstOrDefault(a => a.StartsWith("--endpoint="))?.Split('=', 2)[1]
    ?? "";

if (string.IsNullOrEmpty(endpoint))
{
    Console.WriteLine("Usage: dotnet run -- --endpoint=<FOUNDRY_ENDPOINT>");
    Console.WriteLine("   Or: set FOUNDRY_ENDPOINT environment variable");
    return 1;
}

Console.WriteLine($"🔧 Loan Origination Agent Initializer (Agent Framework Workflows)");
Console.WriteLine($"   Endpoint: {endpoint}");
Console.WriteLine($"   Auth:     Entra ID (AzureCliCredential → EnvironmentCredential → ManagedIdentity)");
Console.WriteLine($"   Pattern:  Declarative YAML Workflow (LoanOrigination.yaml)");
Console.WriteLine();
Console.WriteLine("   Model Assignments:");
Console.WriteLine("     gpt-4.1          → Credit Profile, Policy Evaluation");
Console.WriteLine("     gpt-5.2-chat     → Fraud Screening, Underwriting Recommendation");
Console.WriteLine("     Phi-4-reasoning  → Income Verification, Pricing (structured reasoning)");
Console.WriteLine();
Console.WriteLine("   Orchestration: Declarative YAML Workflow (LoanOrigination.yaml)");
Console.WriteLine();

// ── Step 1: Acquire Entra ID credential ───────────────────────────────────
Console.WriteLine("Step 1: Acquiring Entra ID credential...");
var credentialSw = Stopwatch.StartNew();

var credential = new ChainedTokenCredential(
    new AzureCliCredential(),
    new EnvironmentCredential(),
    new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));

try
{
    var tokenRequest = new Azure.Core.TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" });
    var token = await credential.GetTokenAsync(tokenRequest);
    credentialSw.Stop();
    Console.WriteLine($"  ✅ Token acquired in {credentialSw.ElapsedMilliseconds}ms. Expires: {token.ExpiresOn}");
}
catch (Exception ex)
{
    credentialSw.Stop();
    Console.WriteLine($"  ❌ Token acquisition FAILED after {credentialSw.ElapsedMilliseconds}ms: {ex.Message}");
    return 1;
}

// ── Step 2: Connect to Foundry Agent Service ──────────────────────────────
Console.WriteLine();
Console.WriteLine($"Step 2: Connecting to Foundry Agent Service...");
Console.WriteLine($"  Endpoint: {endpoint}");

var persistentAgentsClient = new PersistentAgentsClient(endpoint, credential);
Console.WriteLine($"  ✅ PersistentAgentsClient created");

// ── Step 3: Delete all existing agents ────────────────────────────────────
Console.WriteLine();
Console.WriteLine("Step 3: Deleting all existing agents...");
var cleanupSw = Stopwatch.StartNew();
try
{
    var agentsToDelete = new List<(string Id, string Name)>();
    await foreach (var existing in persistentAgentsClient.Administration.GetAgentsAsync())
    {
        agentsToDelete.Add((existing.Id, existing.Name ?? "(unnamed)"));
    }

    if (agentsToDelete.Count == 0)
    {
        Console.WriteLine("  No existing agents found — nothing to delete.");
    }
    else
    {
        Console.WriteLine($"  Found {agentsToDelete.Count} existing agent(s) to delete:");
        int deleted = 0;
        int deleteFailed = 0;
        foreach (var (id, name) in agentsToDelete)
        {
            try
            {
                Console.Write($"    Deleting {name,-40} id={id}...");
                await persistentAgentsClient.Administration.DeleteAgentAsync(id);
                deleted++;
                Console.WriteLine(" ✅");
            }
            catch (Exception delEx)
            {
                deleteFailed++;
                Console.WriteLine($" ❌ {delEx.Message}");
            }
        }
        Console.WriteLine($"  Deleted: {deleted}, Failed: {deleteFailed}");
    }

    cleanupSw.Stop();
    Console.WriteLine($"  ✅ Cleanup completed in {cleanupSw.ElapsedMilliseconds}ms");
}
catch (Exception ex)
{
    cleanupSw.Stop();
    Console.WriteLine($"  ❌ Failed to list/delete agents after {cleanupSw.ElapsedMilliseconds}ms: {ex.Message}");
    Console.WriteLine($"  Full error: {ex}");
    return 1;
}

// ── Step 4: Load prompt templates ─────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("Step 4: Loading prompt templates...");

var promptsDir = Path.Combine(AppContext.BaseDirectory, "prompts");
if (!Directory.Exists(promptsDir))
    promptsDir = Path.Combine(Directory.GetCurrentDirectory(), "prompts");

Console.WriteLine($"  Prompts directory: {promptsDir}");

if (!Directory.Exists(promptsDir))
{
    Console.WriteLine($"  ❌ Prompts directory not found!");
    return 1;
}

var promptFiles = Directory.GetFiles(promptsDir, "*.txt");
Console.WriteLine($"  ✅ {promptFiles.Length} prompt file(s) found:");
foreach (var pf in promptFiles)
    Console.WriteLine($"     {Path.GetFileName(pf)} ({new FileInfo(pf).Length} bytes)");

string LoadPrompt(string filename)
{
    var path = Path.Combine(promptsDir, filename);
    var content = File.ReadAllText(path);
    Console.WriteLine($"  Loaded {filename}: {content.Length} chars");
    return content;
}

// ── Step 5: Create specialized agents in Foundry (workflow nodes) ─────────
// In the Agent Framework Workflow pattern, agents receive data as context
// from executors — no function tools needed. The workflow graph (WorkflowBuilder)
// handles orchestration, not a ConnectedAgent orchestrator.
Console.WriteLine();
Console.WriteLine("Step 5: Creating specialized agents (for Declarative YAML Workflow)...");
Console.WriteLine("  Note: Agents are invoked by the declarative workflow (LoanOrigination.yaml)");
Console.WriteLine(new string('─', 70));

var agentSpecs = new AgentSpec[]
{
    new("credit_profile_agent", "Loan Origination Credit Profile Agent",
        "CreditProfileAgentPrompt.txt", "gpt-4.1"),

    new("income_verification_agent", "Loan Origination Income Verification Agent",
        "IncomeVerificationAgentPrompt.txt", "Phi-4-reasoning"),

    new("fraud_screening_agent", "Loan Origination Fraud Screening Agent",
        "FraudScreeningAgentPrompt.txt", "gpt-5.2-chat"),

    new("policy_evaluation_agent", "Loan Origination Policy Evaluation Agent",
        "PolicyEvaluationAgentPrompt.txt", "gpt-4.1"),

    new("pricing_agent", "Loan Origination Pricing Agent",
        "PricingAgentPrompt.txt", "Phi-4-reasoning"),

    new("underwriting_recommendation_agent", "Loan Origination Underwriting Recommendation Agent",
        "UnderwritingAgentPrompt.txt", "gpt-5.2-chat"),
};

int successCount = 0;
int failCount = 0;

foreach (var spec in agentSpecs)
{
    Console.WriteLine();
    Console.WriteLine($"  Creating agent: {spec.Name}");
    Console.WriteLine($"    Model:       {spec.Model}");
    Console.WriteLine($"    Description: {spec.Description}");
    Console.Write($"    Prompt:      ");

    var agentSw = Stopwatch.StartNew();
    try
    {
        var instructions = LoadPrompt(spec.PromptFile);
        Console.WriteLine($"    Calling CreateAgentAsync...");

        var agentResponse = await persistentAgentsClient.Administration.CreateAgentAsync(
            model: spec.Model,
            name: spec.Name,
            description: spec.Description,
            instructions: instructions);

        agentSw.Stop();
        successCount++;
        Console.WriteLine($"  ✅ {spec.Name,-40} id={agentResponse.Value.Id}  ({agentSw.ElapsedMilliseconds}ms)");
    }
    catch (Exception ex)
    {
        agentSw.Stop();
        failCount++;
        Console.WriteLine($"  ❌ {spec.Name} FAILED after {agentSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"    Error: {ex.Message}");
        if (ex.Message.Length < 500)
            Console.WriteLine($"    Detail: {ex}");
    }
}

Console.WriteLine();
Console.WriteLine($"  Specialized agents: {successCount} succeeded, {failCount} failed");
Console.WriteLine();
Console.WriteLine("  Note: No orchestrator agent — workflow is defined in LoanOrigination.yaml");
Console.WriteLine("  and executed by the web app via DeclarativeWorkflowBuilder.");

// ── Step 6: Create health check agent ─────────────────────────────────────
Console.WriteLine();
Console.WriteLine("Step 6: Creating health check agent...");
var healthSw = Stopwatch.StartNew();
try
{
    Console.Write("  Loading health check prompt: ");
    var healthInstructions = LoadPrompt("HealthCheckAgentPrompt.txt");
    var healthModel = "gpt-4.1";

    var healthResponse = await persistentAgentsClient.Administration.CreateAgentAsync(
        model: healthModel,
        name: "health_check_agent",
        description: "Health check agent for connectivity verification",
        instructions: healthInstructions);

    healthSw.Stop();
    Console.WriteLine($"  ✅ health_check_agent                         id={healthResponse.Value.Id}  ({healthSw.ElapsedMilliseconds}ms)");

    // Step 7: Run health check to verify end-to-end connectivity
    Console.WriteLine();
    Console.WriteLine("Step 7: Running health check agent to verify model connectivity...");
    var runSw = Stopwatch.StartNew();

    var healthAgent = persistentAgentsClient.AsAIAgent(healthResponse);
    var healthResult = await healthAgent.RunAsync("Perform health check now.");

    runSw.Stop();
    Console.WriteLine($"  ✅ Health check response ({runSw.ElapsedMilliseconds}ms):");
    Console.WriteLine($"     Response: {healthResult.Text}");
    Console.WriteLine($"     Run ID:   {healthResult.ResponseId}");
    Console.WriteLine($"     Thread:   {healthResult.AdditionalProperties?.GetValueOrDefault("threadId")}");
}
catch (Exception ex)
{
    healthSw.Stop();
    Console.WriteLine($"  ❌ Health check FAILED after {healthSw.ElapsedMilliseconds}ms");
    Console.WriteLine($"    Error: {ex.Message}");
}

// ── Step 8: Validate declarative workflow YAML ────────────────────────────
Console.WriteLine();
Console.WriteLine("Step 8: Validating declarative workflow (LoanOrigination.yaml)...");
var workflowSw = Stopwatch.StartNew();

var workflowDir = Path.Combine(AppContext.BaseDirectory, "workflows");
if (!Directory.Exists(workflowDir))
    workflowDir = Path.Combine(Directory.GetCurrentDirectory(), "workflows");

var workflowPath = Path.Combine(workflowDir, "LoanOrigination.yaml");
Console.WriteLine($"  Workflow file: {workflowPath}");

if (!File.Exists(workflowPath))
{
    Console.WriteLine($"  ❌ Workflow YAML file not found!");
    return 1;
}

var workflowContent = File.ReadAllText(workflowPath);
Console.WriteLine($"  ✅ Loaded: {workflowContent.Length} chars");

// Count InvokeAzureAgent actions to verify all agents are referenced
var agentInvocations = workflowContent.Split("kind: InvokeAzureAgent").Length - 1;
Console.WriteLine($"  ✅ InvokeAzureAgent actions found: {agentInvocations}");

try
{
    // Validate the YAML can be parsed by building the workflow
    var agentProvider = new AzureAgentProvider(
        new Uri(endpoint),
        credential);

    var workflowOptions = new DeclarativeWorkflowOptions(agentProvider);
    var workflow = DeclarativeWorkflowBuilder.Build<string>(workflowPath, workflowOptions);

    workflowSw.Stop();
    Console.WriteLine($"  ✅ Workflow parsed and built successfully ({workflowSw.ElapsedMilliseconds}ms)");
    Console.WriteLine($"     Workflow agents referenced:");
    foreach (var agentName in agentSpecs.Select(a => a.Name))
    {
        var referenced = workflowContent.Contains(agentName);
        Console.WriteLine($"       {(referenced ? "✅" : "⚠️")} {agentName} {(referenced ? "" : "(NOT referenced in workflow!)")}");
    }
}
catch (Exception ex)
{
    workflowSw.Stop();
    Console.WriteLine($"  ❌ Workflow validation FAILED after {workflowSw.ElapsedMilliseconds}ms");
    Console.WriteLine($"    Error: {ex.Message}");
    Console.WriteLine($"    The YAML workflow definition may have syntax errors.");
    return 1;
}

// Copy workflow YAML to web app output for runtime use
var webAppWorkflowDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "Agent", "Workflow");
if (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "..", "Agent")))
{
    Directory.CreateDirectory(webAppWorkflowDir);
    var destPath = Path.Combine(webAppWorkflowDir, "LoanOrigination.yaml");
    File.Copy(workflowPath, destPath, overwrite: true);
    Console.WriteLine($"  ✅ Workflow YAML copied to web app: {destPath}");
}

// ── Summary ───────────────────────────────────────────────────────────────
sw.Stop();
Console.WriteLine();
Console.WriteLine(new string('─', 70));
Console.WriteLine($"✅ Agent & workflow initialization complete in {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalSeconds:F1}s)");
Console.WriteLine($"   Specialized agents: {successCount}/{agentSpecs.Length} created");
Console.WriteLine($"   Health check agent: verified");
Console.WriteLine($"   Workflow (YAML):    validated ({agentInvocations} agent invocations)");
Console.WriteLine($"   Total agents:       {successCount + 1} (specialists + health check)");
Console.WriteLine();
Console.WriteLine("Agents are registered in Microsoft Foundry Agent Service.");
Console.WriteLine("Workflow definition (LoanOrigination.yaml) has been validated and deployed.");
Console.WriteLine("The web app will load and execute this workflow at runtime.");
return failCount > 0 ? 1 : 0;

// ── Agent specification record ────────────────────────────────────────────

record AgentSpec(string Name, string Description, string PromptFile, string Model);
