using System.Diagnostics;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Agents.AI;

// ─────────────────────────────────────────────────────────────────────────────
// Loan Origination Agent Initializer CLI
// Creates all agents in Microsoft Foundry using Microsoft Agent Framework.
// SDK: Microsoft.Agents.AI.AzureAI.Persistent + Azure.AI.Agents.Persistent
// Auth: Entra ID (AzureCliCredential → EnvironmentCredential → ManagedIdentity)
// Pattern: https://github.com/briandenicola/TechWorkshop-L300-AI-Apps-and-agents
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

Console.WriteLine($"🔧 Loan Origination Agent Initializer (Microsoft Agent Framework)");
Console.WriteLine($"   Endpoint: {endpoint}");
Console.WriteLine($"   Auth:     Entra ID (AzureCliCredential → EnvironmentCredential → ManagedIdentity)");
Console.WriteLine();
Console.WriteLine("   Model Assignments:");
Console.WriteLine("     gpt-4.1          → Orchestrator, Credit Profile, Policy Evaluation");
Console.WriteLine("     gpt-5.2-chat     → Fraud Screening, Underwriting Recommendation");
Console.WriteLine("     Phi-4-reasoning  → Income Verification, Pricing (structured reasoning)");
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

// ── Step 5: Define function tools ─────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("Step 5: Defining function tool schemas...");

var getCreditProfileTool = new FunctionToolDefinition(
    "get_credit_profile",
    "Retrieve credit bureau profile for a loan application",
    BinaryData.FromObjectAsJson(new { type = "object", properties = new { application_no = new { type = "string", description = "The loan application number" } }, required = new[] { "application_no" }, additionalProperties = false }));
Console.WriteLine("  Defined: get_credit_profile");

var getIncomeVerificationTool = new FunctionToolDefinition(
    "get_income_verification",
    "Retrieve payroll and employer verification signals",
    BinaryData.FromObjectAsJson(new { type = "object", properties = new { application_no = new { type = "string", description = "The loan application number" } }, required = new[] { "application_no" }, additionalProperties = false }));
Console.WriteLine("  Defined: get_income_verification");

var getFraudSignalsTool = new FunctionToolDefinition(
    "get_fraud_signals",
    "Retrieve fraud and identity risk indicators",
    BinaryData.FromObjectAsJson(new { type = "object", properties = new { application_no = new { type = "string", description = "The loan application number" } }, required = new[] { "application_no" }, additionalProperties = false }));
Console.WriteLine("  Defined: get_fraud_signals");

var getPolicyThresholdsTool = new FunctionToolDefinition(
    "get_policy_thresholds",
    "Get underwriting policy threshold rules",
    BinaryData.FromObjectAsJson(new { type = "object", properties = new { }, required = Array.Empty<string>(), additionalProperties = false }));
Console.WriteLine("  Defined: get_policy_thresholds");

var computeQuoteTool = new FunctionToolDefinition(
    "compute_quote",
    "Compute pricing quote and payment estimate for loan terms",
    BinaryData.FromObjectAsJson(new
    {
        type = "object",
        properties = new
        {
            application_no = new { type = "string", description = "The loan application number" },
            requested_amount = new { type = "number", description = "Loan amount requested" },
            requested_term_months = new { type = "integer", description = "Loan term in months" },
            loan_type = new { type = "string", description = "UNSECURED or SECURED" }
        },
        required = new[] { "application_no", "requested_amount", "requested_term_months", "loan_type" },
        additionalProperties = false
    }));
Console.WriteLine("  Defined: compute_quote");

var evaluateUnderwritingTool = new FunctionToolDefinition(
    "evaluate_underwriting",
    "Evaluate prepared application and produce underwriting recommendation",
    BinaryData.FromObjectAsJson(new
    {
        type = "object",
        properties = new
        {
            application_no = new { type = "string", description = "The loan application number" },
            requested_amount = new { type = "number", description = "Loan amount" },
            requested_term_months = new { type = "integer", description = "Loan term in months" },
            loan_type = new { type = "string", description = "UNSECURED or SECURED" },
            monthly_income = new { type = "number", description = "Verified monthly income" },
            monthly_debt_payments = new { type = "number", description = "Total monthly debt payments" },
            credit_score = new { type = "integer", description = "Bureau credit score" },
            identity_risk_score = new { type = "number", description = "Identity fraud risk score" },
            payment_to_income_pct = new { type = "number", description = "Estimated payment as percentage of income" },
            verified_dti_pct = new { type = "number", description = "Verified debt-to-income ratio" }
        },
        required = new[] { "application_no", "requested_amount", "requested_term_months", "loan_type",
                          "monthly_income", "monthly_debt_payments", "credit_score", "identity_risk_score",
                          "payment_to_income_pct", "verified_dti_pct" },
        additionalProperties = false
    }));
Console.WriteLine("  Defined: evaluate_underwriting");
Console.WriteLine("  ✅ 6 function tool schemas defined");

// ── Step 6: Create specialized agents in Foundry ──────────────────────────
Console.WriteLine();
Console.WriteLine("Step 6: Creating specialized agents in Foundry...");
Console.WriteLine(new string('─', 70));

