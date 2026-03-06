# Loan Origination

**Microsoft Agent Framework + Azure AI Foundry — Declarative YAML Workflow for Automated Underwriting**

A production-style application showcasing Microsoft's Agent Framework and Azure AI Foundry Agent Service for automated loan underwriting. The system uses a **declarative YAML workflow** (`LoanOrigination.yaml`) to orchestrate six specialized AI agents, each responsible for a distinct phase of the underwriting pipeline (S01–S10). A human-in-the-loop review dashboard provides the final decision layer.

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
│  │    ┌──────────────────────────────────────────────┐        │  │
│  │    │  Declarative YAML Workflow                    │        │  │
│  │    │  (LoanOrigination.yaml)                      │        │  │
│  │    │                                              │        │  │
│  │    │  credit_profile_agent        (gpt-4.1)       │        │  │
│  │    │  income_verification_agent   (Phi-4-reasoning)│       │  │
│  │    │  fraud_screening_agent       (gpt-5.2-chat)  │        │  │
│  │    │  policy_evaluation_agent     (gpt-4.1)       │        │  │
│  │    │  pricing_agent               (Phi-4-reasoning)│       │  │
│  │    │  underwriting_recommendation_agent (gpt-5.2) │        │  │
│  │    └──────────────────────────────────────────────┘        │  │
│  │    DeclarativeWorkflowBuilder + InProcessExecution          │  │
│  └────────────────────────┬───────────────────────────────────┘  │
│  ┌────────────────────────▼───────────────────────────────────┐  │
│  │  LoanAgentPlugins  →  CsvDataService + UnderwritingService │  │
│  │  Data enrichment layer (credit, income, fraud, policy)     │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

---

## Declarative YAML Workflow

The workflow is defined in `src/Agent/Workflow/LoanOrigination.yaml` using the **Microsoft Agent Framework Declarative Workflow** format. The YAML file specifies the full agent orchestration graph — no code-based workflow builder needed:

```yaml
kind: Workflow
maxTurns: 50
trigger:
  kind: OnConversationStart
  id: loan_underwriting_workflow
  actions:
    - kind: SetVariable           # Capture enriched application data
    - kind: InvokeAzureAgent      # credit_profile_agent
    - kind: InvokeAzureAgent      # income_verification_agent
    - kind: InvokeAzureAgent      # fraud_screening_agent
    - kind: InvokeAzureAgent      # policy_evaluation_agent
    - kind: InvokeAzureAgent      # pricing_agent
    - kind: SetTextVariable       # Combine all specialist analyses
    - kind: InvokeAzureAgent      # underwriting_recommendation_agent
    - kind: EndWorkflow
```

The workflow is loaded at runtime via `DeclarativeWorkflowBuilder.Build<string>()` with an `AzureAgentProvider` that resolves agents by name from Foundry Agent Service. Each agent receives enriched application data and returns its analysis. The final `underwriting_recommendation_agent` receives all combined analyses and produces the recommendation.

---

## AI Agents

The application uses **Microsoft Agent Framework** to create and interact with persistent agents hosted in **Azure AI Foundry Agent Service**. Each agent has a system prompt and is backed by a specific model deployment.

### Specialist Agents

| Agent | Model | Responsibility |
|-------|-------|----------------|
| `credit_profile_agent` | gpt-4.1 | Assesses credit bureau data (score, delinquencies, utilization) |
| `income_verification_agent` | Phi-4-reasoning | Validates payroll records, employer match, income stability |
| `fraud_screening_agent` | gpt-5.2-chat | Evaluates identity risk, device risk, watchlist hits, synthetic ID flags |
| `policy_evaluation_agent` | gpt-4.1 | Evaluates 10 underwriting policy rules (POL-001 through POL-010) |
| `pricing_agent` | Phi-4-reasoning | Validates APR, monthly payment, payment-to-income ratio |
| `underwriting_recommendation_agent` | gpt-5.2-chat | Produces final APPROVE / CONDITIONAL / DECLINE with confidence score |
| `health_check_agent` | gpt-4.1 | Startup connectivity test — confirms Foundry end-to-end health |

### Agent Initializer CLI

The `agent_init` console app creates all agents in Foundry:

```bash
cd src/agent_init
dotnet run -- --endpoint="https://<resource>.services.ai.azure.com/api/projects/<project>"
```

