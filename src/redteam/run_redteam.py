#!/usr/bin/env python3
"""
Red Team Agent — Azure AI Foundry
Runs cloud-based AI red teaming evaluations against Loan Origination agents.

Uses the OpenAI Evals API + Foundry Evaluation Taxonomies to create and run
red teaming evaluations against Foundry agents.

NOTE: Red team evaluation runs require a supported region (e.g., eastus2,
westus3). Canada East is NOT currently supported.
"""

import json
import os
import time
from datetime import datetime, timezone
from pathlib import Path

from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import (
    AgentTaxonomyInput,
    AzureAIAgentTarget,
    EvaluationTaxonomy,
    RiskCategory,
)
from azure.core.rest import HttpRequest
from azure.identity import DefaultAzureCredential

# ── Configuration ─────────────────────────────────────────────────────────────

PROJECT_ENDPOINT = os.environ.get(
    "PROJECT_ENDPOINT",
    "https://ostrich-61739-foundry.services.ai.azure.com/api/projects/ostrich-61739-project-workflow",
)

DEFAULT_AGENTS = [
    "credit-profile-agent",
    "income-verification-agent",
    "fraud-screening-agent",
    "policy-evaluation-agent",
    "pricing-agent",
    "underwriting-recommendation-agent",
]

AGENT_NAMES = os.environ.get("AGENT_NAMES", "").split(",") if os.environ.get("AGENT_NAMES") else DEFAULT_AGENTS
ATTACK_STRATEGIES = os.environ.get("ATTACK_STRATEGIES", "Flip,Base64,IndirectJailbreak").split(",")
NUM_TURNS = int(os.environ.get("NUM_TURNS", "5"))
MODEL_DEPLOYMENT = os.environ.get("MODEL_DEPLOYMENT_NAME", "gpt-4.1")
API_VERSION = "2025-05-15-preview"

OUTPUT_DIR = Path(__file__).resolve().parent.parent.parent / "output"
OUTPUT_DIR.mkdir(exist_ok=True)

POLL_INTERVAL_SECONDS = 15
POLL_TIMEOUT_SECONDS = 900


def banner():
    print("=" * 70)
    print("  🔴 AI Red Teaming Agent — Azure AI Foundry")
    print("  Loan Origination Specialist Agent Security Evaluation")
    print("=" * 70)
    print(f"  Project:     {PROJECT_ENDPOINT}")
    print(f"  Agents:      {', '.join(AGENT_NAMES)}")
    print(f"  Attacks:     {', '.join(ATTACK_STRATEGIES)}")
    print(f"  Turns:       {NUM_TURNS}")
    print(f"  Model:       {MODEL_DEPLOYMENT}")
    print(f"  Output:      {OUTPUT_DIR}")
    print("=" * 70)
    print()


def resolve_agent_version(project_client: AIProjectClient, agent_name: str) -> str:
    """Resolve the latest version string for a named agent via REST."""
    req = HttpRequest(
        method="GET",
        url=f"{PROJECT_ENDPOINT}/agents/{agent_name}?api-version={API_VERSION}",
    )
    resp = project_client.send_request(req)
    if resp.status_code != 200:
        raise ValueError(f"Agent '{agent_name}' not found (HTTP {resp.status_code})")
    data = resp.json()
    latest = data.get("versions", {}).get("latest", {})
    return latest.get("version", "1")


