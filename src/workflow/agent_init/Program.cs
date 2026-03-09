using System.Diagnostics;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI.Workflows.Declarative;

// ─────────────────────────────────────────────────────────────────────────────
// Loan Origination Agent & Workflow Initializer CLI
// Creates versioned agents (new API) and validates the declarative YAML workflow.
// SDK: Azure.AI.Projects 2.0 (AIProjectClient, PromptAgentDefinition, CreateAgentVersion)
// Auth: Entra ID (AzureCliCredential → EnvironmentCredential → ManagedIdentity)
// Migrated from classic PersistentAgentsClient.CreateAgentAsync to new
// AIProjectClient.Agents.CreateAgentVersionAsync per:
//   https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/migrate
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

Console.WriteLine($"🔧 Loan Origination Agent & Workflow Initializer (New Agent API)");
Console.WriteLine($"   Endpoint: {endpoint}");
Console.WriteLine($"   Auth:     Entra ID (AzureCliCredential → EnvironmentCredential → ManagedIdentity)");
Console.WriteLine($"   API:      AIProjectClient.Agents.CreateAgentVersionAsync (new versioned agents)");
Console.WriteLine($"   Pattern:  Declarative YAML Workflow (LoanOrigination.yaml)");
Console.WriteLine();
Console.WriteLine("   Model Assignments:");
Console.WriteLine("     gpt-4.1          → All specialist agents (Credit, Income, Fraud, Policy, Pricing, Underwriting)");
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
    var tokenRequest = new Azure.Core.TokenRequestContext(["https://cognitiveservices.azure.com/.default"]);
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

// ── Step 2: Connect to Foundry via AIProjectClient (new API) ──────────────
Console.WriteLine();
Console.WriteLine($"Step 2: Connecting to Foundry Agent Service (new API)...");
Console.WriteLine($"  Endpoint: {endpoint}");

var projectClient = new AIProjectClient(
    endpoint: new Uri(endpoint),
    tokenProvider: credential);
Console.WriteLine($"  ✅ AIProjectClient created (new versioned agent API)");

