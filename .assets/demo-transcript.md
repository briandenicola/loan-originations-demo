# Loan Origination Demo — Presenter Transcript

**Duration:** 20 minutes  
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
- Tab 6: README "Built with Copilot CLI" section or a slide with build stats
- Have `sample-data/Loan_Application_Julius_Caesar.pdf` accessible for the upload demo

---

## Stage 1 — CVP Introduction and Handoff (0:00–2:00)

**CVP:**

Thank you everyone for being here. What we're going to show you today is a working loan origination system powered by AI agents on Microsoft Foundry.

Here's the problem we're solving. When a loan application comes in today, a loan officer receives a PDF. They re-key the data. They pull a credit report in one system. Income verification in another. Fraud check in a third. They look up pricing tables. They evaluate policy rules. They write up a recommendation. Then they pass it to a supervisor. That process takes hours. Sometimes days. And the audit trail is whatever the officer remembered to write down.

What we built replaces the assembly and analysis — not the officer. AI agents pull the credit report, verify income, check for fraud, evaluate policy, compute pricing, and synthesize a recommendation in thirty seconds. The officer still makes the final decision, but now they have a complete, explainable recommendation with every supporting data point in front of them.

I'm going to hand it over to [SE name] who built this system and will walk you through the entire demo — the application, how it was built, and why we think Microsoft is the right platform for this kind of work.

---

## Stage 2 — Architecture and Quick Application Demo (2:00–7:00)

**SE:**

Thanks [CVP name]. Let me start with the architecture and then jump right into the application.

### Architecture Overview

*[SE switches to Tab 1 — the application, scrolls to or shows architecture diagram]*

The application runs in Azure Container Apps and has three layers: UI, API, orchestrator. The orchestrator calls Microsoft Foundry where six AI agents — credit, income, fraud, policy, pricing, and underwriting — are running. Each agent uses GPT-4.1. We picked 4.1 for this demo because of its speed, but in production you'd likely use GPT-5.2 or 5.4 for better reasoning. At the bottom of the diagram, we have a full monitoring stack through OpenTelemetry and Application Insights.

### Quick Workflow Run

*[SE shows the intake form]*

I've got the application loaded. We have a pull-down pre-populated with your supplied sample data. Let me select Robert Chen's request — a twenty-two thousand dollar auto loan with a sixty-month term.

*[SE selects the application, fields populate]*

The system has extracted all the fields from the loan application — identity, loan details, income, obligations. Let me kick off the agent workflow.

*[SE clicks "Run Agent Workflow"]*

The step cards update in real time as agents complete. Five enrichment calls fire off — credit bureau, income verification, fraud signals, policy thresholds, pricing engine. In this demo those APIs are backed by CSV data. In production, those are your real bureau connections, payroll services, fraud network.

*[Steps running]*

Each specialist agent does real work in Foundry — not returning a canned response. The credit agent evaluates bureau data, delinquencies, utilization. The underwriting agent receives a compiled brief from all five agents and generates the final recommendation.

This will take a minute to run through all the agents, so let me switch to a completed run to show the output.

### Human-in-the-Loop Review

*[SE switches to Tab 2 — completed workflow with review screen]*

This is the review screen — what the loan officer sees. At the top — the recommendation with a confidence percentage. The AI explains its reasoning in plain language. Below that — credit gauges, income data, fraud signals, policy compliance — everything the officer needs to make a decision.

*[SE points to the explanation factors]*

Key factors driving the recommendation are listed here. The officer can see exactly which policy rules were triggered and why. Every data point traces back to a specific agent's analysis.

The officer can adjust terms — change the loan amount, change the term — and hit Recalculate. The system re-runs the pricing and underwriting agents with the new parameters. These are real Foundry agents re-evaluating, not a local calculation.

When the officer is satisfied, they click Approve or Deny. Decision captured — reviewer ID, timestamp, the recommendation at decision time, final terms. Everything a compliance team needs to reconstruct this decision six months from now.