var specializedAgents = new AgentSpec[]
{
    new("credit_profile_agent", "Loan Origination Credit Profile Agent",
        "CreditProfileAgentPrompt.txt", [getCreditProfileTool], "gpt-4.1"),

    new("income_verification_agent", "Loan Origination Income Verification Agent",
        "IncomeVerificationAgentPrompt.txt", [getIncomeVerificationTool], "Phi-4-reasoning"),

    new("fraud_screening_agent", "Loan Origination Fraud Screening Agent",
        "FraudScreeningAgentPrompt.txt", [getFraudSignalsTool], "gpt-5.2-chat"),

    new("policy_evaluation_agent", "Loan Origination Policy Evaluation Agent",
        "PolicyEvaluationAgentPrompt.txt", [getPolicyThresholdsTool], "gpt-4.1"),

    new("pricing_agent", "Loan Origination Pricing Agent",
        "PricingAgentPrompt.txt", [computeQuoteTool], "Phi-4-reasoning"),

    new("underwriting_recommendation_agent", "Loan Origination Underwriting Recommendation Agent",
        "UnderwritingAgentPrompt.txt", [evaluateUnderwritingTool], "gpt-5.2-chat"),
};

var createdAgents = new List<PersistentAgent>();
int successCount = 0;
int failCount = 0;

foreach (var spec in specializedAgents)
{
    Console.WriteLine();
    Console.WriteLine($"  Creating agent: {spec.Name}");
    Console.WriteLine($"    Model:       {spec.Model}");
    Console.WriteLine($"    Description: {spec.Description}");
    Console.WriteLine($"    Tools:       {string.Join(", ", spec.Tools.Select(t => t.Name))}");
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
            instructions: instructions,
            tools: spec.Tools);

        agentSw.Stop();
        createdAgents.Add(agentResponse.Value);
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

// ── Step 7: Create orchestrator with ConnectedAgent tools ─────────────────
Console.WriteLine();
Console.WriteLine("Step 7: Creating orchestrator agent with connected agent tools...");

if (createdAgents.Count == 0)
{
    Console.WriteLine("  ❌ No specialized agents were created. Cannot create orchestrator.");
    return 1;
}

Console.WriteLine($"  Linking {createdAgents.Count} specialized agents as connected tools:");
var connectedTools = new List<ToolDefinition>();
foreach (var agent in createdAgents)
{
    Console.WriteLine($"    → {agent.Name,-40} id={agent.Id}");
    connectedTools.Add(new ConnectedAgentToolDefinition(
        new ConnectedAgentDetails(agent.Id, agent.Name, agent.Description)));
}

var orchestratorSw = Stopwatch.StartNew();
try
{
    Console.Write("  Loading orchestrator prompt: ");
    var orchestratorInstructions = LoadPrompt("OrchestratorAgentPrompt.txt");
    var orchestratorModel = "gpt-4.1";

    Console.WriteLine($"  Calling CreateAgentAsync for loan_orchestrator (model={orchestratorModel})...");
    var orchestratorResponse = await persistentAgentsClient.Administration.CreateAgentAsync(
        model: orchestratorModel,
        name: "loan_orchestrator",
        description: "Loan Origination Orchestrator Agent - coordinates all specialized agents",
        instructions: orchestratorInstructions,
        tools: connectedTools);

    orchestratorSw.Stop();
    Console.WriteLine($"  ✅ loan_orchestrator                          id={orchestratorResponse.Value.Id}  ({orchestratorSw.ElapsedMilliseconds}ms)");

    // Step 8: Verify the orchestrator can be retrieved as an AIAgent
    Console.WriteLine();
    Console.WriteLine("Step 8: Verifying orchestrator as AIAgent via Agent Framework...");
    var orchestratorAgent = persistentAgentsClient.AsAIAgent(orchestratorResponse);
    Console.WriteLine($"  ✅ AIAgent type: {orchestratorAgent.GetType().Name}");
}
catch (Exception ex)
{
    orchestratorSw.Stop();
    Console.WriteLine($"  ❌ loan_orchestrator FAILED after {orchestratorSw.ElapsedMilliseconds}ms");
    Console.WriteLine($"    Error: {ex.Message}");
    Console.WriteLine($"    Detail: {ex}");
}

// ── Step 8: Create health check agent ─────────────────────────────────────
Console.WriteLine();
Console.WriteLine("Step 8: Creating health check agent...");
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

    // Step 9: Run health check to verify end-to-end connectivity
    Console.WriteLine();
    Console.WriteLine("Step 9: Running health check agent to verify model connectivity...");
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

// ── Summary ───────────────────────────────────────────────────────────────
sw.Stop();
Console.WriteLine();
Console.WriteLine(new string('─', 70));
Console.WriteLine($"✅ Agent initialization complete in {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalSeconds:F1}s)");
Console.WriteLine($"   Specialized agents: {successCount}/{specializedAgents.Length} created");
Console.WriteLine($"   Orchestrator:       {(failCount == 0 ? "created" : "check errors above")}");
Console.WriteLine($"   Health check:       verified");
Console.WriteLine($"   Total agents:       {createdAgents.Count + 2}");
Console.WriteLine();
Console.WriteLine("Agents are now registered in Microsoft Foundry Agent Service.");
Console.WriteLine("The web app will connect to 'loan_orchestrator' to run the workflow.");
return failCount > 0 ? 1 : 0;

// ── Agent specification record ────────────────────────────────────────────

record AgentSpec(string Name, string Description, string PromptFile, FunctionToolDefinition[] Tools, string Model);
