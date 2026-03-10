# Loan Origination Demo — Talk Track Guide

**Audience:** Fiserv technical evaluators and business stakeholders  
**Duration:** 20 minutes  
**Presenters:** Engineering CVP · Technical Solution Engineer  
**Competitive context:** AWS, Silicon Valley AI wrapper companies

---

## Demo Narrative Structure

The demo tells a single story: a loan application arrives, AI agents prepare it, and a human reviewer makes the final call — with full visibility into every step. The narrative progresses through five stages.

| Stage | Time | Who Leads | What Happens |
|-------|------|-----------|-------------|
| **1. The Problem** | 0:00–2:00 | CVP | Business context: why loan origination is ripe for AI |
| **2. Architecture** | 2:00–4:00 | CVP | How the system is built — Foundry, agents, APIs, human loop |
| **3. Live Workflow** | 4:00–10:00 | Solution Engineer | Submit application, watch agents work, show real-time progress |
| **4. Human Review** | 10:00–14:00 | Solution Engineer | Recommendation, risk visuals, adjust terms, make a decision |
| **5. How It Was Built** | 14:00–16:00 | Solution Engineer | Show how Copilot CLI built the entire system from prompts |
| **6. Governance & Dashboards** | 16:00–18:00 | CVP + Solution Engineer | API-first data model, how every data point is dashboardable |
| **7. Why This Matters** | 18:00–20:00 | CVP | Enterprise differentiation, compliance, what comes next |

The arc is deliberate: start with empathy for the problem, show the solution working, demonstrate how fast it was built, show that governance is built in, then close on enterprise value.

---

## Speaker Roles

### Engineering CVP — The Strategist

The CVP owns the opening and closing. Their job is to make the audience care before the demo starts and remember the architecture after it ends.

**What to cover:**

- **The lending problem today.** Loan officers re-key data from PDFs, toggle between credit bureau screens, income verification portals, and fraud databases. They assemble an underwriting packet manually, write up a recommendation, and pass it to a supervisor. This takes hours per application and leaves audit gaps.

- **The architectural thesis.** AI agents should do the assembly and analysis. Trusted financial APIs should supply the data. Humans should make the final decision — with the AI's reasoning in front of them. The system should be explainable, auditable, and deterministic.

- **Why Foundry, not a wrapper.** Azure AI Foundry is the control plane: agent definitions, model deployments, identity, tracing, and governance live in one place. Agents are registered resources — versioned, monitored, and secured with Entra ID. This is not a thin API layer over a model endpoint.

- **The architecture diagram.** Show the `.assets/architecture.png` image. Walk through three layers: Container Apps (UI + API + Orchestrator) → AI Gateway (Foundry Agent Service with six specialists) → Monitoring (OpenTelemetry + Application Insights). Emphasize that the orchestrator is code-based, not LLM-driven — every step executes in a deterministic sequence.

**What NOT to cover:** The CVP should not click through the UI or explain implementation details. That belongs to the Solution Engineer.

---

### Technical Solution Engineer — The Operator

The Solution Engineer drives the live demo. Their job is to make the system feel real, reliable, and production-ready.

**What to cover:**

- **Application intake.** Select a sample borrower, show the pre-populated fields, point out that everything is editable.

- **Workflow execution.** Click "Run Agent Workflow" and narrate what each step does as it completes. Call out agent names, elapsed time, and the SSE-driven real-time updates.

- **Review dashboard.** Walk through the recommendation banner, credit gauge, data grids, and explanation factors. Show how the AI's reasoning maps to specific data points.

- **Adjustment loop.** Change the loan amount, click "Recalculate," and show pricing and recommendation update live. Explain that prior snapshots are preserved for audit.

- **Decision capture.** Click "Approve" and show the decision record with reviewer ID, timestamp, and the recommendation snapshot.

**What NOT to cover:** The Solution Engineer should not explain competitive positioning or business strategy. Keep it technical and grounded in what the system is doing.

