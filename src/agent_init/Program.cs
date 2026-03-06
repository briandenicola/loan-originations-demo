using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Agents.AI;

// ─────────────────────────────────────────────────────────────────────────────
// Loan Origination Agent Initializer CLI
// Creates all agents in Microsoft Foundry using Microsoft Agent Framework.
// SDK: Microsoft.Agents.AI.AzureAI.Persistent + Azure.AI.Agents.Persistent
// Auth: Entra ID (ManagedIdentity → EnvironmentCredential → AzureCliCredential)
// Pattern: https://github.com/briandenicola/TechWorkshop-L300-AI-Apps-and-agents
// ─────────────────────────────────────────────────────────────────────────────

var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT")
    ?? args.FirstOrDefault(a => a.StartsWith("--endpoint="))?.Split('=', 2)[1]
    ?? "";
var model = Environment.GetEnvironmentVariable("GPT_DEPLOYMENT")
    ?? args.FirstOrDefault(a => a.StartsWith("--model="))?.Split('=', 2)[1]
    ?? "gpt-4.1";

if (string.IsNullOrEmpty(endpoint))
{
    Console.WriteLine("Usage: dotnet run -- --endpoint=<FOUNDRY_ENDPOINT> [--model=<deployment>]");
    Console.WriteLine("   Or: set FOUNDRY_ENDPOINT and GPT_DEPLOYMENT environment variables");
    return 1;
}

Console.WriteLine($"🔧 Loan Origination Agent Initializer (Microsoft Agent Framework)");
Console.WriteLine($"   Endpoint: {endpoint}");
Console.WriteLine($"   Model:    {model}");
Console.WriteLine($"   Auth:     Entra ID (ManagedIdentity → EnvironmentCredential → AzureCliCredential)");
Console.WriteLine();

var credential = new ChainedTokenCredential(
    new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned),
    new EnvironmentCredential(),
    new AzureCliCredential());

var persistentAgentsClient = new PersistentAgentsClient(endpoint, credential);

var promptsDir = Path.Combine(AppContext.BaseDirectory, "prompts");
if (!Directory.Exists(promptsDir))
    promptsDir = Path.Combine(Directory.GetCurrentDirectory(), "prompts");

string LoadPrompt(string filename) => File.ReadAllText(Path.Combine(promptsDir, filename));

// ── Define function tools for each agent ──────────────────────────────────

var getCreditProfileTool = new FunctionToolDefinition(
    "get_credit_profile",
    "Retrieve credit bureau profile for a loan application",
    BinaryData.FromObjectAsJson(new { type = "object", properties = new { application_no = new { type = "string", description = "The loan application number" } }, required = new[] { "application_no" }, additionalProperties = false }));

var getIncomeVerificationTool = new FunctionToolDefinition(
    "get_income_verification",
    "Retrieve payroll and employer verification signals",
    BinaryData.FromObjectAsJson(new { type = "object", properties = new { application_no = new { type = "string", description = "The loan application number" } }, required = new[] { "application_no" }, additionalProperties = false }));

var getFraudSignalsTool = new FunctionToolDefinition(
    "get_fraud_signals",
    "Retrieve fraud and identity risk indicators",
    BinaryData.FromObjectAsJson(new { type = "object", properties = new { application_no = new { type = "string", description = "The loan application number" } }, required = new[] { "application_no" }, additionalProperties = false }));

var getPolicyThresholdsTool = new FunctionToolDefinition(
    "get_policy_thresholds",
    "Get underwriting policy threshold rules",
    BinaryData.FromObjectAsJson(new { type = "object", properties = new { }, required = Array.Empty<string>(), additionalProperties = false }));

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

// ── Define specialized agent specs (created first) ────────────────────────

var specializedAgents = new AgentSpec[]
{
    new("credit_profile_agent", "Loan Origination Credit Profile Agent",
        "CreditProfileAgentPrompt.txt", [getCreditProfileTool]),

    new("income_verification_agent", "Loan Origination Income Verification Agent",
        "IncomeVerificationAgentPrompt.txt", [getIncomeVerificationTool]),

    new("fraud_screening_agent", "Loan Origination Fraud Screening Agent",
        "FraudScreeningAgentPrompt.txt", [getFraudSignalsTool]),

    new("policy_evaluation_agent", "Loan Origination Policy Evaluation Agent",
        "PolicyEvaluationAgentPrompt.txt", [getPolicyThresholdsTool]),

    new("pricing_agent", "Loan Origination Pricing Agent",
        "PricingAgentPrompt.txt", [computeQuoteTool]),

    new("underwriting_recommendation_agent", "Loan Origination Underwriting Recommendation Agent",
        "UnderwritingAgentPrompt.txt", [evaluateUnderwritingTool]),
};

// ── Create specialized agents in Foundry ──────────────────────────────────

Console.WriteLine($"Creating {specializedAgents.Length + 1} agents in Microsoft Foundry...");
Console.WriteLine(new string('─', 60));

var createdAgents = new List<PersistentAgent>();

foreach (var spec in specializedAgents)
{
    try
    {
        var instructions = LoadPrompt(spec.PromptFile);

        // Use PersistentAgentsClient.Administration to create persistent agents
        var agentResponse = await persistentAgentsClient.Administration.CreateAgentAsync(
            model: model,
            name: spec.Name,
            description: spec.Description,
            instructions: instructions,
            tools: spec.Tools);

        createdAgents.Add(agentResponse.Value);
        Console.WriteLine($"  ✅ {spec.Name,-40} id={agentResponse.Value.Id}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ❌ {spec.Name,-40} FAILED: {ex.Message}");
    }
}

// ── Create orchestrator with ConnectedAgent tools ─────────────────────────

try
{
    var connectedTools = new List<ToolDefinition>();
    foreach (var agent in createdAgents)
    {
        connectedTools.Add(new ConnectedAgentToolDefinition(
            new ConnectedAgentDetails(agent.Id, agent.Name, agent.Description)));
    }

    var orchestratorInstructions = LoadPrompt("OrchestratorAgentPrompt.txt");
    var orchestratorResponse = await persistentAgentsClient.Administration.CreateAgentAsync(
        model: model,
        name: "loan_orchestrator",
        description: "Loan Origination Orchestrator Agent - coordinates all specialized agents",
        instructions: orchestratorInstructions,
        tools: connectedTools);

    Console.WriteLine($"  ✅ {"loan_orchestrator",-40} id={orchestratorResponse.Value.Id}");

    // Verify the orchestrator can be retrieved as an AIAgent via Agent Framework
    var orchestratorAgent = persistentAgentsClient.AsAIAgent(orchestratorResponse);
    Console.WriteLine($"     → Agent Framework AIAgent type: {orchestratorAgent.GetType().Name}");
}
catch (Exception ex)
{
    Console.WriteLine($"  ❌ {"loan_orchestrator",-40} FAILED: {ex.Message}");
}

Console.WriteLine(new string('─', 60));
Console.WriteLine("✅ Agent initialization complete.");
Console.WriteLine();
Console.WriteLine("Agents are now registered in Microsoft Foundry Agent Service.");
Console.WriteLine("The web app will connect to 'loan-orchestrator' to run the workflow.");
return 0;

// ── Agent specification record ────────────────────────────────────────────

record AgentSpec(string Name, string Description, string PromptFile, FunctionToolDefinition[] Tools);
