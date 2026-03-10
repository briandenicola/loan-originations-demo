// ══════════════════════════════════════════════════════════════
//  Loan Origination — Microsoft Foundry Agent Demo (Frontend)
// ══════════════════════════════════════════════════════════════

const API = '';
let currentApp = null;
let agentResult = null;
let chartInstances = {};

// ── Markdown → HTML renderer ─────────────────────────────────
function renderMarkdown(md) {
    if (!md) return '';

    const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

    // Inline formatting (bold, italic, inline code)
    function inlineFmt(s) {
        return s
            .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
            .replace(/\*(.+?)\*/g, '<em>$1</em>')
            .replace(/`(.+?)`/g, '<code>$1</code>');
    }

    const lines = md.split('\n');
    const out = [];
    let i = 0;

    while (i < lines.length) {
        const raw = lines[i];
        const line = esc(raw);

        // ── Markdown table block ────────────────────────────
        if (/^\|(.+)\|/.test(line)) {
            let rows = [];
            while (i < lines.length && /^\|(.+)\|/.test(esc(lines[i]))) {
                rows.push(esc(lines[i]));
                i++;
            }
            if (rows.length >= 2) {
                const parseRow = r => r.split('|').slice(1, -1).map(c => inlineFmt(c.trim()));
                const headers = parseRow(rows[0]);
                // Skip the separator row (|---|---|)
                const sepIdx = rows.findIndex(r => /^\|[\s\-:|]+\|$/.test(r));
                const dataStart = sepIdx >= 0 ? sepIdx + 1 : 1;
                let tbl = '<table><thead><tr>' +
                    headers.map(h => `<th>${h}</th>`).join('') +
                    '</tr></thead><tbody>';
                for (let r = dataStart; r < rows.length; r++) {
                    const cells = parseRow(rows[r]);
                    tbl += '<tr>' + cells.map(c => `<td>${c}</td>`).join('') + '</tr>';
                }
                tbl += '</tbody></table>';
                out.push(tbl);
            } else {
                out.push(inlineFmt(rows.join('<br>')));
            }
            continue;
        }

        // ── Headings ────────────────────────────────────────
        const h3 = line.match(/^### (.+)$/);
        if (h3) { out.push(`<h4>${inlineFmt(h3[1])}</h4>`); i++; continue; }
        const h2 = line.match(/^## (.+)$/);
        if (h2) { out.push(`<h3>${inlineFmt(h2[1])}</h3>`); i++; continue; }
        const h1 = line.match(/^# (.+)$/);
        if (h1) { out.push(`<h2>${inlineFmt(h1[1])}</h2>`); i++; continue; }

        // ── Horizontal rule ─────────────────────────────────
        if (/^---+$/.test(line)) { out.push('<hr>'); i++; continue; }

        // ── Blockquote ──────────────────────────────────────
        if (/^&gt; /.test(line)) {
            let bq = [];
            while (i < lines.length && /^&gt; /.test(esc(lines[i]))) {
                bq.push(inlineFmt(esc(lines[i]).replace(/^&gt; /, '')));
                i++;
            }
            out.push(`<blockquote>${bq.join('<br>')}</blockquote>`);
            continue;
        }

        // ── Unordered list ──────────────────────────────────
        if (/^- (.+)$/.test(line)) {
            let items = [];
            while (i < lines.length && /^- (.+)$/.test(esc(lines[i]))) {
                items.push(`<li>${inlineFmt(esc(lines[i]).replace(/^- /, ''))}</li>`);
                i++;
            }
            out.push(`<ul>${items.join('')}</ul>`);
            continue;
        }

        // ── Ordered list ────────────────────────────────────
        if (/^\d+\. (.+)$/.test(line)) {
            let items = [];
            while (i < lines.length && /^\d+\. (.+)$/.test(esc(lines[i]))) {
                items.push(`<li>${inlineFmt(esc(lines[i]).replace(/^\d+\. /, ''))}</li>`);
                i++;
            }
            out.push(`<ol>${items.join('')}</ol>`);
            continue;
        }

        // ── Blank line → paragraph break ────────────────────
        if (line.trim() === '') { out.push(''); i++; continue; }

        // ── Regular text ────────────────────────────────────
        out.push(inlineFmt(line));
        i++;
    }

    // Join with <br>, but collapse multiple blank lines
    return out.join('<br>').replace(/(<br>){3,}/g, '<br><br>')
        .replace(/(<\/table>)<br>/g, '$1')
        .replace(/(<\/ul>)<br>/g, '$1')
        .replace(/(<\/ol>)<br>/g, '$1')
        .replace(/(<\/blockquote>)<br>/g, '$1')
        .replace(/(<\/h[2-4]>)<br>/g, '$1')
        .replace(/(<hr>)<br>/g, '$1');
}

// ── Init ──────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', async () => {
    try {
        const res = await fetch(`${API}/api/v1/applications`);
        const data = await res.json();
        const sel = document.getElementById('appSelect');
        data.applications.forEach(a => {
            const opt = document.createElement('option');
            opt.value = a.applicationNo;
            opt.textContent = `${a.applicationNo} — ${a.applicantName} — $${Number(a.loanAmountRequested).toLocaleString()} (${a.loanPurpose})`;
            sel.appendChild(opt);
        });
        document.getElementById('apiStatus').classList.add('online');
        document.getElementById('apiStatusText').textContent = 'API Connected';
    } catch (e) {
        document.getElementById('apiStatusText').textContent = 'API Error';
    }
});

// ── Load Application ──────────────────────────────────────────
async function loadApplication() {
    const appNo = document.getElementById('appSelect').value;
    if (!appNo) { document.getElementById('appForm').style.display = 'none'; return; }

    const res = await fetch(`${API}/api/v1/applications/${appNo}`);
    currentApp = await res.json();

    document.getElementById('fName').value = currentApp.applicantName;
    document.getElementById('fAppNo').value = currentApp.applicationNo;
    document.getElementById('fDob').value = currentApp.dob;
    document.getElementById('fDate').value = currentApp.applicationDate;
    document.getElementById('fEmail').value = currentApp.email;
    document.getElementById('fPhone').value = currentApp.phone;
    document.getElementById('fAddress').value = `${currentApp.currentAddress}, ${currentApp.cityStateZip}`;
    document.getElementById('fAmount').value = currentApp.loanAmountRequested;
    document.getElementById('fTerm').value = currentApp.requestedTermMonths;
    document.getElementById('fPurpose').value = currentApp.loanPurpose;
    document.getElementById('fType').value = currentApp.loanType;
    document.getElementById('fIncome').value = currentApp.grossAnnualIncome;
    document.getElementById('fMonthly').value = currentApp.monthlyNetIncome;
    document.getElementById('fDebt').value = currentApp.totalMonthlyDebtPayments;
    document.getElementById('fHousing').value = currentApp.housingPaymentMonthly;

    document.getElementById('appForm').style.display = 'block';
}

// ── Run Agent Workflow ────────────────────────────────────────
async function runAgent() {
    if (!currentApp) return;
    showSection('workflowSection');

    const stepsContainer = document.getElementById('workflowSteps');
    const stepDefs = [
        { id: 'S01', name: 'Application Intake',           agent: null,                              desc: 'Receive and validate loan application fields' },
        { id: 'S02', name: 'Data Enrichment',              agent: null,                              desc: 'Gather credit, income, fraud, policy & pricing data' },
        { id: 'S03', name: 'Credit Profile Analysis',      agent: 'credit-profile-agent',            desc: 'Analyze bureau score, delinquencies & utilization' },
        { id: 'S04', name: 'Income Verification',          agent: 'income-verification-agent',       desc: 'Verify income, employer match & stability' },
        { id: 'S05', name: 'Fraud Screening',              agent: 'fraud-screening-agent',           desc: 'Assess identity risk, device & watchlist flags' },
        { id: 'S06', name: 'Policy Evaluation',            agent: 'policy-evaluation-agent',         desc: 'Evaluate underwriting rules POL-001 – POL-010' },
        { id: 'S07', name: 'DTI & Affordability',          agent: null,                              desc: 'Compute debt-to-income ratio vs threshold' },
        { id: 'S08', name: 'Pricing Analysis',             agent: 'pricing-agent',                   desc: 'Validate risk tier, APR & payment calculations' },
        { id: 'S09', name: 'Underwriting Recommendation',  agent: 'underwriting-recommendation-agent', desc: 'Synthesize all analyses into final decision' },
        { id: 'S10', name: 'Human Review Ready',           agent: null,                              desc: 'Present recommendation for reviewer approval' },
    ];

    // Render steps with agent info and description
    stepsContainer.innerHTML = stepDefs.map(s =>
        `<div class="wf-step" id="step-${s.id}">
            <span class="wf-step-id">${s.id}</span>
            <div class="wf-step-info">
                <span class="wf-step-name">${s.name}</span>
                ${s.agent ? `<span class="wf-agent-badge">🤖 ${s.agent}</span>` : '<span class="wf-agent-badge wf-agent-badge-system">⚙️ system</span>'}
                <span class="wf-step-desc">${s.desc}</span>
            </div>
            <span class="wf-step-detail"></span>
            <span class="wf-step-icon">⏳</span>
        </div>`
    ).join('');

    // Show a banner indicating the full workflow is running in Foundry
    stepsContainer.insertAdjacentHTML('beforebegin',
        `<div id="ai-workflow-banner" class="ai-workflow-banner">
            <span class="ai-banner-icon">🤖</span>
            <div class="ai-banner-text">
                <strong>AI Agent Workflow running in Microsoft Foundry</strong>
                <span id="ai-banner-detail">Connecting to Foundry agents...</span>
            </div>
        </div>`);

    // ── Use Server-Sent Events for real-time step updates ────────
    const aiStartTime = Date.now();

    // Elapsed-time ticker on the banner
    const bannerTimer = setInterval(() => {
        const elapsed = Math.round((Date.now() - aiStartTime) / 1000);
        const bannerDetail = document.getElementById('ai-banner-detail');
        if (bannerDetail && !bannerDetail.dataset.done) {
            const dots = '.'.repeat((elapsed % 3) + 1);
            bannerDetail.textContent = `Foundry agents processing${dots} (${elapsed}s)`;
        }
    }, 1000);

    try {
        agentResult = await new Promise((resolve, reject) => {
            const url = `${API}/api/v1/agent/run-stream?application_no=${encodeURIComponent(currentApp.applicationNo)}`;
            const es = new EventSource(url);

            es.addEventListener('step', (e) => {
                const { stepId, status, detail } = JSON.parse(e.data);
                const el = document.getElementById(`step-${stepId}`);
                if (!el) return;

                el.classList.remove('running', 'ai-pending', 'pending');

                if (status === 'RUNNING') {
                    el.classList.add('running');
                    el.querySelector('.wf-step-icon').textContent = '⚙️';
                    el.querySelector('.wf-step-detail').textContent = detail || 'Processing...';
                } else if (status === 'COMPLETE') {
                    el.classList.add('complete');
                    el.querySelector('.wf-step-icon').textContent = '✅';
                    el.querySelector('.wf-step-detail').textContent = detail || 'Done';
                } else if (status === 'FAILED') {
                    el.classList.add('error');
                    el.querySelector('.wf-step-icon').textContent = '❌';
                    el.querySelector('.wf-step-detail').textContent = detail || 'Failed';
                }
            });

            es.addEventListener('complete', (e) => {
                es.close();
                resolve(JSON.parse(e.data));
            });

            es.addEventListener('error', (e) => {
                // SSE error event — check if it's a server-sent error or connection failure
                if (e.data) {
                    es.close();
                    reject(new Error(JSON.parse(e.data).message || 'Workflow error'));
                }
                // Browser-level connection errors also fire 'error' — EventSource auto-reconnects
                // but if readyState is CLOSED, it won't reconnect
                if (es.readyState === EventSource.CLOSED) {
                    reject(new Error('Connection to Foundry agent lost'));
                }
            });

            es.onerror = () => {
                if (es.readyState === EventSource.CLOSED) {
                    reject(new Error('Connection to Foundry agent lost'));
                }
            };
        });

        clearInterval(bannerTimer);
        const elapsed = Math.round((Date.now() - aiStartTime) / 1000);

        // Update banner to show completion
        const banner = document.getElementById('ai-workflow-banner');
        banner.classList.add('complete');
        banner.querySelector('.ai-banner-icon').textContent = '✅';
        const bannerDetail = document.getElementById('ai-banner-detail');
        bannerDetail.dataset.done = '1';
        bannerDetail.textContent = `All agents completed successfully (${elapsed}s)`;

        // Show LLM model info
        const execMode = agentResult.workflowLog?.executionMode || agentResult.workflowLog?.execution_mode || '';
        const llmModel = agentResult.workflowLog?.llmModel || agentResult.workflowLog?.llm_model || '';
        if (execMode || llmModel) {
            const header = document.querySelector('#workflowSection .section-header p');
            header.innerHTML = `Agent Framework orchestrating S01–S10 pipeline — <strong style="color:#764ba2">${execMode || llmModel}</strong>`;
        }

        await delay(600);
        renderReviewDashboard();
        showSection('reviewSection');
    } catch (err) {
        clearInterval(bannerTimer);
        const banner = document.getElementById('ai-workflow-banner');
        if (banner) {
            banner.classList.add('error');
            banner.querySelector('.ai-banner-icon').textContent = '❌';
            const bannerDetail = document.getElementById('ai-banner-detail');
            bannerDetail.dataset.done = '1';
            bannerDetail.textContent = 'Workflow failed';
        }
        console.error('Agent workflow error:', err);
        stepsContainer.insertAdjacentHTML('afterend',
            `<div class="agent-error-banner" style="background:#fee;border:2px solid #c00;border-radius:8px;padding:16px;margin-top:16px;color:#900">
                <strong>⚠️ Agent Workflow Error</strong><br>
                <span style="font-size:0.95rem">${err.message}</span><br>
                <span style="font-size:0.85rem;color:#666">Check server logs for details.</span>
            </div>`);
    }
}

// ── Render Review Dashboard ───────────────────────────────────
function renderReviewDashboard() {
    const p = agentResult.prepared;
    const rec = agentResult.recommendation;

    // Recommendation banner
    const banner = document.getElementById('recBanner');
    const status = rec.recommendationStatus || rec.recommendation_status;
    banner.className = `rec-banner ${status}`;
    const conf = rec.confidenceScore || rec.confidence_score;
    banner.innerHTML = `
        <div>
            <div style="font-size:1.4rem">AI Recommendation: ${status}</div>
            <div style="font-weight:400;font-size:0.9rem;margin-top:4px;max-height:80px;overflow:hidden;text-overflow:ellipsis">${(rec.rationaleSummary || rec.rationale_summary || '').split('\n').filter(l => l.trim()).slice(0, 2).join(' ').replace(/\*\*/g, '').replace(/^#+\s*/g, '').substring(0, 200)}...</div>
        </div>
        <div style="text-align:right">
            <div style="font-size:2rem">${Math.round(conf * 100)}%</div>
            <div style="font-weight:400;font-size:0.85rem">Confidence</div>
        </div>`;

    // Credit gauge
    const credit = p.creditProfile || p.credit_profile;
    if (credit) {
        drawCreditGauge(credit.bureauScore || credit.bureau_score);
        document.getElementById('creditData').innerHTML = dataGrid({
            'Score Band': credit.scoreBand || credit.score_band,
            'Delinquencies (24m)': credit.delinquencies24m ?? credit.delinquencies_24m,
            'Utilization': `${credit.utilizationPct || credit.utilization_pct}%`,
            'Hard Inquiries (6m)': credit.hardInquiries6m ?? credit.hard_inquiries_6m,
            'Bankruptcy': credit.bankruptcyFlag || credit.bankruptcy_flag,
            'Open Tradelines': credit.totalOpenTradelines || credit.total_open_tradelines,
        });
    }

    // Income & DTI
    const income = p.incomeVerification || p.income_verification;
    const dti = p.verifiedDtiPct || p.verified_dti_pct;
    if (income) {
        drawDtiChart(dti, p.totalMonthlyDebtPayments || p.total_monthly_debt_payments,
            income.verifiedMonthlyIncome || income.verified_monthly_income);
        document.getElementById('incomeData').innerHTML = dataGrid({
            'Verified Income': `$${(income.verifiedMonthlyIncome || income.verified_monthly_income).toLocaleString()}/mo`,
            'Status': income.verificationStatus || income.verification_status,
            'Employer Match': `${Math.round((income.employerMatchPct || income.employer_match_pct) * 100)}%`,
            'Verified DTI': `${(dti * 100).toFixed(1)}%`,
        });
    }

    // Fraud chart
    const fraud = p.fraudSignals || p.fraud_signals;
    if (fraud) {
        drawFraudChart(fraud);
        document.getElementById('fraudData').innerHTML = dataGrid({
            'Identity Risk': (fraud.identityRiskScore || fraud.identity_risk_score),
            'Device Risk': (fraud.deviceRiskScore || fraud.device_risk_score),
            'Address Mismatch': fraud.addressMismatchFlag || fraud.address_mismatch_flag,
            'Synthetic ID': fraud.syntheticIdFlag || fraud.synthetic_id_flag,
            'Watchlist Hit': fraud.watchlistHitFlag || fraud.watchlist_hit_flag,
            'Manual Review': fraud.recommendedManualReview || fraud.recommended_manual_review,
        });
    }

    // Key factors
    const factors = rec.keyFactors || rec.key_factors || [];
    document.getElementById('keyFactors').innerHTML = factors.map(f => {
        const dir = f.direction;
        const weight = f.impactWeight || f.impact_weight;
        return `<div class="factor-item ${dir}">
            <span class="factor-name">${f.factorName || f.factor_name}</span>
            <div class="factor-bar"><div class="factor-bar-fill ${dir}" style="width:${weight * 100}%"></div></div>
            <span class="factor-text">${f.explanation}</span>
        </div>`;
    }).join('');

    // Recommendation detail
    const conditions = rec.conditions || [];
    const agentEnhanced = rec.agentEnhanced || rec.agent_enhanced || rec.llmEnhanced || rec.llm_enhanced || false;
    document.getElementById('recDetail').innerHTML = `
        ${agentEnhanced ? '<div class="llm-badge">🤖 Agent Framework Enhanced Rationale (Foundry Agent Service)</div>' : ''}
        <div class="agent-rationale">${renderMarkdown(rec.rationaleSummary || rec.rationale_summary)}</div>
        ${conditions.length ? `<div style="margin-bottom:12px"><strong>Conditions:</strong><ul style="margin-top:4px;padding-left:20px">
            ${conditions.map(c => `<li>${c}</li>`).join('')}</ul></div>` : ''}
    `;

    // Policy hits
    const hits = rec.policyHits || rec.policy_hits || [];
    document.getElementById('policyHits').innerHTML = hits.map(h => `
        <div class="policy-item ${h.outcome}">
            <span class="policy-badge ${h.outcome}">${h.outcome}</span>
            <strong>${h.ruleId || h.rule_id}</strong>
            <span>${h.message}</span>
        </div>
    `).join('');

    // Quote
    const q = rec.quote;
    if (q) {
        document.getElementById('quoteData').innerHTML = dataGrid({
            'Risk Tier': q.riskTier || q.risk_tier,
            'APR': `${q.aprPct || q.apr_pct}%`,
            'Monthly Payment': `$${(q.estimatedMonthlyPayment || q.estimated_monthly_payment).toLocaleString()}`,
            'Total Repayable': `$${(q.totalRepayableAmount || q.total_repayable_amount).toLocaleString()}`,
            'Payment/Income': `${((q.paymentToIncomePct || q.payment_to_income_pct) * 100).toFixed(1)}%`,
        });
    }

    // Set adjustment defaults
    document.getElementById('adjAmount').value = p.loanAmountRequested || p.loan_amount_requested;
    document.getElementById('adjTerm').value = p.requestedTermMonths || p.requested_term_months;
}

// ── Chart: Credit Score Gauge ─────────────────────────────────
function drawCreditGauge(score) {
    destroyChart('creditGauge');
    const ctx = document.getElementById('creditGauge').getContext('2d');
    const pct = Math.min(1, Math.max(0, (score - 300) / 550));
    const color = score >= 740 ? '#107c10' : score >= 680 ? '#ff8c00' : '#d13438';

    chartInstances.creditGauge = new Chart(ctx, {
        type: 'doughnut',
        data: {
            datasets: [{
                data: [pct, 1 - pct],
                backgroundColor: [color, '#e8e8e8'],
                borderWidth: 0,
                circumference: 180,
                rotation: 270,
            }]
        },
        options: {
            responsive: false,
            cutout: '75%',
            plugins: { legend: { display: false }, tooltip: { enabled: false } },
        }
    });
    document.getElementById('creditScoreLabel').textContent = score;
    document.getElementById('creditScoreLabel').style.color = color;
}

// ── Chart: DTI ────────────────────────────────────────────────
function drawDtiChart(dti, debt, income) {
    destroyChart('dtiChart');
    const ctx = document.getElementById('dtiChart').getContext('2d');
    chartInstances.dtiChart = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: ['Verified DTI', 'Policy Limit', 'Debt/mo', 'Income/mo'],
            datasets: [{
                data: [dti * 100, 40, debt, income],
                backgroundColor: [
                    dti <= 0.30 ? '#107c10' : dti <= 0.40 ? '#ff8c00' : '#d13438',
                    '#e1dfdd',
                    '#d13438aa',
                    '#107c10aa',
                ],
                borderRadius: 6,
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            plugins: { legend: { display: false } },
            scales: {
                y: { display: false },
                x: { grid: { display: false } },
            }
        }
    });
}