---

## Key Workflow Moments

These are the five moments that matter most during the live demo. Each one should land clearly with the audience.

### Moment 1 — Loan Application Intake (S01–S02)

**What the audience sees:** The Solution Engineer selects a borrower (e.g., Robert Chen, $50,000 auto loan, 60-month term). The intake form shows extracted fields — identity, loan details, income, obligations. The engineer points out that fields are editable, then clicks "Run Agent Workflow."

**What to say:** *"This is the starting point. In production, this PDF would come from a borrower portal or document management system. The system extracted every field — the officer can correct anything before the workflow starts."*

**What to emphasize:** The system does not bypass the officer. It presents extracted data for review before any AI processing begins. This is important for data quality and compliance — the officer validates the input.

---

### Moment 2 — AI Agents Preparing the Application (S03–S06, S08)

**What the audience sees:** Step cards appear in sequence as each agent completes. Each card shows the agent name, a status badge (⏳ → ⚙️ → ✅), duration in milliseconds, and response size. The workflow banner displays elapsed time.

**What to say:** *"Five specialist agents are working through this application right now. The credit agent is analyzing the bureau score, delinquencies, and utilization. The income agent is verifying payroll records. The fraud agent is checking identity risk signals. The policy agent is evaluating ten underwriting rules. The pricing agent is validating the APR and monthly payment. Each one is a registered agent in Foundry running GPT-4.1."*

**What to emphasize:** These are not prompt chains or function calls — they are named, registered agents in Azure AI Foundry. Each one has a specific role and instruction set. The code-based coordinator calls them in a deterministic sequence. There is no black-box LLM deciding which agents to invoke or in what order.

**Pause point:** When the underwriting-recommendation-agent completes (S09), pause briefly. *"That last agent — the underwriting recommender — just read the output of all five specialists and produced a single recommendation. It synthesized credit, income, fraud, policy, and pricing analysis into one coherent decision with reasoning."*

---

### Moment 3 — API-Based Enrichment with Financial Data

**What the audience sees:** The review dashboard shows enriched data grids — credit score and band, delinquencies, utilization, verified monthly income, employer match percentage, identity risk score, fraud flags, policy rule results, APR, monthly payment, payment-to-income ratio.

**What to say:** *"All of this data came from APIs. Credit profile from a bureau. Income verification from payroll records. Fraud signals from an identity network. Pricing from the product engine. In this demo, those APIs are backed by CSV data. In production, they connect to your existing systems — Equifax, Experian, payroll providers, your fraud vendor. The AI agents receive all of this as context, not just one slice."*

**What to emphasize:** The agents are not hallucinating data. Every number on this screen came from a defined API with a known data source. The AI's job is to analyze and reason over real data — not to fabricate it. This is the fundamental difference between an enterprise AI system and a chatbot.

---

### Moment 4 — AI Recommendation and Explainability

**What the audience sees:** The recommendation banner shows APPROVE, CONDITIONAL, or DECLINE with a confidence score and a rationale summary. Below it, explanation factors list the key drivers: credit score strength, DTI within limits, no fraud flags, policy rules passed.

**What to say:** *"This recommendation is not a yes-or-no answer. It comes with a confidence score and a written rationale. The AI is citing specific factors — 'credit score of 720 is in the Good band, verified DTI of 32% is within the 43% threshold, no fraud indicators detected.' A loan officer can read this and understand why the AI reached this conclusion. A compliance auditor can read it too."*

**What to emphasize:** Explainability is not optional in regulated lending. Fair Lending laws, ECOA, and internal audit requirements demand that every recommendation has documented reasoning. This system produces that reasoning automatically — it is not a post-hoc explanation bolted on after the fact.

---

### Moment 5 — Human-in-the-Loop Decision Process

