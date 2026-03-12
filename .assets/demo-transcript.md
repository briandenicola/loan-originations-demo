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

## Stage 1 — Introduction (0:00–1:30)

What we're going to do today is show you a loan origination system powered by AI agents.

What we built is an agentic workflow that prepares a loan application for human review by calling various agents, then combining their generated results to final underwriter agent that makes a recommendation to a human reviewer. It was built over the course of a couple days using Github Copilot with AI Agents running in Microsoft Foundry.

---

## Stage 2 — Architecture and Application Demo (1:30–5:00)

**SE:**
Let me start with the architecture and then show the application live.

### Architecture Overview

*[SE shows architecture diagram]*

It has 3 components the UI, the orchestrator, and agents. Each agent currently uses OpenAI's GPT-4.1 model. This was selected for speed. In production you'd probably a model with stronger reasoning. To round out the picture, we have full monitoring through OpenTelemetry and Application Insights.

### Quick Workflow Run

*[SE shows the intake form]*

I've got sample data pre-loaded. Let me select Particia Harmon for a 5 thousand dollar loan for medical expenses. I'll quickly review the details and kick off the workflow

*[SE selects, fields populate, clicks "Run Agent Workflow"]*

The UI update in real time as each agent completes. It will takes about a few minutes to process because it's performaning real agentic work in the background. So in the interest of time, let me switch to a completed run.

### Human-in-the-Loop Review

*[SE switches to Tab 2 — completed workflow]*

This is the review screen. At the top — the recommendation with a confidence score and reasoning. Below — credit gauges, income data, fraud signals, policy compliance. Every data point is trace back to a specific agent's analysis. The officer can review policy evaultations and maybe adjust terms as needed. In this case the loan officer requests a lower loan amount. They hit Recalculate and the agents re-evaluate. When satisfied, they click Approve or Deny. Decision logged with reviewer ID, timestamp, recommendation, final terms — everything compliance needs to reconstruct this decision later.

---

## Stage 3 — Red Team, Observability, and Governance (5:00–9:00)

### Agent365 and Agent ID

*[SE switches to Tab 5 — Agent ID screen in Agent365 portal]*
Now that you've sseen the application, let's talk about it's actually runs in Microsoft Foundry to be secure, compliant, and performant.

First let's talk governance. This is Agent365 — Microsoft's enterprise-wide governance layer for AI agents. The control plane above Foundry.

What you're looking at is one of our agents published in the Agent365 registry. Every agent — Foundry, Copilot Studio, or third-party — gets registered here with a unique Agent ID.

*[SE walks through the Agent ID screen]*

You can see the agent's identity, registration details, access policies, lifecycle status. IT applies access policies, lifecycle management, and conditional controls — the same way they manage employee identities today, but for AI agents.

Your security team can audit instructions, review access policies, monitor behavior, flag anything unsanctioned.  The Agent Identity can be used for role based access control, just like a user identity. 

Agent365 matters because the #1 Governance problem with AI right now is agent sprawl. Microsoft's research says nearly 30% of Fortune 500 have AI agents running without IT oversight.

### Application Insights

*[SE switches to Tab 3 — Application Insights]*

Next, Let's talk monitoring. This is Application Insights, Azure's application monitoring tool with new capabilities added for Agent monitoring. You're looking at a trace from the workflow we ran earlier. Every agent calls shows up as a span in the distributed trace. 

With Foundry, you're not limited to Application Insights. The platform emits tracess that can be routed to any SIEM or monitoring tool. So, you can built dashboards, anomaly detection, and alerting for AI agents in Application Insights or another tool of your choice.

### Foundry
*[SE switches to Foundry tab]*
This is Microsoft Foundry. What you are seeing here are the agents that backed the application.  The agents run in a fully managed environment called Agent Service, no infrastructure required.  For models, as I mentioned earlier, each agent is using a model from OpenAI, but we have models from hundreds of providers in our Model Catalog including those from Anthropic. 

### Red Team Testing

Lastly, I built a Red Team agent using the Foundry Evals API. It adversarially tests every agent in this workflow — probing for prompt injection, jailbreaks, off-topic responses, and policy violations. You can run it against individual agents like I did here or the entire end-to-end workflow. It produces a scored report you can hand to your model risk management team. That's the kind of testing regulators increasingly expect, and it's built right into the platform.

---

## Stage 4 — Live Building with Copilot CLI (9:00–13:00)

**SE:**
Now that you saw how the application was runs, I want to show you how this was built.  What I used is GitHub Copilot CLI — an AI-powered terminal agent for the professional developer. Github Copilot can also be used in Visual Studio Code, but I like the terminal.

*[SE switches to Tab 4 — Copilot CLI terminal]*
So this is Copilot cli. 

I've written my fair share of code but I'm not a UI guy. When I was given this demo to do, I was extremely nervous given the one week timeframe. I've never written anything like this before. So I did I always do now. I dove the Copilot and had a conversation with the agent. Starting with nothing more than your to your instructions.md and a prompt, Copilot built the entire system you just saw, including the architecture, API, frontend, and AI Agents over the course of 50 plus conversation turns, 38 commits and 2,500 lines of C# code  

Prompt:
**I need to update the UI to accept an upload a loan application. Currently it loads up the supplied sample data, which works, but the client wants the ability to upload a PDF loan application, have it parsed and all data fields updated. You can assume in the processing that the uploaded pdf is the same form as the sample data. This is a big ask so let's create a branch first and only focus on the workflow source**

Let me show you. *[Copy and paste prompt into the Copilot CLI and run]*.  So this is the exact prompt I gave to Copilot. You can see the instructions, the architecture requirements. I didn't write any coder markdown file. I could have - probably should have - but I wanted to see how much it could do with just a prompt.

It will take more than 10 minutes to do any real coding -  I would love to discuss this more in depth one day - but I wanted to show some of the output of the CLI as it built this application.  

*[SE switches to Visual Studio Code Plan File file]*
This is the plan that the CLI generated for starting this application. It shows the thinking the agent did to build the architecture, break down the work into steps, and organize those steps into a plan. 

*[SE switches to Visual Studio Code Branch setuop file]*
This file shows the branching strategy the agent created to build the PDF upload feature that I forgot to add in my initial built. Copilot created a branch for the feature, then implemented the featured, tested it, and commit it into the codebase — all on its own. The review and pull request was handled by a human (me). 

All of this code will be available to the team in my personal Github repo after meeting.

---

## Stage 5 — Close (13:00–15:00)

**SE:**

Let me bring this home.

You saw a loan application come in, six AI agents analyze it, and a loan officer make the final call with full explainability. Every step traced, every recommendation explained, every decision recorded.

You saw Github Copilot in action and how it built the entire system.

You saw how agents are run in Microsoft Foundry, governed through Agent365, monitored with Application Insights and validated with a Red Team agents.

That's the Microsoft platform for regulated AI.

Let us help you build your next generation applications on Microsoft's AI platform.

Thank you and I'll pass it back to [CVP] for Q&A.

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