*[SE briefly shows the output folder in Explorer]*

All output is captured in a structured output directory.

---

## Stage 3 — Live Building with Copilot CLI: PDF Upload Feature (7:00–12:00)

**SE:**

Now I want to show you something different. You've seen the application. But I want to show you how this was built — and more importantly, how fast you can add new features.

The client — you — told us you want the ability to upload a PDF loan application, have it parsed, and populate the form. Let's build that right now.

*[SE switches to Tab 4 — Copilot CLI terminal]*

This is GitHub Copilot CLI. It's an AI-powered terminal agent. I give it a prompt, and it reads the codebase, understands the architecture, and writes the code.

*[SE types the prompt]*

My prompt: "I need to update the UI to accept an uploaded PDF loan application. The uploaded PDF should be parsed and all data fields populated. You can assume the uploaded PDF follows the same format as the sample data."

*[SE runs the prompt, narrates as Copilot works]*

Watch what happens. Copilot is reading the codebase — the controllers, the models, the frontend. It's seeing there are twenty-three fields on the LoanApplication model. It's looking at the sample PDFs to understand the format.

Now it's generating code. A new `PdfParsingService` — this is the backend parser. It's using regex-based extraction mapped to every field in the model. It's adding a new API endpoint — `POST /api/v1/applications/upload`. It's updating the frontend with a drag-and-drop upload zone. It's wiring up the dependency injection.

*[Code generation completes]*

Let me build and verify.

*[SE runs `dotnet build`]*

Clean build. Zero errors. Now let me test it with a PDF I prepared earlier.

*[SE switches to Tab 1, shows the new upload zone in the UI]*

You can see the upload zone — drag and drop or browse. Let me upload Julius Caesar's loan application. Yes, Julius Caesar — a hundred million dollars for a palace on the Palatine Hill.

*[SE uploads Loan_Application_Julius_Caesar.pdf]*

The PDF is parsed, and all twenty-three fields are populated. Application number, name, loan amount, term, income, obligations — all extracted from the PDF. The loan officer can review and edit before submitting.

That entire feature — backend parser, API endpoint, frontend upload zone, drag-and-drop handling — was built in one prompt. No boilerplate. No scaffolding. Copilot read the existing code, understood the data model, and generated a complete, working feature.

### The Build Story

*[SE switches to Tab 6 — README or stats slide]*

And that's not a one-off. This entire system was built the same way. It started with one prompt: *"I need to create a demo of an agent system based on the instructions.md file. This demo must showcase Microsoft Foundry. Let's build."*

Copilot CLI read the requirements, the CSV data, and the OpenAPI spec. First turn — full ASP.NET Core application. Controllers, data services, Chart.js frontend. Working end-to-end.

The conversation continued. "Create Terraform for Foundry" — full infrastructure module. "Use Entra ID instead of keys" — managed identity auth. When the YAML workflow couldn't share context between agents, Copilot tried multiple fixes, then recommended the architectural pivot to the code-based coordinator you just saw.

The numbers: over forty conversational turns. Thirty-eight commits. Eighty-plus files. Twenty-five hundred lines of C#. Twelve hundred lines of Terraform. Three SDK migrations. Two architectural pivots. Hours, not weeks.

For your engineering teams, this means the cost of experimentation drops dramatically. You try ten approaches, keep what works — because each one takes hours to build, not sprints.

---

## Stage 4 — Agent ID and Agent365 Governance (12:00–15:00)

**SE:**

Now let me show you the governance story — because for financial institutions, this is the whole ball game.

### Agent Identity in Foundry

Every agent we built is a registered resource in Microsoft Foundry. They have versioned instruction sets. They're secured with Entra ID — the same identity system your organization already runs. A platform team can inventory every agent, audit its instructions, and control access through the same role-based policies they use for everything else.

### Agent365 and Agent ID

*[SE switches to Tab 5 — Agent ID screen in Agent365 portal]*

