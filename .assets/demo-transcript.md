# Loan Origination Demo — Presenter Transcript

**Duration:** 15 minutes  
**Presenters:**  
- **CVP** — Engineering Corporate Vice President (intro + handoff only)  
- **SE** — Technical Solution Engineer (you — delivers the entire demo)  

Stage directions are in *[brackets]*. Approximate timestamps are noted for pacing.

**Pre-staging notes:** Before the demo begins, have the following ready:
- Tab 1: The loan origination application (intake screen loaded, workflow NOT yet run)
- Tab 2: A completed workflow run with Human-in-the-Loop review screen visible
- Tab 3: Application Insights — live metrics or a recent trace from a prior workflow run
- Tab 4: GitHub Copilot CLI terminal session (ready to run)
- Tab 5: Agent ID screen in Agent365 portal showing one of your published agents
- Have `sample-data/Loan_Application_Julius_Caesar.pdf` accessible for the upload demo

---

## Stage 1 — CVP Introduction and Handoff (0:00–1:30)

**CVP:**

Thank you everyone for being here. What we're going to show you today is a working loan origination system powered by AI agents on Microsoft Foundry.

Here's the problem. When a loan application comes in today, a loan officer re-keys data from a PDF, pulls credit in one system, income in another, fraud in a third, looks up pricing, evaluates policy, writes a recommendation, and passes it to a supervisor. That takes hours. Sometimes days. And the audit trail is whatever the officer remembered to write down.

What we built replaces the assembly and analysis — not the officer. AI agents handle credit, income, fraud, policy, pricing, and synthesize a recommendation in thirty seconds. The officer still makes the final call, but now they have a complete, explainable recommendation with every data point in front of them.

I'm going to hand it over to [SE name] who built this system and will walk you through the entire demo.

---

## Stage 2 — Architecture and Application Demo (1:30–5:00)

**SE:**

Thanks [CVP name]. Let me start with the architecture and then show the application live.

### Architecture Overview

*[SE shows architecture diagram]*

Three layers: UI, API, orchestrator — running in Azure Container Apps. The orchestrator calls Microsoft Foundry where six AI agents are running — credit, income, fraud, policy, pricing, and underwriting. Each agent uses GPT-4.1 for speed; in production you'd use GPT-5.2 or 5.4 for stronger reasoning. Full monitoring through OpenTelemetry and Application Insights.

### Quick Workflow Run

*[SE shows the intake form]*

I've got sample data pre-loaded. Let me select Robert Chen — a twenty-two thousand dollar auto loan, sixty-month term.

*[SE selects, fields populate, clicks "Run Agent Workflow"]*

Step cards update in real time. Five enrichment calls fire — credit bureau, income verification, fraud signals, policy thresholds, pricing engine. In this demo those are backed by CSV data. In production, those are your real bureau connections, payroll services, fraud networks.

This takes about a minute because it's real Foundry work, not canned responses. Let me switch to a completed run.

### Human-in-the-Loop Review

*[SE switches to Tab 2 — completed workflow]*

This is the review screen. At the top — the recommendation with a confidence score and plain-language reasoning. Below — credit gauges, income data, fraud signals, policy compliance. Every data point traces back to a specific agent's analysis.

The officer can adjust terms and hit Recalculate — real Foundry agents re-evaluate, not a local calculation. When satisfied, they click Approve or Deny. Decision captured with reviewer ID, timestamp, recommendation, final terms — everything compliance needs to reconstruct this decision later.

---

## Stage 3 — Live Building with Copilot CLI (5:00–9:00)

**SE:**

Now I want to show you how this was built — and how fast you can add features.

You told us you want the ability to upload a PDF loan application, have it parsed, and populate the form. Let's build that right now.

*[SE switches to Tab 4 — Copilot CLI terminal]*

This is GitHub Copilot CLI — an AI-powered terminal agent. I give it a prompt, it reads the codebase and writes the code.

*[SE types the prompt]*

My prompt: "I need to update the UI to accept an uploaded PDF loan application. The uploaded PDF should be parsed and all data fields populated. Assume the PDF follows the same format as the sample data."

*[SE runs the prompt, narrates as Copilot works]*

Copilot is reading the codebase — controllers, models, frontend. It sees twenty-three fields on the LoanApplication model. It's looking at the sample PDFs to understand the format.

Now it's generating: a `PdfParsingService` for backend parsing, a new `POST /api/v1/applications/upload` endpoint, a drag-and-drop upload zone in the frontend, dependency injection wiring.

*[Code generation completes, SE runs `dotnet build`]*

Clean build. Zero errors. Let me test it.

*[SE switches to Tab 1, uploads Loan_Application_Julius_Caesar.pdf]*

Julius Caesar — a hundred million dollars for a palace on the Palatine Hill. The PDF is parsed, all twenty-three fields populated. Application number, name, amount, term, income, obligations — all extracted.

That entire feature — backend, API, frontend — built in one prompt.

### The Full Build Story

This entire system was built the same way. One starting prompt: *"I need to create a demo of an agent system based on the instructions.md file. This demo must showcase Microsoft Foundry. Let's build."*

Copilot CLI read the requirements, the CSV data, the OpenAPI spec. First turn — full ASP.NET Core application, working end-to-end. Then: "Create Terraform for Foundry" — full infrastructure. "Use Entra ID instead of keys" — managed identity auth. When the YAML workflow couldn't share context between agents, Copilot recommended the architectural pivot to the code-based coordinator you just saw.

The numbers: forty-plus conversational turns. Thirty-eight commits. Eighty-plus files. Twenty-five hundred lines of C#. Twelve hundred lines of Terraform. Hours, not weeks. For your teams, the cost of experimentation drops dramatically.