def create_eval(oai_client, agent_name: str) -> str:
    """Create a red team eval group with built-in safety evaluators."""
    print(f"  📋 Creating eval for '{agent_name}'...")

    red_team = oai_client.evals.create(
        name=f"Red Team — {agent_name}",
        data_source_config={"type": "azure_ai_source", "scenario": "red_team"},
        testing_criteria=[
            {
                "type": "azure_ai_evaluator",
                "name": "Prohibited Actions",
                "evaluator_name": "builtin.prohibited_actions",
                "evaluator_version": "1",
            },
            {
                "type": "azure_ai_evaluator",
                "name": "Task Adherence",
                "evaluator_name": "builtin.task_adherence",
                "evaluator_version": "1",
                "initialization_parameters": {"deployment_name": MODEL_DEPLOYMENT},
            },
            {
                "type": "azure_ai_evaluator",
                "name": "Sensitive Data Leakage",
                "evaluator_name": "builtin.sensitive_data_leakage",
                "evaluator_version": "1",
            },
        ],
    )

    print(f"     Eval created: {red_team.id}")
    return red_team.id


def create_taxonomy(project_client: AIProjectClient, agent_name: str, agent_version: str) -> str:
    """Generate a prohibited-actions taxonomy for the target agent."""
    print(f"  🧬 Generating taxonomy for '{agent_name}' v{agent_version}...")

    target = AzureAIAgentTarget(name=agent_name, version=agent_version)

    taxonomy = project_client.beta.evaluation_taxonomies.create(
        name=agent_name,
        body=EvaluationTaxonomy(
            description=f"Red teaming taxonomy for {agent_name}",
            taxonomy_input=AgentTaxonomyInput(
                risk_categories=[RiskCategory.PROHIBITED_ACTIONS],
                target=target,
            ),
        ),
    )

    taxonomy_id = taxonomy.id if hasattr(taxonomy, "id") else str(taxonomy)
    print(f"     Taxonomy created: {taxonomy_id}")
    return taxonomy_id


def create_run(oai_client, eval_id: str, agent_name: str, agent_version: str, taxonomy_id: str) -> str:
    """Create a red teaming run with attack strategies."""
    print(f"  🚀 Starting run for '{agent_name}'...")
    print(f"     Attacks: {', '.join(ATTACK_STRATEGIES)}")
    print(f"     Turns:   {NUM_TURNS}")

    target = AzureAIAgentTarget(name=agent_name, version=agent_version)

    eval_run = oai_client.evals.runs.create(
        eval_id=eval_id,
        name=f"Red Team Run — {agent_name}",
        data_source={
            "type": "azure_ai_red_team",
            "item_generation_params": {
                "type": "red_team_taxonomy",
                "attack_strategies": ATTACK_STRATEGIES,
                "num_turns": NUM_TURNS,
                "source": {"type": "file_id", "id": taxonomy_id},
            },
            "target": target.as_dict(),
        },
    )

    print(f"     Run created: {eval_run.id} (status: {eval_run.status})")
    return eval_run.id


def poll_run(oai_client, eval_id: str, run_id: str) -> str:
    """Poll until the run completes, fails, or times out."""
    print(f"  ⏳ Polling run {run_id}...")
    start = time.monotonic()

    while True:
        run = oai_client.evals.runs.retrieve(run_id=run_id, eval_id=eval_id)
        elapsed = int(time.monotonic() - start)
        print(f"     [{elapsed:>4}s] Status: {run.status}")

        if run.status in ("completed", "failed", "canceled"):
            return run.status

        if elapsed > POLL_TIMEOUT_SECONDS:
            print(f"  ⚠️  Timeout after {POLL_TIMEOUT_SECONDS}s — run still {run.status}")
            return run.status

        time.sleep(POLL_INTERVAL_SECONDS)


def fetch_results(oai_client, eval_id: str, run_id: str, agent_name: str) -> list:
    """Fetch and save output items from a completed run."""
    print(f"  📥 Fetching results for '{agent_name}'...")
    items = list(oai_client.evals.runs.output_items.list(run_id=run_id, eval_id=eval_id))

    output_path = OUTPUT_DIR / f"redteam_eval_output_items_{agent_name}.json"
    with open(output_path, "w") as f:
        json.dump(_to_serializable(items), f, indent=2, default=str)

    print(f"     Saved {len(items)} items → {output_path}")
    return items