This is Agent365 — Microsoft's enterprise-wide governance layer for AI agents. Think of it as the control plane above Foundry.

What you're looking at is one of our agents published in the Agent365 registry. Every agent — whether it's built in Foundry, Copilot Studio, or a third-party framework — gets registered here. Each agent receives a unique Agent ID.

*[SE walks through the Agent ID screen]*

You can see the agent's identity, its registration details, access policies, and lifecycle status. IT applies access policies, lifecycle management, and conditional controls — the same way they manage employee identities today, but for AI agents.

For the system we just demoed — the six agents: credit, income, fraud, policy, pricing, underwriting — each one appears in this registry. Your security team can see them alongside every other agent in the organization. They can audit the instructions, review the access policies, monitor behavior, and flag anything unsanctioned.

There's an Agent Map that visualizes the relationships between agents, users, and data — so you can see exactly which agents are accessing which data sources and who authorized them.

This matters because the number-one governance problem in AI right now is agent sprawl. Microsoft's own research says nearly thirty percent of Fortune 500 AI agents are running without IT approval. Agent365 closes that gap. And because the agents we built in Foundry are already registered resources with Entra ID identities, they integrate into Agent365 natively. There's no retrofit. No migration. You're building on the platform that the governance layer is designed for.

### Red Team Testing

One more thing on governance. We also built a Red Team agent using the Foundry Evals API. It's designed to adversarially test every agent in this workflow — probing for prompt injection, jailbreaks, off-topic responses, and policy violations. You can run it against individual agents or the entire end-to-end workflow. It produces a scored report you can hand to your model risk management team. That's the kind of testing that regulators increasingly expect, and it's built right into the platform.

---

## Stage 5 — Observability and Platform Value (15:00–18:00)

**SE:**

*[SE switches to Tab 3 — Application Insights]*

Let me show you what observability looks like. You're looking at the traces from the workflow we ran earlier. Every agent call — credit, income, fraud, policy, pricing, underwriting — shows up as a span in the distributed trace. You can see the duration of each call, the dependencies, any errors.

This is OpenTelemetry flowing from the orchestrator through each Foundry agent invocation. This isn't something we added after the fact. The tracing is baked into the orchestrator. Every workflow run is observable from day one.

You can create dashboards and alerts within Application Insights, or the logs can be exported to any logging or SIEM tool you use for monitoring. Everything is exposed through APIs — every data point in this system is API-accessible, which means you can build custom governance dashboards, compliance reports, whatever your audit team needs.

### Why Microsoft for AI Workloads

Let me be direct about the value proposition. When you build AI workflows on Microsoft, you get four things that matter in financial services:

**First — enterprise identity.** Entra ID end to end. No API keys to rotate. No secrets to manage. The same identity governance your organization already runs extends to your AI agents. Compare that to anonymous Lambda functions behind an AWS Bedrock endpoint.

**Second — integrated observability.** OpenTelemetry traces from your orchestrator through your agents into Application Insights. One platform, one trace, one place to look when something goes wrong or when audit asks for evidence.

**Third — a control plane for AI.** Foundry is not just a model endpoint. It's where your agents live, where their instructions are versioned, where their access is governed, where their behavior is monitored. That's the difference between running AI and governing AI.

**Fourth — enterprise agent governance.** Agent365 provides the governance layer across every agent in the organization. Foundry agents, Copilot Studio agents, third-party agents — all visible, all governed, all auditable in one place.

None of that exists in the AWS or wrapper-tool ecosystem.

---

## Stage 6 — Close (18:00–20:00)

**SE:**

Let me bring this home.

You saw what this system does. A loan application comes in. Six AI agents analyze it in thirty seconds — credit, income, fraud, policy, pricing — and a final underwriter synthesizes their work into a recommendation with written reasoning. A loan officer reviews it, adjusts the terms, watches the AI recalculate, and makes the final call. Every step is traced. Every recommendation is explained. Every decision is recorded.