The initializer:
1. Acquires an Entra ID credential (AzureCli → Environment → ManagedIdentity)
2. Connects to Foundry Agent Service
3. **Deletes all existing agents** (clean slate)
4. Loads prompt templates from `prompts/` directory
5. Creates 6 specialized agents + 1 health check agent
6. Runs a health check to verify end-to-end connectivity

---

## Workflow Steps (S01–S10)

| Step | Name | Description |
|------|------|-------------|
| S01 | Application Intake | Validate and accept the loan application |
| S02 | Data Enrichment | Call all enrichment APIs (credit, income, fraud, policy, pricing) |
| S03 | Credit Profile Agent | Bureau score assessment via `credit_profile_agent` |
| S04 | Income Verification Agent | Payroll validation via `income_verification_agent` |
| S05 | Fraud Screening Agent | Risk signal evaluation via `fraud_screening_agent` |
| S06 | Policy Evaluation Agent | 10 policy rules evaluated via `policy_evaluation_agent` |
| S07 | DTI & Affordability | Compute verified debt-to-income ratio |
| S08 | Pricing Agent | APR quote, monthly payment via `pricing_agent` |
| S09 | Underwriting Recommendation | AI-generated rationale via declarative YAML workflow |
| S10 | Human Review Ready | Package results for human-in-the-loop decision |

Steps S01–S02 and S07 are handled by deterministic data enrichment. Steps S03–S06, S08–S09 are handled by AI agents via the declarative workflow.

---

## Infrastructure

The `infrastructure/` directory contains Terraform IaC for provisioning the Azure environment.

| File | Purpose |
|------|---------|
| `main.tf` | Root module configuration |
| `providers.tf` | Azure provider setup |
| `variables.tf` | Input variables (location, naming, tags) |
| `rg.tf` | Resource Group |
| `ai_foundry.tf` | AI Foundry Hub with `disableLocalAuth = true` (Entra ID only) |
| `ai_foundry_projects.tf` | AI Foundry Project resources and model deployments |
| `network.tf` | VNet, subnets, NSGs, managed network integration |
| `logging.tf` | Log Analytics Workspace and diagnostics |
| `roles.tf` | RBAC: Cognitive Services OpenAI User/Contributor, Azure AI Developer/Project Manager |
| `random.tf` | Random IDs for unique naming |
| `references.tf` | Data source references (subscription, client config) |
| `output.tf` | Outputs (endpoint URLs, resource IDs) |

### Authentication

All authentication uses **Entra ID** — no API keys. The credential chain:

