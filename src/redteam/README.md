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

Set environment variables or use defaults from the workflow project:

| Variable | Description | Default |
|----------|-------------|---------|
| `PROJECT_ENDPOINT` | Foundry project endpoint | From `appsettings.json` |
| `AGENT_NAMES` | Comma-separated agent names to test | All 6 specialist agents |
| `ATTACK_STRATEGIES` | Comma-separated attack strategies | `Flip,Base64,IndirectJailbreak` |
| `NUM_TURNS` | Multi-turn conversation depth | `5` |

## Usage

```bash
# Run red teaming against all agents
python run_redteam.py

# Target specific agents
AGENT_NAMES=credit-profile-agent,fraud-screening-agent python run_redteam.py

# Custom attack strategies
ATTACK_STRATEGIES=Flip,Base64 NUM_TURNS=3 python run_redteam.py
```

Or via Taskfile:

```bash
task redteam:run
```

## Output

Results are saved to `output/` as JSON files:
- `redteam_eval_output_items_{agent_name}.json` — per-agent evaluation results
- `redteam_summary.json` — consolidated summary across all agents

## Reference

- [Azure AI Red Teaming in the Cloud](https://learn.microsoft.com/en-us/azure/foundry/how-to/develop/run-ai-red-teaming-cloud)
- [Agent Red Teaming Sample](https://aka.ms/agent-redteam-sample)
