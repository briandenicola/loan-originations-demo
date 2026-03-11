# Loan Origination Demo — Presenter Transcript

**Duration:** 20 minutes  
**Presenters:**  
- **CVP** — Engineering Corporate Vice President  
- **SE** — Technical Solution Engineer  

Stage directions are in *[brackets]*. Approximate timestamps are noted for pacing.

**Pre-staging notes:** Before the demo begins, the SE should have the following ready in separate browser tabs:
- Tab 1: The loan origination application (intake screen loaded)
- Tab 2: Application Insights — live metrics or a recent trace from a prior workflow run
- Tab 3: The README "Built with Copilot CLI" section or a slide with build stats

---

## Stage 1 — The Problem and Architecture (0:00–2:00)

**CVP:**

Thank you everyone for being here. What we're going to show you today is a working loan origination system powered by AI agents on Microsoft Foundry.

Here's the problem. When a loan application comes in today, a loan officer receives a PDF. They re-key the data. They pull a credit report in one system. Income verification in another. Fraud check in a third. They look up pricing tables. They evaluate policy rules. They write up a recommendation. Then they pass it to a supervisor. That process takes hours. Sometimes days. And the audit trail is whatever the officer remembered to write down.

What we built replaces the assembly and analysis — not the officer. AI agents pull the credit report, verify income, check for fraud, evaluate policy, compute pricing, and synthesize a recommendation in thirty seconds. The officer still makes the final decision, but now they have a complete, explainable recommendation with every supporting data point in front of them.

The orchestrator is code-based — deterministic C# calling every agent in sequence. No LLM deciding which steps to skip. That matters when a regulator asks you to explain a decision.

[SE name], take us through it live.

---

## Stage 2 — Live Workflow (2:00–8:00)

### Application Intake

**SE:**
*[SE switches to Tab 0 — the architecture diagram]*

Quickly on to the architecture. The application runs in Azure Container Apps and has three layers: UI, API, orchestrator. The orchestrator calls Microsoft Foundry where six AI  agents — credit, income, fraud, policy, pricing, and underwriting — are running. Each agent uses GPT-4.1. New models could have been choosen but 4.1 was picked for this demo because of its speed compared to newer models. Of course, a model like GPT-5.2 or 5.4 would be better in production. Lastly, as you can see at the bottom of the diagram, we have a full monitoring stack through OpenTelemetry and Application Insights.


*[SE switches to Tab 1 — the application is already loaded]*

Okay, I've got the application ready. We have a pull down pre-populated with your supplied sample data. I also created my own loan application that we can use as well.  The application can take an uploaded pdf and parse it. For this demo, let's use Robert Chen's reqwuest for a 22 thousand dollar auto loan with a sixty-month term. As you can see after I selected the loan, the system has extracted all the fields from the loan application — identity, loan details, income, obligations. The officer can review the details before the workflow starts. Let me kick off the agent workflow.

*[SE clicks "Run Agent Workflow"]*

### Workflow Execution

**SE:**

The step cards are updated in real time as the agents are complete.  Each step is a call to a distinct AI agent.

*[S02 completes]*

So five calls were just fired off — credit bureau, income verification, fraud signals, policy thresholds, pricing engine. In this demo those APIs are backed by CSV data. In production, those are your real bureau connections, payroll services, fraud network.

*[Steps starts, then completes]*
It's going through the specialist agents now. For example, Step 04 is credit agent. The agent is looking at the bureau data, computing a credit score, evaluating delinquencies, utilization, inquiries, and writing up a narrative. This will take some time because it's doing real work in our Foundry —service  not returning a canned response. In production, with the APIs and Foundry deployment co-located, this takes 5 to 10 seconds per specialist.
The critical step is S09 — the underwriting recommendation agent. This agent will receive a character brief compiled from the other five agents. It will then generate one recommendation based on credit, income, fraud, policy, and pricing.

*[pause briefly]*
Okay I am going to switch over to a completed analysis to show off the Human in the Loop. We'll switch back in a little bit to show the completed workflow.

---

## Stage 3 — Human Review and Decision (8:00–13:00)

### Recommendation Dashboard

**SE:**

This is the review screen — what the loan officer sees.

*[SE points to recommendation banner]*

At the top — CONDITIONAL, it shows the percent confidence. The AI explains its reasoning: "Robert T. Chen demonstrates strong overall credit and capacity metrics. His credit score of 758 places him well within the prime tier, with no recent delinquencies or derogatory credit history, and a well-established trade line history (over 6 years)"

*[SE points to credit gauge and data grids]*

The sole condition to be resolved before approval is the standard collateral validation for high-ticket secured loans.

*[SE points to explanation factors]*

Key factors driving the recommendation. It shows that the condition triggering the condionial approval is policy rule POL-007: loan amount requested > $20,000

### The Adjustment Loop

**SE:**

Now the human-in-the-loop moment. The borrower wants a lower amount. The officer wants to see how that changes things. Let's change the amount to $19,999.

*[SE changes loan amount from $50,000 to $35,000, clicks "Recalculate"]*