1. **ManagedIdentityCredential** (System Assigned) — for production on Azure
2. **EnvironmentCredential** — for service principal (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`)
3. **AzureCliCredential** — for local development after `az login`

---

## Project Structure

```
scenario2/
├── infrastructure/           # Terraform IaC for Azure AI Foundry
│   ├── ai_foundry.tf         # AI Foundry Hub + Agent Service
│   ├── roles.tf              # RBAC role assignments (Entra ID)
│   └── ...                   # Network, logging, variables
├── materials/
│   ├── data/                 # CSV sample data (6 files)
│   │   ├── loan_application_register.csv
│   │   ├── credit_bureau_extract.csv
│   │   ├── income_verification_extract.csv
│   │   ├── fraud_screening_extract.csv
│   │   ├── policy_thresholds.csv
│   │   └── product_pricing_matrix.csv
│   └── openapi.yaml          # API contract specification
├── output/                   # Generated JSON output files
├── Taskfile.yaml             # Task runner (up, down, agents, run, sync, etc.)
└── src/
    ├── LoanOrigination.csproj
    ├── Program.cs             # App bootstrap, DI, startup health check
    ├── appsettings.json       # Foundry endpoint config
    ├── Agent/
    │   ├── LoanAgentOrchestrator.cs  # Workflow orchestration + data enrichment
    │   └── Workflow/
    │       ├── LoanOrigination.yaml  # ★ Declarative YAML workflow definition
    │       ├── LoanWorkflowRunner.cs # Loads YAML + executes via DeclarativeWorkflowBuilder
    │       ├── LoanExecutors.cs      # Custom executors (intake, bridge, aggregation)
    │       └── LoanWorkflowState.cs  # Shared workflow state model
    ├── Controllers/
    │   ├── AgentController.cs        # /api/v1/agent endpoints (503/500 error handling)
    │   └── LoanApiController.cs      # /api/v1/ data endpoints
    ├── Models/
    │   └── LoanModels.cs             # Domain models
    ├── Services/
    │   ├── CsvDataService.cs         # CSV data loader
    │   └── UnderwritingService.cs    # Pricing engine + policy evaluation
    ├── wwwroot/                      # SPA frontend
    │   ├── index.html
    │   ├── css/styles.css
    │   └── js/app.js                 # Error banners for agent failures
    └── agent_init/                   # Agent initializer CLI
        ├── LoanOrigination.AgentInit.csproj
        ├── Program.cs                # Creates 7 agents in Foundry (deletes existing first)
        └── prompts/                  # System prompts for each agent
            ├── CreditProfileAgentPrompt.txt
            ├── IncomeVerificationAgentPrompt.txt
            ├── FraudScreeningAgentPrompt.txt
            ├── PolicyEvaluationAgentPrompt.txt
            ├── PricingAgentPrompt.txt
            ├── UnderwritingAgentPrompt.txt
            └── HealthCheckAgentPrompt.txt
```

---

## Running the Application

### Prerequisites

- .NET 10.0 SDK
- Azure subscription with AI Foundry provisioned
- Entra ID authentication (one of: Managed Identity, Service Principal env vars, or Azure CLI)
- [Task](https://taskfile.dev/) runner (optional, for `task` commands)

### Quick Start with Taskfile

```bash
# Provision infrastructure, sync config, and create agents
task up

# Run the web application
task run
```

### Manual Steps

#### 1. Provision Infrastructure

```bash
cd infrastructure
terraform init
terraform apply
```

#### 2. Initialize Agents in Foundry

```bash
cd src/agent_init
dotnet run -- --endpoint="https://<resource>.services.ai.azure.com/api/projects/<project>"
```

This deletes any existing agents, then creates 6 specialized agents + 1 health check agent in Foundry.

#### 3. Run the Web Application

```bash
cd src
dotnet run --urls "http://localhost:8081"
```

On startup, the application runs a health check against the `health_check_agent` in Foundry to verify end-to-end connectivity.

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

| Command | Description |
|---------|-------------|
| `task up` | Full provisioning: init → apply → sync → agents |
| `task init` | Initialize Terraform workspace |
| `task apply` | Apply Terraform infrastructure |
| `task agents` | Create agents in Foundry (reads endpoint from Terraform output) |
| `task sync` | Sync Foundry endpoint from Terraform to appsettings.json |
| `task build` | Build the .NET web application |
| `task run` | Run the web application on port 8081 |
| `task clean` | Clean build artifacts |
| `task down` | Destroy all Azure resources and clean up Terraform state |
| `task docker-build` | Build Docker container |
| `task docker-run` | Run Docker container locally |

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

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "DeploymentName": "gpt-4.1"
  },
  "Foundry": {
    "WorkflowPattern": "DeclarativeYaml"
  }
}
```

### Environment Variables

| Variable | Purpose |
|----------|---------|
| `AZURE_TENANT_ID` | Entra ID tenant for service principal auth |
| `AZURE_CLIENT_ID` | Service principal application ID |
| `AZURE_CLIENT_SECRET` | Service principal secret |
| `FOUNDRY_ENDPOINT` | Alternative to appsettings for agent init CLI |

---

## Key Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Agents.AI.AzureAI.Persistent` | 1.0.0-preview | Agent Framework — `PersistentAgentsClient`, `AIAgent`, `RunAsync` |
| `Microsoft.Agents.AI.Workflows` | 1.0.0-rc3 | Workflow engine — `WorkflowBuilder`, `InProcessExecution`, `Executor<T>` |
| `Microsoft.Agents.AI.Workflows.Declarative` | 1.0.0-rc3 | Declarative YAML parser — `DeclarativeWorkflowBuilder.Build<T>()` |
| `Microsoft.Agents.AI.Workflows.Declarative.AzureAI` | 1.0.0-rc3 | Azure agent provider — `AzureAgentProvider` for Foundry agent resolution |
| `Azure.Identity` | 1.18.0 | Entra ID authentication (ManagedIdentity, Environment, AzureCli) |
| `CsvHelper` | 33.0.1 | CSV data file parsing |

---

## Error Handling

The application does **not** fall back to local mode. If Foundry Agent Service is unavailable:

- The startup health check logs a warning and reports the failure
- API endpoints return **503 Service Unavailable** with error details
- The frontend displays a **red error banner** with the failure message
- No silent degradation — errors are surfaced immediately

---

## License

This project is licensed under the [MIT License](LICENSE).