// ── Chart: Fraud Risk ─────────────────────────────────────────
function drawFraudChart(fraud) {
    destroyChart('fraudChart');
    const ctx = document.getElementById('fraudChart').getContext('2d');
    const idRisk = fraud.identityRiskScore ?? fraud.identity_risk_score;
    const devRisk = fraud.deviceRiskScore ?? fraud.device_risk_score;
    chartInstances.fraudChart = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: ['Identity Risk', 'Device Risk', 'Threshold (0.20)', 'Hard Limit (0.30)'],
            datasets: [{
                data: [idRisk, devRisk, 0.20, 0.30],
                backgroundColor: [
                    idRisk > 0.20 ? '#d13438' : '#107c10',
                    devRisk > 0.20 ? '#d13438' : '#107c10',
                    '#ff8c0088',
                    '#d1343888',
                ],
                borderRadius: 6,
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            indexAxis: 'y',
            plugins: { legend: { display: false } },
            scales: {
                x: { max: 0.5, grid: { display: false } },
                y: { grid: { display: false } },
            }
        }
    });
}

function destroyChart(id) {
    if (chartInstances[id]) { chartInstances[id].destroy(); delete chartInstances[id]; }
}

// ── Recompute ─────────────────────────────────────────────────
async function recompute() {
    const btn = document.querySelector('[onclick="recompute()"]');
    const origText = btn.textContent;
    btn.textContent = '⏳ Recomputing...';
    btn.disabled = true;

    try {
        const amount = parseFloat(document.getElementById('adjAmount').value);
        const term = parseInt(document.getElementById('adjTerm').value);
        const p = agentResult.prepared;

        console.log('Recompute request:', { application_no: currentApp.applicationNo, amount, term });

        const res = await fetch(`${API}/api/v1/agent/recompute`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                application_no: currentApp.applicationNo,
                run_id: agentResult.runId,
                requested_amount: amount,
                requested_term_months: term,
                loan_type: p.loanType || p.loan_type,
            }),
        });

        if (!res.ok) {
            const err = await res.text();
            console.error('Recompute failed:', res.status, err);
            alert(`Recompute failed: ${res.status} ${err}`);
            return;
        }

        const result = await res.json();
        console.log('Recompute result:', result);

        // Update recommendation and quote in agentResult
        if (result.quote) {
            agentResult.recommendation.quote = result.quote;
        }
        if (result.recommendation) {
            agentResult.recommendation.recommendation_status = result.recommendation.recommendationStatus || result.recommendation.recommendation_status;
            agentResult.recommendation.confidence_score = result.recommendation.confidenceScore || result.recommendation.confidence_score;
            agentResult.recommendation.rationale_summary = result.recommendation.rationaleSummary || result.recommendation.rationale_summary;
            agentResult.recommendation.key_factors = result.recommendation.keyFactors || result.recommendation.key_factors;
            agentResult.recommendation.conditions = result.recommendation.conditions || result.recommendation.conditions;
            agentResult.recommendation.policy_hits = result.recommendation.policyHits || result.recommendation.policy_hits;
        }

        renderReviewDashboard();
        btn.textContent = '✅ Updated!';
        setTimeout(() => { btn.textContent = origText; }, 2000);
    } catch (err) {
        console.error('Recompute error:', err);
        alert(`Recompute error: ${err.message}`);
    } finally {
        btn.disabled = false;
        if (btn.textContent === '⏳ Recomputing...') btn.textContent = origText;
    }
}