**What the audience sees:** The Solution Engineer adjusts the loan amount from $50,000 to $35,000 using the input control, clicks "Recalculate," and the system re-runs pricing and underwriting agents. The recommendation updates — possibly from CONDITIONAL to APPROVE as the lower amount reduces risk. The engineer then clicks "Approve."

**What to say:** *"This is the human-in-the-loop loop. The officer can adjust terms and see how the recommendation changes in real time. Maybe the borrower called and said they can put more down. Maybe the officer wants to see what happens at a shorter term. The AI recalculates instantly — pricing agent re-runs, underwriting agent re-evaluates. Prior snapshots are kept, so the audit trail shows what the officer saw at decision time."*

**What to emphasize:** The AI does not make the decision. The human does. The AI provides analysis, reasoning, and a recommendation — the officer evaluates it, adjusts if needed, and makes the final call. The decision record captures everything: the recommendation, the terms, the reviewer, and the timestamp.

---

## Critical Messages the Audience Must Remember

If the audience forgets everything else, these three ideas should stay with them.

### 1. AI orchestrates trusted systems — it does not replace them

The agents call real APIs (credit bureaus, income verification, fraud networks, pricing engines). They reason over real data from existing financial systems. The AI layer adds analysis and synthesis — it does not invent data or bypass the systems of record. This is integration, not replacement.

**How to reinforce:** Every time enriched data appears on screen, remind the audience where it came from. *"This credit score came from the bureau API. This income figure came from payroll verification. The AI agent analyzed these — it did not generate them."*

### 2. Every recommendation is explainable and auditable

The system produces four JSON output files per run: the prepared application, the workflow log with timestamps for every step, the recommendation summary with confidence and key factors, and the human decision record. OpenTelemetry traces capture every agent call, API call, and response. This is not a black box.

**How to reinforce:** Mention the audit trail at least twice — once during the workflow execution (*"every step is logged with a timestamp and the agent's response"*) and once during the decision (*"this decision record captures exactly what the officer saw, what the AI recommended, and what terms were in effect"*).

### 3. Humans control the decision — AI accelerates the analysis

The system is designed for human-in-the-loop decisioning. The AI prepares, enriches, analyzes, and recommends. The officer reviews, adjusts, and decides. The adjustment loop (change terms → recalculate → review new recommendation) is not a workaround — it is a core feature. Regulators expect human judgment in lending decisions.

**How to reinforce:** The recalculate moment is the most powerful proof point. When the officer changes the amount and the recommendation updates, it demonstrates that the AI is a tool the officer uses — not an authority the officer obeys.

## How This Was Built — The Copilot CLI Story

This section is a differentiator in its own right. The entire application — infrastructure, backend, frontend, agents, deployment, and documentation — was built in a single continuous session using GitHub Copilot CLI. No code was written manually. The audience should understand that this is not just a demo of the loan origination system — it is a demo of how fast an enterprise AI application can go from requirements document to working system.

### What the Solution Engineer Should Show

**The origin prompt.** Show the first conversational prompt that started the build:

> *"I need to create a demo of an agent system based on the information in the instructions.md and instructions.docx file. This demo must showcase Microsoft Foundry and agentic system. Let's build."*

Copilot CLI read the instruction documents, the CSV data files, and the OpenAPI spec. It produced a full ASP.NET Core application with a Semantic Kernel plugin pattern, CSV-backed data services, REST controllers, and a single-page frontend — on the first turn.

**The evolution through conversation.** Walk through how the system evolved through natural language:

| Prompt | What Changed |
|--------|-------------|
| *"Let's create some Terraform to provision Foundry and deploy the models, agents and workflow"* | Complete `infrastructure/` module: AI Foundry Hub, projects, VNet, Container Apps, ACR, model deployments |
| *"Use Entra ID instead of account keys"* | Replaced API keys with `ChainedTokenCredential`, disabled local auth in Terraform |
| *"The code is using Semantic Kernel, not Agent Framework"* | Full SDK migration to Microsoft Agent Framework |
| *"Can we create a workflow that has a centralized coordinator?"* | Pivoted from YAML declarative workflow to code-based coordinator |
| *"I don't like the hard coded values in the apps terraform"* | Convention-based naming — one variable (`APP_NAME`) derives all resource names |