---

## Stage 4 — Red Team, Observability, and Governance (9:00–13:00)

**SE:**

Now the governance story — for financial institutions, this is the whole ball game.

### Red Team Testing

We built a Red Team agent using the Foundry Evals API. It adversarially tests every agent in this workflow — probing for prompt injection, jailbreaks, off-topic responses, and policy violations. You can run it against individual agents or the entire end-to-end workflow. It produces a scored report you can hand to your model risk management team. That's the kind of testing regulators increasingly expect, and it's built right into the platform.

### Application Insights

*[SE switches to Tab 3 — Application Insights]*

Here's what observability looks like. You're looking at traces from the workflow we ran. Every agent call — credit, income, fraud, policy, pricing, underwriting — shows up as a span in the distributed trace. Duration, dependencies, errors — all visible.

This is OpenTelemetry baked into the orchestrator. Every workflow run is observable from day one. You can build dashboards and alerts here, or export logs to any SIEM tool you use. Everything is API-accessible — custom governance dashboards, compliance reports, whatever your audit team needs.

### Agent365 and Agent ID

*[SE switches to Tab 5 — Agent ID screen in Agent365 portal]*

This is Agent365 — Microsoft's enterprise-wide governance layer for AI agents. The control plane above Foundry.

What you're looking at is one of our agents published in the Agent365 registry. Every agent — Foundry, Copilot Studio, or third-party — gets registered here with a unique Agent ID.

*[SE walks through the Agent ID screen]*

You can see the agent's identity, registration details, access policies, lifecycle status. IT applies access policies, lifecycle management, and conditional controls — the same way they manage employee identities today, but for AI agents.

The six agents we demoed — credit, income, fraud, policy, pricing, underwriting — each one appears in this registry. Your security team sees them alongside every other agent in the organization. They can audit instructions, review access policies, monitor behavior, flag anything unsanctioned. There's an Agent Map that visualizes relationships between agents, users, and data.

This matters because the number-one governance problem in AI right now is agent sprawl. Microsoft's research says nearly thirty percent of Fortune 500 AI agents are running without IT approval. Agent365 closes that gap. And because our Foundry agents already have Entra ID identities, they integrate into Agent365 natively. No retrofit. No migration.

---

## Stage 5 — Close (13:00–15:00)

**SE:**

Let me bring this home.

You saw a loan application come in, six AI agents analyze it in thirty seconds, and a loan officer make the final call with full explainability. Every step traced, every recommendation explained, every decision recorded.

You saw us build a brand-new feature live with Copilot CLI — in one prompt. This entire system was built the same way: forty prompts, hours not weeks.

You saw how agents are tested with red team evaluation, monitored through Application Insights, and governed through Agent365 with unique Agent IDs, access policies, and enterprise-wide visibility.

That's the Microsoft platform for regulated AI: Foundry for building and running agents. Agent365 for governing them. Entra ID for identity. Application Insights for observability. None of that exists in the AWS or wrapper-tool ecosystem.

The pattern works beyond loan origination — insurance underwriting, credit line reviews, KYC, account opening. Any workflow where you ingest, enrich, analyze, recommend, and let a human decide.

Let us help you build your next generation applications on Microsoft's AI platform.

Thank you. Happy to take your questions.

---

## Appendix: Presenter Notes

### Timing Checkpoints

| Time | You Should Be At |
|------|-----------------|
| 1:30 | CVP finished intro, handed off to SE |
| 3:00 | Architecture done, workflow running |
| 5:00 | App demo complete, transitioning to Copilot CLI |
| 7:00 | PDF upload built live, testing it |
| 9:00 | Build story complete, transitioning to governance |
| 11:00 | App Insights and Agent365 shown |
| 13:00 | Delivering close |
| 15:00 | Open for questions |

### Pre-Staged Tabs

| Tab | Content | Used In |
|-----|---------|---------|
| 1 | Loan origination app (intake screen) | Stage 2, 3 |
| 2 | Completed workflow run (review screen) | Stage 2 |
| 3 | Application Insights (trace from prior run) | Stage 4 |
| 4 | GitHub Copilot CLI terminal session | Stage 3 |
| 5 | Agent365 portal — Agent ID screen with published agent | Stage 4 |

### Files to Have Ready

| File | Purpose |
|------|---------|
| `sample-data/Loan_Application_Julius_Caesar.pdf` | Upload demo in Stage 3 |

### If the Workflow Takes Longer Than Expected

- "Each agent is doing genuine analysis in Foundry, not canned responses. Co-located in Azure, total workflow time drops to fifteen to twenty seconds."
- Continue narrating steps as they complete.

### If an Agent Fails

- "The system is designed for graceful degradation — remaining agents continue. In production, this triggers an alert in Application Insights."
- Continue the demo. The review dashboard still functions.

### If the Live Build Has Issues

- "This is real code generation against a real codebase — sometimes it needs a second pass."
- Fall back to the completed `upload-pdf` branch: `git checkout upload-pdf`

### Handling Questions

- **"Real credit bureau data?"** — "APIs backed by representative CSV data. In production, those connect to your existing integrations. Agent code doesn't change."

- **"Can we add agents?"** — "Register in Foundry, add one line to the coordinator. Designed to extend."

- **"Model updates?"** — "Agents reference a specific Foundry deployment. Deploy new version, test, cut over. Instructions don't change."

- **"Latency in production?"** — "Co-located with Foundry, fifteen to twenty seconds total. Three to five seconds per specialist."

- **"How does this compare to AWS?"** — Registered agent resources vs. anonymous Lambdas. Entra ID vs. IAM federation. Single-trace observability vs. stitching four services. Agent365 governance — nothing comparable exists.
