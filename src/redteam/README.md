# Red Team Agent — Azure AI Foundry

Automated AI red teaming for the Loan Origination specialist agents using Azure AI Foundry's built-in Red Teaming Agent.

## Overview

This tool runs cloud-based red teaming evaluations against the Foundry agents deployed in the workflow project. It tests each agent for:

- **Prohibited Actions** — attempts to make the agent perform unauthorized actions
- **Task Adherence** — verifies the agent stays within its defined role
- **Sensitive Data Leakage** — probes for unintended data disclosure

Attack strategies include prompt injection techniques (Flip, Base64, IndirectJailbreak) over multi-turn conversations.

## Prerequisites

- Python 3.10+
- Azure AI Foundry project with deployed agents
- **Azure AI User** role on the Foundry project
- Authenticated via `az login` (or Managed Identity / Service Principal)

## Setup

```bash
cd src/redteam
pip install -r requirements.txt
```

## Configuration

All options can be set via CLI arguments, environment variables, or Taskfile (which sources from Terraform output). CLI arguments take precedence over environment variables.

| CLI Argument | Env Variable | Description | Default |
|---|---|---|---|
| `--project-endpoint` | `PROJECT_ENDPOINT` | Foundry project endpoint | Hardcoded workflow project |
| `--agents` | `AGENT_NAMES` | Comma-separated agent names to test | All 6 specialist agents |
| `--attack-strategies` | `ATTACK_STRATEGIES` | Comma-separated attack strategies | `Flip,Base64,IndirectJailbreak` |
| `--num-turns` | `NUM_TURNS` | Multi-turn conversation depth | `5` |
| `--model-deployment` | `MODEL_DEPLOYMENT_NAME` | Model deployment for evaluators | `gpt-4.1` |
| `--output-dir` | — | Directory for result files | `output/` |

## Usage

```bash
# Run via Taskfile (auto-reads endpoint from Terraform output)
task redteam:run

# Run directly with explicit endpoint from Terraform
python run_redteam.py --project-endpoint "$(terraform -chdir=./infrastructure output -raw FOUNDRY_NEXTGEN_ENDPOINT)"

# Run with endpoint as environment variable
PROJECT_ENDPOINT=https://my-foundry.services.ai.azure.com/api/projects/my-project python run_redteam.py

# Target specific agents
python run_redteam.py --project-endpoint "$(terraform -chdir=./infrastructure output -raw FOUNDRY_NEXTGEN_ENDPOINT)" --agents credit-profile-agent,fraud-screening-agent

# Custom attack strategies
python run_redteam.py --attack-strategies Flip,Base64 --num-turns 3

# Show all options
python run_redteam.py --help
```

## Output

Results are saved to `output/` as JSON files:
- `redteam_eval_output_items_{agent_name}.json` — per-agent evaluation results
- `redteam_summary.json` — consolidated summary across all agents

## Reference

- [Azure AI Red Teaming in the Cloud](https://learn.microsoft.com/en-us/azure/foundry/how-to/develop/run-ai-red-teaming-cloud)
- [Agent Red Teaming Sample](https://aka.ms/agent-redteam-sample)