**The numbers.** Display these statistics:

- ~40 conversational turns → 38 git commits
- 80+ files created or modified
- ~2,500 lines of C#, ~1,200 lines of Terraform, ~600 lines of JavaScript
- 3 SDK migrations navigated (Semantic Kernel → Agent Framework → AIProjectClient)
- 2 architectural pivots (YAML workflow → code-based coordinator)
- Zero lines of manually written code

### What to Say

*"This entire system was built by having a conversation with Copilot CLI. We started with a requirements document and sample data. Forty prompts later, we had a production-ready application with Terraform infrastructure, Entra ID security, six AI agents in Foundry, a real-time streaming UI, and Container Apps deployment."*

*"When we hit a wall — the YAML workflow couldn't share context between agents — Copilot tried multiple fixes, then recommended the architectural pivot to a code-based coordinator. It didn't just write code. It made design decisions, debugged failures, and adapted the architecture."*

*"This is the developer productivity story. A solution engineer with Copilot CLI can build, iterate, and deploy an enterprise AI application in hours, not weeks. And everything it produced — the Terraform, the C#, the agent configurations — is production-grade, not prototype-grade."*

### Why This Matters for Fiserv

The implication is clear: if Fiserv's engineering teams adopt Copilot CLI, they can build AI-powered workflows at this velocity. The POC-to-production gap shrinks. The cost of experimentation drops. Teams can try ten approaches and keep the one that works — because trying each one takes hours, not sprints.

---

## Governance and API-Driven Dashboards

This section addresses a critical enterprise concern: how do you govern an AI-powered decisioning system? The answer is that every piece of data in this system is exposed through APIs, which means dashboards, compliance reports, and monitoring views can be built on top of the same data layer the agents use.

### The API-First Data Architecture

Every data point in the loan origination workflow is accessible through a well-defined REST API. This is not an afterthought — it is the core design principle. The agents consume the same APIs that a dashboard or compliance tool would consume.

| API Endpoint | Data Exposed | Dashboard Use Case |
|-------------|-------------|-------------------|
| `GET /api/v1/credit-profile` | Bureau score, score band, delinquencies, utilization, hard inquiries, bankruptcy flag | **Portfolio Risk Dashboard** — distribution of credit scores, delinquency trends, utilization patterns |
| `GET /api/v1/income-verification` | Verified income, verification status, employer match %, income variance | **Income Verification Dashboard** — verification success rates, income distribution, employer match rates |
| `GET /api/v1/fraud-signals` | Identity risk score, device risk, synthetic ID flag, watchlist hits, manual review flag | **Fraud Operations Dashboard** — fraud signal trends, manual review queue metrics, synthetic ID detection rates |
| `GET /api/v1/policy/thresholds` | 10 underwriting policy rules with thresholds (min credit score, max DTI, etc.) | **Policy Compliance Dashboard** — rule pass/fail rates, threshold breach frequency, policy drift detection |
| `POST /api/v1/loan-products/quote` | Risk tier, APR, monthly payment, total repayable, payment-to-income ratio | **Pricing Analytics Dashboard** — APR distribution by risk tier, payment-to-income trends, pricing model performance |
| `POST /api/v1/underwriting/recommendation` | Recommendation status, confidence score, rationale, key factors, conditions | **AI Decision Dashboard** — approval/decline rates, confidence score distribution, recommendation override rates |
| `GET /api/v1/audit/reference-decisions/{id}` | Prior human decisions for comparison | **Audit & Fair Lending Dashboard** — decision consistency, human-vs-AI alignment, demographic analysis |
| `GET /api/v1/agent/health` | Agent availability and Foundry connectivity | **Operations Dashboard** — system health, agent uptime, response latency |

