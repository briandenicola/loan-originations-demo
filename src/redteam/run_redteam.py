#!/usr/bin/env python3
"""
Red Team Agent — Azure AI Foundry
Runs cloud-based AI red teaming evaluations against Loan Origination agents.

Uses the OpenAI Evals API + Foundry Evaluation Taxonomies to create and run
red teaming evaluations against Foundry agents.

NOTE: Red team evaluation runs require a supported region (e.g., eastus2,
westus3). Canada East is NOT currently supported.
"""

import argparse
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

DEFAULT_PROJECT_ENDPOINT = (
    "https://mayfly-47401-foundry.services.ai.azure.com/api/projects/mayfly-47401-project-workflow"
)

DEFAULT_AGENTS = [
    "credit-profile-agent",
    "income-verification-agent",
    "fraud-screening-agent",
    "policy-evaluation-agent",
    "pricing-agent",
    "underwriting-recommendation-agent",
]

API_VERSION = "2025-05-15-preview"

POLL_INTERVAL_SECONDS = 15
POLL_TIMEOUT_SECONDS = 900


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    """Parse CLI arguments with environment-variable fallbacks."""
    parser = argparse.ArgumentParser(
        description="Run AI red teaming evaluations against Loan Origination agents.",
    )
    parser.add_argument(
        "--project-endpoint",
        default=os.environ.get("PROJECT_ENDPOINT", DEFAULT_PROJECT_ENDPOINT),
        help=(
            "Azure AI Foundry project endpoint URL. "
            "Can also be set via PROJECT_ENDPOINT env var or sourced from "
            "'terraform output -raw FOUNDRY_NEXTGEN_ENDPOINT'. "
            "(default: %(default)s)"
        ),
    )
    parser.add_argument(
        "--agents",
        default=os.environ.get("AGENT_NAMES", ""),
        help="Comma-separated agent names to test (default: all 6 specialist agents).",
    )
    parser.add_argument(
        "--attack-strategies",
        default=os.environ.get("ATTACK_STRATEGIES", "Flip,Base64,IndirectJailbreak"),
        help="Comma-separated attack strategies (default: %(default)s).",
    )
    parser.add_argument(
        "--num-turns",
        type=int,
        default=int(os.environ.get("NUM_TURNS", "5")),
        help="Multi-turn conversation depth (default: %(default)s).",
    )
    parser.add_argument(
        "--model-deployment",
        default=os.environ.get("MODEL_DEPLOYMENT_NAME", "gpt-4.1"),
        help="Model deployment name for evaluators (default: %(default)s).",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parent.parent.parent / "output",
        help="Directory for result files (default: %(default)s).",
    )
    return parser.parse_args(argv)


def banner(cfg: argparse.Namespace):
    print("=" * 70)
    print("  🔴 AI Red Teaming Agent — Azure AI Foundry")
    print("  Loan Origination Specialist Agent Security Evaluation")
    print("=" * 70)
    print(f"  Project:     {cfg.project_endpoint}")
    print(f"  Agents:      {', '.join(cfg.agent_names)}")
    print(f"  Attacks:     {', '.join(cfg.attack_strategies)}")
    print(f"  Turns:       {cfg.num_turns}")
    print(f"  Model:       {cfg.model_deployment}")
    print(f"  Output:      {cfg.output_dir}")
    print("=" * 70)
    print()


def resolve_agent_version(project_client: AIProjectClient, agent_name: str, project_endpoint: str) -> str:
    """Resolve the latest version string for a named agent via REST."""
    req = HttpRequest(
        method="GET",
        url=f"{project_endpoint}/agents/{agent_name}?api-version={API_VERSION}",
    )
    resp = project_client.send_request(req)
    if resp.status_code != 200:
        raise ValueError(f"Agent '{agent_name}' not found (HTTP {resp.status_code})")
    data = resp.json()
    latest = data.get("versions", {}).get("latest", {})
    return latest.get("version", "1")


