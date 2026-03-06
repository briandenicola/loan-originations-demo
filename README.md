# Loan Origination

**Microsoft Agent Framework + Azure AI Foundry — Multi-Agent Underwriting Workflow**

A production-style application showcasing Microsoft's Agent Framework and Azure AI Foundry Agent Service for automated loan underwriting. The system uses a multi-agent architecture where an orchestrator agent coordinates six specialized agents, each responsible for a distinct phase of the underwriting workflow (S01–S10). A human-in-the-loop review dashboard provides the final decision layer.

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
│  │    │  Foundry Agent Service (Agentic Mode)        │        │  │
│  │    │                                              │        │  │
│  │    │  loan_orchestrator (AIAgent)                  │        │  │
│  │    │    ├─ credit_profile_agent                    │        │  │
│  │    │    ├─ income_verification_agent               │        │  │
│  │    │    ├─ fraud_screening_agent                   │        │  │
│  │    │    ├─ policy_evaluation_agent                 │        │  │
│  │    │    ├─ pricing_agent                           │        │  │
│  │    │    └─ underwriting_recommendation_agent       │        │  │
│  │    └──────────────────────────────────────────────┘        │  │
│  │    ↕ Fallback: local rule-based orchestration              │  │
│  └────────────────────────┬───────────────────────────────────┘  │
│  ┌────────────────────────▼───────────────────────────────────┐  │
│  │  LoanAgentPlugins  →  CsvDataService + UnderwritingService │  │
│  │  Function tool bridge backing agent tool calls             │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

---

## AI Agents

The application uses **Microsoft Agent Framework** (`Microsoft.Agents.AI.AzureAI.Persistent`) to create and interact with persistent agents hosted in **Azure AI Foundry Agent Service**. Each agent has a system prompt, function tools, and is backed by a GPT-4.1 model deployment.

### Agent Topology

The `loan_orchestrator` is the central agent. It uses `ConnectedAgentToolDefinition` to delegate work to six specialized agents. When the web app runs a workflow, it:

1. Resolves the orchestrator agent by listing agents in Foundry and matching by name
2. Retrieves it as an `AIAgent` via `PersistentAgentsClient.GetAIAgentAsync(agentId)`
3. Calls `agent.RunAsync(prompt)` — this creates a **thread** and **run** in Foundry
4. The orchestrator LLM analyzes the enriched application data and produces a comprehensive rationale
5. Foundry thread/run IDs are captured and returned in the workflow log

### Specialized Agents

| Agent | Function Tool | Responsibility |
|-------|---------------|----------------|
| `credit_profile_agent` | `get_credit_profile` | Retrieves and assesses credit bureau data (score, delinquencies, utilization) |
| `income_verification_agent` | `get_income_verification` | Validates payroll records, employer match, income stability |
| `fraud_screening_agent` | `get_fraud_signals` | Evaluates identity risk, device risk, watchlist hits, synthetic ID flags |
| `policy_evaluation_agent` | `get_policy_thresholds` | Retrieves underwriting policy rules (10 rules: POL-001 through POL-010) |
| `pricing_agent` | `compute_quote` | Computes APR, monthly payment, payment-to-income ratio |
| `underwriting_recommendation_agent` | `evaluate_underwriting` | Produces final APPROVE / CONDITIONAL / DECLINE with confidence score |

### Orchestrator Agent

The `loan_orchestrator` receives enriched application data and generates a detailed underwriting assessment including:
- Applicant risk profile summary
- Explanation of the recommendation with supporting data
- Key risk factors and borrower strengths
- Conditions or flags requiring human attention
- Professional rationale rendered as markdown in the UI

### Agent Initializer CLI

The `LoanOrigination.AgentInit` console app creates all agents in Foundry:

```bash
cd src/agent_init
dotnet run -- --endpoint="https://<resource>.services.ai.azure.com/api/projects/<project>" --model="gpt-4.1"
```

This creates the 6 specialized agents with `FunctionToolDefinition` schemas, then creates the orchestrator with `ConnectedAgentToolDefinition` references linking to each specialized agent by ID.

---

## Workflow (S01–S10)