### The Workflow Audit Trail

Beyond the enrichment APIs, the workflow itself produces four JSON output files per run that are designed for downstream consumption:

| Output File | Contents | Governance Value |
|-------------|----------|-----------------|
| `loan_application_prepared.json` | Normalized application + all enriched data | **Data lineage** — shows exactly what data was available at decision time |
| `workflow_run_log.json` | Ordered S01–S10 steps with timestamps, durations, agent names | **Process audit** — proves every required step executed, in order, with timing |
| `loan_recommendation_summary.json` | AI recommendation, confidence, key factors, conditions, full rationale | **Explainability audit** — documents exactly what the AI recommended and why |
| `human_decision_record.json` | Final decision, reviewer ID, timestamp, terms at decision time, recommendation snapshot | **Decision audit** — captures who decided, when, what they saw, and what terms were in effect |

### What to Show During the Demo

The Solution Engineer should briefly demonstrate two things:

1. **API accessibility.** Open a browser tab or curl command showing a raw API response (e.g., `GET /api/v1/credit-profile?application_no=APP-2026-001`). Point out that this is the same data the credit-profile-agent used. *"Any BI tool — Power BI, Tableau, Grafana — can call this API and build dashboards. The data layer is not locked inside the AI system."*

2. **Audit file output.** Show one of the JSON output files (e.g., `workflow_run_log.json`). Point out the ordered step IDs, timestamps, and agent names. *"This file is produced automatically on every run. Feed it into your SIEM, your compliance database, or a monitoring dashboard. The audit trail is not something you build later — it ships with every decision."*

### What the CVP Should Say

*"Governance is not a feature we add at the end. It is the architecture. Every data source the AI agents use is exposed through a REST API. Every workflow step is logged with timestamps. Every recommendation includes its reasoning. Every human decision is captured with the reviewer's identity and the terms in effect."*

*"What that means in practice: your compliance team can build a Fair Lending dashboard by querying the same APIs the agents use. Your operations team can monitor agent health and response latency. Your audit team can reconstruct any loan decision — what data was available, what the AI recommended, what the officer decided, and when. All from APIs. No special tooling required."*

*"This is what API-first governance looks like. The data model is the governance model. Dashboards are not a separate project — they are a natural extension of the same data layer that powers the agents."*

### Why This Matters for Fiserv

Financial institutions face regulatory pressure from multiple directions — Fair Lending, ECOA, CFPB examinations, internal audit, model risk management. Each of these requires different views of the same underlying data. Because this system exposes everything through APIs, Fiserv can build compliance dashboards, model monitoring views, and operational analytics without modifying the core application. The data is already structured, accessible, and audit-ready.

---

### Against AWS

AWS offers Bedrock Agents and Step Functions. The differentiation is not about model quality — it is about the control plane.

- **Agent governance.** In Foundry, agents are registered resources with versioning, identity (Entra ID), and monitoring. They are not anonymous Lambda functions calling a model endpoint. An enterprise platform team can inventory every agent, audit its instructions, and control its access.

- **Identity and access.** The entire system uses Entra ID managed identities. No API keys are stored or rotated. The credential chain (Managed Identity → Environment → Azure CLI) works from local development through production without code changes. AWS IAM achieves similar goals but does not integrate with an organization's existing Active Directory without federation complexity.

- **Observability.** OpenTelemetry traces flow from the orchestrator through each agent call into Application Insights. A single trace spans the entire S01–S10 workflow. In AWS, tracing across Bedrock Agents, Step Functions, and Lambda requires stitching multiple services together.

- **Infrastructure as code.** The entire environment — Foundry hub, projects, model deployments, agents, Container Apps, networking — is provisioned with Terraform. One `task deploy` command builds everything. This is not a console-click demo.

### Against AI Wrapper Companies