// ── Submit Decision ───────────────────────────────────────────
async function submitDecision(decision) {
    const notes = document.getElementById('reviewNotes').value;
    const adjAmount = parseFloat(document.getElementById('adjAmount').value);
    const adjTerm = parseInt(document.getElementById('adjTerm').value);

    const body = {
        runId: agentResult.runId,
        applicationNo: currentApp.applicationNo,
        reviewerId: 'reviewer-001',
        decision: decision,
        adjustedAmount: adjAmount,
        adjustedTermMonths: adjTerm,
        notes: notes,
        recommendationSnapshot: agentResult.recommendation,
    };

    const res = await fetch(`${API}/api/v1/agent/decision`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
    });
    const record = await res.json();

    const emoji = decision === 'APPROVE' ? '✅' : '❌';
    document.getElementById('decisionResult').innerHTML = `
        <div class="decision-result">
            <div class="big-status">${emoji}</div>
            <h2 style="color:${decision === 'APPROVE' ? '#107c10' : '#d13438'}">${decision}D</h2>
            <table>
                <tr><td>Decision ID</td><td>${record.decisionId || record.decision_id}</td></tr>
                <tr><td>Application</td><td>${record.applicationNo || record.application_no}</td></tr>
                <tr><td>Run ID</td><td>${record.runId || record.run_id}</td></tr>
                <tr><td>Reviewer</td><td>${record.reviewerId || record.reviewer_id}</td></tr>
                <tr><td>Decided At</td><td>${new Date(record.decidedAt || record.decided_at).toLocaleString()}</td></tr>
                ${notes ? `<tr><td>Notes</td><td>${notes}</td></tr>` : ''}
            </table>
            <p style="margin-top:16px;color:var(--text-muted)">
                📁 Output files written to <code>/output/</code> directory:<br>
                loan_application_prepared.json • workflow_run_log.json • loan_recommendation_summary.json • human_decision_record.json
            </p>
        </div>`;
    showSection('decisionSection');
}

// ── Helpers ───────────────────────────────────────────────────
function showSection(id) {
    document.querySelectorAll('.section').forEach(s => s.style.display = 'none');
    document.getElementById(id).style.display = 'block';
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

function resetApp() {
    agentResult = null;
    currentApp = null;
    document.getElementById('appSelect').value = '';
    document.getElementById('appForm').style.display = 'none';
    showSection('intakeSection');
}

function dataGrid(obj) {
    return Object.entries(obj).map(([k, v]) =>
        `<div class="data-item"><span class="data-label">${k}</span><span class="data-value">${v}</span></div>`
    ).join('');
}

function delay(ms) { return new Promise(r => setTimeout(r, ms)); }