You saw how it was built. Copilot CLI — requirements document to production-ready system in forty prompts. And you watched us add a brand-new feature live in minutes.

You saw how agents are governed. Agent365 gives you enterprise-wide visibility and control over every AI agent in the organization — with unique Agent IDs, access policies, and lifecycle management.

And you saw where it runs. Azure AI Foundry — agents as governed resources, Entra ID identity, deterministic orchestration, full observability, infrastructure as code.

This is what AI-powered financial services looks like when it's built on the right platform. The agents work within your existing systems. The recommendations are explainable. The decisions are auditable. The governance extends from the individual agent in Foundry to the enterprise-wide registry in Agent365. And the platform gives you the control that regulated lending demands.

The pattern works beyond loan origination — insurance underwriting, credit line reviews, KYC, account opening. Any workflow where you ingest, enrich, analyze, recommend, and let a human decide.

The next step is straightforward. Let us help you develop your next generation applications on top of our Microsoft AI platform.

Thank you. We'd love to take your questions.

---

## Appendix: Presenter Notes

### Timing Checkpoints

| Time | You Should Be At |
|------|-----------------|
| 2:00 | CVP finished intro, handed off to SE |
| 4:00 | Architecture overview complete, starting workflow run |
| 7:00 | Application demo complete, transitioning to Copilot CLI |
| 10:00 | PDF upload feature built live, testing it |
| 12:00 | Build story complete, transitioning to Agent365 |
| 15:00 | Agent ID and governance complete, moving to observability |
| 18:00 | Delivering close |
| 20:00 | Open for questions |

### Pre-Staged Tabs

| Tab | Content | Used In |
|-----|---------|---------|
| 1 | Loan origination app (intake screen) | Stage 2 |
| 2 | Completed workflow run (review screen) | Stage 2 |
| 3 | Application Insights (trace from prior run) | Stage 5 |
| 4 | GitHub Copilot CLI terminal session | Stage 3 |
| 5 | Agent365 portal — Agent ID screen with published agent | Stage 4 |
| 6 | README "Built with Copilot CLI" section or stats slide | Stage 3 |

### Files to Have Ready

| File | Purpose |
|------|---------|
| `sample-data/Loan_Application_Julius_Caesar.pdf` | Upload demo in Stage 3 |

### If the Workflow Takes Longer Than Expected

- "Each agent is doing genuine analysis in Foundry, not returning canned responses. In a Container Apps deployment in the same Azure region, total workflow time drops to fifteen to twenty seconds."
- Continue narrating each step as it completes. The real-time updates give you natural talking points.

### If an Agent Fails

- "The system is designed for graceful degradation — remaining agents continue. In production, this triggers an alert in Application Insights. The workflow log captures the failure for diagnostics."
- Continue the demo. The review dashboard will still function.

### If the Live Build Has Issues

- "This is real code generation against a real codebase — sometimes it needs a second pass. That's the nature of AI-assisted development. The key is that Copilot understands the entire project context and generates code that fits."
- Fall back to showing the completed upload-pdf branch: `git checkout upload-pdf`

### Handling Questions During the Demo

- **"Is this using real credit bureau data?"** — "The APIs are backed by representative CSV data. In production, those endpoints connect to your existing integrations. The agent code doesn't change — only the data source."

- **"Can we add more agents?"** — "Register a new agent in Foundry, add one line to the coordinator. The architecture is designed to extend."

- **"How do you handle model updates?"** — "Agents reference a specific model deployment in Foundry. Deploy a new version, test, cut over. Agent instructions don't change. Foundry gives you versioning and rollback."

- **"What about latency in production?"** — "In a Container Apps deployment co-located with Foundry, total workflow time drops to fifteen to twenty seconds. Each specialist call is three to five seconds."

- **"How does this compare to AWS?"** — Key points: registered agent resources vs. anonymous Lambdas, Entra ID vs. IAM federation, single-trace observability vs. stitching four services, Agent365 governance vs. nothing comparable, Terraform-managed infrastructure.
