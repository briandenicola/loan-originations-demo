# Loan Origination

**Microsoft Agent Framework + Azure AI Foundry — Automated Underwriting with Multi-Agent Orchestration**

A reference architecture showcasing Microsoft's Agent Framework and Azure AI Foundry Agent Service for automated loan underwriting. The project provides **two parallel implementations** demonstrating different agent orchestration patterns:

| Implementation | Directory | Orchestration Pattern | SDK |
|---|---|---|---|
| **Classic** | `src/classic/` | Agentic — LLM-driven via ConnectedAgentTools | `PersistentAgentsClient` (legacy) |
| **Workflow** | `src/workflow/` | Code-Based Coordinator — deterministic sequential agent calls | `AIProjectClient` (new versioned API) |

Both implementations share the same frontend, data layer, and six specialist AI agents. They differ in **how** the agents are orchestrated.

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│  Browser SPA (HTML/CSS/JS + Chart.js)                            │
│  Intake Form → Workflow Viz → Review Dashboard → Decision Panel  │
└─────────────────────────────┬────────────────────────────────────┘
                              │ REST API
┌─────────────────────────────▼────────────────────────────────────┐
│  ASP.NET Core 10.0 Web API                                       │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  AgentController  (/api/v1/agent)                          │  │
│  │    POST /run      — Execute S01–S10 workflow               │  │
│  │    POST /decision — Record reviewer decision               │  │
│  │    POST /recompute — Recalculate with adjusted terms       │  │
│  └────────────────────────┬───────────────────────────────────┘  │
│  ┌────────────────────────▼───────────────────────────────────┐  │
│  │  LoanAgentOrchestrator                                     │  │
│  │                                                            │  │
│  │   ┌─ Classic ─────────────────────────────────────────┐    │  │
│  │   │  loan_orchestrator agent (LLM-driven)             │    │  │
│  │   │  ConnectedAgentTools → 6 specialist agents        │    │  │
│  │   └───────────────────────────────────────────────────┘    │  │
│  │                        OR                                  │  │
│  │   ┌─ Workflow ────────────────────────────────────────┐    │  │
│  │   │  Code-Based Coordinator (C#)                      │    │  │
│  │   │  AIProjectClient → 5 specialists → underwriter    │    │  │
│  │   └───────────────────────────────────────────────────┘    │  │
│  │                                                            │  │
│  │    credit-profile-agent         (gpt-4.1)                  │  │
│  │    income-verification-agent    (Phi-4-reasoning)           │  │
│  │    fraud-screening-agent        (gpt-5.2-chat)             │  │
│  │    policy-evaluation-agent      (gpt-4.1)                  │  │
│  │    pricing-agent                (Phi-4-reasoning)           │  │
│  │    underwriting-recommendation-agent (gpt-5.2-chat)        │  │
│  └────────────────────────┬───────────────────────────────────┘  │
│  ┌────────────────────────▼───────────────────────────────────┐  │
│  │  LoanAgentPlugins  →  CsvDataService + UnderwritingService │  │
│  │  Data enrichment layer (credit, income, fraud, policy)     │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

---

## Classic vs Workflow

### Classic (Agentic Orchestration)

The classic implementation uses a `loan_orchestrator` agent that receives all six specialist agents as **ConnectedAgentTools**. The orchestrator LLM decides which agents to call and in what order.

- **SDK**: `Microsoft.Agents.AI.AzureAI.Persistent` (`PersistentAgentsClient`)
- **Agent creation**: `CreateAgentAsync(model, name, instructions, tools)`
- **Orchestration**: LLM-driven — the orchestrator agent controls the flow
- **Agents**: 6 specialists + 1 orchestrator + 1 health check = **8 agents**
- **Agent naming**: `credit_profile_agent` (underscores)
- **Foundry endpoint**: `FOUNDRY_ENDPOINT`

### Workflow (Code-Based Coordinator)

The workflow implementation uses a **code-based coordinator** that calls each specialist agent individually via `AIProjectClient`, gathers their responses, compiles a comprehensive brief, and sends it to the underwriting agent for the final recommendation. This guarantees deterministic execution order while ensuring full context is passed to the underwriter.

- **SDK**: `Azure.AI.Projects` 2.0.0-beta.1 (`AIProjectClient`, `PromptAgentDefinition`)
- **Agent creation**: `CreateAgentVersionAsync(agentName, AgentVersionCreationOptions)` — versioned agents
- **Orchestration**: Code-based coordinator in `LoanAgentOrchestrator.RunWorkflowAsync()`
  1. Resolves all agents from Foundry via `GetAgentsAsync()`
  2. Calls 5 specialist agents sequentially, each receiving full enriched application data
  3. Compiles all specialist responses into a comprehensive brief (~16K chars)
  4. Sends the compiled brief to the underwriting-recommendation-agent
  5. Returns a fully-informed APPROVE / CONDITIONAL / DECLINE recommendation
- **Agents**: 6 specialists + 1 health check = **7 agents**
- **Agent naming**: `credit-profile-agent` (hyphens — required by new API)
- **Foundry endpoint**: `FOUNDRY_NEXTGEN_ENDPOINT`
- **Observability**: OpenTelemetry traces + metrics to Application Insights and console

> **Note**: The repository also contains a declarative YAML workflow definition (`LoanOrigination.yaml`) which served as an earlier approach. It is preserved for reference but is not used at runtime — the code-based coordinator replaced it because the YAML workflow engine does not reliably propagate specialist agent outputs via `MessageText()` variables.

---

## AI Agents

Both implementations create six specialist agents backed by specific model deployments, plus a health check agent.

| Agent | Model | Responsibility |
|-------|-------|----------------|
| `credit-profile-agent` | gpt-4.1 | Assesses credit bureau data (score, delinquencies, utilization) |
| `income-verification-agent` | gpt-4.1 | Validates payroll records, employer match, income stability |
| `fraud-screening-agent` | gpt-4.1 | Evaluates identity risk, device risk, watchlist hits, synthetic ID flags |
| `policy-evaluation-agent` | gpt-4.1 | Evaluates 10 underwriting policy rules (POL-001 through POL-010) |
| `pricing-agent` | gpt-4.1 | Validates APR, monthly payment, payment-to-income ratio |
| `underwriting-recommendation-agent` | gpt-4.1 | Produces final APPROVE / CONDITIONAL / DECLINE with confidence score |
| `health-check-agent` | gpt-4.1 | Startup connectivity test — confirms Foundry end-to-end health |

> **Note**: Classic uses underscore-separated names (`credit_profile_agent`). Workflow uses hyphen-separated names (`credit-profile-agent`) as required by the new versioned agent API.

### Agent Initializer CLI

Each implementation has its own `agent_init` project:

```bash
# Classic
task classic:agents

# Workflow
task workflow:agents
```

Both initializers:
1. Acquire an Entra ID credential (AzureCli → Environment → ManagedIdentity)
2. Connect to Foundry Agent Service
3. **Delete all existing agents** (clean slate)
4. Load prompt templates from `prompts/` directory
5. Create 6 specialized agents + 1 health check agent
6. Verify connectivity

The workflow initializer additionally:
7. Validates the declarative YAML workflow
8. Registers a `loan-origination-workflow` agent in Foundry (using `WorkflowAgentDefinition` with `Foundry-Features: WorkflowAgents=V1Preview`)
9. Copies the YAML to the web app directory

---

## Workflow Steps (S01–S10)

| Step | Name | Description |
|------|------|-------------|
| S01 | Application Intake | Validate and accept the loan application |
| S02 | Data Enrichment | Call all enrichment APIs (credit, income, fraud, policy, pricing) |
| S03 | Credit Profile Agent | Bureau score assessment |
| S04 | Income Verification Agent | Payroll validation |
| S05 | Fraud Screening Agent | Risk signal evaluation |
| S06 | Policy Evaluation Agent | 10 policy rules evaluated |
| S07 | DTI & Affordability | Compute verified debt-to-income ratio |
| S08 | Pricing Agent | APR quote, monthly payment |
| S09 | Underwriting Recommendation | AI-generated rationale via agent orchestration |
| S10 | Human Review Ready | Package results for human-in-the-loop decision |

Steps S01–S02 and S07 are deterministic data enrichment. Steps S03–S06, S08–S09 are handled by AI agents.

---

## Infrastructure

The `infrastructure/` directory contains Terraform IaC that provisions **two AI Foundry projects** — one for each implementation.

### Terraform Files

| File | Purpose |
|------|---------|
| `main.tf` | Root module configuration |
| `providers.tf` | Azure provider setup |
| `variables.tf` | Input variables (location, naming, tags) |
| `rg.tf` | Resource Group |
| `ai_foundry.tf` | AI Foundry Hub (AIServices, S0 SKU, `disableLocalAuth = true`) |
| `ai_foundry_projects.tf` | Two project modules: `project_classic` and `project_workflow` |
| `project/` | Shared module for AI Foundry project + model deployments |
| `network.tf` | VNet, subnets, NSGs, managed network integration |
| `cae.tf` | Container App Environment for hosting applications |
| `acr.tf` | Azure Container Registry for container images |
| `logging.tf` | Log Analytics Workspace and diagnostics |
| `roles.tf` | RBAC: OpenAI User, AI Developer, Project Manager |
| `output.tf` | Outputs: `APP_NAME`, `FOUNDRY_ENDPOINT`, `FOUNDRY_NEXTGEN_ENDPOINT`, `ACR_NAME` |

### Terraform Outputs

| Output | Description |
|--------|-------------|
| `APP_NAME` | Resource name (used as the single input to `app/` module) |
| `APP_RESOURCE_GROUP` | Resource group name |
| `OPENAI_ENDPOINT` | AI Foundry hub endpoint |
| `FOUNDRY_ENDPOINT` | Classic project endpoint |
| `FOUNDRY_NEXTGEN_ENDPOINT` | Workflow project endpoint |
| `ACR_NAME` | Azure Container Registry name (used by `acr-build` tasks) |

### Model Deployments

| Project | Model | Version | SKU |
|---------|-------|---------|-----|
| `project_classic` | gpt-4.1 | 2025-04-14 | GlobalStandard (250) |
| `project_workflow` | gpt-4.1 | 2025-04-14 | GlobalStandard (250) |

### Authentication

All authentication uses **Entra ID** — no API keys. The credential chain:

1. **ManagedIdentityCredential** (System Assigned) — for production on Azure
2. **EnvironmentCredential** — for service principal (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`)
3. **AzureCliCredential** — for local development after `az login`

---

## Project Structure

```
scenario2/
├── infrastructure/                # Terraform IaC for Azure AI Foundry
│   ├── ai_foundry.tf              # AI Foundry Hub + Agent Service
│   ├── ai_foundry_projects.tf     # Two projects: classic + workflow
│   ├── project/                   # Shared module for project + model deployments
│   │   ├── ai_foundry_project.tf
│   │   ├── ai_foundry_project_models.tf
│   │   └── ...
│   ├── acr.tf                     # Azure Container Registry
│   ├── cae.tf                     # Container App Environment
│   ├── roles.tf                   # RBAC role assignments (Entra ID)
│   ├── output.tf                  # APP_NAME, FOUNDRY_ENDPOINT, ACR_NAME
│   └── ...                        # Network, logging, variables
├── app/                           # Terraform for ACA deployment
│   ├── containerapp-classic.tf    # Classic agent Container App
│   ├── containerapp-workflow.tf   # Workflow agent Container App
│   ├── identities.tf             # User Assigned Managed Identity
│   ├── roles.tf                   # AcrPull, Cognitive Services User, AI Developer
│   ├── references.tf             # Data sources (ACR, CAE, AI Services, App Insights)
│   └── ...                        # main.tf, variables.tf, outputs.tf
├── materials/
│   ├── data/                      # CSV sample data (6 files)
│   │   ├── loan_application_register.csv
│   │   ├── credit_bureau_extract.csv
│   │   ├── income_verification_extract.csv
│   │   ├── fraud_screening_extract.csv
│   │   ├── policy_thresholds.csv
│   │   └── product_pricing_matrix.csv
│   └── openapi.yaml               # API contract specification
├── sample-data/                   # PDF loan application examples (4 files)
├── output/                        # Generated JSON output files
├── Taskfile.yaml                  # Root task runner (up, down, init, apply)
├── Taskfile.classic.yaml          # Classic-specific tasks (classic:agents, classic:run, etc.)
├── Taskfile.workflow.yaml         # Workflow-specific tasks (workflow:agents, workflow:run, etc.)
├── Taskfile.app.yaml              # ACA deployment tasks (app:deploy, app:build, app:apply)
├── Taskfile.redteam.yaml          # Red team tasks (redteam:run, redteam:setup)
│
└── src/
    ├── redteam/                   # ── Red Team Agent ──────────────────────
    │   ├── run_redteam.py         # Cloud-based AI red teaming via Foundry Evals API
    │   ├── requirements.txt       # azure-ai-projects, azure-identity
    │   └── README.md              # Red team usage guide
    │
    ├── classic/                   # ── Classic Implementation ──────────────
    │   ├── LoanOrigination.csproj
    │   ├── Program.cs             # PersistentAgentsClient + startup health check
    │   ├── appsettings.json       # OrchestratorAgentName: loan_orchestrator
    │   ├── Dockerfile
    │   ├── Agent/
    │   │   └── LoanAgentOrchestrator.cs   # Agentic orchestration via ConnectedAgentTools
    │   ├── Controllers/
    │   ├── Models/
    │   ├── Services/
    │   ├── wwwroot/               # SPA frontend
    │   └── agent_init/            # Classic agent initializer
    │       ├── LoanOrigination.AgentInit.csproj
    │       ├── Program.cs         # Creates 8 agents (6 + orchestrator + health check)
    │       └── prompts/           # System prompts (7 files)
    │
    └── workflow/                  # ── Workflow Implementation ─────────────
        ├── LoanOrigination.csproj
        ├── Program.cs             # AIProjectClient + OpenTelemetry + startup health check
        ├── appsettings.json       # Foundry endpoint, AppInsights connection string
        ├── Dockerfile
        ├── Agent/
        │   ├── LoanAgentOrchestrator.cs   # ★ Code-based coordinator workflow
        │   └── Workflow/
        │       ├── LoanOrigination.yaml   # Declarative YAML workflow (preserved, not used at runtime)
        │       └── LoanWorkflowRunner.cs  # YAML workflow runner (preserved, not used at runtime)
        ├── Controllers/
        ├── Models/
        ├── Services/
        ├── wwwroot/               # SPA frontend
        └── agent_init/            # Workflow agent initializer
            ├── LoanOrigination.AgentInit.csproj
            ├── Program.cs         # Creates 7 agents (6 specialists + health check)
            ├── prompts/           # System prompts (7 files)
            └── workflows/
                └── LoanOrigination.yaml  # YAML template (preserved for reference)
```

---

## Running the Application

### Prerequisites

- .NET 10.0 SDK
- Azure subscription with AI Foundry provisioned
- Entra ID authentication (one of: Managed Identity, Service Principal env vars, or Azure CLI)
- [Task](https://taskfile.dev/) runner (optional, for `task` commands)
- Docker (for local container builds) or Azure CLI (for ACR builds)

### Quick Start with Taskfile

```bash
# Provision infrastructure
task up

# ── Run the Classic implementation ──
task classic:agents    # Create agents in Foundry
task classic:run       # Run on http://localhost:8081

# ── Run the Workflow implementation ──
task workflow:agents   # Create agents + workflow in Foundry
task workflow:run      # Run on http://localhost:8082
```

### Deploy to Azure Container Apps

The `app/` Terraform module deploys both implementations as Container Apps. It takes a single input — `APP_NAME` from the infrastructure outputs — and derives all resource names, endpoints, and connection strings by convention.

```bash
# 1. Provision infrastructure (if not already done)
task up

# 2. Initialize agents in Foundry
task classic:agents
task workflow:agents

# 3. Initialize the app Terraform
task app:init

# 4. Build container images in ACR and deploy to ACA
task app:deploy
```

This runs `az acr build` for both Dockerfiles against the provisioned ACR, then applies the `app/` Terraform which creates:

- **Resource Group**: `{app_name}_apps_rg`
- **Managed Identity**: `{app_name}-app-identity` with `AcrPull`, `Cognitive Services User`, and `Azure AI Developer` roles
- **Container App (Classic)**: `{app_name}-classic` — connected to the classic Foundry project
- **Container App (Workflow)**: `{app_name}-workflow` — connected to the workflow Foundry project

You can also run the steps individually:

```bash
# Build images only (no deploy)
task app:build

# Deploy only (images must already exist in ACR)
task app:apply

# Tear down the Container Apps
task app:destroy
```

#### Naming Convention

All `app/` resources are derived from `APP_NAME` (e.g., `cub-34185`):

| Resource | Derived Name |
|----------|-------------|
| Resource Group | `cub-34185_apps_rg` |
| Identity | `cub-34185-app-identity` |
| ACR | `cub34185acr` (looked up from `cub-34185-core_rg`) |
| CAE | `cub-34185-env` (looked up from `cub-34185-core_rg`) |
| Classic Container App | `cub-34185-classic` |
| Workflow Container App | `cub-34185-workflow` |
| Foundry Classic Endpoint | `https://cub-34185-foundry.services.ai.azure.com/api/projects/cub-34185-project-classic` |
| Foundry Workflow Endpoint | `https://cub-34185-foundry.services.ai.azure.com/api/projects/cub-34185-project-workflow` |
| App Insights (Classic) | `cub-34185-project-classic-appinsights` in `cub-34185-project-classic_rg` |
| App Insights (Workflow) | `cub-34185-project-workflow-appinsights` in `cub-34185-project-workflow_rg` |

### Manual Steps

#### 1. Provision Infrastructure

```bash
cd infrastructure
terraform init
terraform apply
```

#### 2. Initialize Agents

```bash
# Classic
cd src/classic/agent_init
dotnet run -- --endpoint="$(terraform -chdir=../../infrastructure output -raw FOUNDRY_ENDPOINT)"

# Workflow
cd src/workflow/agent_init
dotnet run -- --endpoint="$(terraform -chdir=../../infrastructure output -raw FOUNDRY_NEXTGEN_ENDPOINT)"
```

#### 3. Run the Web Application

```bash
# Classic
cd src/classic && dotnet run --urls "http://localhost:8081"

# Workflow
cd src/workflow && dotnet run --urls "http://localhost:8082"
```

On startup, the application runs a health check against the health check agent in Foundry to verify end-to-end connectivity.

### 4. Use the Application

1. Open `http://localhost:8081` in a browser
2. Select a loan application from the dropdown
3. Click **Run Agent Workflow** — watch S01–S10 steps animate
4. Review the AI-generated recommendation with markdown-rendered rationale
5. Examine enrichment data panels (credit, income, fraud, key factors, policy hits)
6. Use decision controls to APPROVE, APPROVE WITH CONDITIONS, or DECLINE
7. Adjust loan amount or term and click **Recalculate** for a new underwriting assessment
8. JSON output files are written to `output/`

---

## Taskfile Commands

### Root Tasks

| Command | Description |
|---------|-------------|
| `task up` | Full provisioning: init → apply |
| `task init` | Initialize Terraform workspace |
| `task apply` | Apply Terraform infrastructure |
| `task down` | Destroy all Azure resources and clean up Terraform state |

### App Deployment Tasks (`task app:*`)

| Command | Description |
|---------|-------------|
| `task app:init` | Initialize Terraform for the `app/` module |
| `task app:deploy` | Build ACR images + apply Terraform (full deploy) |
| `task app:build` | Build both classic and workflow images in ACR |
| `task app:apply` | Apply app Terraform only (images must exist) |
| `task app:destroy` | Tear down the Container Apps |

### Classic Tasks (`task classic:*`)

| Command | Description |
|---------|-------------|
| `task classic:agents` | Create agents in Foundry (reads `FOUNDRY_ENDPOINT` from Terraform) |
| `task classic:build` | Build the .NET web application |
| `task classic:run` | Run the web application on port 8081 |
| `task classic:clean` | Clean build artifacts |
| `task classic:docker-build` | Build Docker container locally (tagged `loan-origination:classic`) |
| `task classic:acr-build` | Build Docker container in ACR (tagged `loan-origination-classic:{sha}`) |
| `task classic:docker-run` | Run Docker container locally |

### Workflow Tasks (`task workflow:*`)

| Command | Description |
|---------|-------------|
| `task workflow:agents` | Create agents + workflow in Foundry (reads `FOUNDRY_NEXTGEN_ENDPOINT`) |
| `task workflow:build` | Build the .NET web application |
| `task workflow:run` | Run the web application on port 8082 |
| `task workflow:clean` | Clean build artifacts |
| `task workflow:docker-build` | Build Docker container locally (tagged `loan-origination:workflow`) |
| `task workflow:acr-build` | Build Docker container in ACR (tagged `loan-origination-workflow:{sha}`) |
| `task workflow:docker-run` | Run Docker container locally |

### Red Team Tasks (`task redteam:*`)

| Command | Description |
|---------|-------------|
| `task redteam:setup` | Create Python venv and install dependencies |
| `task redteam:run` | Run AI red teaming against all specialist agents |
| `task redteam:clean` | Remove virtual environment |

---

## Output Files

The workflow produces four JSON files in the `output/` directory:

| File | Contents |
|------|----------|
| `loan_application_prepared.json` | Enriched application with all data from S01–S08 |
| `workflow_run_log.json` | Ordered S01–S10 steps with timestamps and execution mode |
| `loan_recommendation_summary.json` | AI recommendation, confidence score, rationale, policy hits |
| `human_decision_record.json` | Reviewer's final decision with adjusted terms and notes |

---

## Configuration

### appsettings.json

**Classic:**
```json
{
  "AzureOpenAI": {
    "Endpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "DeploymentName": "gpt-4.1"
  },
  "Foundry": {
    "OrchestratorAgentName": "loan_orchestrator"
  }
}
```

**Workflow:**
```json
{
  "AzureOpenAI": {
    "Endpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "DeploymentName": "gpt-4.1"
  },
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=...;IngestionEndpoint=..."
  }
}
```

### Environment Variables

| Variable | Purpose |
|----------|---------|
| `AZURE_TENANT_ID` | Entra ID tenant for service principal auth |
| `AZURE_CLIENT_ID` | Service principal application ID |
| `AZURE_CLIENT_SECRET` | Service principal secret |
| `FOUNDRY_ENDPOINT` | Foundry endpoint for classic implementation |
| `FOUNDRY_NEXTGEN_ENDPOINT` | Foundry endpoint for workflow implementation |

---

## Key Packages

### Classic

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Agents.AI.AzureAI.Persistent` | 1.0.0-preview | Agent Framework — `PersistentAgentsClient`, `AIAgent`, ConnectedAgentTools |
| `Azure.Identity` | 1.18.0 | Entra ID authentication |
| `CsvHelper` | 33.0.1 | CSV data file parsing |

### Workflow

| Package | Version | Purpose |
|---------|---------|---------|
| `Azure.AI.Projects` | 2.0.0-beta.1 | New agent API — `AIProjectClient`, `CreateAgentVersionAsync` |
| `Azure.AI.Projects.OpenAI` | 2.0.0-beta.1 | `PromptAgentDefinition`, `GetAIAgentAsync` extension |
| `Azure.Identity` | 1.18.0 | Entra ID authentication |
| `Azure.Monitor.OpenTelemetry.Exporter` | 1.4.0 | Application Insights exporter for traces, metrics, logs |
| `OpenTelemetry.Extensions.Hosting` | | OpenTelemetry SDK integration |
| `CsvHelper` | 33.0.1 | CSV data file parsing |

---

## Observability

Both implementations emit structured traces, metrics, and logs via **OpenTelemetry**, exported to both the console and **Azure Application Insights**.

### Traces

Each workflow run produces distributed traces with spans for:
- `RunWorkflow` — top-level span covering the full S01–S10 pipeline
- `InvokeAgent_{StepId}` — per-agent call spans (S03–S09) with tags for agent name, response length, and call duration
- HTTP spans for outbound Foundry API calls

### Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `loan.workflow.duration_ms` | Histogram | End-to-end workflow duration per application |
| `loan.workflow.agent_errors` | Counter | Agent call failures |

### Configuration

Observability is configured in `Program.cs` via the OpenTelemetry SDK:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("LoanOrigination.Workflow"))
    .WithTracing(t => t.AddSource("LoanOrigination").AddAspNetCoreInstrumentation()...)
    .WithMetrics(m => m.AddMeter("LoanOrigination").AddAspNetCoreInstrumentation()...);
```

When `ApplicationInsights:ConnectionString` is configured, traces/metrics/logs are exported to Application Insights. Console export is always active.

---

## Error Handling

The application does **not** fall back to local mode. If Foundry Agent Service is unavailable:

- The startup health check logs a warning and reports the failure
- API endpoints return **503 Service Unavailable** with error details
- The frontend displays a **red error banner** with the failure message
- No silent degradation — errors are surfaced immediately

---

## Built with GitHub Copilot CLI

This entire application — infrastructure, backend, frontend, agents, deployment, and documentation — was built in a single continuous session using **GitHub Copilot CLI** (powered by Claude). No code was written manually. The project evolved through natural language prompts over the course of ~40 conversational turns.

### The Session Timeline

The build progressed through distinct phases, each driven by a conversational prompt:

**Phase 1 — Scaffold the Application**
> *"I need to create a demo of an agent system based on the information in the instructions.md and instructions.docx file. This demo must showcase Microsoft Foundry and agentic system. Let's build"*

Copilot read the instruction documents and CSV data files, built the full ASP.NET Core Web API with a Semantic Kernel plugin pattern (S01–S10 workflow), CSV-backed data services, REST controllers, and a single-page application frontend with Chart.js visualizations. First end-to-end test passed on the initial run.

**Phase 2 — Infrastructure as Code**
> *"Can we update the agent to use actual LLM models deployed in Microsoft Foundry? Let's create some Terraform to provision Foundry and deploy the models, agents and workflow"*

Created the complete `infrastructure/` Terraform module: AI Foundry Hub, two project modules (classic + workflow), VNet with subnets, Container App Environment, ACR, Log Analytics, model deployments, and RBAC roles.

**Phase 3 — Security Hardening**
> *"Need to update the Agent Framework code to use Entra ID instead of account keys. The login chain should be Managed Identities then Azure CLI credentials"*

Replaced API key authentication with `ChainedTokenCredential` (ManagedIdentity → EnvironmentCredential → AzureCliCredential), set `disableLocalAuth = true` in Terraform, and removed all secret outputs.

**Phase 4 — Agent Framework Migration**
> *"The code is using Semantic Kernel, not Agent Framework"*

Migrated the entire codebase from Semantic Kernel to Microsoft Agent Framework (`Microsoft.Agents.AI.AzureAI.Persistent`), with `PersistentAgentsClient` for agent creation and `AIAgent.RunAsync()` for Foundry thread/run execution.

**Phase 5 — Dual Implementation Architecture**
Evolved through several iterations:
- Created a CLI agent initializer to register agents in Foundry
- Split the project into `src/classic/` (agentic LLM-driven orchestration) and `src/workflow/` (declarative YAML workflow)
- Migrated workflow from classic `PersistentAgentsClient` to new `AIProjectClient` API with versioned agents

**Phase 6 — Workflow Context Sharing**
> *"Still having issues with workflow and context — required specialist analysis and data inputs are missing"*

This was the most iterative phase. The declarative YAML workflow couldn't reliably propagate specialist agent outputs via `MessageText()` variables. After multiple debugging rounds, pivoted to a **code-based coordinator** that calls each agent directly, compiles a ~16K character brief from all 5 specialists, and sends it to the underwriting agent. This solved the context sharing problem completely.

**Phase 7 — Observability & UI Polish**
> *"Let's update the code base to send logs and traces to Application Insights as well as the console"*

Added OpenTelemetry instrumentation with distributed traces, custom metrics, and Application Insights export. Fixed markdown rendering in the UI (tables, ordered lists, headings). Added real-time SSE streaming so workflow steps update live as each agent completes.

**Phase 8 — Red Team & Deployment**
> *"Create a new folder for Red Team Agents"*
> *"Let's add some Terraform to deploy this application into ACA"*

Created a Python-based red team agent using the OpenAI Evals API. Built the `app/` Terraform module for Azure Container Apps deployment with convention-based naming. Added ACR build tasks to the Taskfiles.

### Prompts That Shaped Key Decisions

| Prompt | Impact |
|--------|--------|
| *"The workflow agent is running but having an interesting error... We're not sending the right data"* | Led to the coordinator pattern redesign — each agent now receives full enriched data |
| *"Can we create a workflow that has a centralized coordinator that calls the various agents?"* | Triggered the pivot from YAML declarative to code-based coordinator |
| *"Since the Workflow now is doing all the work, let's update the S01-S09 Queued table entries"* | Drove the real-time SSE streaming implementation |
| *"I don't like the hard coded values in the apps terraform"* | Led to convention-based naming where `APP_NAME` derives all resource names |
| *"The code appears to be trying to get a token... but I am running on my laptop"* | Fixed the auth chain ordering to prefer AzureCliCredential for local dev |

### Session Statistics

| Metric | Value |
|--------|-------|
| Conversational turns | ~40 |
| Git commits | 38 |
| Files created or modified | 80+ |
| Terraform resources | 25+ (infrastructure) + 7 (app deployment) |
| AI agents registered | 7 (6 specialists + 1 health check) |
| SDK migrations | 3 (Semantic Kernel → Agent Framework → AIProjectClient) |
| Workflow architecture pivots | 2 (YAML declarative → code-based coordinator) |
| Lines of C# | ~2,500 |
| Lines of Terraform | ~1,200 |
| Lines of JavaScript | ~600 |

### How Copilot CLI Helped

- **Rapid prototyping**: Full working app from instruction docs in the first turn
- **SDK discovery**: Navigated three different Microsoft agent SDKs, discovering correct API surfaces through package inspection when docs were incomplete
- **Iterative debugging**: When the YAML workflow couldn't share context between agents, Copilot tried multiple fixes (converter functions, TurnToken, fresh conversations) before recommending the architectural pivot to code-based coordination
- **Cross-cutting changes**: UI updates, Terraform, C# backend, and Taskfile changes were made atomically in single turns
- **Convention enforcement**: Naming conventions, auth patterns, and infrastructure outputs stayed consistent across 38 commits

---

## License

This project is licensed under the [MIT License](LICENSE).