| Step | Name | Description |
|------|------|-------------|
| S01 | Application Intake | Validate and accept the loan application |
| S02 | Data Enrichment | Call all enrichment APIs (credit, income, fraud, policy, pricing) |
| S03 | Credit Profile Agent | Bureau score assessment (score band, delinquencies, utilization) |
| S04 | Income Verification Agent | Payroll validation (verified income, employer match, variance) |
| S05 | Fraud Screening Agent | Risk signal evaluation (identity risk, device risk, watchlist) |
| S06 | Policy Evaluation Agent | 10 policy rules evaluated against thresholds |
| S07 | DTI & Affordability | Compute verified debt-to-income ratio |
| S08 | Pricing Agent | APR quote, monthly payment, payment-to-income ratio |
| S09 | Orchestrator Agent Analysis | LLM generates comprehensive rationale via Foundry Agent Service |
| S10 | Human Review Ready | Package results for human-in-the-loop decision |

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
| `ai_foundry_projects.tf` | AI Foundry Project resources and model connections |
| `network.tf` | VNet, subnets, NSGs, managed network integration |
| `logging.tf` | Log Analytics Workspace and diagnostics |
| `roles.tf` | RBAC: `Cognitive Services OpenAI User`, `Cognitive Services OpenAI Contributor`, `Azure AI Developer`, `Azure AI Project Manager` |
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
└── src/
    ├── LoanOrigination.csproj
    ├── Program.cs             # App bootstrap, DI, Agent Framework config
    ├── appsettings.json       # Foundry endpoint + orchestrator agent config
    ├── Agent/
    │   └── LoanAgentOrchestrator.cs  # Agentic workflow (Foundry + fallback)
    ├── Controllers/
    │   ├── AgentController.cs        # /api/v1/agent endpoints
    │   └── LoanApiController.cs      # /api/v1/ data endpoints
    ├── Models/
    │   └── LoanModels.cs             # Domain models
    ├── Services/
    │   ├── CsvDataService.cs         # CSV data loader
    │   └── UnderwritingService.cs    # Pricing engine + policy evaluation
    ├── wwwroot/                      # SPA frontend
    │   ├── index.html
    │   ├── css/styles.css
    │   └── js/app.js
    └── agent_init/                   # Agent initializer CLI
        ├── LoanOrigination.AgentInit.csproj
        ├── Program.cs                # Creates all 7 agents in Foundry
        └── prompts/                  # System prompts for each agent
            ├── OrchestratorAgentPrompt.txt
            ├── CreditProfileAgentPrompt.txt
            ├── IncomeVerificationAgentPrompt.txt
            ├── FraudScreeningAgentPrompt.txt
            ├── PolicyEvaluationAgentPrompt.txt
            ├── PricingAgentPrompt.txt
            └── UnderwritingAgentPrompt.txt
```

---

## Running the Application

### Prerequisites

- .NET 10.0 SDK
- Azure subscription with AI Foundry provisioned
- Entra ID authentication (one of: Managed Identity, Service Principal env vars, or Azure CLI)

### 1. Provision Infrastructure

```bash
cd infrastructure
terraform init
terraform apply
```

### 2. Initialize Agents in Foundry

```bash
# Set auth (choose one)
export AZURE_TENANT_ID="<tenant>"
export AZURE_CLIENT_ID="<client>"
export AZURE_CLIENT_SECRET="<secret>"

cd src/agent_init
dotnet run -- --endpoint="<FOUNDRY_ENDPOINT>" --model="gpt-4.1"
```

This registers all 7 agents (6 specialized + orchestrator) in Foundry Agent Service.

### 3. Run the Web Application

```bash
cd src
dotnet run --urls "http://localhost:8081"
```

### 4. Use the Application

1. Open `http://localhost:8081` in a browser
2. Select a loan application from the dropdown
3. Click **Run Agent Workflow** — watch S01–S10 steps animate
4. Review the AI-generated recommendation with markdown-rendered rationale
5. Examine enrichment data panels (credit, income, fraud, key factors, policy hits)
6. Use decision controls to APPROVE, APPROVE WITH CONDITIONS, or DECLINE
7. JSON output files are written to `output/`

---

## Output Files

The workflow produces four JSON files in the `output/` directory:

| File | Contents |
|------|----------|
| `loan_application_prepared.json` | Enriched application with all data from S01–S08 |
| `workflow_run_log.json` | Ordered S01–S10 steps with timestamps, Foundry thread/run IDs |
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
    "OrchestratorAgentName": "loan_orchestrator"
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
| `GPT_DEPLOYMENT` | Model deployment name (default: `gpt-4.1`) |

---

## Key Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Agents.AI.AzureAI.Persistent` | 1.0.0-preview | Microsoft Agent Framework — `PersistentAgentsClient`, `AIAgent`, `RunAsync` |
| `Azure.AI.Agents.Persistent` | (transitive) | Foundry Agent Service SDK — `FunctionToolDefinition`, `ConnectedAgentToolDefinition` |
| `Azure.Identity` | 1.18.0 | Entra ID authentication (ManagedIdentity, Environment, AzureCli) |
| `CsvHelper` | 33.0.1 | CSV data file parsing |

---

## License

This project is licensed under the [MIT License](LICENSE).

---

## Fallback Behavior

When Foundry Agent Service is unavailable (no endpoint configured, auth fails, or agent not found), the orchestrator gracefully falls back to **local rule-based mode**:

- S01–S08 execute via direct `LoanAgentPlugins` calls
- S09 uses the rule-based `UnderwritingService` for recommendation (no LLM rationale)
- The workflow log indicates `execution_mode: "Local Rule-based"`

This ensures the application is always functional, even without Azure connectivity.
