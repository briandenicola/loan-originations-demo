#!/usr/bin/env python3
"""
Red Team Agent — Azure AI Foundry
Runs cloud-based AI red teaming evaluations against Loan Origination agents.

Uses the beta.red_teams API from azure-ai-projects SDK 2.0 to create and
run red teaming evaluations against Foundry agents.
"""

import json
import os
import time
from datetime import datetime, timezone
from pathlib import Path

from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import (
    AttackStrategy,
    AzureAIAgentTarget,
    RedTeam,
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

# Maps user-friendly names to SDK enum values
STRATEGY_MAP = {
    "Flip": AttackStrategy.FLIP,
    "Base64": AttackStrategy.BASE64,
    "IndirectJailbreak": AttackStrategy.INDIRECT_JAILBREAK,
    "Jailbreak": AttackStrategy.JAILBREAK,
    "Crescendo": AttackStrategy.CRESCENDO,
    "Baseline": AttackStrategy.BASELINE,
    "ROT13": AttackStrategy.ROT13,
    "Leetspeak": AttackStrategy.LEETSPEAK,
    "UnicodeSubstitution": AttackStrategy.UNICODE_SUBSTITUTION,
    "MultiTurn": AttackStrategy.MULTI_TURN,
}

AGENT_NAMES = os.environ.get("AGENT_NAMES", "").split(",") if os.environ.get("AGENT_NAMES") else DEFAULT_AGENTS
RAW_STRATEGIES = os.environ.get("ATTACK_STRATEGIES", "Flip,Base64,IndirectJailbreak").split(",")
ATTACK_STRATEGIES = [STRATEGY_MAP.get(s.strip(), s.strip()) for s in RAW_STRATEGIES]
NUM_TURNS = int(os.environ.get("NUM_TURNS", "5"))
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
    print(f"  Attacks:     {', '.join(str(s) for s in ATTACK_STRATEGIES)}")
    print(f"  Turns:       {NUM_TURNS}")
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

        # Build target
        target = AzureAIAgentTarget(name=agent_name, version=agent_version)

        # Create and start the red team
        print(f"  🚀 Creating red team run...")
        print(f"     Attacks:    {', '.join(str(s) for s in ATTACK_STRATEGIES)}")
        print(f"     Turns:      {NUM_TURNS}")
        print(f"     Categories: ProhibitedActions")

        red_team = project_client.beta.red_teams.create(
            red_team=RedTeam(
                name=f"redteam-{agent_name}",
                display_name=f"Red Team — {agent_name}",
                target=target,
                attack_strategies=ATTACK_STRATEGIES,
                risk_categories=[RiskCategory.PROHIBITED_ACTIONS],
                num_turns=NUM_TURNS,
            )
        )

        rt_name = red_team.name if hasattr(red_team, "name") else str(red_team)
        rt_status = getattr(red_team, "status", "unknown")
        print(f"     Created: {rt_name} (status: {rt_status})")
        result["red_team_name"] = rt_name

        # Poll for completion
        print(f"  ⏳ Polling for completion...")
        start = time.monotonic()

        while True:
            fetched = project_client.beta.red_teams.get(name=rt_name)
            status = getattr(fetched, "status", "unknown")
            elapsed = int(time.monotonic() - start)
            print(f"     [{elapsed:>4}s] Status: {status}")

            if status in ("completed", "Completed", "failed", "Failed", "canceled", "Canceled"):
                result["status"] = status.lower()
                break

            if elapsed > POLL_TIMEOUT_SECONDS:
                print(f"  ⚠️  Timeout after {POLL_TIMEOUT_SECONDS}s")
                result["status"] = f"timeout ({status})"
                break

            time.sleep(POLL_INTERVAL_SECONDS)

        # Save the red team result
        output_path = OUTPUT_DIR / f"redteam_{agent_name}.json"
        serialized = _to_serializable(fetched)
        with open(output_path, "w") as f:
            json.dump(serialized, f, indent=2, default=str)
        print(f"  📥 Results saved → {output_path}")
        result["output_file"] = str(output_path)

    except Exception as e:
        print(f"  ❌ Error: {e}")
        result["error"] = str(e)

    result["completed_at"] = datetime.now(timezone.utc).isoformat()
    return result


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


def main():
    banner()

    credential = DefaultAzureCredential()
    project_client = AIProjectClient(endpoint=PROJECT_ENDPOINT, credential=credential)
    print("🔐 Authenticated via DefaultAzureCredential")
    print()

    summary = {
        "project_endpoint": PROJECT_ENDPOINT,
        "attack_strategies": [str(s) for s in ATTACK_STRATEGIES],
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

    summary["completed_at"] = datetime.now(timezone.utc).isoformat()

    summary_path = OUTPUT_DIR / "redteam_summary.json"
    with open(summary_path, "w") as f:
        json.dump(summary, f, indent=2, default=str)

    print()
    print("=" * 70)
    print("  📊 Red Teaming Complete")
    print("=" * 70)
    for r in summary["agents"]:
        icon = "✅" if r["status"] == "completed" else "❌"
        print(f"  {icon} {r['agent']}: {r['status']}")
    print()
    print(f"  Summary saved → {summary_path}")
    print()


if __name__ == "__main__":
    main()