def create_eval(oai_client, agent_name: str, model_deployment: str) -> str:
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
                "initialization_parameters": {"deployment_name": model_deployment},
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


def create_run(oai_client, eval_id: str, agent_name: str, agent_version: str, taxonomy_id: str, *, attack_strategies: list[str], num_turns: int) -> str:
    """Create a red teaming run with attack strategies."""
    print(f"  🚀 Starting run for '{agent_name}'...")
    print(f"     Attacks: {', '.join(attack_strategies)}")
    print(f"     Turns:   {num_turns}")

    target = AzureAIAgentTarget(name=agent_name, version=agent_version)

    eval_run = oai_client.evals.runs.create(
        eval_id=eval_id,
        name=f"Red Team Run — {agent_name}",
        data_source={
            "type": "azure_ai_red_team",
            "item_generation_params": {
                "type": "red_team_taxonomy",
                "attack_strategies": attack_strategies,
                "num_turns": num_turns,
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


def fetch_results(oai_client, eval_id: str, run_id: str, agent_name: str, output_dir: Path) -> list:
    """Fetch and save output items from a completed run."""
    print(f"  📥 Fetching results for '{agent_name}'...")
    items = list(oai_client.evals.runs.output_items.list(run_id=run_id, eval_id=eval_id))

    output_path = output_dir / f"redteam_eval_output_items_{agent_name}.json"
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


def run_redteam_for_agent(project_client: AIProjectClient, agent_name: str, cfg: argparse.Namespace) -> dict:
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
        agent_version = resolve_agent_version(project_client, agent_name, cfg.project_endpoint)
        result["agent_version"] = agent_version
        print(f"  📌 Agent version: {agent_version}")

        oai = project_client.get_openai_client()

        # Step 1: Create eval group
        eval_id = create_eval(oai, agent_name, cfg.model_deployment)
        result["eval_id"] = eval_id

        # Step 2: Create taxonomy
        taxonomy_id = create_taxonomy(project_client, agent_name, agent_version)
        result["taxonomy_id"] = taxonomy_id

        # Step 3: Create and start run
        run_id = create_run(
            oai, eval_id, agent_name, agent_version, taxonomy_id,
            attack_strategies=cfg.attack_strategies,
            num_turns=cfg.num_turns,
        )
        result["run_id"] = run_id

        # Step 4: Poll for completion
        status = poll_run(oai, eval_id, run_id)
        result["status"] = status

        # Step 5: Fetch results
        if status == "completed":
            items = fetch_results(oai, eval_id, run_id, agent_name, cfg.output_dir)
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
    args = parse_args()

    # Resolve list values from comma-separated strings
    args.agent_names = (
        [a.strip() for a in args.agents.split(",") if a.strip()]
        if args.agents
        else DEFAULT_AGENTS
    )
    args.attack_strategies = [s.strip() for s in args.attack_strategies.split(",") if s.strip()]
    args.output_dir.mkdir(exist_ok=True)

    banner(args)

    credential = DefaultAzureCredential()
    project_client = AIProjectClient(endpoint=args.project_endpoint, credential=credential)
    print("🔐 Authenticated via DefaultAzureCredential")
    print()

    summary = {
        "project_endpoint": args.project_endpoint,
        "attack_strategies": args.attack_strategies,
        "num_turns": args.num_turns,
        "started_at": datetime.now(timezone.utc).isoformat(),
        "agents": [],
    }

    for agent_name in args.agent_names:
        agent_name = agent_name.strip()
        if not agent_name:
            continue
        result = run_redteam_for_agent(project_client, agent_name, args)
        summary["agents"].append(result)

        # Stop early if region is not supported
        if result.get("status") == "region_not_supported":
            print("\n  🛑 Stopping — region not supported for red team evaluations.")
            break

    summary["completed_at"] = datetime.now(timezone.utc).isoformat()

    summary_path = args.output_dir / "redteam_summary.json"
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