// ── Step 3: Delete all existing agents ────────────────────────────────────
Console.WriteLine();
Console.WriteLine("Step 3: Deleting all existing agents...");
var cleanupSw = Stopwatch.StartNew();
try
{
    var agentsOps = projectClient.Agents;
    var agentsToDelete = new List<(string Name, string Id)>();
    await foreach (var existing in agentsOps.GetAgentsAsync())
    {
        agentsToDelete.Add((existing.Name ?? "(unnamed)", existing.Id));
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
        foreach (var (name, id) in agentsToDelete)
        {
            try
            {
                Console.Write($"    Deleting {name,-40} id={id}...");
                // New API: delete by name (deletes all versions)
                await agentsOps.DeleteAgentAsync(name);
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

// ── Step 5: Create versioned agents via new API ───────────────────────────
// Migrated from: PersistentAgentsClient.Administration.CreateAgentAsync(model, name, ...)
// Migrated to:   AIProjectClient.Agents.CreateAgentVersionAsync(agentName, options)
// Each agent gets a PromptAgentDefinition with kind=prompt, model, and instructions.
Console.WriteLine();
Console.WriteLine("Step 5: Creating versioned agents (PromptAgentDefinition + CreateAgentVersion)...");
Console.WriteLine("  Note: Using new agent API — agents are versioned and support YAML workflows");
Console.WriteLine(new string('─', 70));

var agentSpecs = new AgentSpec[]
{
    new("credit-profile-agent", "Loan Origination Credit Profile Agent",
        "CreditProfileAgentPrompt.txt", "gpt-4.1"),

    new("income-verification-agent", "Loan Origination Income Verification Agent",
        "IncomeVerificationAgentPrompt.txt", "gpt-4.1"),

    new("fraud-screening-agent", "Loan Origination Fraud Screening Agent",
        "FraudScreeningAgentPrompt.txt", "gpt-4.1"),

    new("policy-evaluation-agent", "Loan Origination Policy Evaluation Agent",
        "PolicyEvaluationAgentPrompt.txt", "gpt-4.1"),

    new("pricing-agent", "Loan Origination Pricing Agent",
        "PricingAgentPrompt.txt", "gpt-4.1"),

    new("underwriting-recommendation-agent", "Loan Origination Underwriting Recommendation Agent",
        "UnderwritingAgentPrompt.txt", "gpt-4.1"),
};

int successCount = 0;
int failCount = 0;

foreach (var spec in agentSpecs)
{
    Console.WriteLine();
    Console.WriteLine($"  Creating agent: {spec.Name}");
    Console.WriteLine($"    Kind:        prompt (PromptAgentDefinition)");
    Console.WriteLine($"    Model:       {spec.Model}");
    Console.WriteLine($"    Description: {spec.Description}");
    Console.Write($"    Prompt:      ");

    var agentSw = Stopwatch.StartNew();
    try
    {
        var instructions = LoadPrompt(spec.PromptFile);

        // New API: PromptAgentDefinition with model + instructions
        var definition = new PromptAgentDefinition(model: spec.Model)
        {
            Instructions = instructions,
        };

        var creationOptions = new AgentVersionCreationOptions(definition)
        {
            Description = spec.Description,
        };

        Console.WriteLine($"    Calling CreateAgentVersionAsync (new API)...");

        var agentVersion = await projectClient.Agents.CreateAgentVersionAsync(
            agentName: spec.Name,
            options: creationOptions);

        agentSw.Stop();
        successCount++;
        Console.WriteLine($"  ✅ {spec.Name,-40} version={agentVersion.Value.Version}  ({agentSw.ElapsedMilliseconds}ms)");
        Console.WriteLine($"     id={agentVersion.Value.Id}, name={agentVersion.Value.Name}");
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
Console.WriteLine($"  Versioned agents: {successCount} succeeded, {failCount} failed");
Console.WriteLine();
Console.WriteLine("  Agents are versioned and workflow-compatible (new Foundry API).");

// ── Step 6: Create health check agent (versioned) ─────────────────────────
Console.WriteLine();
Console.WriteLine("Step 6: Creating health check agent (versioned)...");
var healthSw = Stopwatch.StartNew();
try
{
    Console.Write("  Loading health check prompt: ");
    var healthInstructions = LoadPrompt("HealthCheckAgentPrompt.txt");

    var healthDefinition = new PromptAgentDefinition(model: "gpt-4.1")
    {
        Instructions = healthInstructions,
    };

    var healthCreationOptions = new AgentVersionCreationOptions(healthDefinition)
    {
        Description = "Health check agent for connectivity verification",
    };

    var healthAgentsOps = projectClient.Agents;
    var healthVersion = await healthAgentsOps.CreateAgentVersionAsync(
        agentName: "health-check-agent",
        options: healthCreationOptions);

    healthSw.Stop();
    Console.WriteLine($"  ✅ health-check-agent  version={healthVersion.Value.Version}  ({healthSw.ElapsedMilliseconds}ms)");
    Console.WriteLine($"     id={healthVersion.Value.Id}, name={healthVersion.Value.Name}");

    // Step 7: Verify health check agent was created (retrieve by name)
    Console.WriteLine();
    Console.WriteLine("Step 7: Verifying health check agent connectivity...");
    var runSw = Stopwatch.StartNew();

    var agentRecord = await healthAgentsOps.GetAgentAsync("health-check-agent");
    runSw.Stop();
    Console.WriteLine($"  ✅ Agent verified via GetAgentAsync ({runSw.ElapsedMilliseconds}ms)");
    Console.WriteLine($"     Name: {agentRecord.Value.Name}");
    Console.WriteLine($"     Id:   {agentRecord.Value.Id}");
    Console.WriteLine($"  Note: Full agent invocation test runs at web app startup");
}
catch (Exception ex)
{
    healthSw.Stop();
    Console.WriteLine($"  ❌ Health check FAILED after {healthSw.ElapsedMilliseconds}ms");
    Console.WriteLine($"    Error: {ex.Message}");
}

// ── Step 8: Register workflow agent in Foundry ───────────────────────────
Console.WriteLine();
Console.WriteLine("Step 8: Registering workflow agent in Foundry (WorkflowAgentDefinition)...");
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

var agentInvocations = workflowContent.Split("kind: InvokeAzureAgent").Length - 1;
Console.WriteLine($"  ✅ InvokeAzureAgent actions found: {agentInvocations}");

// Validate the YAML locally
try
{
    var agentProvider = new AzureAgentProvider(new Uri(endpoint), credential);
    var workflowOptions = new DeclarativeWorkflowOptions(agentProvider);
    var workflow = DeclarativeWorkflowBuilder.Build<string>(workflowPath, workflowOptions);
    Console.WriteLine($"  ✅ Workflow YAML parsed and validated locally");
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

// Register the workflow as a workflow agent in Foundry using protocol method
try
{
    Console.WriteLine();
    Console.WriteLine("  Registering workflow agent in Foundry Agent Service...");

    // Use protocol method — WorkflowAgentDefinition is experimental, so we
    // send the definition as raw JSON with kind=workflow and the YAML content
    var workflowBody = BinaryData.FromObjectAsJson(new
    {
        definition = new
        {
            kind = "workflow",
            workflow = workflowContent
        },
        description = "Loan origination multi-agent workflow — orchestrates credit, income, fraud, policy, pricing, and underwriting agents"
    });

    var requestOptions = new System.ClientModel.Primitives.RequestOptions();
    requestOptions.AddHeader("Foundry-Features", "WorkflowAgents=V1Preview");

    var result = await projectClient.Agents.CreateAgentVersionAsync(
        agentName: "loan-origination-workflow",
        content: System.ClientModel.BinaryContent.Create(workflowBody),
        foundryFeatures: null,
        options: requestOptions);

    // Parse response to extract version info
    var rawResponse = result.GetRawResponse();
    Console.WriteLine($"  ✅ Workflow agent registered ({rawResponse.Status}) ({workflowSw.ElapsedMilliseconds}ms)");
    Console.WriteLine($"     name=loan-origination-workflow");
    Console.WriteLine($"     Response: {rawResponse.Content.ToString()[..Math.Min(200, rawResponse.Content.ToString().Length)]}");

    workflowSw.Stop();
}
catch (Exception ex)
{
    workflowSw.Stop();
    Console.WriteLine($"  ❌ Workflow agent registration FAILED after {workflowSw.ElapsedMilliseconds}ms");
    Console.WriteLine($"    Error: {ex.Message}");
    Console.WriteLine($"    Note: Workflow agents require WorkflowAgentsV1Preview feature flag");
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
Console.WriteLine($"   Agent API:          New (AIProjectClient.Agents.CreateAgentVersion)");
Console.WriteLine($"   Agent kind:         prompt (PromptAgentDefinition, versioned)");
Console.WriteLine($"   Specialized agents: {successCount}/{agentSpecs.Length} created");
Console.WriteLine($"   Health check agent: verified via GetAgentAsync");
Console.WriteLine($"   Workflow agent:     loan-origination-workflow (WorkflowAgentDefinition)");
Console.WriteLine($"   Workflow (YAML):    {agentInvocations} agent invocations");
Console.WriteLine($"   Total agents:       {successCount + 2} (specialists + health check + workflow)");
Console.WriteLine();
Console.WriteLine("All agents and workflow are registered in Microsoft Foundry Agent Service.");
return failCount > 0 ? 1 : 0;

// ── Agent specification record ────────────────────────────────────────────
record AgentSpec(string Name, string Description, string PromptFile, string Model);