The system is re-running the pricing and underwriting agents with the new terms. These are the actual Foundry agents re-evaluating — not a local calculation.  It will take about a minute to recalculate so let's switch to a couple other runs that I've already performed.  This one is for John Doe.  It's reasoning for approval is stated as - John A. Doe demonstrates strong creditworthiness, with a "Good" bureau score of 688 (Tier B), no recent delinquencies, and a well-established credit history (102 months oldest tradeline). Verified income is $4,550/month, from 24 months of payroll records with a 98% employer match and only 1.6% variance, supporting a computed DTI of just 13.8%—

*[Results return]*
Okay. Let's switch back to the recalculation.  As you can see the UI adjusted to RECOMPUTED and the recommnedation status is APPROVED.

### Final Decision

**SE:**

So now the Officer is satisfied. They click Approve.

*[SE clicks "Approve"]*

Decision captured — reviewer ID, timestamp, the recommendation at decision time, final terms. Everything a compliance team needs to reconstruct this decision six months from now.  

*[SE shows Explorer with the output folder]*
All output is captured in the output directory, and we can provide copies to you after the session.  


*[SE shows Application Insights*
It is also captured in our Azure Monitoring tool called Application Insights. You're looking at the traces from the workflow we just ran. Every agent call — credit, income, fraud, policy, pricing, underwriting — shows up as a span in the distributed trace. You can see the duration of each call, the dependencies, any errors. This is OpenTelemetry flowing from the orchestrator through each Foundry agent invocation. This is not something we added after the fact. The tracing is baked into the orchestrator. Every workflow run is observable from day one.

All of this data is stored in Azure Log Analytics that can be exported to any logging or SIEM tool you use from which you can create custom dashboards, alerts and reports.

---

## Stage 4 — How This Was Built (13:00–15:00)

**SE:**

Now I want to show you how this was built — because this is a story in itself.

*[SE switches to Tab 4 — README or slide with build stats]*

This entire system — infrastructure, backend, frontend, agents, deployment — was built in using GitHub Copilot CLI. 99% of the lines of written were written by the AI.

It started with one prompt: "I need to create a demo of an agent system based on the instructions.md file. This demo must showcase Microsoft Foundry. Let's build."

Copilot CLI read the requirements, the CSV data, and the OpenAPI spec. First turn — full ASP.NET Core application. Controllers, data services, Chart.js frontend. Working end-to-end.

The conversation continued. "Create Terraform for Foundry" — full infrastructure module. "Use Entra ID instead of keys" — managed identity auth. When the YAML workflow couldn't share context between agents, Copilot tried multiple fixes, then recommended the architectural pivot to the code-based coordinator you just saw.

The numbers: over 40 conversational turns. Thirty-eight commits. Eighty-plus files. Twenty-five hundred lines of C#. Twelve hundred lines of Terraform. Three SDK migrations. Two architectural pivots. Hours, not weeks.

For your engineering teams, this means the cost of experimentation drops dramatically. You try ten approaches, keep what works — because each one takes hours to build, not sprints.

One more thing before I hand back to [CVP name]. We also built a Red Team agent using the Foundry Evals API. It's designed to adversarially test every agent in this workflow — probing for prompt injection, jailbreaks, off-topic responses, and policy violations. You can run it against individual agents or the entire end-to-end workflow. It's how you validate that the deterministic pipeline you just saw actually holds up under adversarial conditions — and it produces a scored report you can hand to your model risk management team. That's the kind of testing that regulators increasingly expect, and it's built right into the platform.

---

## Stage 5 — Why Foundry, Why Microsoft (15:00–18:00)

**CVP:**

Thanks [SE name]. Now I want to land something important. You've seen what the system does. I want to talk about why we built it on Foundry, and why that choice matters against the alternatives.

### Governance as Architecture

Let's start with governance — because for financial institutions, this is the whole ball game.Now let me talk about what makes Foundry different from the alternatives.

In Foundry, the agents are registered resources. They have versioned instruction sets. They're secured with Entra ID — the same identity system your organization already runs. A platform team can inventory every agent, audit its instructions, and control access through the same role-based policies they use for everything else. That's not possible when your agents are anonymous Lambda functions behind an AWS Bedrock endpoint.


### The Microsoft Platform Story

Let me be direct about the value proposition. When you build AI workflows on Microsoft, you get three things that matter in financial services — and a fourth that's about to change the governance conversation entirely.

First — enterprise identity. Entra ID end to end. No API keys to rotate. No secrets to manage. The same identity governance your organization already runs extends to your AI agents.

Second — integrated observability. OpenTelemetry traces from your orchestrator through your agents into Application Insights. One platform, one trace, one place to look when something goes wrong or when audit asks for evidence.

Third — a control plane for AI. Foundry is not just a model endpoint. It's where your agents live, where their instructions are versioned, where their access is governed, where their behavior is monitored. That's the difference between running AI and governing AI. For regulated industries, that's the difference that matters.

And fourth — and this is where the roadmap gets compelling — Agent365. Microsoft is rolling out Agent365 as the enterprise-wide governance layer for AI agents. Think of it as the control plane above Foundry. Every agent — whether it's built in Foundry, Copilot Studio, or a third-party framework — gets registered in a central directory. Each agent receives a unique Agent ID. IT applies access policies, lifecycle management, and conditional controls — the same way they manage employee identities today, but for AI agents.

What does that mean for this system? The six agents we just ran — credit, income, fraud, policy, pricing, underwriting — each one would appear in Agent365's registry. Your security team can see them alongside every other agent in the organization. They can audit the instructions, review the access policies, monitor behavior, and flag anything unsanctioned. There's an "Agent Map" that visualizes the relationships between agents, users, and data — so you can see exactly which agents are accessing which data sources and who authorized them. 

This matters because the number-one governance problem in AI right now is agent sprawl. Microsoft's own research says nearly thirty percent of Fortune 500 AI agents are running without IT approval. Agent365 closes that gap. And because the agents we built in Foundry are already registered resources with Entra ID identities, they integrate into Agent365 natively. There's no retrofit. No migration. You're building on the platform that the governance layer is designed for.

That's the full Microsoft story for regulated AI: Foundry for building and running agents. Agent365 for governing them at enterprise scale. Entra ID for identity. Application Insights for observability. And none of that exists in the AWS or wrapper-tool ecosystem.

---

## Stage 6 — Close (18:00–20:00)

**CVP:**

Let me bring this home.

You saw what this system does. A loan application comes in. Six AI agents analyze it in thirty seconds — credit, income, fraud, policy, pricing — and a final underwriter synthesizes their work into a recommendation with written reasoning. A loan officer reviews it, adjusts the terms, watches the AI recalculate, and makes the final call. Every step is traced. Every recommendation is explained. Every decision is recorded.

You saw how it was built. A single Copilot CLI session. Requirements document to production-ready system in forty prompts.

And you saw where it runs. Azure AI Foundry — agents as governed resources, Entra ID identity, deterministic orchestration, full observability, infrastructure as code. With Agent365 providing enterprise-wide governance across every agent in the organization.

This is what AI-powered financial services looks like when it's built on the right platform. The agents work within your existing systems. The recommendations are explainable. The decisions are auditable. The governance extends from the individual agent in Foundry to the enterprise-wide registry in Agent365. And the platform gives you the control that regulated lending demands.

The pattern works beyond loan origination — insurance underwriting, credit line reviews, KYC, account opening. Any workflow where you ingest, enrich, analyze, recommend, and let a human decide.

The next step is straightforward. Let us help you develop your next generation applications on top of our Microsoft AI platform. 

Thank you. We'd love to take your questions.

---

## Appendix: Presenter Notes

### Timing Checkpoints

| Time | You Should Be At |
|------|-----------------|
| 2:00 | CVP finished problem + architecture, handing to SE |
| 5:00 | Agents are completing steps on screen |
| 8:00 | Workflow complete, transitioning to review dashboard |
| 11:00 | Showing adjustment loop (recalculate) |
| 13:00 | SE transitions to "How It Was Built" |
| 15:00 | CVP takes over for "Why Foundry, Why Microsoft" |
| 16:30 | SE shows App Insights traces |
| 18:00 | CVP delivers close |
| 20:00 | Open for questions |

### Pre-Staged Tabs

| Tab | Content | Used In |
|-----|---------|---------|
| 1 | Loan origination app (intake screen) | Stages 2–4 |
| 2 | Application Insights (trace from prior run or live metrics) | Stage 5 |
| 3 | README "Built with Copilot CLI" section or stats slide | Stage 4 |

### If the Workflow Takes Longer Than Expected

- **SE:** "Each agent is doing genuine analysis in Foundry, not returning canned responses. In a Container Apps deployment in the same Azure region, total workflow time drops to fifteen to twenty seconds."
- Continue narrating each step as it completes. The real-time updates give you natural talking points.

### If an Agent Fails

- **SE:** "The system is designed for graceful degradation — remaining agents continue. In production, this triggers an alert in Application Insights. The workflow log captures the failure for diagnostics."
- Continue the demo. The review dashboard will still function.

### Handling Questions During the Demo

- **"Is this using real credit bureau data?"** — "The APIs are backed by representative CSV data. In production, those endpoints connect to your existing integrations. The agent code doesn't change — only the data source."

- **"Can we add more agents?"** — "Register a new agent in Foundry, add one line to the coordinator. The architecture is designed to extend."

- **"How do you handle model updates?"** — "Agents reference a specific model deployment in Foundry. Deploy a new version, test, cut over. Agent instructions don't change. Foundry gives you versioning and rollback."

- **"What about latency in production?"** — "In a Container Apps deployment co-located with Foundry, total workflow time drops to fifteen to twenty seconds. Each specialist call is three to five seconds."

- **"How does this compare to AWS?"** — CVP takes this. Key points: registered agent resources vs. anonymous Lambdas, Entra ID vs. IAM federation, single-trace observability vs. stitching four services, Terraform-managed infrastructure.
