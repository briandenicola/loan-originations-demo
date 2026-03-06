using Azure.AI.Agents.Persistent;
using Azure.Identity;
using LoanOriginationDemo.Agent;
using LoanOriginationDemo.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// Register core services
builder.Services.AddSingleton<CsvDataService>();
builder.Services.AddSingleton<UnderwritingService>();
builder.Services.AddSingleton<LoanAgentPlugins>();

// Build Entra ID credential chain: Managed Identity → Environment → Azure CLI
var credential = new ChainedTokenCredential(
    new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned),
    new EnvironmentCredential(),
    new AzureCliCredential());

// Diagnostic: attempt to acquire a token and print the result
try
{
    var tokenRequest = new Azure.Core.TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" });
    var token = await credential.GetTokenAsync(tokenRequest);
    Console.WriteLine($"✅ Entra ID token acquired successfully. Expires: {token.ExpiresOn}");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Entra ID token acquisition FAILED: {ex.Message}");
}

// Configure Microsoft Agent Framework (PersistentAgentsClient for Foundry Agent Service)
var foundryEndpoint = builder.Configuration["AzureOpenAI:Endpoint"];
if (!string.IsNullOrEmpty(foundryEndpoint))
{
    var persistentAgentsClient = new PersistentAgentsClient(foundryEndpoint, credential);
    builder.Services.AddSingleton(persistentAgentsClient);
    Console.WriteLine($"✅ Microsoft Agent Framework configured: {foundryEndpoint}");
    Console.WriteLine($"   Orchestrator agent: {builder.Configuration["Foundry:OrchestratorAgentName"] ?? "loan_orchestrator"}");
    Console.WriteLine("   Auth chain: ManagedIdentity → EnvironmentCredential → AzureCliCredential");
}
else
{
    Console.WriteLine("ℹ️  Azure AI Foundry not configured — running in local rule-based mode.");
    Console.WriteLine("   Set AzureOpenAI:Endpoint in appsettings.json");
}

builder.Services.AddSingleton<LoanAgentOrchestrator>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthorization();
app.MapControllers();

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

app.Run();