Lightweight AI tools (LangChain wrappers, prompt orchestration startups) can produce impressive demos quickly. The differentiation is in what happens after the demo.

- **No agent governance.** Wrapper tools typically treat agents as code constructs — functions with prompts. There is no platform-level registry, no identity boundary, no audit log of agent instructions. When a compliance team asks "what instructions did the underwriting agent have on March 3rd?" there is no answer.

- **No deterministic execution.** Most agentic frameworks are LLM-driven — the model decides which tools to call and in what order. This is powerful for open-ended tasks. It is dangerous for regulated workflows. A lending regulator will ask "why did the system skip the fraud check on this application?" and "the LLM decided it wasn't necessary" is not an acceptable answer. The code-based coordinator guarantees every step runs, every time, in order.

- **No enterprise identity.** Wrapper tools authenticate with API keys. Foundry authenticates with Entra ID. In a financial institution with thousands of developers, centralized identity management is not optional.

- **No path to production.** A wrapper demo runs on a laptop. This system runs on Azure Container Apps with managed identities, private networking, container registries, and Terraform-managed infrastructure. The gap between demo and production is small, not infinite.

---

## Closing Message

The CVP should deliver the close. The goal is to leave the audience with a clear understanding of the architectural pattern and why it matters for their business.

**Guidance for the close:**

Return to the business problem. Remind the audience what loan origination looks like today — manual, slow, error-prone, hard to audit. Then contrast it with what they just saw: a ten-step workflow that completes in under a minute, with six specialist AI agents analyzing the application, a synthesized recommendation with written reasoning, and a human reviewer who has everything they need to make an informed decision.

Emphasize the architecture, not the demo. The demo showed one loan application. The architecture supports any decisioning workflow — insurance underwriting, credit line reviews, account opening, KYC checks. The pattern is the same: ingest, enrich from trusted APIs, analyze with specialist agents, recommend with reasoning, and let a human decide.

Name the five pillars explicitly:

1. **AI orchestrating trusted financial systems** — agents call real APIs and reason over real data
2. **Explainable recommendations for regulated environments** — every recommendation has a rationale, key factors, and an audit trail
3. **Human-controlled decisioning with full auditability** — the officer decides, the system records everything
4. **Built at AI speed** — the entire system was built from a requirements document using Copilot CLI in a single session, demonstrating the developer productivity multiplier
5. **API-first governance** — every data point is exposed through REST APIs, making dashboards, compliance reports, and monitoring views a natural extension of the platform

Close with what comes next. This is a reference architecture. The next step is to connect it to real data sources — actual credit bureaus, payroll providers, fraud networks, and policy engines. The Foundry agents, the orchestration pattern, the audit trail, and the human review experience are ready. The integration surface is defined by OpenAPI contracts that map directly to existing systems.

**Suggested closing line:** *"What you saw today is not a prototype. It is a production-ready pattern — built in a single Copilot CLI session, governed by APIs from day one, with AI agents that work within your systems, produce recommendations your officers can trust, and generate the audit trail your compliance team requires. The question is not whether AI can help with loan origination. The question is whether your AI platform gives you the governance, explainability, and velocity that regulated lending demands."*

---

## Appendix: Demo Checklist

Before the presentation, verify the following:

- [ ] Foundry agents are deployed and healthy (`/api/v1/agent/health` returns 200)
- [ ] Sample applications load in the dropdown (Robert Chen, Patricia Harmon, Lisa Ramirez)
- [ ] Full workflow completes end-to-end (S01–S10) without errors
- [ ] Recalculate works — adjust amount, click recalculate, see updated recommendation
- [ ] Approve/Decline buttons produce decision records
- [ ] Architecture diagram (`.assets/architecture.png`) is ready for the opening slides
- [ ] Network connectivity to Azure AI Foundry is stable (agents need ~30s total)
- [ ] Fallback plan: pre-recorded workflow run video in case of connectivity issues
