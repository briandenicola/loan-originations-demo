# Scenario 2 Instructions

## Scenario Background
A lending team receives consumer loan applications as uploaded documents. Today, analysts manually re-key borrower details, gather supporting data from internal services, and make an underwriting recommendation before a supervisor gives final approval.

This challenge simulates that process. You will build a loan-origination experience with an agentic workflow that prepares an application packet, calls APIs for enrichment data, and presents a human reviewer with a clear recommendation and decision controls.

## Objective
Build and run a solution that:
1. Accepts a loan application through a UI.
2. Runs an agentic workflow to extract/prep application data.
3. Calls APIs (defined by OpenAPI) to enrich and validate borrower/loan context.
4. Produces an AI recommendation with an explanation.
5. Supports human-in-the-loop review and final action.

## Provided Files
- `sample-data/Loan_Application_Demo.pdf`
- `sample-data/Loan_Application_Robert_Chen.pdf`
- `sample-data/Loan_Application_Patricia_Harmon.pdf`
- `sample-data/Loan_Application_Lisa_Ramirez.pdf`
- `materials/data/loan_application_register.csv`
- `materials/data/credit_bureau_extract.csv`
- `materials/data/income_verification_extract.csv`
- `materials/data/fraud_screening_extract.csv`
- `materials/data/product_pricing_matrix.csv`
- `materials/data/policy_thresholds.csv`
- `materials/openapi.yaml`
- `materials/workflow_step_by_step_instructions.docx`

## API Implementation Note
Use `materials/openapi.yaml` as the contract for API integration.
Only OpenAPI + CSV-backed data sources are required for this scenario (no additional backend assets).

## Functional Requirements
1. **Loan Intake UI**
   - Provide a screen/form to submit or upload a loan application.
   - Capture/edit key loan fields (applicant identity, requested amount, term, product type, income, obligations).

2. **Agentic Preparation Workflow**
   - Parse the application and normalize data into a structured application record.
   - Pull supporting data via API calls (for example: credit profile, debt/income signals, fraud/risk indicators, eligibility checks).
   - Record workflow stages and data provenance for reviewer transparency.

3. **Human-in-the-Loop Review Screen**
   - Show original application data and all pulled/enriched data side-by-side.
   - Present an AI recommendation (`APPROVE`, `CONDITIONAL`, or `DECLINE`) with rationale.
   - Include visual components for risk interpretation (for example: credit score gauge, DTI bar, affordability/risk summary chart).
   - Provide an explainability panel describing key drivers of the recommendation.

4. **Decision Actions**
   - Include action buttons for `Approve` and `Decline`.
   - Support adjustment of loan values/terms (for example: requested amount, term length, rate assumptions) before final decision.
   - Recompute and refresh recommendation context after adjustments.

## Required Outputs
- `loan_application_prepared.json`
- `workflow_run_log.json`
- `loan_recommendation_summary.json`
- `human_decision_record.json`

Decision record requirement:
- Persist final reviewer decision (`APPROVE` or `DECLINE`).
- Persist reviewer-adjusted loan terms (if changed).
- Persist the AI recommendation shown at decision time and explanation snapshot.

## Process Alignment Requirement
Your implementation must preserve a clear end-to-end sequence aligned to:
- `materials/workflow_step_by_step_instructions.docx`

Your run log must include ordered step IDs `S01` through `S10`:
1. Intake
2. Agent preparation
3. API enrichment
4. Recommendation generation
5. Human review
6. Final decision + audit record