def _to_serializable(obj):
    """Convert SDK objects to JSON-serializable form."""
    if hasattr(obj, "as_dict"):
        return obj.as_dict()
    if hasattr(obj, "model_dump"):
        return obj.model_dump()
    if isinstance(obj, list):
        return [_to_serializable(i) for i in obj]
    if isinstance(obj, dict):
        return {k: _to_serializable(v) for k, v in obj.items()}
    return obj


def run_redteam_for_agent(project_client: AIProjectClient, agent_name: str) -> dict:
    """Run the full red teaming pipeline for a single agent."""
    print()
    print(f"{'─' * 60}")
    print(f"  🎯 Target: {agent_name}")
    print(f"{'─' * 60}")

    result = {
        "agent": agent_name,
        "started_at": datetime.now(timezone.utc).isoformat(),
        "status": "error",
    }

    try:
        # Resolve agent version
        agent_version = resolve_agent_version(project_client, agent_name)
        result["agent_version"] = agent_version
        print(f"  📌 Agent version: {agent_version}")

        oai = project_client.get_openai_client()

        # Step 1: Create eval group
        eval_id = create_eval(oai, agent_name)
        result["eval_id"] = eval_id

        # Step 2: Create taxonomy
        taxonomy_id = create_taxonomy(project_client, agent_name, agent_version)
        result["taxonomy_id"] = taxonomy_id

        # Step 3: Create and start run
        run_id = create_run(oai, eval_id, agent_name, agent_version, taxonomy_id)
        result["run_id"] = run_id

        # Step 4: Poll for completion
        status = poll_run(oai, eval_id, run_id)
        result["status"] = status

        # Step 5: Fetch results
        if status == "completed":
            items = fetch_results(oai, eval_id, run_id, agent_name)
            result["output_items_count"] = len(items)
        else:
            print(f"  ❌ Run ended with status: {status}")

    except Exception as e:
        error_msg = str(e)
        print(f"  ❌ Error: {error_msg}")
        result["error"] = error_msg

        if "not supported in" in error_msg and "region" in error_msg:
            print()
            print("  ⚠️  Red team evaluation is not available in this region.")
            print("     Deploy your Foundry project to a supported region")
            print("     (e.g., eastus2, westus3) and try again.")
            result["status"] = "region_not_supported"

    result["completed_at"] = datetime.now(timezone.utc).isoformat()
    return result


def main():
    banner()

    credential = DefaultAzureCredential()
    project_client = AIProjectClient(endpoint=PROJECT_ENDPOINT, credential=credential)
    print("🔐 Authenticated via DefaultAzureCredential")
    print()

    summary = {
        "project_endpoint": PROJECT_ENDPOINT,
        "attack_strategies": ATTACK_STRATEGIES,
        "num_turns": NUM_TURNS,
        "started_at": datetime.now(timezone.utc).isoformat(),
        "agents": [],
    }

    for agent_name in AGENT_NAMES:
        agent_name = agent_name.strip()
        if not agent_name:
            continue
        result = run_redteam_for_agent(project_client, agent_name)
        summary["agents"].append(result)

        # Stop early if region is not supported
        if result.get("status") == "region_not_supported":
            print("\n  🛑 Stopping — region not supported for red team evaluations.")
            break

    summary["completed_at"] = datetime.now(timezone.utc).isoformat()

    summary_path = OUTPUT_DIR / "redteam_summary.json"
    with open(summary_path, "w") as f:
        json.dump(summary, f, indent=2, default=str)

    print()
    print("=" * 70)
    print("  📊 Red Teaming Summary")
    print("=" * 70)
    for r in summary["agents"]:
        icon = "✅" if r["status"] == "completed" else "⚠️" if r["status"] == "region_not_supported" else "❌"
        items = r.get("output_items_count", "—")
        print(f"  {icon} {r['agent']}: {r['status']} ({items} items)")
    print()
    print(f"  Summary saved → {summary_path}")
    print()


if __name__ == "__main__":
    main()
