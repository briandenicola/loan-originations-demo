using System.Diagnostics;
using Azure.AI.Projects;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using LoanOriginationDemo.Agent;
using LoanOriginationDemo.Services;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ──────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("LoanOriginationDemo", LogLevel.Trace);
builder.Logging.AddFilter("Microsoft.Agents", LogLevel.Debug);
builder.Logging.AddFilter("Azure.AI.Agents", LogLevel.Debug);
builder.Logging.AddFilter("Azure.Core", LogLevel.Warning);

// ── OpenTelemetry (Traces + Metrics + Logs) ──────────────────────────────────
var serviceName = "LoanOrigination";
var serviceVersion = "1.0.0";
var appInsightsCs = builder.Configuration["ApplicationInsights:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"]
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName, serviceVersion: serviceVersion)
    .AddAttributes(new Dictionary<string, object>
    {
        ["deployment.environment"] = builder.Environment.EnvironmentName,
        ["host.name"] = Environment.MachineName,
    });

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName, serviceVersion: serviceVersion))
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(resourceBuilder)
            .AddSource(serviceName)
            .AddSource("Microsoft.Agents.AI")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter();

        if (!string.IsNullOrEmpty(appInsightsCs))
            tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = appInsightsCs);
        if (!string.IsNullOrEmpty(otlpEndpoint))
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(resourceBuilder)
            .AddMeter(serviceName)
            .AddMeter("Microsoft.Agents.AI")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter();

        if (!string.IsNullOrEmpty(appInsightsCs))
            metrics.AddAzureMonitorMetricExporter(o => o.ConnectionString = appInsightsCs);
        if (!string.IsNullOrEmpty(otlpEndpoint))
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    });

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.SetResourceBuilder(resourceBuilder);
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;

    if (!string.IsNullOrEmpty(appInsightsCs))
        logging.AddAzureMonitorLogExporter(o => o.ConnectionString = appInsightsCs);
    if (!string.IsNullOrEmpty(otlpEndpoint))
        logging.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
});

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
builder.Services.AddSingleton<PdfParsingService>();
builder.Services.AddSingleton<LoanAgentPlugins>();

// Build Entra ID credential chain: Azure CLI → Environment → Managed Identity
var credential = new ChainedTokenCredential(
    new AzureCliCredential(),
    new EnvironmentCredential(),
    new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));

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

// Configure Microsoft Agent Framework (AIProjectClient for Foundry Agent Service)
var foundryEndpoint = builder.Configuration["AzureOpenAI:Endpoint"];
if (!string.IsNullOrEmpty(foundryEndpoint))
{
    var projectClient = new AIProjectClient(
        endpoint: new Uri(foundryEndpoint),
        tokenProvider: credential);
    builder.Services.AddSingleton(projectClient);
    Console.WriteLine($"✅ Microsoft Agent Framework configured: {foundryEndpoint}");
    Console.WriteLine($"   Workflow:  Code-Based Coordinator (sequential agent calls via AIProjectClient)");
    Console.WriteLine("   Auth chain: ManagedIdentity → EnvironmentCredential → AzureCliCredential");
}
else
{
    Console.WriteLine("ℹ️  Azure AI Foundry not configured — running in local rule-based mode.");
    Console.WriteLine("   Set AzureOpenAI:Endpoint in appsettings.json");
}

// Log telemetry configuration
Console.WriteLine($"📊 OpenTelemetry configured: service={serviceName}");
Console.WriteLine($"   App Insights: {(string.IsNullOrEmpty(appInsightsCs) ? "Not configured" : "Connected")}");
Console.WriteLine($"   OTLP endpoint: {(string.IsNullOrEmpty(otlpEndpoint) ? "Not configured" : otlpEndpoint)}");
Console.WriteLine($"   Console exporter: Enabled");

builder.Services.AddSingleton<LoanAgentOrchestrator>();

var app = builder.Build();

// ── Startup health check: verify Foundry agent connectivity ──────────────
Console.WriteLine();
Console.WriteLine("🏥 Running startup health check...");
using (var scope = app.Services.CreateScope())
{
    var orchestrator = scope.ServiceProvider.GetRequiredService<LoanAgentOrchestrator>();
    var healthy = await orchestrator.HealthCheckAsync();
    if (healthy)
    {
        Console.WriteLine("✅ Foundry Agent Service health check passed — agents are operational");
    }
    else
    {
        Console.WriteLine("⚠️  Foundry Agent Service health check FAILED — agent workflows will return errors");
        Console.WriteLine("   Ensure agents are initialized: cd src/agent_init && dotnet run -- --endpoint=<FOUNDRY_ENDPOINT>");
    }
}
Console.WriteLine();

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
