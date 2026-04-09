const API = 'http://localhost:5000';
const MASTER_KEYS = {config:'Key',resources:'Resource_ID',routing:'SKU_ID',queue:'From_Operation',changeover:'From \\ To',skus:'SKU_ID',bom:'BOM_ID',inventory:'SKU_ID','campaign-config':'Grade',scenarios:'Parameter'};
const MASTER_LABELS = {config:'Config',resources:'Resource Master',routing:'Routing',queue:'Queue Times',changeover:'Changeover Matrix',skus:'SKU Master',bom:'BOM',inventory:'Inventory','campaign-config':'Campaign Config',scenarios:'Scenarios'};
const state = {
  orders:[], campaigns:[], capacity:[], scenario_metrics:[], scenarios:[], bomGross:[], bomNet:[],
  routeManifest:null, selectedMasterKey:null, masterMode:'create',
  campFilter:'all', horizon: 14,
  // Planning workflow state
  poolOrders:[], windowSOs:[], planningOrders:[], heatBatches:[],
  planningWindow: 7,
  planningMaterialRequestId: 0,
  // Configuration from Excel
  config: {
    horizon_days: 7,
    heat_size_mt: 50,
    section_tolerance_mm: 0.6,
    max_lot_mt: 300,
    max_heats_per_lot: 8,
    max_due_spread_days: 3
  },
  // Simulation parameters (user-configurable before simulate)
  simConfig: {
    horizon_days: 14,
    heat_size_mt: 50,
    priority_filter: '',
    sms_lines: 2,
    rm_lines: 2
  },
  // Planner-set rolling mode overrides for individual SOs (so_id → 'HOT'|'COLD')
  soRollingOverrides: {}
};

function qs(id){ return document.getElementById(id); }
function parseDate(v){ if(!v) return new Date(NaN); return new Date(String(v).replace(' ','T')); }
function setText(id,v){ const el=qs(id); if(el) el.textContent = v; }
function escapeHtml(v){ return String(v ?? '').replace(/[&<>"']/g, m => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m])); }
function fmtDate(v){ if(!v) return '—'; const d=parseDate(v); if(Number.isNaN(d.getTime())) return String(v); return d.toLocaleDateString('en-GB',{day:'2-digit',month:'short'}).replace(' ','-'); }
function fmtDateTime(v){ if(!v) return '—'; const d=parseDate(v); if(Number.isNaN(d.getTime())) return String(v); const date = d.toLocaleDateString('en-GB',{day:'2-digit',month:'short'}); const time = d.toLocaleTimeString('en-GB',{hour:'2-digit',minute:'2-digit',hour12:false}); return `${date} ${time}`; }
function num(v, fallback=0){ const n = Number(v); return Number.isFinite(n) ? n : fallback; }
function upper(v){ return String(v || '').trim().toUpperCase(); }
function badgeForStatus(status){
  const s = String(status || '').toUpperCase();
  if (s.includes('HOLD')) return '<span class="badge amber">'+escapeHtml(status || 'HOLD')+'</span>';
  if (s.includes('LATE') || s==='CRITICAL' || s==='BLOCKED') return '<span class="badge red">'+escapeHtml(status || 'LATE')+'</span>';
  if (s.includes('RUN')) return '<span class="badge blue">'+escapeHtml(status || 'RUNNING')+'</span>';
  return '<span class="badge green">'+escapeHtml(status || 'RELEASED')+'</span>';
}
function formatStatus(text){ return String(text || '').replace(/_/g, ' '); }
function utilBar(u){
  const cls = u > 100 ? 'red' : u > 85 ? 'gold' : u > 60 ? 'green' : 'blue';
  return '<div class="mini-track"><div class="mini-bar '+cls+'" style="left:0;width:'+Math.min(Math.max(u,0),100)+'%"></div></div>';
}
async function apiFetch(url, opts={}){
  const res = await fetch(API + url, {headers:{'Content-Type':'application/json'}, ...opts});
  const data = await res.json().catch(()=>({}));
  if(!res.ok) throw new Error(data.error || ('API error ' + res.status));
  return data;
}
function activatePage(page) {
  const remap = {
    campaigns: 'execution',
    schedule: 'execution',
    dispatch: 'execution',
    'planning-pool': 'planning',
    'planning-board': 'planning',
    'heat-builder': 'planning',
    'finite-scheduler': 'planning',
    'release-board': 'planning'
  };

  page = remap[page] || page;

  document.querySelectorAll('.page').forEach((el) => {
    el.classList.toggle('active', el.id === `page-${page}`);
  });

  document.querySelectorAll('#topTabs [data-page]').forEach((el) => {
    el.classList.toggle('active', el.dataset.page === page);
  });

  const isPlanning = page === 'planning';
  const ctrlDefault = qs('ctrl-default');
  const ctrlPlanning = qs('ctrl-planning');

  if (ctrlDefault) ctrlDefault.style.display = isPlanning ? 'none' : '';
  if (ctrlPlanning) ctrlPlanning.style.display = isPlanning ? '' : 'none';

  if (page === 'material') initSplitResizer('materialDivider');
  if (page === 'bom') initSplitResizer('bomDivider');
  if (page === 'capacity') renderCapacityBars();
}

function switchExecView(view){
  document.querySelectorAll('.exec-view').forEach(el=>el.classList.remove('active-view'));
  qs('exec-view-'+view).classList.add('active-view');
  document.querySelectorAll('[data-exec-view]').forEach(b=>b.classList.toggle('active',b.dataset.execView===view));
}

function _ridToOp(rid){
  const r = String(rid||'').toUpperCase();
  if(r.startsWith('CCM')) return 'CCM';
  if(r.startsWith('EAF')) return 'EAF';
  if(r.startsWith('LRF')) return 'LRF';
  if(r.startsWith('VD'))  return 'VD';
  if(r.startsWith('RM'))  return 'RM';
  if(r.startsWith('BF'))  return 'BF';
  return r.split('-')[0] || '—';
}
function renderCapacityBars(){
  const rows = state.capacity || [];
  const barsEl = qs('capacityBars');
  if(!barsEl) return;
  if(!rows.length){ barsEl.innerHTML = '<div style="font-size:.8rem;color:var(--text-soft)">Run schedule to load capacity data.</div>'; return; }

  let overloaded = 0, slack = 0, totalUtil = 0;
  barsEl.innerHTML = rows.map(r=>{
    const util = Math.round(num(r['Utilisation_%'] || r.utilisation || 0));
    totalUtil += util;
    if(util > 100) overloaded++;
    if(util < 60) slack++;
    const barColor = util > 100 ? 'var(--danger)' : util > 85 ? 'var(--warning)' : 'var(--success)';
    const rid = escapeHtml(r.Resource_ID || r.resource_id || '—');
    const op = escapeHtml(r.Operation_Group || r.Operation || _ridToOp(r.Resource_ID || r.resource_id));
    const cappedUtil = Math.min(util, 100);
    return `<div style="display:flex;align-items:center;gap:.5rem;font-size:.78rem;margin-bottom:.2rem">
      <div style="width:70px;flex-shrink:0;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${rid}</div>
      <div style="width:36px;flex-shrink:0;color:var(--text-faint);font-size:.7rem">${op}</div>
      <div style="flex:1;background:var(--panel-muted);border-radius:3px;height:.85rem;overflow:hidden">
        <div style="height:100%;width:${cappedUtil}%;background:${barColor};border-radius:3px;transition:width .3s"></div>
      </div>
      <div style="width:38px;flex-shrink:0;text-align:right;font-weight:700;color:${util>100?'var(--danger)':util>85?'var(--warning)':'var(--text-soft)'}">${util}%</div>
    </div>`;
  }).join('');

  setText('capOverloaded', overloaded);
  setText('capSlack', slack);
  if(rows.length) setText('capAvg', Math.round(totalUtil/rows.length) + '%');
}

// ---- Pipeline stage accordion ----
function toggleStage(id){
  const body = qs(id + '-body');
  const chevron = qs(id + '-chevron');
  const isCollapsed = body.classList.contains('collapsed');
  body.classList.toggle('collapsed', !isCollapsed);
  chevron.classList.toggle('collapsed', !isCollapsed);
}

function setPipelineStageStatus(id, status, meta, kpis){
  // status: pending | running | done | warn | error
  const badge = qs(id + '-badge');
  const metaEl = qs(id + '-meta');
  const kpisEl = qs(id + '-kpis');
  const stage = qs(id);
  const labels = {pending:'PENDING',running:'RUNNING…',done:'DONE',warn:'CHECK',error:'ERROR'};
  badge.className = 'ps-badge ' + status;
  badge.textContent = labels[status] || status.toUpperCase();
  stage.className = 'pipeline-stage ' + (status === 'pending' ? '' : status);
  if(meta) metaEl.textContent = meta;
  if(kpis){
    kpisEl.innerHTML = kpis.map(k=>`<span><strong>${k.v}</strong> ${k.l}</span>`).join('');
  }
}

function stageExpand(id){ const b=qs(id+'-body'),c=qs(id+'-chevron'); b.classList.remove('collapsed'); c.classList.remove('collapsed'); }
function stageCollapse(id){ const b=qs(id+'-body'),c=qs(id+'-chevron'); b.classList.add('collapsed'); c.classList.add('collapsed'); }

async function runFullPipeline(){
  const btn = qs('pipelineRunBtn');
  btn.textContent = '⏳ Running…';
  btn.disabled = true;

  qs('pipelineStatusBadge').style.background = '#dbeafe';
  qs('pipelineStatusBadge').style.color = '#2563eb';
  qs('pipelineStatusBadge').textContent = 'Running…';

  const pipelineChip = qs('chipPipeline');
  pipelineChip.style.display = '';
  pipelineChip.className = 'chip info';
  setText('chipPipelineText', '⏳ Pipeline running...');

  try {
    // Stage 1 - select window
    setPipelineStageStatus('ps-propose', 'running');
    stageExpand('ps-propose');
    await selectPlanningWindow();

    // Stage 2 - propose orders
    await proposePlanningOrders();

    // Stage 3 - derive heats
    setPipelineStageStatus('ps-heats', 'running');
    stageExpand('ps-heats');
    await deriveHeatBatches();

    // PRE-RUN BOM so material panel is ready later
    setText('chipPipelineText', '⏳ Running BOM explosion...');
    await runBom();

    // Stage 4 - simulate
    setPipelineStageStatus('ps-schedule', 'running');
    stageExpand('ps-schedule');
    await simulateSchedule();

    // Stage 5 - load release
    setPipelineStageStatus('ps-release', 'running');
    stageExpand('ps-release');
    await loadReleaseBoard();

    qs('pipelineStatusBadge').style.background = '#dcfce7';
    qs('pipelineStatusBadge').style.color = '#16a34a';
    qs('pipelineStatusBadge').textContent = 'Complete';

    qs('chipPipeline').className = 'chip success';
    setText('chipPipelineText', '✓ Pipeline ready to release');
  } catch(e) {
    qs('pipelineStatusBadge').style.background = '#fee2e2';
    qs('pipelineStatusBadge').style.color = '#dc2626';
    qs('pipelineStatusBadge').textContent = 'Error';

    qs('chipPipeline').className = 'chip danger';
    setText('chipPipelineText', '✗ Pipeline error: ' + e.message);

    console.error('Pipeline error:', e);
  }

  btn.textContent = '▶ Run Pipeline';
  btn.disabled = false;
}


function initSplitResizer(dividerId) {
  const divider = qs(dividerId);
  if (!divider || divider.dataset.bound === 'true') return;

  const container = divider.parentElement;
  const leftPane = container?.children?.[0];
  if (!container || !leftPane) return;

  let isResizing = false;

  const onMouseMove = (e) => {
    if (!isResizing) return;

    const rect = container.getBoundingClientRect();
    const nextWidth = e.clientX - rect.left;
    const minWidth = 220;
    const maxWidth = rect.width - 320;

    if (nextWidth >= minWidth && nextWidth <= maxWidth) {
      leftPane.style.flex = `0 0 ${nextWidth}px`;
    }
  };

  const onMouseUp = () => {
    isResizing = false;
    document.body.style.cursor = '';
    document.body.style.userSelect = '';
  };

  divider.addEventListener('mousedown', () => {
    isResizing = true;
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
  });

  document.addEventListener('mousemove', onMouseMove);
  document.addEventListener('mouseup', onMouseUp);

  divider.dataset.bound = 'true';
}

function initNavigation() {
  const topTabs = qs('topTabs');

  if (topTabs && !topTabs.dataset.bound) {
    topTabs.addEventListener('click', (e) => {
      const btn = e.target.closest('[data-page]');
      if (!btn) return;

      activatePage(btn.dataset.page);
    });

    topTabs.dataset.bound = 'true';
  }

  document.querySelectorAll('[data-page-link]').forEach((btn) => {
    if (btn.dataset.bound) return;
    btn.addEventListener('click', () => activatePage(btn.dataset.pageLink));
    btn.dataset.bound = 'true';
  });
}


function initAppUi() {
  initNavigation();
  initSplitResizer('materialDivider');
  initSplitResizer('bomDivider');
}

initAppUi();
async function checkHealth(){
  try{
    const d = await apiFetch('/api/health?quick=1');
    qs('chipApi').className = 'chip success';
    setText('chipApiText', d.workbook_ok ? 'API + workbook connected' : 'API up, workbook issue');
  }catch(e){
    qs('chipApi').className = 'chip danger';
    setText('chipApiText', 'API unavailable');
  }
}

function latestSimResult() {
  return state.lastSimResult && typeof state.lastSimResult === 'object' ? state.lastSimResult : null;
}

function simulationStatusLabel(sim = latestSimResult()) {
  if (!sim) return null;
  if (sim.horizon_exceeded) return 'HORIZON EXCEEDED';
  if (sim.solver_status) return String(sim.solver_status);
  return sim.feasible ? 'FEASIBLE' : 'INFEASIBLE';
}

function setMetricTone(id, tone = '') {
  const el = qs(id);
  if (!el) return;
  el.className = ['metric', tone].filter(Boolean).join(' ');
}

function derivePlanStatus() {
  const sim = latestSimResult();
  if (sim) {
    if (sim.feasible) {
      return {
        value: 'Feasible',
        sub: sim.message || `${num(sim.horizon_hours || 0).toFixed(1)}h horizon`,
        tone: 'success'
      };
    }
    if (sim.horizon_exceeded) {
      return {
        value: `${num(sim.overflow_hours || 0).toFixed(1)}h over`,
        sub: `${num(sim.total_duration_hours || 0).toFixed(1)}h vs ${num(sim.horizon_hours || 0).toFixed(1)}h horizon`,
        tone: 'danger'
      };
    }
    return {
      value: 'At Risk',
      sub: sim.message || simulationStatusLabel(sim),
      tone: 'warn'
    };
  }

  if ((state.planningOrders || []).length) {
    return {
      value: 'Pending',
      sub: `${state.planningOrders.length} POs await feasibility`,
      tone: 'warn'
    };
  }

  return {
    value: 'Not Run',
    sub: 'Feasibility not run',
    tone: 'info'
  };
}

function dashboardCapacityRows(summary = state.overview || {}) {
  const capacityRows = state.capacity || [];
  if (capacityRows.length) return capacityRows;

  const scheduleRows = state.lastScheduleRows && state.lastScheduleRows.length ? state.lastScheduleRows : (state.gantt || []);
  if (!scheduleRows.length) return [];

  const horizonHours = num(latestSimResult()?.horizon_hours || state.simConfig?.horizon_days * 24 || summary.horizon_hours || 24, 24);
  const byResource = new Map();
  scheduleRows.forEach(row => {
    const resourceId = String(row.Resource_ID || row.resource_id || '').trim();
    if (!resourceId) return;
    const current = byResource.get(resourceId) || {
      Resource_ID: resourceId,
      Operation_Group: row.Operation || _ridToOp(resourceId),
      Demand_Hrs: 0,
      Avail_Hrs_14d: horizonHours,
      source: 'simulation'
    };
    current.Demand_Hrs += num(row.Duration_Hrs || row.duration_hrs || 0);
    current['Utilisation_%'] = Math.round((current.Demand_Hrs / Math.max(horizonHours, 1)) * 1000) / 10;
    byResource.set(resourceId, current);
  });

  return [...byResource.values()].sort((a, b) =>
    num(b['Utilisation_%'] || 0) - num(a['Utilisation_%'] || 0)
  );
}

function deriveAppKpis(summary = state.overview || {}) {
  const planningOrders = state.planningOrders || [];
  const campaigns = state.campaigns || [];
  const poolOrders = state.poolOrders || [];
  const heatBatches = state.heatBatches || [];
  const capacityItems = dashboardCapacityRows(summary);
  const ganttJobs = state.gantt || [];
  const sim = latestSimResult();

  const releasedPlanningOrders = planningOrders.filter(po => upper(po.planner_status) === 'RELEASED');
  const heldPlanningOrders = planningOrders.filter(po => upper(po.planner_status).includes('HOLD'));
  const releasedCampaigns = campaigns.filter(c => upper(c.release_status || c.Release_Status || c.Status) === 'RELEASED');
  const heldCampaigns = campaigns.filter(c => upper(c.release_status || c.Release_Status || c.Status).includes('HOLD'));
  const lateCampaigns = campaigns.filter(c => upper(c.Status).includes('LATE'));

  const planningOrderCount = planningOrders.length || num(summary.campaigns_total || campaigns.length);
  const releasedCount = planningOrders.length ? releasedPlanningOrders.length : num(summary.campaigns_released || releasedCampaigns.length);
  const heldCount = planningOrders.length ? heldPlanningOrders.length : num(summary.campaigns_held || heldCampaigns.length);
  const lateCount = campaigns.length ? lateCampaigns.length : num(summary.campaigns_late);

  const totalMt = planningOrders.length
    ? planningOrders.reduce((sum, po) => sum + num(po.total_qty_mt || 0), 0)
    : num(summary.total_mt);
  const releasedMt = planningOrders.length
    ? releasedPlanningOrders.reduce((sum, po) => sum + num(po.total_qty_mt || 0), 0)
    : num(summary.released_mt ?? summary.total_mt);

  const totalHeatsFromOrders = planningOrders.reduce((sum, po) => {
    const heats = Array.isArray(po.heats) ? po.heats.length : 0;
    return sum + (heats || num(po.heats_required || 0));
  }, 0);
  const releasedHeatsFromOrders = releasedPlanningOrders.reduce((sum, po) => {
    const heats = Array.isArray(po.heats) ? po.heats.length : 0;
    return sum + (heats || num(po.heats_required || 0));
  }, 0);
  const totalHeats = heatBatches.length || totalHeatsFromOrders || num(summary.total_heats);
  const releasedHeats = heatBatches.length
    ? heatBatches.filter(h => upper(h.released_status) === 'RELEASED' || (upper(h.scheduling_status) === 'SCHEDULED' && h.released)).length
    : (releasedHeatsFromOrders || num(summary.released_heats ?? summary.total_heats));

  const onTimePct = summary.on_time_pct != null
    ? num(summary.on_time_pct)
    : (campaigns.length ? (100 * Math.max(campaigns.length - lateCount, 0) / campaigns.length) : null);

  const sortedCapacity = [...capacityItems].sort((a,b)=>
    num(b['Utilisation_%'] || b.Utilisation_Percent || b.utilisation || 0) -
    num(a['Utilisation_%'] || a.Utilisation_Percent || a.utilisation || 0)
  );
  const bottleneckResource = sortedCapacity[0] || null;
  const maxUtil = bottleneckResource
    ? Math.round(num(bottleneckResource['Utilisation_%'] || bottleneckResource.Utilisation_Percent || bottleneckResource.utilisation || summary.max_utilisation || 0))
    : (summary.max_utilisation != null ? Math.round(num(summary.max_utilisation)) : null);

  const stageState = {
    pool: poolOrders.length > 0,
    propose: planningOrderCount > 0,
    heats: totalHeats > 0,
    simulate: Boolean(state.lastSimResult) || ganttJobs.length > 0 || capacityItems.length > 0,
    release: releasedCount > 0
  };
  const completedStages = Object.values(stageState).filter(Boolean).length;

  return {
    planningOrderCount,
    releasedCount,
    heldCount,
    lateCount,
    totalHeats,
    releasedHeats,
    totalMt,
    releasedMt,
    onTimePct,
    solverStatus: simulationStatusLabel(sim) || summary.solver_status || '—',
    maxUtil,
    capacityLabel: bottleneckResource ? String(bottleneckResource.Resource_ID || bottleneckResource.resource_id || 'peak resource').substring(0, 12) : 'peak resource',
    urgentPoolCount: poolOrders.filter(o => upper(o.priority) === 'URGENT').length,
    progressPct: Math.round((completedStages / 5) * 100),
    stageState,
    sim
  };
}

function hydrateSummary(summary){
  const kpis = deriveAppKpis(summary);
  const ot = kpis.onTimePct == null ? '—' : kpis.onTimePct.toFixed(1) + '%';
  const simSub = kpis.sim
    ? (kpis.sim.horizon_exceeded
      ? `Needs ${num(kpis.sim.total_duration_hours || 0).toFixed(1)}h vs ${num(kpis.sim.horizon_hours || 0).toFixed(1)}h horizon`
      : (kpis.sim.message || kpis.solverStatus))
    : (kpis.solverStatus || 'delivery rate');
  const planStatus = derivePlanStatus();

  setText('summaryCampaigns', kpis.planningOrderCount || '—');
  setText('summaryCampaignsSub', `${kpis.releasedCount} released · ${kpis.heldCount} held`);
  setText('summaryHeats', kpis.totalHeats || '—');
  setText('summaryHeatsSub', kpis.totalMt ? `${kpis.totalMt.toLocaleString()} MT total` : 'MT planned');
  setText('summaryMt', kpis.totalMt ? kpis.totalMt.toLocaleString() : '—');
  setText('summaryMtSub', kpis.totalHeats ? `${kpis.totalHeats} heats` : 'in heats');
  setText('summaryOt', ot);
  setText('summaryOtSub', kpis.lateCount ? `${kpis.lateCount} late` : simSub);
  setText('summaryCapacity', planStatus.value);
  setText('summaryCapacitySub', planStatus.sub);
  setMetricTone('summaryPlanCard', planStatus.tone);

  setText('chipSolverText', 'Solver ' + (kpis.solverStatus || '—'));
  setText('chipHeld', `${kpis.heldCount} on hold`);
  setText('chipLate', `${kpis.lateCount} late`);

  updateStatusBarProgress(kpis.progressPct);
}

function updateStatusBarProgress(progressPct = null) {
  const progress = progressPct == null ? deriveAppKpis().progressPct : Math.max(0, Math.min(100, num(progressPct)));
  const progressBar = qs('statusFooterProgressFill');
  if (progressBar) {
    progressBar.style.width = progress + '%';
  }
}

// ──────────────────────────────────────────────
//  DASHBOARD RENDERERS
// ──────────────────────────────────────────────

function renderDashboard() {
  renderDashboardPipeline();
  renderDashboardAlerts();
  renderBottleneck(state.overview || {});
  renderDashboardDelivery();
  renderDashboardInventory();
  renderDashActivePOs();
  renderDashboardExecution();
}


function renderDashboardPipeline() {
  const pos = state.planningOrders || [];
  const pool = state.poolOrders || [];
  const kpis = deriveAppKpis();
  const simReady = kpis.stageState.simulate;
  const sim = kpis.sim;
  const simDetail = sim
    ? (sim.feasible
      ? 'FEASIBLE · ' + (sim.horizon_hours || '—') + 'h horizon'
      : (sim.horizon_exceeded
        ? 'HORIZON EXCEEDED · ' + num(sim.total_duration_hours || 0).toFixed(1) + 'h vs ' + num(sim.horizon_hours || 0).toFixed(1) + 'h'
        : (simulationStatusLabel(sim) + ' — remediation needed')))
    : (simReady ? 'Loaded from saved schedule' : 'Not run');

  const stages = [
    {
      label: 'Order Pool',
      dot: kpis.stageState.pool ? 'done' : 'pending',
      detail: pool.length ? pool.length + ' SOs loaded · ' + kpis.urgentPoolCount + ' urgent' : 'Not loaded'
    },
    {
      label: 'Propose Orders',
      dot: kpis.stageState.propose ? 'done' : 'pending',
      detail: kpis.stageState.propose ? kpis.planningOrderCount + ' POs · ' + kpis.totalMt.toFixed(0) + ' MT' : 'Pending'
    },
    {
      label: 'Derive Heats',
      dot: kpis.stageState.heats ? 'done' : 'pending',
      detail: kpis.stageState.heats ? kpis.totalHeats + ' heats available' : 'Pending'
    },
    {
      label: 'Feasibility Check',
      dot: sim ? (sim.feasible ? 'done' : 'warn') : (simReady ? 'done' : 'pending'),
      detail: simDetail
    },
    {
      label: 'Release',
      dot: kpis.stageState.release ? 'done' : 'pending',
      detail: kpis.releasedCount + ' POs released'
    }
  ];

  qs('dashboardPipelineBody').innerHTML = stages.map(s => `
    <div class="dash-pipe-row">
      <div class="dash-pipe-dot ${s.dot}"></div>
      <div style="flex:1;min-width:0">
        <div style="font-weight:600;font-size:.7rem">${escapeHtml(s.label)}</div>
        <div style="font-size:.62rem;color:var(--text-soft);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${escapeHtml(s.detail)}</div>
      </div>
    </div>`).join('');
}

function renderDashboardAlerts() {
  const alerts = [];

  // Check urgent SOs not yet in PO
  const urgentUnplanned = (state.poolOrders||[]).filter(o=>o.priority==='URGENT' && !(state.planningOrders||[]).some(po=>(po.selected_so_ids||[]).includes(o.so_id)));
  if (urgentUnplanned.length > 0)
    alerts.push({type:'critical', icon:'⚠', title: urgentUnplanned.length + ' URGENT SOs not yet planned', sub: urgentUnplanned.slice(0,3).map(o=>o.so_id).join(', ') + (urgentUnplanned.length>3?'…':'')});

  // Infeasible plan
  if (state.lastSimResult && !state.lastSimResult.feasible)
    alerts.push({type:'critical', icon:'✗', title:'Plan is INFEASIBLE', sub:'Resources over capacity — open Feasibility Check'});

  // Over-capacity resources
  const overCap = dashboardCapacityRows().filter(r=>num(r['Utilisation_%']||r.Utilisation_Percent||0)>100);
  if (overCap.length > 0)
    alerts.push({type:'warn', icon:'◈', title: overCap.length + ' resource(s) over capacity', sub: overCap.slice(0,2).map(r=>String(r.Resource_ID||'').substring(0,8)).join(', ')});

  // Material shorts from inventory master
  const invItems = state.master?.inventory || [];
  const invShorts = invItems.filter(r=>num(r.Qty_On_Hand||r.qty_on_hand||0) < num(r.Min_Stock||r.min_stock||0));
  if (invShorts.length > 0)
    alerts.push({type:'warn', icon:'↓', title: invShorts.length + ' material(s) below minimum stock', sub: invShorts.slice(0,3).map(r=>String(r.SKU_ID||r.sku_id||'').substring(0,12)).join(', ')});

  // No POs proposed yet but SOs exist
  if ((state.poolOrders||[]).length > 0 && (state.planningOrders||[]).length === 0)
    alerts.push({type:'info', icon:'→', title:'Order Pool loaded — ready to propose POs', sub:'Go to Planning → Propose Orders'});

  // Heats derived but not simulated
  if ((state.heatBatches||[]).length > 0 && !state.lastSimResult)
    alerts.push({type:'info', icon:'→', title:'Heats derived — run Feasibility Check', sub:'Verify resource fit before releasing'});

  // All good
  if (alerts.length === 0)
    alerts.push({type:'ok', icon:'✓', title:'No active alerts', sub:'System nominal'});

  const countEl = qs('dashAlertCount');
  const critWarn = alerts.filter(a=>a.type==='critical'||a.type==='warn').length;
  if (countEl) {
    if (critWarn > 0) { countEl.textContent = critWarn; countEl.style.display = ''; }
    else countEl.style.display = 'none';
  }

  qs('dashAlertsList').innerHTML = alerts.map(a => `
    <div class="dash-alert ${a.type}">
      <div class="dash-alert-icon">${a.icon}</div>
      <div class="dash-alert-body">
        <div class="dash-alert-title">${escapeHtml(a.title)}</div>
        ${a.sub ? `<div class="dash-alert-sub">${escapeHtml(a.sub)}</div>` : ''}
      </div>
    </div>`).join('');
}

function renderBottleneck(summary) {
  const sorted = [...dashboardCapacityRows(summary)].sort((a,b)=>
    num(b['Utilisation_%']||b.Utilisation_Percent||b.utilisation||0) -
    num(a['Utilisation_%']||a.Utilisation_Percent||a.utilisation||0)
  ).slice(0, 8);

  const maxUtil = sorted.length ? num(sorted[0]['Utilisation_%']||sorted[0].Utilisation_Percent||sorted[0].utilisation||0) : 0;
  const peakPct = state.capacity?.length && summary.max_utilisation != null
    ? Math.round(num(summary.max_utilisation))
    : (maxUtil ? Math.round(maxUtil) : null);
  const peakEl = qs('bottleneckPeak');
  if (peakEl) {
    const srcLabel = state.capacity?.length ? '' : ' · simulated';
    peakEl.textContent = peakPct != null ? 'Peak ' + peakPct + '%' + srcLabel : '—';
    peakEl.style.color = peakPct > 90 ? 'var(--danger)' : peakPct > 75 ? 'var(--warning)' : 'var(--text-soft)';
  }

  qs('bottleneckList').innerHTML = sorted.length ? sorted.map(r => {
    const util = Math.round(num(r['Utilisation_%']||r.Utilisation_Percent||r.utilisation||0));
    const rid = String(r.Resource_ID||r.resource_id||'—').substring(0, 10);
    const plant = r.Plant || (rid.startsWith('SMS')||rid.startsWith('EAF')?'SMS':rid.startsWith('RM')||rid.startsWith('LRF')?'RM':'BF');
    const plantColor = plant==='BF'?'#3b82f6':plant==='SMS'?'#f97316':'#8b5cf6';
    const barColor = util > 100 ? 'var(--danger)' : util > 80 ? 'var(--warning)' : 'var(--success)';
    const demand = num(r.Demand_Hrs || r.demand_hrs || 0);
    const avail = num(r.Avail_Hrs_14d || r.avail_hrs || latestSimResult()?.horizon_hours || 0);
    return `<div class="dash-cap-row">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:.2rem">
        <div style="display:flex;align-items:center;gap:.35rem">
          <div style="width:.35rem;height:.35rem;border-radius:50%;background:${plantColor};flex-shrink:0"></div>
          <span style="font-size:.68rem;font-weight:600">${escapeHtml(rid)}</span>
        </div>
        <span style="font-size:.68rem;font-weight:700;color:${barColor}">${util}%</span>
      </div>
      <div class="dash-cap-bar-bg">
        <div class="dash-cap-bar" style="width:${Math.min(util,100)}%;background:${barColor}"></div>
      </div>
      <div style="display:flex;justify-content:space-between;gap:.5rem;font-size:.6rem;color:var(--text-faint)">
        <span>${escapeHtml(String(r.Operation_Group || r.Operation || _ridToOp(rid)))}</span>
        <span>${demand.toFixed(1)}h / ${avail.toFixed(1)}h</span>
      </div>
    </div>`;
  }).join('') : '<div style="text-align:center;color:var(--text-faint);font-size:.75rem;padding:1rem 0">Run Schedule or Feasibility Check to see utilisation</div>';
}

function renderDashboardDelivery() {
  const kpis = deriveAppKpis();
  const sim = latestSimResult();
  const scheduleRows = state.lastScheduleRows && state.lastScheduleRows.length ? state.lastScheduleRows : (state.gantt || []);
  const onTime = num(kpis.onTimePct, 0);
  const late = num(kpis.lateCount, 0);
  const avgLate = state.overview?.avg_days_late ?? '—';
  const planGap = avgLate !== '—'
    ? avgLate + 'd'
    : (sim?.horizon_exceeded
      ? `${num(sim.overflow_hours || 0).toFixed(1)}h over`
      : (sim?.feasible ? 'On horizon' : '—'));
  const color = onTime >= 90 ? 'var(--success)' : onTime >= 75 ? 'var(--warning)' : 'var(--danger)';
  const el = qs('perfOnTime');
  if (el) { el.textContent = kpis.onTimePct != null ? onTime + '%' : '—'; el.style.color = color; }
  const bar = qs('perfOnTimeBar');
  if (bar) { bar.style.width = onTime + '%'; bar.style.background = color; }
  setText('perfLate', late || '—');
  setText('perfAvgLate', planGap);
  const perfGap = qs('perfAvgLate');
  if (perfGap) {
    perfGap.style.color = sim?.horizon_exceeded ? 'var(--danger)' : sim?.feasible ? 'var(--success)' : 'var(--text)';
  }
}

function renderDashboardInventory() {
  // Show only material CONSTRAINTS: items that are SHORT or LOW
  // This tells planners which materials are blocking production
  const inv = state.master?.inventory || [];
  if (!inv.length) {
    qs('dashboardInventoryBody').innerHTML = '<div style="text-align:center;color:var(--text-faint);font-size:.75rem;padding:.5rem 0">Load Master Data to see inventory</div>';
    return;
  }

  const constrained = [...inv].map(r => {
    const qty = num(r.Qty_On_Hand || r.qty_on_hand || 0);
    const min = num(r.Min_Stock || r.min_stock || 100); // Default min stock if unset
    const status = qty < min ? 'SHORT' : qty < min * 1.2 ? 'LOW' : null; // Only show if constrained
    if (!status) return null;
    const shortfallMT = Math.max(0, min - qty);
    return { id: String(r.SKU_ID || r.sku_id || ''), qty, min, status, shortfallMT };
  }).filter(Boolean).sort((a,b) => {
    if (a.status !== b.status) return a.status === 'SHORT' ? -1 : 1;
    return b.shortfallMT - a.shortfallMT;
  }).slice(0, 10);

  if (constrained.length === 0) {
    qs('dashboardInventoryBody').innerHTML = '<div style="text-align:center;color:var(--success);font-size:.75rem;padding:.8rem .4rem"><div style="font-size:1.2rem;margin-bottom:.3rem">✓</div>All materials above safety stock</div>';
    return;
  }

  qs('dashboardInventoryBody').innerHTML = constrained.map(m => {
    const pct = Math.min(100, Math.round(m.qty / m.min * 100)); // % of minimum required
    const barColor = m.status === 'SHORT' ? 'var(--danger)' : 'var(--warning)';
    const tagBg = m.status === 'SHORT' ? 'var(--danger-soft)' : 'var(--warning-soft)';
    const tagColor = m.status === 'SHORT' ? 'var(--danger)' : 'var(--warning)';
    return `<div class="dash-mat-row">
      <div class="dash-mat-primary">
        <div class="dash-mat-name" title="${escapeHtml(m.id)}">${escapeHtml(m.id.substring(0,16))}</div>
        <div class="dash-mat-shortfall">${m.shortfallMT.toFixed(0)} MT short</div>
      </div>
      <div class="dash-mat-bar-wrap">
        <div class="dash-cap-bar-bg">
          <div class="dash-cap-bar" style="width:${pct}%;background:${barColor}"></div>
        </div>
        <div style="font-size:.58rem;color:var(--text-faint);margin-top:.12rem">${m.qty} MT on hand · need ${m.min} MT</div>
      </div>
      <span class="dash-mat-tag" style="background:${tagBg};color:${tagColor}">${m.status}</span>
    </div>`;
  }).join('');
}

function renderDashActivePOs() {
  const pos = state.planningOrders || [];
  if (!pos.length) {
    qs('dashActivePOs').innerHTML = '<div style="text-align:center;color:var(--text-faint);font-size:.75rem;padding:.4rem 0">No POs yet — run Propose Orders</div>';
    return;
  }
  const active = pos
    .filter(p => p.planner_status !== 'RELEASED')
    .sort((a, b) => num(b.total_qty_mt || 0) - num(a.total_qty_mt || 0))
    .slice(0, 7);
  qs('dashActivePOs').innerHTML = active.map(po => {
    const st = (po.planner_status || 'PROPOSED').toLowerCase();
    const mt = num(po.total_qty_mt||0).toFixed(0);
    const grade = (po.grade_family||'—').substring(0,10);
    const soCount = (po.selected_so_ids||[]).length;
    const badgeColor = st==='frozen' ? 'var(--warning)' : 'var(--info)';
    const heats = num(po.heats_required || 0);
    const dueWindow = Array.isArray(po.due_window) ? po.due_window.filter(Boolean).join(' → ') : '';
    return `<div class="dash-po-row ${st}">
      <div class="dash-po-main">
        <div style="font-weight:700;font-size:.7rem;white-space:nowrap">${escapeHtml(po.po_id)}</div>
        <div class="dash-po-meta">${escapeHtml(grade)} · ${soCount} SOs · ${heats} heats${dueWindow ? ` · ${escapeHtml(dueWindow)}` : ''}</div>
      </div>
      <div class="dash-po-side">
        <div style="font-size:.68rem;font-weight:700;white-space:nowrap">${mt} MT</div>
        <div style="font-size:.58rem;font-weight:700;color:${badgeColor};white-space:nowrap">${(po.planner_status||'PROP').substring(0,4)}</div>
      </div>
    </div>`;
  }).join('');
}

function renderDashboardExecution() {
  const kpis = deriveAppKpis();
  const jobs = state.lastScheduleRows && state.lastScheduleRows.length ? state.lastScheduleRows : (state.gantt || []);
  const resourceRows = dashboardCapacityRows();
  const today = new Date();
  today.setHours(0,0,0,0);
  const weekEnd = new Date(today);
  weekEnd.setDate(weekEnd.getDate() + 7);

  const thisWeekJobs = jobs.filter(j => {
    const s = parseDate(j.Planned_Start);
    return s >= today && s <= weekEnd;
  });
  const uniqueHeats = new Set(jobs.map(j => j.Heat_ID || j.heat_id).filter(Boolean));
  const activeEquip = new Set(jobs.map(j => j.Resource_ID || j.resource_id).filter(Boolean));

  setText('execStarted', (state.planningOrders || []).length || '—');
  setText('execHeats', uniqueHeats.size || thisWeekJobs.length || '—');
  setText('execEquip', activeEquip.size || resourceRows.length || '—');
  setText('execHolds', kpis.heldCount || '0');
}

function renderDashboardHolds(){
  // Retained for compatibility — holds now shown via alerts
}

function renderDashboardPerformance() {
  renderDashboardDelivery();
}

function renderCampaigns(){
  const box = qs('campaignCards');
  let items = [...(state.campaigns||[])];
  if(state.campFilter === 'released') items = items.filter(x=>String(x.release_status||x.Release_Status||'').toUpperCase()==='RELEASED');
  if(state.campFilter === 'held') items = items.filter(x=>String(x.release_status||x.Release_Status||x.Status||'').toUpperCase().includes('HOLD'));
  if(state.campFilter === 'late') items = items.filter(x=>String(x.Status||'').toUpperCase().includes('LATE'));
  if(!items.length){ box.innerHTML = '<div class="notice">No campaigns match the current filter.</div>'; return; }

  box.innerHTML = `<div style="overflow-x:auto">
  <table class="table" style="min-width:700px">
    <thead><tr>
      <th>Campaign / Grade</th>
      <th>Volume</th>
      <th>Heats</th>
      <th>Due</th>
      <th>Margin</th>
      <th>Status</th>
      <th style="text-align:right">Actions</th>
    </tr></thead>
    <tbody>
    ${items.map(c=>{
      const cid = c.campaign_id || c.Campaign_ID || '';
      const status = c.release_status || c.Release_Status || c.Status || '—';
      const isReleased = String(status).toUpperCase() === 'RELEASED';
      const margin = c.Margin_Hrs == null ? '—' : (num(c.Margin_Hrs)>=0?'+':'') + Math.round(num(c.Margin_Hrs)) + 'h';
      const marginColor = c.Margin_Hrs == null ? '' : num(c.Margin_Hrs) < 0 ? 'color:var(--danger)' : 'color:var(--success)';
      const rowStyle = isReleased ? 'background:rgba(34,197,94,.05);opacity:.75' : '';
      return `<tr style="${rowStyle}">
        <td><div style="font-weight:700;font-size:.82rem">${escapeHtml(cid)}${isReleased?' <span style="font-size:.65rem;background:#dcfce7;color:#16a34a;padding:.1rem .35rem;border-radius:.75rem;font-weight:700">✓ Released</span>':''}</div><div style="font-size:.72rem;color:var(--text-soft)">${escapeHtml(c.grade||c.Grade||'—')}</div></td>
        <td style="font-weight:600">${escapeHtml(String(c.total_mt||c.Total_MT||'—'))} MT</td>
        <td>${escapeHtml(String(c.heats||c.Heats||'—'))}</td>
        <td>${escapeHtml(fmtDate(c.Due_Date))}</td>
        <td style="${marginColor};font-weight:600">${escapeHtml(margin)}</td>
        <td>${badgeForStatus(status)}</td>
        <td style="text-align:right;white-space:nowrap">
          <button class="btn success" style="font-size:.72rem;padding:.25rem .55rem;${isReleased?'opacity:.4;cursor:not-allowed':''}" ${isReleased?'disabled':''} onclick="updateCampaignStatus('${escapeHtml(cid)}','Release_Status','RELEASED')">Release</button>
          <button class="btn warn" style="font-size:.72rem;padding:.25rem .55rem" onclick="updateCampaignStatus('${escapeHtml(cid)}','Release_Status','MATERIAL HOLD')">Hold</button>
          <button class="btn ghost" style="font-size:.72rem;padding:.25rem .55rem" onclick="activatePage('execution');switchExecView('gantt')">Gantt</button>
        </td>
      </tr>`;
    }).join('')}
    </tbody>
  </table></div>`;
}
function renderSchedule(){
  const wrap = qs('scheduleTimeline');
  const jobs = (state.gantt||[]);
  if(!jobs.length){ wrap.innerHTML = '<div class="notice">Run Schedule to populate the Gantt.</div>'; return; }
  const starts = jobs.map(j=>parseDate(j.Planned_Start)).filter(d=>!isNaN(d));
  const ends = jobs.map(j=>parseDate(j.Planned_End)).filter(d=>!isNaN(d));
  const t0 = starts.length ? new Date(Math.min(...starts)) : new Date();
  t0.setHours(0,0,0,0);
  const tEnd = ends.length ? new Date(Math.max(...ends)) : new Date(t0.getTime() + 14*86400000);
  const days = Math.max(14, Math.ceil((tEnd - t0) / 86400000) + 1);
  const horizonMs = days*86400000;

  // Group by campaign
  const campaigns = {};
  jobs.forEach(job=>{
    const cid = job.Campaign || 'Unknown';
    if(!campaigns[cid]) campaigns[cid] = {id:cid, grade:job.Grade, jobs:[], minStart:new Date(8640000000000000), maxEnd:new Date(0)};
    const s = parseDate(job.Planned_Start);
    const e = parseDate(job.Planned_End);
    if(!isNaN(s) && !isNaN(e)){
      campaigns[cid].jobs.push(job);
      if(s < campaigns[cid].minStart) campaigns[cid].minStart = s;
      if(e > campaigns[cid].maxEnd) campaigns[cid].maxEnd = e;
    }
  });

  const dayLabels=Array.from({length:days},(_,i)=>{const d=new Date(t0.getTime()+i*86400000);const m=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];return`${d.getDate()}<small>${m[d.getMonth()]}</small>`;});
  wrap.innerHTML='';
  const hdr=document.createElement('div');hdr.className='gantt-hdr';hdr.innerHTML=`<div class="gantt-res-col" style="font-weight:700">Campaign</div><div class="gantt-days">${dayLabels.map(d=>`<div class="gantt-day">${d}</div>`).join('')}</div>`;wrap.appendChild(hdr);

  Object.values(campaigns).sort((a,b)=>a.id.localeCompare(b.id)).forEach(camp=>{
    const row=document.createElement('div');row.className='gantt-row';row.innerHTML=`<div class="gantt-res-lbl"><div class="rid">${camp.id}</div><div class="rop" style="font-size:.7rem">${camp.grade||'—'}</div></div>`;
    const tl=document.createElement('div');tl.className='gantt-tl';
    const s = camp.minStart; const e = camp.maxEnd;
    if(!isNaN(s) && !isNaN(e) && s <= e){
      const left=Math.max(0,(s-t0)/horizonMs*100);
      const width=Math.max(1,(e-s)/horizonMs*100);
      const bar=document.createElement('div');bar.className='gantt-job eaf';bar.style.left=left+'%';bar.style.width=Math.min(width,100-left)+'%';bar.style.cursor='pointer';
      bar.innerHTML=`<span>${camp.id}</span> <span style="opacity:.6;font-size:9px">${camp.jobs.length}H</span>`;
      bar.addEventListener('click',()=>{selectCampaignFromGantt(camp.id);});
      bar.addEventListener('mouseenter',ev=>{const tt=document.getElementById('tt');tt.innerHTML=`<strong>${camp.id}</strong><br>Grade: ${camp.grade||'—'}<br>Heats: ${camp.jobs.length}<br>Start: ${fmtDate(s)}<br>End: ${fmtDate(e)}<br><small style="color:var(--brand)">Click to view equipment schedule</small>`;tt.classList.add('show');});
      bar.addEventListener('mousemove',ev=>{const tt=document.getElementById('tt');tt.style.left=(ev.clientX+14)+'px';tt.style.top=(ev.clientY-10)+'px';});
      bar.addEventListener('mouseleave',()=>{document.getElementById('tt').classList.remove('show');});
      tl.appendChild(bar);
    }
    row.appendChild(tl);
    wrap.appendChild(row);
  });
}
function selectCampaignFromGantt(campaignId) {
  state.selectedCampaign = campaignId;

  const panel = qs('campaignDetailsPanel');
  const content = qs('campaignDetailsContent');
  const clearBtn = qs('clearCampaignBtn');
  const campSel = qs('dispatchFilterCampaign');

  if (panel) panel.style.display = 'block';
  if (content) {
    content.innerHTML = `<div style="padding:1rem"><strong>${escapeHtml(campaignId)}</strong><br><small>Campaign details loading...</small></div>`;
  }
  if (clearBtn) clearBtn.style.display = 'block';
  if (campSel) campSel.value = campaignId;

  activatePage('execution');
  switchExecView('timeline');
  renderDispatch();
  renderCampaignDetails();
}

function clearCampaignSelection(){
  state.selectedCampaign = null;
  document.getElementById('clearCampaignBtn').style.display = 'none';
  const panel = document.getElementById('campaignDetailsPanel');
  if(panel) panel.style.display = 'none';
  const campSel = qs('dispatchFilterCampaign');
  if(campSel) campSel.value = '';
  renderDispatch();
}
function togglePlant(plantId){
  const body = document.getElementById(plantId);
  if(body) body.style.display = body.style.display === 'none' ? 'block' : 'none';
  const header = body?.previousElementSibling;
  if(header) header.querySelector('span').textContent = body.style.display === 'none' ? '▶' : '▼';
}
function renderCampaignDetails(){
  const panel = qs('campaignDetailsPanel');
  const contentDiv = qs('campaignDetailsContent');
  if(!state.selectedCampaign){
    panel.style.display = 'none';
    return;
  }
  const campaign = (state.campaigns || []).find(c=>(c.campaign_id || c.Campaign_ID) === state.selectedCampaign);
  if(!campaign){
    contentDiv.innerHTML = '<div style="padding:1rem;color:var(--text-faint)">Campaign not found</div>';
    panel.style.display = 'block';
    return;
  }
  const grade = campaign.grade || campaign.Grade || '—';
  const totalMt = campaign.total_mt || campaign.Total_MT || 0;
  const heats = campaign.heats || campaign.Heats || 0;
  const dueDate = campaign.Due_Date || campaign.due_date || '—';
  const soIds = campaign.so_list || campaign.SO_List || [];

  const orders = (state.orders || []).filter(o=>soIds.includes(o.SO_ID || o.so_id));

  contentDiv.innerHTML = `
    <div class="campaign-details-panel-header">
      <div class="campaign-details-panel-header-title">${escapeHtml(state.selectedCampaign)}</div>
      <div class="campaign-details-panel-header-grade">${escapeHtml(grade)}</div>
    </div>
    <div class="campaign-summary">
      <div class="summary-row">
        <span class="summary-row-label">Material:</span>
        <span class="summary-row-value">${totalMt.toFixed(1)} MT</span>
      </div>
      <div class="summary-row">
        <span class="summary-row-label">Heats:</span>
        <span class="summary-row-value">${heats}</span>
      </div>
      <div class="summary-row">
        <span class="summary-row-label">Due Date:</span>
        <span class="summary-row-value">${escapeHtml(String(dueDate))}</span>
      </div>
      <div class="summary-row">
        <span class="summary-row-label">Orders:</span>
        <span class="summary-row-value">${soIds.length}</span>
      </div>
    </div>
    <div class="campaign-orders-section">
      <div class="campaign-orders-title">Sales Orders</div>
      ${orders.length > 0 ? orders.map((o,idx)=>{
        const custName = o.Customer || o.customer || '—';
        const priority = (o.Priority || o.priority || 'NORMAL').toUpperCase();
        const deliveryDate = o.Delivery_Date || o.delivery_date || '—';
        const qty = o.Order_Qty_MT || o.qty_mt || 0;
        const sku = o.SKU || o.SKU_ID || o.sku || '—';
        const priorityClass = priority === 'URGENT' ? 'urgent' : priority === 'HIGH' ? 'high' : 'normal';
        return `<div class="order-item" onclick="toggleOrderItem(this)">
          <div class="order-item-header">
            <div class="order-item-id">${escapeHtml(o.SO_ID || o.so_id || '—')}</div>
            <div class="order-item-priority ${priorityClass}">${escapeHtml(priority)}</div>
          </div>
          <div class="order-item-details">
            <div class="order-detail-row">
              <span class="order-detail-label">Customer:</span>
              <span class="order-detail-value">${escapeHtml(custName)}</span>
            </div>
            <div class="order-detail-row">
              <span class="order-detail-label">Delivery:</span>
              <span class="order-detail-value">${escapeHtml(String(deliveryDate))}</span>
            </div>
            <div class="order-detail-row">
              <span class="order-detail-label">Quantity:</span>
              <span class="order-detail-value">${qty.toFixed(1)} MT</span>
            </div>
            <div class="order-detail-row">
              <span class="order-detail-label">SKU:</span>
              <span class="order-detail-value">${escapeHtml(sku)}</span>
            </div>
          </div>
        </div>`;
      }).join('') : '<div style="font-size:.8rem;color:var(--text-faint);padding:.5rem">No orders linked</div>'}
    </div>
  `;
  panel.style.display = 'block';
}
function toggleOrderItem(el){
  el.classList.toggle('expanded');
}
function filterDispatchByDropdown(){
  const campaignFilter = qs('dispatchFilterCampaign').value;
  const gradeFilter = qs('dispatchFilterGrade').value;
  const plantFilter = qs('dispatchFilterPlant').value;

  state.dispatchFilters = { campaignFilter, gradeFilter, plantFilter };
  renderDispatch();
}
function populateDispatchFilters(){
  const campaigns = [...new Set((state.gantt || []).map(j=>j.Campaign || j.campaign_id).filter(Boolean))].sort();
  const grades = [...new Set((state.gantt || []).map(j=>j.Grade || '').filter(Boolean))].sort();
  const plants = [...new Set((state.gantt || []).map(j=>j.Plant || 'Shared').filter(Boolean))].sort();

  const campSel = qs('dispatchFilterCampaign');
  const gradeSel = qs('dispatchFilterGrade');
  const plantSel = qs('dispatchFilterPlant');

  campSel.innerHTML = '<option value="">All Campaigns</option>' + campaigns.map(c=>`<option value="${escapeHtml(c)}">${escapeHtml(c)}</option>`).join('');
  gradeSel.innerHTML = '<option value="">All Grades</option>' + grades.map(g=>`<option value="${escapeHtml(g)}">${escapeHtml(g)}</option>`).join('');
  plantSel.innerHTML = '<option value="">All Plants</option>' + plants.map(p=>`<option value="${escapeHtml(p)}">${escapeHtml(p)}</option>`).join('');
}
function renderDispatch(){
  const grid = qs('dispatchGrid');
  let jobs = state.gantt || [];
  const filters = state.dispatchFilters || {};

  if(filters.campaignFilter) jobs = jobs.filter(j=>(j.Campaign||j.campaign_id)===filters.campaignFilter);
  if(filters.gradeFilter) jobs = jobs.filter(j=>(j.Grade||'')===filters.gradeFilter);
  if(filters.plantFilter) jobs = jobs.filter(j=>(j.Plant||'Shared')===filters.plantFilter);
  if(state.selectedCampaign) jobs = jobs.filter(j=>(j.Campaign||j.campaign_id)===state.selectedCampaign);

  if(!jobs.length){
    grid.innerHTML = '<div class="notice">No schedule data. Run Schedule first.</div>';
    setText('dispatchMachines','—'); setText('dispatchJobs','—'); setText('dispatchDuration','—'); setText('dispatchMt','—');
    return;
  }

  // Time range
  const allStarts = jobs.map(j => parseDate(j.Planned_Start)).filter(d => !Number.isNaN(d.getTime()));
  const allEnds   = jobs.map(j => parseDate(j.Planned_End)).filter(d => !Number.isNaN(d.getTime()));
  const t0 = allStarts.length ? new Date(Math.min(...allStarts)) : new Date();
  t0.setHours(0,0,0,0);
  const tMax = allEnds.length ? new Date(Math.max(...allEnds)) : new Date(t0.getTime()+14*86400000);
  const totalMs = Math.max(tMax - t0, 86400000);

  // KPIs
  const byResource = {};
  jobs.forEach(j=>{ const r=j.Resource_ID||'?'; if(!byResource[r]) byResource[r]=[]; byResource[r].push(j); });
  const totalMt = jobs.reduce((a,j)=>a+num(j.Qty_MT||j.Order_Qty_MT||j.qty_mt),0);
  const durationH = Math.round((tMax-t0)/3600000);
  setText('dispatchMachines', Object.keys(byResource).length);
  setText('dispatchJobs', jobs.length);
  setText('dispatchDuration', durationH+'h');
  setText('dispatchMt', totalMt.toFixed(1)+' MT');

  // Day axis labels
  const days = Math.ceil(totalMs/86400000);
  let axisHtml = '';
  for(let i=0;i<days;i++){
    const d = new Date(t0.getTime()+i*86400000);
    const pct = (i/days*100).toFixed(2);
    axisHtml += `<div style="position:absolute;left:${pct}%;top:0;height:100%;border-left:1px solid var(--border-soft);padding-left:3px;font-size:.62rem;color:var(--text-faint);white-space:nowrap">${d.toLocaleDateString('en-US',{month:'short',day:'numeric'})}</div>`;
  }

  // Build rows — one flat row per resource, individual job bars
  const LABEL_W = 80; // px
  let html = `<div style="display:table;width:100%;border-collapse:collapse">`;

  // Header axis row
  html += `<div style="display:table-row">
    <div style="display:table-cell;width:${LABEL_W}px;vertical-align:middle;padding-right:6px"></div>
    <div style="display:table-cell;vertical-align:top;padding-bottom:4px">
      <div style="position:relative;height:22px;background:var(--panel-muted);border-radius:3px;border:1px solid var(--border-soft)">${axisHtml}</div>
    </div>
  </div>`;

  Object.keys(byResource).sort().forEach(rid=>{
    const list = byResource[rid];
    const utilRow = (state.capacity||[]).find(c=>(c.Resource_ID||c.resource_id)===rid);
    const util = Math.round(num(utilRow&&(utilRow['Utilisation_%']||utilRow.utilisation)));
    const pillCls = util>100?'color:#ef4444;font-weight:700':util>85?'color:#f59e0b;font-weight:700':'color:#22c55e;font-weight:700';

    // Individual job bars
    const bars = list.map(j=>{
      const s = parseDate(j.Planned_Start);
      const e = parseDate(j.Planned_End);
      if (Number.isNaN(s.getTime()) || Number.isNaN(e.getTime())) return '';
      const left  = Math.max(0,(s-t0)/totalMs*100);
      const width = Math.max(0.3,(e-s)/totalMs*100);
      const op = (j.Operation||'').toUpperCase();
      const cls = ['EAF','LRF','VD','CCM','RM'].includes(op)?op.toLowerCase():'eaf';
      const camp = escapeHtml(j.Campaign||'');
      return `<div class="eq-gantt-bar ${cls}" style="position:absolute;left:${left.toFixed(2)}%;width:${Math.min(width,100-left).toFixed(2)}%;top:1px;bottom:1px;border-radius:2px;cursor:pointer;min-width:2px" onclick="selectCampaignFromGantt('${camp}')" title="${camp} | ${escapeHtml(j.Job_ID||'')} | ${fmtDateTime(s)} → ${fmtDateTime(e)}"></div>`;
    }).join('');

    html += `<div style="display:table-row">
      <div style="display:table-cell;width:${LABEL_W}px;vertical-align:middle;padding:.25rem .4rem .25rem 0;border-top:1px solid var(--border-soft)">
        <div style="font-size:.75rem;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${escapeHtml(rid)}</div>
        <div style="font-size:.62rem;${pillCls}">${util}%</div>
      </div>
      <div style="display:table-cell;vertical-align:middle;padding:.25rem 0;border-top:1px solid var(--border-soft)">
        <div style="position:relative;height:34px;background:var(--panel-soft);border-radius:3px;border:1px solid var(--border-soft);overflow:hidden">${bars}</div>
      </div>
    </div>`;
  });

  html += '</div>';
  grid.innerHTML = html;
  populateDispatchFilters();
  if(state.selectedCampaign) renderCampaignDetails();
}
function deriveMaterialRows(){
  return (state.campaigns || []).flatMap(c=>{
    const shortages = c.shortages || [];
    if(shortages.length){
      return shortages.map(s=>({
        Campaign_ID: c.campaign_id || c.Campaign_ID,
        Grade: c.grade || c.Grade,
        Material_SKU: s.sku_id || s.Material_SKU,
        Required_Qty: s.qty || s.Required_Qty,
        Status: c.release_status || c.Release_Status || 'MATERIAL HOLD'
      }));
    }
    return [];
  });
}
function renderMaterial(){
  // state.material is {summary:{}, campaigns:[...]} — not an array
  const matPlan = (state.material && typeof state.material === 'object') ? state.material : {};
  let camps = matPlan.campaigns || [];

  // Fallback: derive from state.campaigns if material plan not populated
  if(!camps.length){
    camps = (state.campaigns||[]).map(c=>({
      campaign_id: c.campaign_id || c.Campaign_ID || '',
      grade: c.grade || c.Grade || '—',
      release_status: c.release_status || c.Release_Status || '',
      required_qty: num(c.total_mt || c.Total_MT),
      shortage_qty: (c.shortages||[]).reduce((a,s)=>a+num(s.qty),0),
      material_status: c.material_status || '',
      shortages: c.shortages || []
    }));
  }

  if(!camps.length){
    qs('materialDetailContent').innerHTML = '<div class="material-detail-empty">No material data. Run BOM Netting first.</div>';
    qs('materialTree').innerHTML = '';
    return;
  }

  const withShortage = camps.filter(c=>num(c.shortage_qty)>0).length;
  const withHold     = camps.filter(c=>String(c.material_status||'').toUpperCase().includes('HOLD')).length;
  const covered      = camps.filter(c=>num(c.shortage_qty)<=0 && !String(c.material_status||'').toUpperCase().includes('HOLD')).length;
  setText('matOk', covered);
  setText('matLow', withHold);
  setText('matCrit', withShortage);
  setText('matHeld', withHold);

  buildMaterialTree(camps);
}

function buildMaterialTree(camps){
  state.materialCampaigns = [...camps].sort((a,b)=>num(b.shortage_qty)-num(a.shortage_qty));
  const tree = qs('materialTree');
  tree.innerHTML = state.materialCampaigns.map((camp, idx)=>{
    const shortQty = num(camp.shortage_qty);
    const isHold   = String(camp.material_status||'').toUpperCase().includes('HOLD');
    const icon     = shortQty > 0 ? '⚠' : isHold ? '⏸' : '✓';
    const iconColor= shortQty > 0 ? 'var(--danger)' : isHold ? 'var(--warning)' : 'var(--success)';
    const label    = shortQty > 0 ? shortQty.toFixed(1)+' MT short' : isHold ? 'On hold' : 'Ready';
    const cid      = camp.campaign_id || camp.id || '';
    return '<div class="tree-item">'+
      '<div class="tree-node campaign" onclick="selectMaterialCampaign(this,'+idx+')" data-campaign="'+escapeHtml(cid)+'">'+
        '<div class="tree-toggle leaf"></div>'+
        '<div class="tree-node-icon" style="color:'+iconColor+'">'+icon+'</div>'+
        '<span style="font-size:.8rem">'+escapeHtml(cid)+'<span style="font-size:.7rem;color:var(--text-faint);margin-left:.4rem">'+escapeHtml(camp.grade||'—')+' — '+label+'</span></span>'+
      '</div>'+
    '</div>';
  }).join('');

  if(state.materialCampaigns.length > 0){
    const first = qs('materialTree')?.querySelector('.tree-node.campaign');
    if(first) first.classList.add('selected');
    renderMaterialDetail(state.materialCampaigns[0]);
  }
}

function selectMaterialCampaign(el, idx){
  document.querySelectorAll('.tree-node.campaign').forEach(n=>n.classList.remove('selected'));
  el.classList.add('selected');
  renderMaterialDetail(state.materialCampaigns[idx]);
}

function renderMaterialDetail(campaign){
  const cid       = campaign.campaign_id || campaign.id || 'Unknown';
  const grade     = campaign.grade || '—';
  const reqQty    = num(campaign.required_qty);
  const shortQty  = num(campaign.shortage_qty);
  const matStatus = campaign.material_status || '';
  const isHold    = matStatus.toUpperCase().includes('HOLD');

  // Shortages can be in campaign.shortages (array) or campaign.material_shortages (object)
  const shortages = campaign.shortages && campaign.shortages.length > 0
    ? campaign.shortages
    : Object.entries(campaign.material_shortages || {}).map(([sku_id, qty])=>({sku_id, qty}));

  let html = `<div class="material-detail-header">
    <div class="material-detail-title">${escapeHtml(cid)}</div>
    <div class="material-detail-subtitle">${escapeHtml(grade)} — ${escapeHtml(campaign.release_status||'—')} — ${escapeHtml(matStatus||'OK')}</div>
  </div>
  <div class="material-detail-stats">
    <div class="material-detail-stat" style="background:rgba(34,197,94,.1);border-color:rgba(34,197,94,.3)">
      <div class="material-detail-stat-label">Required</div>
      <div class="material-detail-stat-value">${reqQty.toFixed(1)} MT</div>
    </div>
    <div class="material-detail-stat" style="background:rgba(220,38,38,.1);border-color:rgba(220,38,38,.3)">
      <div class="material-detail-stat-label">Short</div>
      <div class="material-detail-stat-value" style="color:${shortQty>0?'#dc2626':'#16a34a'}">${shortQty.toFixed(1)} MT</div>
    </div>
    <div class="material-detail-stat" style="background:rgba(245,158,11,.1);border-color:rgba(245,158,11,.3)">
      <div class="material-detail-stat-label">Status</div>
      <div class="material-detail-stat-value" style="font-size:.8rem">${escapeHtml(matStatus||'OK')}</div>
    </div>
  </div>`;

  if(shortages.length > 0){
    html += `<div style="margin-top:1.25rem">
      <div style="font-weight:700;font-size:.82rem;padding:.5rem .8rem;background:rgba(220,38,38,.08);border-left:3px solid #dc2626;margin-bottom:.5rem">Material Shortages</div>
      <table class="table" style="font-size:.78rem">
        <thead><tr><th>SKU</th><th style="text-align:right">Shortage MT</th></tr></thead>
        <tbody>${shortages.map(s=>`<tr>
          <td style="font-weight:600">${escapeHtml(s.sku_id||s.Material_SKU||'')}</td>
          <td style="text-align:right;color:#dc2626;font-weight:700">${num(s.qty||s.Required_Qty||0).toFixed(1)}</td>
        </tr>`).join('')}</tbody>
      </table></div>`;
  } else {
    html += `<div style="margin-top:2rem;padding:1.5rem;text-align:center;color:var(--text-faint)">
      <div style="font-size:2rem;margin-bottom:.4rem">✓</div>
      <div style="font-weight:600">All materials available</div>
    </div>`;
  }

  qs('materialDetailContent').innerHTML = html;
}
function renderCapacity(){
  const rows = [...(state.capacity||[])].sort((a,b)=>num(b['Utilisation_%'] || b.utilisation) - num(a['Utilisation_%'] || a.utilisation));
  const body = qs('capacityBody');
  if(!rows.length){ body.innerHTML = '<tr><td colspan="6">No capacity rows loaded.</td></tr>'; return; }
  const avg = rows.reduce((a,r)=>a + num(r['Utilisation_%'] || r.utilisation),0) / Math.max(rows.length,1);
  setText('capAvg', avg.toFixed(1) + '%');
  setText('capBottleneck', rows[0].Resource_ID || rows[0].resource_id || '—');
  setText('capBottleneckSub', Math.round(num(rows[0]['Utilisation_%'] || rows[0].utilisation)) + '% utilisation');
  body.innerHTML = rows.map(r=>{
    const util = Math.round(num(r['Utilisation_%'] || r.utilisation));
    return '<tr>'+
      '<td><strong>'+escapeHtml(r.Resource_ID || r.resource_id || '—')+'</strong></td>'+
      '<td>'+escapeHtml(r.Operation_Group || r.Operation || _ridToOp(r.Resource_ID || r.resource_id))+'</td>'+
      '<td>'+escapeHtml(r.Demand_Hrs || r.demand_hrs || '—')+'</td>'+
      '<td>'+escapeHtml(r.Avail_Hrs_14d || r.avail_hrs || '—')+'</td>'+
      '<td>'+util+'%</td>'+
      '<td>'+badgeForStatus(r.Status || (util > 100 ? 'Overloaded' : util > 85 ? 'High' : 'OK'))+'</td>'+
    '</tr>';
  }).join('');
}
function renderScenarios(){
  const items = state.scenarios || [];
  const cardBox = qs('scenarioCards');
  const tableBody = qs('scenarioTableBody');
  const top = items.slice(0,3);
  cardBox.innerHTML = top.map((r, idx)=>{
    const key = Object.keys(r)[0];
    const value = r[key];
    return '<div class="card"><div class="card-body">'+
      '<div class="metric-label">Scenario</div><div class="metric-value">'+escapeHtml(key || ('Scenario ' + (idx+1)))+'</div>'+
      '<div class="metric-sub">'+escapeHtml(value || 'Workbook-backed scenario row')+'</div>'+
      '<div style="margin-top:.75rem;display:flex;gap:.45rem;flex-wrap:wrap"><button class="btn primary" onclick="applyScenarioByName(\''+escapeHtml(key || '')+'\')">Apply</button><button class="btn" onclick="editScenario(\''+escapeHtml(key || '')+'\')">Edit</button></div>'+
    '</div></div>';
  }).join('') || '<div class="notice">No scenario rows available.</div>';
  tableBody.innerHTML = items.map(r=>{
    const key = Object.keys(r)[0] || '—';
    const value = r[key];
    return '<tr><td><strong>'+escapeHtml(key)+'</strong></td><td>'+escapeHtml(value)+'</td><td><button class="btn" onclick="applyScenarioByName(\''+escapeHtml(key)+'\')">Apply</button> <button class="btn" onclick="editScenario(\''+escapeHtml(key)+'\')">Edit</button> <button class="btn danger" onclick="deleteScenario(\''+escapeHtml(key)+'\')">Delete</button></td></tr>';
  }).join('') || '<tr><td colspan="3">No scenarios returned.</td></tr>';
}
function renderCTPHistory(){
  const body = qs('ctpHistoryBody');
  const reqs = state.ctpRequests || [];
  const outs = state.ctpOutput || [];
  const merged = reqs.slice(0,12).map(r=>{
    const key = r.Request_ID || r.request_id;
    const out = outs.find(o=>String(o.Request_ID || o.request_id || '') === String(key || '')) || {};
    return {
      sku: r.SKU_ID || r.sku_id || '—',
      qty: r.Order_Qty_MT || r.qty_mt || '—',
      requested: r.Requested_Date || r.requested_date || '',
      earliest: out.Earliest_Delivery || out.earliest_delivery || '',
      margin: out.Lateness_Days ?? out.lateness_days ?? '—',
      feasible: out.Feasible ?? out.feasible ?? out.Plant_Completion_Feasible ?? out.plant_completion_feasible
    };
  });
  body.innerHTML = merged.map(r=>'<tr>'+
    '<td>'+escapeHtml(r.sku)+'</td><td>'+escapeHtml(r.qty)+'</td><td>'+escapeHtml(fmtDate(r.requested))+'</td><td>'+escapeHtml(fmtDate(r.earliest))+'</td><td>'+escapeHtml(r.margin)+'</td><td>'+badgeForStatus(r.feasible === true ? 'FEASIBLE' : r.feasible === false ? 'NOT FEASIBLE' : '—')+'</td>'+
  '</tr>').join('') || '<tr><td colspan="6">No CTP history yet.</td></tr>';
}
function renderMaster(){
  const section = qs('masterSection').value;
  const rows = state.master[section] || [];
  setText('masterTitle', MASTER_LABELS[section] || section);
  const head = qs('masterHead');
  const body = qs('masterBody');
  if(!rows.length){
    head.innerHTML = '';
    body.innerHTML = '<tr><td>No rows returned for this section.</td></tr>';
    setText('masterSelectionNote', 'No row selected.');
    state.selectedMasterKey = null;
    return;
  }
  const cols = Object.keys(rows[0]);
  head.innerHTML = '<tr>' + cols.map(c=>'<th>'+escapeHtml(c)+'</th>').join('') + '</tr>';
  const keyField = MASTER_KEYS[section] || cols[0];
  body.innerHTML = rows.map(r=>{
    const key = String(r[keyField] ?? r[cols[0]] ?? '');
    const selected = state.selectedMasterKey === key ? ' class="master-row-selected"' : '';
    return '<tr'+selected+' data-key="'+escapeHtml(key)+'">'+cols.map(c=>'<td>'+escapeHtml(r[c] ?? '—')+'</td>').join('')+'</tr>';
  }).join('');
  body.querySelectorAll('tr[data-key]').forEach(tr=>{
    tr.addEventListener('click', ()=>{
      state.selectedMasterKey = tr.dataset.key;
      setText('masterSelectionNote', 'Selected ' + state.selectedMasterKey);
      renderMaster();
    });
  });
}
async function loadApplicationState(options = {}){
  const { deferHeavy = false } = options;
  const [overview, campaigns, releaseQueue, gantt, capacity, material, dispatch, planningOrders] = await Promise.all([
    apiFetch('/api/aps/dashboard/overview').catch(()=>null),
    apiFetch('/api/aps/campaigns/list').catch(()=>({items:[]})),
    apiFetch('/api/aps/campaigns/release-queue').catch(()=>({items:[]})),
    apiFetch('/api/aps/schedule/gantt').catch(()=>({jobs:[]})),
    apiFetch('/api/aps/capacity/map').catch(()=>({items:[]})),
    apiFetch('/api/aps/material/plan').catch(()=>({items:[]})),
    apiFetch('/api/aps/dispatch/board').catch(()=>({resources:[]})),
    apiFetch('/api/aps/planning/orders').catch(()=>({planning_orders:[]}))
  ]);

  state.overview = overview ? overview.summary || overview : null;
  state.lastSimResult = overview?.last_simulation || null;
  state.campaigns = campaigns.items || [];
  state.releaseQueue = releaseQueue.items || [];
  state.gantt = gantt.jobs || [];
  state.lastScheduleRows = state.gantt;
  state.capacity = capacity.items || [];
  state.material = material || { summary: {}, campaigns: [] };
  state.dispatch = dispatch.resources || [];
  // Restore in-memory planning orders (persists across page refresh if server is running)
  state.planningOrders = planningOrders.planning_orders || [];
  refreshSimulationGradeOptions();

  hydrateSummary(state.overview || {});
  renderDashboard();
  renderCampaigns();
  renderSchedule();
  renderDispatch();
  renderMaterial();
  renderCapacity();

  const loadDeferredData = async () => {
    const [scenarios, ctpReqs, ctpOut, master] = await Promise.all([
      apiFetch('/api/aps/scenarios/list').catch(()=>({items:[]})),
      apiFetch('/api/aps/ctp/requests').catch(()=>({items:[]})),
      apiFetch('/api/aps/ctp/output').catch(()=>({items:[]})),
      apiFetch('/api/aps/masterdata').catch(()=>({}))
    ]);

    state.scenarios = scenarios.items || [];
    state.ctpRequests = ctpReqs.items || [];
    state.ctpOutput = ctpOut.items || [];
    state.master = master || {};

    renderDashboard();
    renderScenarios();
    renderCTPHistory();
    renderMaster();
  };

  if (deferHeavy) {
    void loadDeferredData().catch(err => console.warn('Deferred app state load failed:', err));
  } else {
    await loadDeferredData();
  }
}
async function loadOrdersOnly(){
  const d = await apiFetch('/api/aps/orders/list').catch(()=>({items:[]}));
  state.orders = d.items || [];
  // Orders loaded for use by campaigns and other features
}
async function loadSkusForCtp(){
  const d = await apiFetch('/api/aps/masterdata/skus').catch(async ()=>await apiFetch('/api/data/skus').catch(()=>({items:[], skus:[]})));
  const rows = d.items || d.skus || [];
  qs('ctpSku').innerHTML = '<option value="">Select SKU…</option>' + rows.map(r=>{
    const id = r.SKU_ID || r.sku_id || '';
    const nm = r.SKU_Name || r.sku_name || '';
    return '<option value="'+escapeHtml(id)+'">'+escapeHtml(id + (nm ? (' · ' + nm) : ''))+'</option>';
  }).join('');
}
async function runSchedule(){
  const btn = qs('runBtn');
  const loadingBar = qs('loadingBar');
  const old = btn.innerHTML;
  const oldDisabled = btn.disabled;

  try{
    // Immediately show loading state
    btn.textContent = '';
    btn.innerHTML = '<span class="spinner"></span> Running Schedule...';
    btn.disabled = true;
    btn.style.opacity = '0.8';

    // Show loading bar
    if(loadingBar) {
      loadingBar.style.opacity = '1';
      loadingBar.style.width = '10%';
      loadingBar.style.background = 'linear-gradient(90deg, #7c3aed, #0f9f8c)';
    }

    // Animate loading bar
    const barInterval = setInterval(() => {
      if(loadingBar && parseInt(loadingBar.style.width) < 90) {
        loadingBar.style.width = (parseInt(loadingBar.style.width) + Math.random() * 20) + '%';
      }
    }, 300);

    // Run the schedule
    const result = await apiFetch('/api/aps/schedule/run', {
      method:'POST',
      body: JSON.stringify({
        time_limit: Number(qs('solverDepth').value || 60),
        horizon_days: Number(state.horizon || 14)
      })
    });

    clearInterval(barInterval);

    // Show near completion
    if(loadingBar) loadingBar.style.width = '95%';

    await loadApplicationState();
    await loadOrdersOnly();

    // Complete
    if(loadingBar) {
      loadingBar.style.width = '100%';
      setTimeout(() => {
        loadingBar.style.opacity = '0';
        loadingBar.style.width = '0%';
      }, 800);
    }

    // Show success and reset button
    btn.innerHTML = '✓ Schedule Complete';
    btn.style.opacity = '1';

    // Force reset after delay
    setTimeout(() => {
      btn.textContent = 'Run Schedule';
      btn.disabled = false;
      btn.style.opacity = '1';
      btn.classList.remove('loading');
    }, 2500);

  }catch(e){
    if(loadingBar) {
      loadingBar.style.background = 'linear-gradient(90deg, #ef4444, #dc2626)';
      loadingBar.style.width = '100%';
    }
    btn.innerHTML = '✗ Error: ' + e.message.substring(0, 30);
    btn.style.opacity = '1';
    btn.disabled = false;

    setTimeout(() => {
      btn.textContent = 'Run Schedule';
      btn.disabled = false;
      btn.style.opacity = '1';
      btn.classList.remove('loading');
    }, 3500);
  }
}
async function updateCampaignStatus(campaignId, fieldName, value){
  try{
    const payload = {}; payload[fieldName] = value;
    await apiFetch('/api/aps/campaigns/' + encodeURIComponent(campaignId) + '/status', {method:'PATCH', body: JSON.stringify({data: payload})});
    await loadApplicationState();
  }catch(e){ alert('Campaign update failed: ' + e.message); }
}
async function runCtp(){
  const sku = qs('ctpSku').value;
  if(!sku){ alert('Select a SKU first.'); return; }
  const payload = {sku_id: sku, qty_mt: Number(qs('ctpQty').value || 0), requested_date: qs('ctpDate').value};
  const btn = qs('ctpRunBtn');
  const old = btn.innerHTML;
  btn.innerHTML = '<span class="spinner"></span> Checking…';
  btn.disabled = true;
  try{
    await apiFetch('/api/aps/ctp/requests', {method:'POST', body: JSON.stringify({data:{Request_ID:'REQ-'+Date.now(), SKU_ID: payload.sku_id, Order_Qty_MT: payload.qty_mt, Requested_Date: payload.requested_date}})}).catch(()=>null);
    const d = await apiFetch('/api/aps/ctp/check', {method:'POST', body: JSON.stringify(payload)});
    qs('ctpResult').innerHTML = '<strong>' + escapeHtml((d.feasible || d.plant_completion_feasible) ? 'Feasible' : 'Not feasible') + '</strong><br>' +
      'Earliest delivery: ' + escapeHtml(fmtDate(d.earliest_delivery)) + '<br>' +
      'Margin / lateness days: ' + escapeHtml(d.lateness_days ?? '—') + '<br>' +
      'Material gaps: ' + escapeHtml((d.material_gaps || []).length);
    const reqs = await apiFetch('/api/aps/ctp/requests').catch(()=>({items:[]}));
    const outs = await apiFetch('/api/aps/ctp/output').catch(()=>({items:[]}));
    state.ctpRequests = reqs.items || [];
    state.ctpOutput = outs.items || [];
    renderCTPHistory();
  }catch(e){ qs('ctpResult').textContent = 'CTP failed: ' + e.message; }
  finally{ btn.innerHTML = old; btn.disabled = false; }
}
async function runBom() {
  const activeId = document.activeElement?.id;
  const btn = activeId === 'bomExplodeBtn'
    ? qs('bomExplodeBtn')
    : activeId === 'bomRunBtn'
      ? qs('bomRunBtn')
      : null;
  const old = btn ? btn.innerHTML : '';

  if (btn) {
    btn.innerHTML = '<span class="spinner"></span> Exploding…';
    btn.disabled = true;
  }

  try {
    const d = await apiFetch('/api/run/bom', { method: 'POST' });

    state.bomGrouped = d.grouped_bom || [];
    state.bomSummary = d.summary || {};
    state.bomFlat = d.net_bom || [];
    state.bomGross = d.gross_bom || [];
    state.bomNet = d.net_bom || [];

    renderBomSummary();
    renderBomGrouped();
  } catch (e) {
    const detail = qs('bomDetailContent');
    if (detail) {
      detail.innerHTML = `<div class="bom-detail-empty">BOM Explosion failed: ${escapeHtml(e.message)}</div>`;
    }
  } finally {
    if (btn) {
      btn.innerHTML = old;
      btn.disabled = false;
    }
  }
}
function renderBomSummary(){
  if(!state.bomSummary) return;
  const s = state.bomSummary;
  setText('bomKpiLinesVal', String(s.total_sku_lines || 0));
  setText('bomKpiShortVal', String(s.short_lines || 0));
  setText('bomKpiPartialVal', String(s.partial_lines || 0));
  setText('bomKpiCoveredVal', String(s.covered_lines || 0));
  setText('bomKpiByproductVal', String(s.byproduct_lines || 0));
  setText('bomKpiGrossVal', ((s.total_gross_req || 0) / 1000).toFixed(1) + 'k');
  setText('bomKpiNetVal', ((s.total_net_req || 0) / 1000).toFixed(1) + 'k');
  setText('bomKpiLinesVal2', String(s.total_sku_lines || 0));
  setText('bomKpiCoveredVal2', String(s.covered_lines || 0));
  setText('bomKpiShortVal2', String(s.short_lines || 0));
  setText('bomKpiPartialVal2', String(s.partial_lines || 0));
  setText('bomKpiGrossVal2', ((s.total_gross_req || 0) / 1000).toFixed(1) + 'k');
  setText('bomKpiNetVal2', ((s.total_net_req || 0) / 1000).toFixed(1) + 'k');
  if (qs('bomSummaryStrip')) qs('bomSummaryStrip').style.display = 'flex';
  if (qs('bomSummaryStrip2')) qs('bomSummaryStrip2').style.display = 'flex';
}

function bomStatusBadgeClass(status){
  const m = {'COVERED': 'covered', 'PARTIAL SHORT': 'partial', 'SHORT': 'short', 'BYPRODUCT': 'byproduct'};
  return m[status] || 'short';
}

function renderBomGrouped(){
  if(!state.bomGrouped || !state.bomGrouped.length){
    qs('bomDetailContent').innerHTML = '<div class="bom-detail-empty">No BOM data. Run BOM Explosion first.</div>';
    return;
  }
  buildBomTree(state.bomGrouped);
}

function buildBomTree(data){
  const tree = qs('bomTree');
  tree.innerHTML = data.map(plant=>{
    const mtItems = plant.material_types || [];
    return '<div class="bom-tree-item">'+
      '<div class="bom-tree-node" onclick="toggleBomNode(this); selectBomStage(this,'+escapeHtml(JSON.stringify(plant).replace(/'/g,"&#39;"))+')" data-plant="'+escapeHtml(plant.plant)+'">'+
        '<div class="bom-tree-toggle collapsed"></div>'+
        '<div class="bom-tree-icon">🏭</div>'+
        '<span>'+escapeHtml(plant.plant)+'</span>'+
        '<span style="margin-left:auto;font-size:.7rem;color:var(--text-faint)">'+mtItems.length+'</span>'+
      '</div>'+
      '<div class="bom-tree-children">'+
        mtItems.map(mt=>'<div class="bom-tree-item">'+
          '<div class="bom-tree-node" onclick="selectBomMaterialType(this,'+escapeHtml(JSON.stringify({plant:plant.plant,materialType:mt}).replace(/'/g,"&#39;"))+')" data-plant="'+escapeHtml(plant.plant)+'" data-type="'+escapeHtml(mt.material_type)+'">'+
            '<div class="bom-tree-toggle leaf"></div>'+
            '<div class="bom-tree-icon">📊</div>'+
            '<span>'+escapeHtml(mt.material_type)+'</span>'+
            '<span style="margin-left:auto;font-size:.7rem;color:var(--text-faint)">'+mt.row_count+'</span>'+
          '</div>'+
        '</div>').join('')+
      '</div>'+
    '</div>';
  }).join('');

  if(data.length > 0 && data[0].material_types && data[0].material_types.length > 0){
    selectBomMaterialType(null, {plant:data[0].plant, materialType:data[0].material_types[0]});
  }
}

function toggleBomNode(el){
  const toggle = el.querySelector('.bom-tree-toggle');
  const children = el.nextElementSibling;
  if(toggle.classList.contains('collapsed')){
    toggle.classList.remove('collapsed');
    toggle.classList.add('expanded');
    children.classList.add('open');
  } else {
    toggle.classList.add('collapsed');
    toggle.classList.remove('expanded');
    children.classList.remove('open');
  }
}

function selectBomMaterialType(el, data){
  if(el) {
    document.querySelectorAll('.bom-tree-node').forEach(n=>n.classList.remove('selected'));
    el.classList.add('selected');
  }
  renderBomDetail(data.plant, data.materialType);
}

function selectBomStage(el, plantData){
  renderBomDetail(plantData.plant, null);
}

function renderBomDetail(plant, materialType){
  const plantData = state.bomGrouped.find(p=>p.plant === plant);
  if(!plantData){
    qs('bomDetailContent').innerHTML = '<div class="bom-detail-empty">No data selected.</div>';
    return;
  }

  const rows = materialType ? (materialType.rows || []) : [];
  const totalGross = materialType ? (materialType.gross_req || 0) : (plantData.gross_req || 0);
  const totalNet = materialType ? (materialType.net_req || 0) : (plantData.net_req || 0);
  const totalProduced = materialType ? (materialType.produced_qty || 0) : (plantData.produced_qty || 0);
  const covered = rows.filter(r=>r.status==='COVERED').length;
  const short = rows.filter(r=>r.status==='SHORT').length;
  const partial = rows.filter(r=>r.status==='PARTIAL SHORT').length;

  let html = '<div class="bom-detail-header">'+
    '<div class="bom-detail-title">'+escapeHtml(plant)+'</div>';
  if(materialType) html += '<div class="bom-detail-subtitle">'+escapeHtml(materialType.material_type)+'</div>';
  html += '</div>'+
    '<div class="bom-detail-stats">'+
      '<div class="bom-detail-stat"><div class="bom-detail-stat-label">Gross Req</div><div class="bom-detail-stat-value">'+totalGross.toFixed(1)+' MT</div></div>'+
      '<div class="bom-detail-stat"><div class="bom-detail-stat-label">Produced</div><div class="bom-detail-stat-value">'+totalProduced.toFixed(1)+' MT</div></div>'+
      '<div class="bom-detail-stat"><div class="bom-detail-stat-label">Net Req</div><div class="bom-detail-stat-value">'+totalNet.toFixed(1)+' MT</div></div>'+
    '</div>';

  if(!materialType) {
    html += '<div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:.75rem">'+
      plantData.material_types.map(mt=>'<div class="bom-section-title" style="grid-column:span 1;margin:0;cursor:pointer;background:var(--panel);border:1px solid var(--border);border-left:3px solid #7c3aed" onclick="selectBomMaterialType(this,'+escapeHtml(JSON.stringify({plant:plant,materialType:mt}).replace(/'/g,"&#39;"))+')" data-plant="'+escapeHtml(plant)+'" data-type="'+escapeHtml(mt.material_type)+'" style="grid-column:span 1">'+escapeHtml(mt.material_type)+'<br><span style="font-size:.65rem;font-weight:400;color:var(--text-faint)">'+mt.row_count+' items</span></div>').join('')+
    '</div>';
  } else {
    html += '<div class="bom-items-list">'+
      rows.map(r=>'<div class="bom-item">'+
        '<div class="bom-item-row"><div><div class="bom-item-label">SKU</div><div class="bom-item-value">'+escapeHtml(r.sku_id)+'</div></div><div><div class="bom-item-label">Parent</div><div class="bom-item-value">'+escapeHtml(r.parent_skus)+'</div></div></div>'+
        '<div class="bom-item-row"><div><div class="bom-item-label">Gross Req</div><div class="bom-item-value">'+r.gross_req.toFixed(1)+' MT</div></div><div><div class="bom-item-label">Available</div><div class="bom-item-value">'+r.available_before.toFixed(1)+' MT</div></div></div>'+
        '<div class="bom-item-row-last"><div><div class="bom-item-label">Net Req</div><div class="bom-item-value">'+r.net_req.toFixed(1)+' MT</div></div><div><span class="bom-item-badge '+bomStatusBadgeClass(r.status)+'">'+escapeHtml(r.status)+'</span></div><div><span class="bom-item-badge '+r.flow_type.toLowerCase()+'">'+escapeHtml(r.flow_type)+'</span></div></div>'+
      '</div>').join('')+
    '</div>';
  }

  qs('bomDetailContent').innerHTML = html;
}

function hookBomFilters(){}
async function applyScenarioByName(name){
  try{
    await apiFetch('/api/aps/scenarios/apply', {method:'POST', body: JSON.stringify({scenario: name})});
    alert('Scenario apply acknowledged for ' + name);
  }catch(e){ alert('Scenario apply failed: ' + e.message); }
}
async function createScenario(){
  const key = prompt('Scenario key / parameter');
  if(!key) return;
  const value = prompt('Scenario value', '') || '';
  const data = {Parameter: key, Value: value};
  try{
    await apiFetch('/api/aps/scenarios', {method:'POST', body: JSON.stringify({data})});
    await loadApplicationState();
  }catch(e){ alert('Create scenario failed: ' + e.message); }
}
async function editScenario(keyValue){
  const row = (state.scenarios || []).find(r=>String(Object.values(r)[0] || '') === String(keyValue));
  if(!row){ alert('Scenario row not found.'); return; }
  const key = Object.keys(row)[0];
  const value = prompt('Update value', row[key]) || row[key];
  try{
    await apiFetch('/api/aps/scenarios/' + encodeURIComponent(keyValue), {method:'PATCH', body: JSON.stringify({data:{[key]: value}})});
    await loadApplicationState();
  }catch(e){ alert('Update scenario failed: ' + e.message); }
}
async function deleteScenario(keyValue){
  if(!confirm('Delete scenario ' + keyValue + '?')) return;
  try{
    await apiFetch('/api/aps/scenarios/' + encodeURIComponent(keyValue), {method:'DELETE'});
    await loadApplicationState();
  }catch(e){ alert('Delete scenario failed: ' + e.message); }
}
function openMasterModal(mode){
  state.masterMode = mode;
  qs('masterModal').classList.add('show');
}
function closeMasterModal(){
  qs('masterModal').classList.remove('show');
  qs('masterFormGrid').innerHTML = '';
  qs('masterBulkBox').style.display = 'none';
  qs('masterSaveBtn').style.display = '';
}
function buildMasterForm(section, row={}){
  const rows = state.master[section] || [];
  const sample = Object.keys(row).length ? row : (rows[0] || {});
  const cols = Object.keys(sample);
  qs('masterFormGrid').innerHTML = cols.map(c=>'<div class="master-form-field"><label>'+escapeHtml(c)+'</label><input name="'+escapeHtml(c)+'" value="'+escapeHtml(row[c] ?? '')+'"></div>').join('') || '<div class="notice">No columns detected for this section.</div>';
}
function currentMasterSection(){ return qs('masterSection').value; }
function openMasterCreate(){
  const section = currentMasterSection();
  qs('masterModalTitle').textContent = 'Add ' + (MASTER_LABELS[section] || section) + ' row';
  qs('masterModalSub').textContent = 'Create a new workbook-backed row.';
  buildMasterForm(section, {});
  openMasterModal('create');
}
function openMasterEdit(){
  const section = currentMasterSection();
  if(!state.selectedMasterKey){ alert('Select a row first.'); return; }
  const keyField = MASTER_KEYS[section];
  const row = (state.master[section] || []).find(r=>String(r[keyField] ?? r[Object.keys(r)[0]] ?? '') === String(state.selectedMasterKey));
  if(!row){ alert('Selected row not found.'); return; }
  qs('masterModalTitle').textContent = 'Patch ' + (MASTER_LABELS[section] || section) + ' row';
  qs('masterModalSub').textContent = 'Editing key ' + state.selectedMasterKey;
  buildMasterForm(section, row);
  openMasterModal('edit');
}
function openMasterBulk(){
  const section = currentMasterSection();
  qs('masterModalTitle').textContent = 'Bulk replace ' + (MASTER_LABELS[section] || section);
  qs('masterModalSub').textContent = 'Paste a JSON array for the section.';
  qs('masterFormGrid').innerHTML = '';
  qs('masterBulkBox').style.display = 'block';
  qs('masterBulkText').value = JSON.stringify(state.master[section] || [], null, 2);
  openMasterModal('bulk');
}
async function submitMasterForm(ev){
  ev.preventDefault();
  const section = currentMasterSection();
  try{
    if(state.masterMode === 'bulk'){
      const items = JSON.parse(qs('masterBulkText').value || '[]');
      await apiFetch('/api/aps/masterdata/' + encodeURIComponent(section) + '/bulk-replace', {method:'PUT', body: JSON.stringify({items})});
    }else{
      const fd = new FormData(qs('masterForm'));
      const data = {};
      fd.forEach((v,k)=>data[k]=v);
      if(state.masterMode === 'create'){
        await apiFetch('/api/aps/masterdata/' + encodeURIComponent(section), {method:'POST', body: JSON.stringify({data})});
      }else{
        await apiFetch('/api/aps/masterdata/' + encodeURIComponent(section) + '/' + encodeURIComponent(state.selectedMasterKey), {method:'PATCH', body: JSON.stringify({data})});
      }
    }
    closeMasterModal();
    await loadApplicationState();
  }catch(e){ alert('Master data save failed: ' + e.message); }
}
async function deleteMasterRow(){
  const section = currentMasterSection();
  if(!state.selectedMasterKey){ alert('Select a row first.'); return; }
  if(!confirm('Delete ' + state.selectedMasterKey + ' from ' + section + '?')) return;
  try{
    await apiFetch('/api/aps/masterdata/' + encodeURIComponent(section) + '/' + encodeURIComponent(state.selectedMasterKey), {method:'DELETE'});
    state.selectedMasterKey = null;
    await loadApplicationState();
  }catch(e){ alert('Delete failed: ' + e.message); }
}
async function patchJob() {
  const jobId = prompt('Job_ID to patch');
  if (!jobId) return;

  const payloadText = prompt(
    'JSON patch',
    '{"Planned_Start":"2026-04-04T08:00:00","Planned_End":"2026-04-04T12:00:00"}'
  );
  if (!payloadText) return;

  try {
    await apiFetch(
      '/api/aps/schedule/jobs/' + encodeURIComponent(jobId) + '/reschedule',
      { method: 'PATCH', body: JSON.stringify({ data: JSON.parse(payloadText) }) }
    );

    await loadApplicationState();
    activatePage('execution');
    switchExecView('gantt');
  } catch (e) {
    alert('Job patch failed: ' + e.message);
  }
}

qs('runBtn').addEventListener('click', runSchedule);
qs('campaignRerunBtn').addEventListener('click', runSchedule);
qs('ctpRunBtn').addEventListener('click', runCtp);
qs('bomRunBtn').addEventListener('click', runBom);
qs('bomExplodeBtn')?.addEventListener('click', runBom);
hookBomFilters();
qs('newScenarioBtn').addEventListener('click', createScenario);
qs('masterSection').addEventListener('change', ()=>{ state.selectedMasterKey = null; renderMaster(); });
qs('masterRefreshBtn').addEventListener('click', loadApplicationState);
qs('masterAddBtn').addEventListener('click', openMasterCreate);
qs('masterEditBtn').addEventListener('click', openMasterEdit);
qs('masterDeleteBtn').addEventListener('click', deleteMasterRow);
qs('masterBulkBtn').addEventListener('click', openMasterBulk);
qs('masterModalCloseBtn').addEventListener('click', closeMasterModal);
qs('masterModalCancelBtn').addEventListener('click', closeMasterModal);
qs('masterForm').addEventListener('submit', submitMasterForm);

qs('patchJobBtn').addEventListener('click', patchJob);
qs('horizonSelect').addEventListener('change', (e)=>{
  state.horizon = Number(e.target.value);
  setText('summaryMtSub', state.horizon + '-day horizon');
  const d = new Date(Date.now() + state.horizon*86400000);
  qs('ctpDate').value = d.toISOString().slice(0,10);
});
document.querySelectorAll('[data-camp-filter]').forEach(btn=>btn.addEventListener('click', ()=>{
  document.querySelectorAll('[data-camp-filter]').forEach(b=>b.classList.remove('active'));
  btn.classList.add('active');
  state.campFilter = btn.dataset.campFilter;
  renderCampaigns();
}));

// ===== APS PLANNING WORKFLOW FUNCTIONS =====

async function loadConfig(){
  try {
    const data = await apiFetch('/api/aps/masterdata/config');
    const configData = data.config || {};

    // Map API response to state.config
    state.config = {
      horizon_days: num(configData.APS_PLANNING_HORIZON_DAYS, 7),
      heat_size_mt: num(configData.HEAT_SIZE_MT, 50),
      section_tolerance_mm: num(configData.APS_SECTION_TOLERANCE_MM, 0.6),
      max_lot_mt: num(configData.APS_MAX_LOT_MT, 300),
      max_heats_per_lot: num(configData.APS_MAX_HEATS_PER_LOT, 8),
      max_due_spread_days: num(configData.APS_MAX_DUE_SPREAD_DAYS, 3)
    };

    // Initialize simulation config with defaults from APS config
    state.simConfig = {
      horizon_days: state.config.horizon_days,
      heat_size_mt: state.config.heat_size_mt,
      priority_filter: '',
      sms_lines: 1,
      rm_lines: 1
    };

    // Update UI with loaded config defaults
    initializeSimulationPanel();

  } catch(e) {
    console.warn('Failed to load config, using defaults:', e);
    // State already has defaults
    initializeSimulationPanel();
  }
}

function initializeSimulationPanel(){
  // Set simulation config panel with defaults from loaded config
  const horizonSelect = qs('simHorizonDays');
  if(horizonSelect) {
    horizonSelect.value = state.simConfig?.horizon_days || state.config.horizon_days;
  }

  const heatSizeInput = qs('simHeatSizeMt');
  if(heatSizeInput) {
    heatSizeInput.value = state.simConfig?.heat_size_mt || state.config.heat_size_mt;
  }

  const smsLinesSelect = qs('simSmsLines');
  if (smsLinesSelect) {
    smsLinesSelect.value = String(state.simConfig?.sms_lines || 1);
  }

  const rmLinesSelect = qs('simRmLines');
  if (rmLinesSelect) {
    rmLinesSelect.value = String(state.simConfig?.rm_lines || 1);
  }

  const prioritySelect = qs('simPriorityFilter');
  if (prioritySelect) {
    prioritySelect.value = state.simConfig?.priority_filter || '';
  }

  const rollingModeSelect = qs('simRollingMode');
  if (rollingModeSelect) {
    rollingModeSelect.value = state.simConfig?.rolling_mode_filter || '';
  }

  // Show default values
  setText('configHorizonDefault', state.config.horizon_days);
  setText('configHeatDefault', state.config.heat_size_mt);

  // Also update the heat size in Stage 3
  const heatStageinput = qs('heatSizeMt');
  if(heatStageinput) heatStageinput.value = state.config.heat_size_mt;

  refreshSimulationGradeOptions();
}

function refreshSimulationGradeOptions(selectedGrade = null) {
  const gradeEl = qs('simGradeFilter');
  if (!gradeEl) return;

  const currentValue = selectedGrade ?? gradeEl.value ?? '';
  const grades = [...new Set(
    (state.planningOrders || [])
      .map(po => String(po.grade || po.grade_family || '').trim())
      .filter(Boolean)
  )].sort((a, b) => a.localeCompare(b));

  gradeEl.innerHTML = '<option value="">All grades</option>' +
    grades.map(g => `<option value="${escapeHtml(g)}">${escapeHtml(g)}</option>`).join('');
  gradeEl.value = grades.includes(currentValue) ? currentValue : '';
}

async function loadPlanningOrderPool(){
  try {
    setPipelineStageStatus('ps-pool', 'running', 'Loading order pool…');
    const data = await apiFetch('/api/aps/planning/orders/pool');
    state.poolOrders = data.orders || [];
    renderPlanningOrderPool();
    updateSOPoolSelectionCount();
    hydrateSummary(state.overview || {});
    const urgentCount = state.poolOrders.filter(o=>o.priority==='URGENT').length;
    setPipelineStageStatus('ps-pool', 'done',
      state.poolOrders.length + ' open SOs loaded',
      [{v: state.poolOrders.length, l:'orders'}, {v: urgentCount, l:'urgent'}]
    );
  } catch(e) {
    console.error('Failed to load order pool:', e);
    setPipelineStageStatus('ps-pool', 'error', 'Load failed');
    qs('poolBody').innerHTML = '<tr><td colspan="8" style="text-align:center;color:var(--text-soft)">Error: ' + e.message + '</td></tr>';
  }
}

// Update top KPI cards after planning operations
function updatePlanningKPIs() {
  const kpis = deriveAppKpis();

  // Use actual planning horizon from sim config, default to 14 days
  const horizon = state.simConfig?.horizon_days || 14;
  const throughput = kpis.totalMt > 0 ? (kpis.totalMt / horizon).toFixed(1) : '—';

  setText('kpiOrders', kpis.planningOrderCount > 0 ? kpis.planningOrderCount : '—');
  setText('kpiHeats', kpis.totalHeats > 0 ? kpis.totalHeats : '—');
  setText('kpiMT', kpis.totalMt > 0 ? kpis.totalMt.toFixed(0) : '—');
  setText('kpiThroughput', throughput);

  hydrateSummary(state.overview || {});
  renderDashboardAlerts();
  renderDashActivePOs();
}

function renderPlanningOrderPool() {
  const orders = state.poolOrders || [];
  let filtered = [...orders];

  const searchTerm = (qs('poolSearch')?.value || '').trim().toLowerCase();
  const gradeFilt = qs('poolGrade')?.value || '';
  const priorityFilt = qs('poolPriority')?.value || '';
  const orderTypeFilt = qs('poolOrderType')?.value || '';
  const rollingModeFilt = qs('poolRollingMode')?.value || '';

  if (searchTerm) {
    filtered = filtered.filter((so) =>
      String(so.so_id || '').toLowerCase().includes(searchTerm) ||
      String(so.grade || '').toLowerCase().includes(searchTerm) ||
      String(so.customer_id || '').toLowerCase().includes(searchTerm)
    );
  }

  if (gradeFilt) filtered = filtered.filter((so) => so.grade === gradeFilt);
  if (priorityFilt) filtered = filtered.filter((so) => so.priority === priorityFilt);
  if (orderTypeFilt) filtered = filtered.filter((so) => (so.order_type || 'MTO') === orderTypeFilt);
  if (rollingModeFilt) filtered = filtered.filter((so) => (so.rolling_mode || 'HOT') === rollingModeFilt);

  const poolBody = qs('poolBody');
  if (!poolBody) return;

  poolBody.innerHTML = filtered.map((so) => {
    const isHeld = so._held;
    const rm = state.soRollingOverrides[so.so_id] || so.rolling_mode || 'HOT';
    const rowStyle = isHeld ? 'opacity:.45;background:rgba(0,0,0,.03)' : '';
    return `<tr style="cursor:pointer;${rowStyle}" onclick="if(event.target.type !== 'checkbox' && !event.target.closest('button') && !event.target.closest('select')) checkMaterialForSO('${escapeHtml(so.so_id)}')">
      <td><input type="checkbox" class="pool-so-check" data-so="${escapeHtml(so.so_id)}" ${isHeld ? '' : 'checked'} ${isHeld ? 'disabled' : ''} style="width:1.2rem;height:1.2rem;cursor:pointer" onclick="event.stopPropagation()"></td>
      <td>${escapeHtml(so.so_id)}</td>
      <td style="font-size:.8rem;font-weight:500;min-width:8rem">${escapeHtml(so.sku_id || '—')}</td>
      <td>${escapeHtml(so.customer_id)}</td>
      <td>${escapeHtml(so.grade)}</td>
      <td>${so.section_mm ? `${so.section_mm}mm` : '—'}</td>
      <td>${num(so.qty_mt).toFixed(0)}</td>
      <td>${fmtDate(so.due_date)}</td>
      <td><span class="badge ${so.priority === 'URGENT' ? 'red' : so.priority === 'HIGH' ? 'amber' : 'blue'}" style="white-space:nowrap">${escapeHtml(so.priority)}</span></td>
      <td><span style="font-size:.8rem;padding:.2rem .4rem;background:rgba(107,114,207,.1);border-radius:.2rem">${escapeHtml(so.order_type || 'MTO')}</span></td>
      <td onclick="event.stopPropagation()">
        <select class="sel" style="font-size:.75rem;padding:.15rem .3rem;height:1.6rem;border-radius:.2rem;font-weight:600;color:${rm==='HOT'?'#dc2626':'#2563eb'};background:${rm==='HOT'?'rgba(239,68,68,.08)':'rgba(59,130,246,.08)'}" onchange="setSORollingMode('${escapeHtml(so.so_id)}',this.value);this.style.color=this.value==='HOT'?'#dc2626':'#2563eb';this.style.background=this.value==='HOT'?'rgba(239,68,68,.08)':'rgba(59,130,246,.08)'">
          <option value="HOT" ${rm==='HOT'?'selected':''}>HOT</option>
          <option value="COLD" ${rm==='COLD'?'selected':''}>COLD</option>
        </select>
      </td>
      <td>${isHeld ? '<span class="badge amber" style="white-space:nowrap">HELD</span>' : escapeHtml(so.status)}</td>
      <td style="display:flex;gap:.3rem;flex-wrap:nowrap">
        <button class="btn ghost" style="font-size:.72rem;padding:.2rem .5rem;height:1.7rem;white-space:nowrap" onclick="event.stopPropagation();checkMaterialForSO('${escapeHtml(so.so_id)}')">Mat</button>
        <button class="btn ${isHeld ? 'primary' : 'warn'}" style="font-size:.72rem;padding:.2rem .5rem;height:1.7rem;white-space:nowrap" onclick="event.stopPropagation();toggleHoldSO('${escapeHtml(so.so_id)}')">${isHeld ? 'Unhold' : 'Hold'}</button>
      </td>
    </tr>`;
  }).join('') || `
    <tr>
      <td colspan="13" style="text-align:center;color:var(--text-soft)">No orders match filters.</td>
    </tr>
  `;

  const gradeOpts = qs('poolGrade');
  if (gradeOpts && gradeOpts.dataset.populated !== 'true') {
    const grades = [...new Set(orders.map((so) => so.grade).filter(Boolean))].sort();
    gradeOpts.innerHTML =
      '<option value="">All grades</option>' +
      grades.map((g) => `<option value="${escapeHtml(g)}">${escapeHtml(g)}</option>`).join('');
    gradeOpts.dataset.populated = 'true';
  }
}
async function selectPlanningWindow() {
  const windowDays = Number(qs('planningWindowDays')?.value || 7);
  const btn = qs('planningProposeBtn');

  try {
    setPipelineStageStatus('ps-propose', 'running', 'Selecting window…');
    if (btn) {
      btn.textContent = 'Loading…';
      btn.disabled = true;
    }

    const data = await apiFetch('/api/aps/planning/window/select', {
      method: 'POST',
      body: JSON.stringify({ days: windowDays })
    });

    state.planningWindow = windowDays;
    state.windowSOs = data.candidates || [];

    setText('planningWindowLabel', `${windowDays} days`);
    setText('planningCandidates', data.candidate_count || state.windowSOs.length || 0);

    return data;
  } catch (e) {
    setPipelineStageStatus('ps-propose', 'error', 'Window selection failed');
    throw e;
  } finally {
    if (btn) {
      btn.textContent = 'Propose';
      btn.disabled = false;
    }
  }
}
async function proposePlanningOrders() {
  const btn = qs('planningProposeBtn');

  try {
    if (!state.windowSOs || !state.windowSOs.length) {
      await selectPlanningWindow();
    }

    setPipelineStageStatus('ps-propose', 'running', 'Proposing lots…');
    if (btn) {
      btn.textContent = 'Proposing…';
      btn.disabled = true;
    }

    const windowDays = state.planningWindow || Number(qs('planningWindowDays')?.value || 7);

    // Collect checked SO IDs
    const checkedSoIds = [...document.querySelectorAll('.pool-so-check:checked')].map(el => el.dataset.so);

    const data = await apiFetch('/api/aps/planning/orders/propose', {
      method: 'POST',
      body: JSON.stringify({ days: windowDays, so_ids: checkedSoIds.length > 0 ? checkedSoIds : undefined })
    });

    state.planningOrders = data.planning_orders || [];
    refreshSimulationGradeOptions();
    renderPlanningBoard();
    updatePOToolbarButtons();
    renderDashboard();
    updatePlanningKPIs();

    const totalMT = state.planningOrders.reduce((sum, po) => sum + num(po.total_qty_mt), 0);

    // Display correct candidate count based on user selection
    const candidateLabel = checkedSoIds.length > 0 ? `${checkedSoIds.length} selected` : `${(state.windowSOs || []).length} candidates`;

    setText('planningProposedCount', state.planningOrders.length);
    setText('planningTotalMT', totalMT.toFixed(0));

    setPipelineStageStatus(
      'ps-propose',
      'done',
      `${windowDays}-day window · ${candidateLabel}`,
      [
        { v: state.planningOrders.length, l: 'POs' },
        { v: `${totalMT.toFixed(0)}MT`, l: 'total' }
      ]
    );

    stageExpand('ps-propose');
  } catch (e) {
    setPipelineStageStatus('ps-propose', 'error', 'Proposal failed');
    throw e;
  } finally {
    if (btn) {
      btn.textContent = 'Propose';
      btn.disabled = false;
    }
  }
}
function renderPlanningBoard(){
  let pos = state.planningOrders || [];
  const poRollingModeFilt = qs('poRollingMode')?.value || '';
  if (poRollingModeFilt) pos = pos.filter(po => (po.rolling_mode || 'HOT') === poRollingModeFilt);

  const totalMT = pos.reduce((sum, po) => sum + num(po.total_qty_mt), 0);

  // Separate into RELEASED and PROPOSED/FROZEN
  const releasedPos = pos.filter(po => po.planner_status === 'RELEASED');
  const proposedPos = pos.filter(po => po.planner_status !== 'RELEASED');

  setText('planningProposedCount', proposedPos.length);
  setText('planningTotalMT', totalMT.toFixed(0));
  setText('planningNote', `${proposedPos.length} proposed, ${releasedPos.length} released`);

  const statusBadgeClass = (status) => {
    if (status === 'FROZEN') return 'amber';
    if (status === 'RELEASED') return 'green';
    return 'blue';
  };

  const buildPORow = (po) => {
    const soIds = po.selected_so_ids || [];
    const skuIds = [...new Set(soIds.map(soId => {
      const so = state.poolOrders?.find(o => o.so_id === soId);
      return so?.sku_id || null;
    }).filter(Boolean))].join(', ');

    const soRows = soIds.map(soId => {
      const so = state.poolOrders?.find(o => o.so_id === soId) || {};
      return `<tr>
        <td><input type="checkbox" class="po-so-split-check" data-po="${escapeHtml(po.po_id)}" data-so="${escapeHtml(soId)}" style="width:1.2rem;height:1.2rem;cursor:pointer" ${po.planner_status === 'RELEASED' ? 'disabled' : ''}></td>
        <td>${escapeHtml(soId)}</td>
        <td>${so.qty_mt ? num(so.qty_mt).toFixed(0) : '—'}</td>
        <td>${so.due_date ? fmtDate(so.due_date) : '—'}</td>
        <td>${so.priority || '—'}</td>
      </tr>`;
    }).join('');

    const isReleased = po.planner_status === 'RELEASED';
    const rowStyle = isReleased ? 'opacity:.6;background:rgba(107,114,207,.02)' : '';

    return `<tr class="po-row" id="po-row-${escapeHtml(po.po_id)}" onclick="togglePODetail('${escapeHtml(po.po_id)}')" style="${rowStyle}">
      <td onclick="event.stopPropagation()"><input type="checkbox" class="po-check" data-po="${escapeHtml(po.po_id)}" style="width:1.2rem;height:1.2rem;cursor:pointer" ${isReleased ? 'disabled' : ''}></td>
      <td><strong>${escapeHtml(po.po_id)}</strong>${isReleased ? ' ✓' : ''}</td>
      <td>${escapeHtml(soIds.join(', ') || '—')}</td>
      <td style="font-size:.75rem;color:var(--text-soft)">${escapeHtml(skuIds || '—')}</td>
      <td>${num(po.total_qty_mt).toFixed(0)}</td>
      <td>${escapeHtml(po.grade_family)}</td>
      <td>${po.due_window ? po.due_window[0] + ' to ' + po.due_window[1] : '—'}</td>
      <td>${po.heats_required || 0}</td>
      <td><span style="font-size:.8rem;padding:.2rem .4rem;background:${po.rolling_mode === 'HOT' ? 'rgba(239,68,68,.1);color:#dc2626' : 'rgba(59,130,246,.1);color:#2563eb'};border-radius:.2rem;white-space:nowrap;font-weight:600">${escapeHtml(po.rolling_mode || 'HOT')}</span></td>
      <td><span class="badge ${statusBadgeClass(po.planner_status)}">${escapeHtml(po.planner_status)}</span></td>
      <td onclick="event.stopPropagation()"><button class="btn ghost ps-action" style="margin-right:.2rem" onclick="freezePO('${escapeHtml(po.po_id)}')" ${isReleased ? 'disabled' : ''}>Freeze</button><button class="btn ghost ps-action" onclick="checkMaterialForPO('${escapeHtml(po.po_id)}')">Mat</button></td>
    </tr>
    <tr class="po-detail-row" id="po-detail-${escapeHtml(po.po_id)}" style="display:none">
      <td colspan="11">
        <div style="padding:.5rem 1rem;background:var(--panel-soft);border-top:1px solid var(--border-soft)">
          <table class="table" style="font-size:.75rem">
            <thead><tr>
              <th style="width:2rem"><input type="checkbox" class="po-so-split-all" data-po="${escapeHtml(po.po_id)}" style="width:1.2rem;height:1.2rem;cursor:pointer" ${isReleased ? 'disabled' : ''}></th>
              <th>SO ID</th><th>Qty MT</th><th>Due Date</th><th>Priority</th>
            </tr></thead>
            <tbody>
              ${soRows}
            </tbody>
          </table>
          ${!isReleased ? `<button class="btn ghost" style="margin-top:.5rem;font-size:.75rem" onclick="splitPO('${escapeHtml(po.po_id)}')">Move checked to new lot</button>` : '<div style="font-size:.75rem;color:var(--text-soft);padding:.5rem 0">This PO is released and cannot be modified.</div>'}
        </div>
      </td>
    </tr>`;
  };

  let html = '';

  // PROPOSED/FROZEN section
  if (proposedPos.length > 0) {
    html += proposedPos.map(buildPORow).join('');
  }

  // RELEASED section (greyed out)
  if (releasedPos.length > 0) {
    html += '<tr style="border-top:2px solid var(--border);"><td colspan="11" style="padding:.6rem;background:rgba(16,185,129,.05);font-weight:600;font-size:.8rem;color:#059669">RELEASED ORDERS</td></tr>';
    html += releasedPos.map(buildPORow).join('');
  }

  qs('planningBoard').innerHTML = html || '<tr><td colspan="11" style="text-align:center;color:var(--text-soft)">No planning orders proposed yet. Select a window first.</td></tr>';
}


async function deriveHeatBatches() {
  if (!state.planningOrders || !state.planningOrders.length) {
    setPipelineStageStatus('ps-heats', 'error', 'No planning orders available');
    throw new Error('Propose orders first (Stage 2).');
  }

  try {
    setPipelineStageStatus('ps-heats', 'running', 'Deriving heats…');
    setText('heatDeriveBtn', 'Deriving…');

    const heatSizeMt = Number(qs('heatSizeMt')?.value || 50);
    const data = await apiFetch('/api/aps/planning/heats/derive', {
      method: 'POST',
      body: JSON.stringify({
        planning_orders: state.planningOrders,
        heat_size_mt: heatSizeMt
      })
    });

    state.heatBatches = data.heats || [];
    state.heatBomMaterials = {};

    if (!state.heatBatches.length) {
      renderHeatBuilder();
      updatePlanningKPIs();
      setPipelineStageStatus('ps-heats', 'error', 'No heats derived');
      setText('heatDeriveBtn', 'Derive');
      return;
    }

    // Build FG demand from actual SO quantities, not from heat qty repeated per SO.
    const uniqueSkus = new Map(); // sku_id -> total so qty_mt

    (state.planningOrders || []).forEach(po => {
      (po.selected_so_ids || []).forEach(soId => {
        const so = (state.poolOrders || []).find(s => s.so_id === soId);
        if (!so?.sku_id) return;

        const skuId = String(so.sku_id).trim();
        const soQtyMt = Number(so.qty_mt || 0);
        if (!skuId || !Number.isFinite(soQtyMt) || soQtyMt <= 0) return;

        const current = uniqueSkus.get(skuId) || 0;
        uniqueSkus.set(skuId, current + soQtyMt);
      });
    });

    if (uniqueSkus.size > 0) {
      const items = Array.from(uniqueSkus.entries()).map(([sku_id, qty_mt]) => ({
        sku_id,
        qty_mt
      }));

      try {
        const resp = await fetch(API + '/api/aps/bom/for-skus', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ items })
        });

        if (!resp.ok) {
          throw new Error('BOM for-skus request failed with status ' + resp.status);
        }

        const bomData = await resp.json();
        state.heatBomMaterials = bomData.materials || {};
      } catch (e) {
        console.warn('Failed to load heat materials:', e);
        state.heatBomMaterials = {};
      }
    }

    renderHeatBuilder();
    updatePlanningKPIs();

    const totalMT = state.heatBatches.reduce((sum, h) => sum + num(h.qty_mt), 0);

    setPipelineStageStatus(
      'ps-heats',
      'done',
      state.heatBatches.length + ' heats · ' + totalMT.toFixed(0) + ' MT',
      [
        { v: state.heatBatches.length, l: 'heats' },
        { v: (state.heatBatches.length * 2) + 'h', l: 'est.' }
      ]
    );

    stageExpand('ps-heats');
    setText('heatDeriveBtn', 'Derive');
  } catch (e) {
    setPipelineStageStatus('ps-heats', 'error', 'Derive failed');
    setText('heatDeriveBtn', 'Derive');
    throw e;
  }
}

function renderHeatBuilder(){
  const heats = state.heatBatches || [];
  const totalMT = heats.reduce((sum, h) => sum + num(h.qty_mt), 0);
  const avgMT = heats.length > 0 ? totalMT / heats.length : 0;

  setText('heatTotal', heats.length);
  setText('heatMT', totalMT.toFixed(0));
  setText('heatAvg', avgMT.toFixed(1));
  setText('heatDuration', (heats.length * 2) + 'h');

  // Find last heat per PO for fill visualization
  const lastHeatByPO = {};
  heats.forEach(h => { lastHeatByPO[h.planning_order_id] = h.heat_id; });
  const heatSize = Number(qs('heatSizeMt')?.value || 50);

  qs('heatBody').innerHTML = heats.map(h => {
    // Get SKU IDs for this heat's PO
    const po = state.planningOrders?.find(p => p.po_id === h.planning_order_id);
    const skuIds = po ? [...new Set((po.selected_so_ids || []).map(soId => {
      const so = state.poolOrders?.find(o => o.so_id === soId);
      return so?.sku_id || null;
    }).filter(Boolean))].join(', ') : '—';

    const isLast = lastHeatByPO[h.planning_order_id] === h.heat_id;
    const fillPct = isLast ? Math.min(100, (num(h.qty_mt) / heatSize) * 100) : 100;
    const fillColor = fillPct < 70 ? 'var(--warning)' : 'var(--success)';
    const fillBar = isLast
      ? `<div style="display:inline-block;width:2.5rem;height:.35rem;background:var(--border);border-radius:.2rem;margin-left:.4rem;vertical-align:middle">
           <div style="width:${fillPct.toFixed(0)}%;height:100%;background:${fillColor};border-radius:.2rem"></div>
         </div>`
      : '';
    // Get materials for this heat's SKUs
    const materialsHtml = (() => {
      const materials = (state.heatBomMaterials || {})[skuIds.split(', ')[0]] || [];
      if (!materials || materials.length === 0) return '—';
      return materials
        .slice(0, 3)
        .map(m => `${m.label} ${num(m.qty_mt).toFixed(1)}t`)
        .join(' • ');
    })();

    return `<tr>
      <td><strong>${escapeHtml(h.heat_id)}</strong></td>
      <td>${escapeHtml(h.planning_order_id)}</td>
      <td style="font-size:.75rem;color:var(--text-soft)">${escapeHtml(skuIds)}</td>
      <td>${escapeHtml(h.grade)}</td>
      <td style="font-size:.75rem;color:var(--text-soft)">${materialsHtml}</td>
      <td>${num(h.qty_mt).toFixed(0)}${fillBar}</td>
      <td>${h.heat_number_seq || '—'}</td>
      <td>${escapeHtml(h.upstream_route || '—')}</td>
      <td>${h.expected_duration_hours || 0}h</td>
    </tr>`;
  }).join('') || '<tr><td colspan="9" style="text-align:center;color:var(--text-soft)">No heats derived yet.</td></tr>';
}

// SO Due Date Gantt - based on due dates (no schedule simulation needed)
function renderSODueGantt(soList){
  if(!soList || !soList.length) return '<div style="padding:2rem;text-align:center;color:var(--text-soft)">No SOs selected.</div>';

  let minTime = Infinity, maxTime = -Infinity;
  const bySO = {};
  const plantColor = {BF: '#3b82f6', SMS: '#f97316', RM: '#8b5cf6'};

  soList.forEach(so => {
    const soId = so.so_id;
    const dueDate = new Date(so.due_date);

    if(isNaN(dueDate)) return;

    if(!bySO[soId]) bySO[soId] = so;

    minTime = Math.min(minTime, dueDate.getTime());
    maxTime = Math.max(maxTime, dueDate.getTime());
  });

  if(minTime === Infinity || Object.keys(bySO).length === 0)
    return '<div style="padding:2rem;text-align:center;color:var(--text-soft)">No valid due dates.</div>';

  // Extend range 3 days before earliest and 7 days after latest
  minTime -= 3 * 86400000;
  maxTime += 7 * 86400000;

  const span = maxTime - minTime || 86400000;
  const soIds = Object.keys(bySO).sort();
  const dayWidth = 120;
  const totalDays = Math.ceil(span / 86400000);
  const ganttWidth = dayWidth * (totalDays + 1);

  const dateLabels = [];
  for(let i = 0; i <= totalDays; i++){
    const d = new Date(minTime + i * 86400000);
    dateLabels.push(d.toLocaleDateString('en-US', {month: 'short', day: 'numeric'}));
  }

  let html = `
    <div style="border:1px solid var(--border);border-radius:.3rem;overflow:hidden;background:var(--panel);font-size:.75rem">
      <!-- Header - FROZEN (sticky) -->
      <div style="display:flex;border-bottom:2px solid var(--border);position:sticky;top:0;background:var(--panel);z-index:11;box-shadow:0 2px 4px rgba(0,0,0,.08)">
        <div style="width:11rem;flex-shrink:0;padding:.5rem;font-weight:600;border-right:1px solid var(--border)">SO | SKU | Qty</div>
        <div style="display:flex;width:${ganttWidth}px;background:var(--panel)">
          ${dateLabels.map(d => `<div style="width:${dayWidth}px;padding:.3rem;text-align:center;font-size:.7rem;border-right:1px solid var(--border-soft);color:var(--text-soft);background:var(--panel)">${d}</div>`).join('')}
        </div>
      </div>

      <!-- SO rows by priority -->
      ${['URGENT', 'HIGH', 'NORMAL'].map(priority => {
        const sosInPriority = soIds.filter(soId => bySO[soId].priority === priority);
        if(sosInPriority.length === 0) return '';

        const priorityColor = priority === 'URGENT' ? '#ef4444' : priority === 'HIGH' ? '#f59e0b' : '#3b82f6';

        return `
          <div style="border-bottom:1px solid var(--border)">
            <div style="padding:.4rem;background:${priorityColor};color:#fff;font-weight:600;font-size:.8rem">${priority}</div>
            ${sosInPriority.map(soId => {
              const so = bySO[soId];
              const dueDate = new Date(so.due_date).getTime();
              const dueLeft = Math.max(0, (dueDate - minTime) / span * 100);
              const qty = num(so.qty_mt || 0).toFixed(0);
              const skuId = so.sku_id ? escapeHtml(so.sku_id.substring(0, 12)) : '—';

              return `
                <div style="display:flex;border-bottom:1px solid var(--border-soft);align-items:center">
                  <div style="width:11rem;flex-shrink:0;padding:.5rem;border-right:1px solid var(--border);min-height:2.2rem;display:flex;flex-direction:column;justify-content:center">
                    <div style="font-weight:600;font-size:.8rem">${soId}</div>
                    <div style="font-size:.7rem;color:var(--text-soft);margin-top:.15rem">${skuId}</div>
                    <div style="font-size:.7rem;color:var(--text-soft)">${qty}MT</div>
                  </div>
                  <div style="position:relative;width:${ganttWidth}px;height:2.4rem">
                    ${dateLabels.map((d,i) => `<div style="position:absolute;left:${i*dayWidth}px;width:${dayWidth}px;height:100%;border-right:1px solid var(--border-soft);opacity:.1"></div>`).join('')}
                    <div style="position:absolute;left:${dueLeft}%;width:2.5rem;top:.5rem;bottom:.5rem;background:${priorityColor};opacity:.85;border-radius:.2rem;border:2px solid ${priorityColor};display:flex;align-items:center;justify-content:center;font-size:.65rem;color:#fff;font-weight:700;white-space:nowrap;padding:0 .2rem" title="${so.due_date}">DUE</div>
                  </div>
                </div>
              `;
            }).join('')}
          </div>
        `;
      }).join('')}
    </div>
  `;

  return html;
}

// SO Gantt - clean, minimal: SO ID | Plant | Timeline bars
function renderSOGantt(scheduleRows){
  if(!scheduleRows || !scheduleRows.length) return '<div style="padding:2rem;text-align:center;color:var(--text-soft)">No schedule data.</div>';

  let minTime = Infinity, maxTime = -Infinity;
  const bySO = {};
  const plantColor = {BF: '#3b82f6', SMS: '#f97316', RM: '#8b5cf6'};

  scheduleRows.forEach(row => {
    const campaign = row.Campaign || '';
    const soMatch = campaign.match(/SO-\d+/);
    const so = soMatch ? soMatch[0] : null;
    if(!so) return; // Skip if no valid SO

    if(!bySO[so]) bySO[so] = [];
    bySO[so].push(row);

    const start = new Date(row.Planned_Start);
    const end = new Date(row.Planned_End);
    if(!isNaN(start)) minTime = Math.min(minTime, start.getTime());
    if(!isNaN(end)) maxTime = Math.max(maxTime, end.getTime());
  });

  if(minTime === Infinity || Object.keys(bySO).length === 0)
    return '<div style="padding:2rem;text-align:center;color:var(--text-soft)">No valid schedule data.</div>';

  const span = maxTime - minTime || 86400000;
  const soList = Object.keys(bySO).sort();
  const dayWidth = 120;
  const totalDays = Math.ceil(span / 86400000);
  const ganttWidth = dayWidth * (totalDays + 1);

  // Date headers - JUST DATES
  const dateLabels = [];
  for(let i = 0; i <= totalDays; i++){
    const d = new Date(minTime + i * 86400000);
    dateLabels.push(d.toLocaleDateString('en-US', {month: 'short', day: 'numeric'}));
  }

  let html = `
    <div style="border:1px solid var(--border);border-radius:.3rem;overflow:hidden;background:var(--panel);font-size:.75rem">
      <!-- Header -->
      <div style="display:flex;border-bottom:2px solid var(--border);position:sticky;top:0;background:var(--panel);z-index:10">
        <div style="width:8rem;flex-shrink:0;padding:.5rem;font-weight:600;border-right:1px solid var(--border)">SO</div>
        <div style="display:flex;width:${ganttWidth}px">
          ${dateLabels.map(d => `<div style="width:${dayWidth}px;padding:.3rem;text-align:center;font-size:.7rem;border-right:1px solid var(--border-soft);color:var(--text-soft)">${d}</div>`).join('')}
        </div>
      </div>

      <!-- SO rows by plant -->
      ${['BF', 'SMS', 'RM'].map(plant => {
        const sosInPlant = soList.filter(so => bySO[so].some(o => (o.Plant || 'SMS') === plant));
        if(sosInPlant.length === 0) return '';

        return `
          <div style="border-bottom:1px solid var(--border)">
            <div style="padding:.4rem;background:${plantColor[plant]};color:#fff;font-weight:600;font-size:.8rem">${plant}</div>
            ${sosInPlant.map(so => {
              const ops = bySO[so].filter(o => (o.Plant || 'SMS') === plant);
              return `
                <div style="display:flex;border-bottom:1px solid var(--border-soft)">
                  <div style="width:8rem;flex-shrink:0;padding:.5rem;border-right:1px solid var(--border);font-weight:600">${so}</div>
                  <div style="position:relative;width:${ganttWidth}px;height:1.8rem">
                    ${dateLabels.map((d,i) => `<div style="position:absolute;left:${i*dayWidth}px;width:${dayWidth}px;height:100%;border-right:1px solid var(--border-soft);opacity:.1"></div>`).join('')}
                    ${ops.map(op => {
                      const opStart = new Date(op.Planned_Start).getTime();
                      const opEnd = new Date(op.Planned_End).getTime();
                      const opLeft = Math.max(0, (opStart - minTime) / span * 100);
                      const opWidth = Math.max(1.5, (opEnd - opStart) / span * 100);
                      return `<div style="position:absolute;left:${opLeft}%;width:${opWidth}%;top:.3rem;bottom:.3rem;background:${plantColor[plant]};opacity:.85;border-radius:.2rem;border:1px solid ${plantColor[plant]};display:flex;align-items:center;justify-content:center;font-size:.6rem;color:#fff;font-weight:600" title="${(opEnd-opStart)/3600000|0}h"></div>`;
                    }).join('')}
                  </div>
                </div>
              `;
            }).join('')}
          </div>
        `;
      }).join('')}
    </div>
  `;

  return html;
}

// PO Gantt - show each PO as a row, with plant it's assigned to
function renderPOGantt(scheduleRows){
  if(!scheduleRows || !scheduleRows.length) return '<div style="padding:2rem;text-align:center;color:var(--text-soft)">No schedule data.</div>';

  let minTime = Infinity, maxTime = -Infinity;
  const byPO = {};
  const plantColor = {BF: '#3b82f6', SMS: '#f97316', RM: '#8b5cf6'};

  // Group by PO
  scheduleRows.forEach(row => {
    const campaign = row.Campaign || '';
    const poMatch = campaign.match(/PO-\d+/);
    const po = poMatch ? poMatch[0] : 'Unknown';

    if(!byPO[po]) byPO[po] = [];
    byPO[po].push(row);

    const start = new Date(row.Planned_Start);
    const end = new Date(row.Planned_End);
    if(!isNaN(start)) minTime = Math.min(minTime, start.getTime());
    if(!isNaN(end)) maxTime = Math.max(maxTime, end.getTime());
  });

  if(minTime === Infinity) return '<div style="padding:1rem;color:var(--text-soft)">No valid time data.</div>';

  const span = maxTime - minTime || 86400000;
  const poList = Object.keys(byPO).sort();
  const dayWidth = 100;
  const totalDays = span / 86400000;
  const ganttWidth = Math.max(900, dayWidth * (totalDays + 1));

  const dateLabels = [];
  for(let i = 0; i <= Math.ceil(totalDays); i++){
    const d = new Date(minTime + i * 86400000);
    dateLabels.push(fmtDate(d));
  }

  let html = `
    <div style="border:1px solid var(--border);border-radius:.3rem;overflow:hidden;background:var(--panel)">
      <!-- Header -->
      <div style="display:flex;border-bottom:2px solid var(--border);position:sticky;top:0;background:var(--panel)">
        <div style="width:7rem;flex-shrink:0;padding:.5rem;font-weight:600;border-right:1px solid var(--border);font-size:.75rem">PO ID | Plant</div>
        <div style="display:flex;width:${ganttWidth}px">
          ${dateLabels.map(d => `<div style="width:${dayWidth}px;padding:.3rem;text-align:center;font-size:.7rem;border-right:1px solid var(--border-soft);color:var(--text-soft)">${d}</div>`).join('')}
        </div>
      </div>

      <!-- PO rows -->
      ${poList.map(po => {
        const ops = byPO[po];
        const primaryPlant = ops[0]?.Plant || 'SMS';
        const color = plantColor[primaryPlant];
        const totalHours = ops.reduce((sum, o) => sum + (num(o.Duration_Hrs) || 0), 0);

        return `
          <div style="display:flex;border-bottom:1px solid var(--border-soft)">
            <div style="width:7rem;flex-shrink:0;padding:.6rem;font-weight:600;border-right:1px solid var(--border);font-size:.8rem">
              <div>${po}</div>
              <div style="font-size:.7rem;color:${color};margin-top:.2rem">${primaryPlant}</div>
            </div>
            <div style="position:relative;width:${ganttWidth}px;height:2.5rem">
              <!-- Grid -->
              ${dateLabels.map((d,i) => `<div style="position:absolute;left:${i*dayWidth}px;width:${dayWidth}px;height:100%;border-right:1px solid var(--border-soft);opacity:.15"></div>`).join('')}

              <!-- Bars -->
              ${ops.map(op => {
                const opStart = new Date(op.Planned_Start);
                const opEnd = new Date(op.Planned_End);
                const opLeft = Math.max(0, (opStart.getTime() - minTime) / span * 100);
                const opWidth = Math.max(1, (opEnd.getTime() - opStart.getTime()) / span * 100);
                const dur = num(op.Duration_Hrs || 0).toFixed(0);
                return `
                  <div style="position:absolute;left:${opLeft}%;width:${opWidth}%;top:.4rem;bottom:.4rem;background:${color};opacity:.8;border-radius:.2rem;border:1px solid ${color};display:flex;align-items:center;justify-content:center;font-size:.65rem;color:#fff;font-weight:600" title="${op.Resource_ID} | ${dur}h">${dur}h</div>
                `;
              }).join('')}
            </div>
          </div>
        `;
      }).join('')}
    </div>

    <div style="margin-top:1rem;display:flex;gap:1rem;font-size:.75rem">
      <div><span style="color:var(--text-soft)">Start:</span> <strong>${fmtDateTime(minTime).substring(0, 16)}</strong></div>
      <div><span style="color:var(--text-soft)">End:</span> <strong>${fmtDateTime(maxTime).substring(0, 16)}</strong></div>
      <div><span style="color:var(--text-soft)">Total POs:</span> <strong>${poList.length}</strong></div>
    </div>
  `;

  return html;
}

// Heat/Equipment Gantt - per equipment with plant grouping
function renderEquipmentGantt(scheduleRows){
  if(!scheduleRows || !scheduleRows.length) return '<div style="padding:2rem;text-align:center;color:var(--text-soft)">No schedule data.</div>';

  let minTime = Infinity, maxTime = -Infinity;
  const byEquip = {};
  const plantColor = {BF: '#3b82f6', SMS: '#f97316', RM: '#8b5cf6'};

  scheduleRows.forEach(row => {
    const equip = row.Resource_ID || 'Unknown';
    if(!byEquip[equip]) byEquip[equip] = [];
    byEquip[equip].push(row);

    const start = new Date(row.Planned_Start);
    const end = new Date(row.Planned_End);
    if(!isNaN(start)) minTime = Math.min(minTime, start.getTime());
    if(!isNaN(end)) maxTime = Math.max(maxTime, end.getTime());
  });

  if(minTime === Infinity) return '<div style="padding:1rem;color:var(--text-soft)">No valid time data.</div>';

  const span = maxTime - minTime || 86400000;
  const dayWidth = 100;
  const totalDays = span / 86400000;
  const ganttWidth = Math.max(900, dayWidth * (totalDays + 1));

  const dateLabels = [];
  for(let i = 0; i <= Math.ceil(totalDays); i++){
    const d = new Date(minTime + i * 86400000);
    dateLabels.push(fmtDate(d));
  }

  let html = `
    <div style="border:1px solid var(--border);border-radius:.3rem;overflow:hidden;background:var(--panel)">
      <!-- Header -->
      <div style="display:flex;border-bottom:2px solid var(--border);position:sticky;top:0;background:var(--panel)">
        <div style="width:9rem;flex-shrink:0;padding:.5rem;font-weight:600;border-right:1px solid var(--border);font-size:.75rem">Equipment | Plant</div>
        <div style="display:flex;width:${ganttWidth}px">
          ${dateLabels.map(d => `<div style="width:${dayWidth}px;padding:.3rem;text-align:center;font-size:.7rem;border-right:1px solid var(--border-soft);color:var(--text-soft)">${d}</div>`).join('')}
        </div>
      </div>

      <!-- Equipment rows grouped by plant -->
      ${(() => {
        let html = '';
        const plantOrder = ['BF', 'SMS', 'RM'];

        plantOrder.forEach(plant => {
          const equipInPlant = Object.keys(byEquip).filter(equip => {
            return byEquip[equip].some(o => (o.Plant || 'SMS') === plant);
          }).sort();

          if(equipInPlant.length === 0) return;

          html += `<div style="padding:.4rem;background:${plantColor[plant]};color:#fff;font-weight:600;font-size:.8rem;border-bottom:2px solid var(--border)">${plant} Plant</div>`;

          equipInPlant.forEach(equip => {
            const ops = byEquip[equip].filter(o => (o.Plant || 'SMS') === plant);
            const color = plantColor[plant];
            const utilization = (ops.reduce((sum, o) => sum + (num(o.Duration_Hrs) || 0), 0) / (totalDays * 24) * 100).toFixed(0);

            html += `
              <div style="display:flex;border-bottom:1px solid var(--border-soft)">
                <div style="width:9rem;flex-shrink:0;padding:.5rem;border-right:1px solid var(--border);font-size:.75rem">
                  <div style="font-weight:600">${equip}</div>
                  <div style="font-size:.7rem;color:var(--text-soft);margin-top:.2rem">${utilization}% util</div>
                </div>
                <div style="position:relative;width:${ganttWidth}px;height:2rem">
                  <!-- Grid -->
                  ${dateLabels.map((d,i) => `<div style="position:absolute;left:${i*dayWidth}px;width:${dayWidth}px;height:100%;border-right:1px solid var(--border-soft);opacity:.15"></div>`).join('')}

                  <!-- Bars -->
                  ${ops.map(op => {
                    const opStart = new Date(op.Planned_Start);
                    const opEnd = new Date(op.Planned_End);
                    const opLeft = Math.max(0, (opStart.getTime() - minTime) / span * 100);
                    const opWidth = Math.max(1, (opEnd.getTime() - opStart.getTime()) / span * 100);
                    const campaign = (op.Campaign || '').substring(0, 10);
                    return `
                      <div style="position:absolute;left:${opLeft}%;width:${opWidth}%;top:.3rem;bottom:.3rem;background:${color};opacity:.8;border-radius:.2rem;border:1px solid ${color};display:flex;align-items:center;justify-content:center;font-size:.65rem;color:#fff;font-weight:600;overflow:hidden" title="${campaign}">${campaign}</div>
                    `;
                  }).join('')}
                </div>
              </div>
            `;
          });
        });

        return html;
      })()}
    </div>

    <div style="margin-top:1rem;display:flex;gap:1rem;font-size:.75rem">
      <div><span style="color:var(--text-soft)">Start:</span> <strong>${fmtDateTime(minTime).substring(0, 16)}</strong></div>
      <div><span style="color:var(--text-soft)">End:</span> <strong>${fmtDateTime(maxTime).substring(0, 16)}</strong></div>
      <div><span style="color:var(--text-soft)">Total Equipment:</span> <strong>${Object.keys(byEquip).length}</strong></div>
    </div>
  `;

  return html;
}

// Show gantt for selected SOs in pool
function showPoolSOGantt(){
  // Show gantt based on SO due dates (no schedule simulation needed)
  const checkedSoIds = [...document.querySelectorAll('.pool-so-check:checked')].map(el => el.dataset.so);
  if (!checkedSoIds.length) {
    alert('Select at least one SO to view gantt');
    return;
  }

  const selectedSOs = (state.poolOrders || []).filter(so => checkedSoIds.includes(so.so_id));
  showGanttModal('Sales Orders - Due Date Timeline', selectedSOs, 'so-due');
}

// Show gantt modal - clean large panel (not cramped overlay)
function showGanttModal(title, data, type = 'so'){
  const modal = document.createElement('div');
  modal.style.cssText = 'position:fixed;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,.0);display:flex;align-items:center;justify-content:center;z-index:9999;padding:.5rem;backdrop-filter:none';

  let ganttContent = '';
  if(type === 'so') ganttContent = renderSOGantt(data);
  else if(type === 'so-due') ganttContent = renderSODueGantt(data);
  else if(type === 'po') ganttContent = renderPOGantt(data);
  else if(type === 'heat') ganttContent = renderEquipmentGantt(data);

  const content = document.createElement('div');
  content.style.cssText = 'background:var(--page-bg);border:1px solid var(--border);border-radius:.4rem;box-shadow:0 10px 40px rgba(0,0,0,.2);width:95%;height:92vh;overflow:auto;padding:1.25rem;display:flex;flex-direction:column';
  content.innerHTML = `
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:1rem;flex-shrink:0;border-bottom:1px solid var(--border);padding-bottom:.8rem">
      <h3 style="margin:0;font-size:1rem;font-weight:600">${escapeHtml(title)}</h3>
      <button style="background:none;border:none;font-size:1.5rem;cursor:pointer;color:var(--text-soft);flex-shrink:0;padding:0;width:2rem;height:2rem;display:flex;align-items:center;justify-content:center">✕</button>
    </div>
    <div style="flex:1;overflow:auto;min-height:0">
      ${ganttContent}
    </div>
  `;

  const closeBtn = content.querySelector('button');
  closeBtn.addEventListener('click', () => modal.remove());

  modal.appendChild(content);
  document.body.appendChild(modal);

  modal.addEventListener('click', (e) => {
    if(e.target === modal) modal.remove();
  });
}


async function simulateSchedule(config = {}){
  if(!state.heatBatches || !state.heatBatches.length){
    alert('Derive heats first (Stage 3).');
    return;
  }

  try {
    setPipelineStageStatus('ps-schedule', 'running', 'Simulating…');
    setText('schedulerSimulateBtn', 'Simulating…');
    const selectedGrade = qs('simGradeFilter')?.value || '';

    const simParams = {
      planning_orders: state.planningOrders,
      horizon_hours: Number(qs('simHorizonDays')?.value || 14) * 24,
      num_sms:        Number(qs('simSmsLines')?.value || 1),
      num_rm:         Number(qs('simRmLines')?.value || 1),
      priority_filter: qs('simPriorityFilter')?.value || '',
      rolling_mode_filter: qs('simRollingMode')?.value || '',
      grade_filter:   qs('simGradeFilter')?.value || '',
    };
    state.simConfig = {
      ...state.simConfig,
      horizon_days: Number(qs('simHorizonDays')?.value || state.simConfig?.horizon_days || 14),
      sms_lines: simParams.num_sms,
      rm_lines: simParams.num_rm,
      priority_filter: simParams.priority_filter,
      rolling_mode_filter: simParams.rolling_mode_filter
    };
    const horizonHours = simParams.horizon_hours || 24;
    const data = await apiFetch('/api/aps/planning/simulate', {
      method: 'POST',
      body: JSON.stringify(simParams)
    });
    state.lastSimResult = data;
    hydrateSummary(state.overview || {});
    renderDashboard();

    refreshSimulationGradeOptions(selectedGrade);
    const feasible = data.feasible;
    const duration = data.total_duration_hours || 0;
    const returnedHorizonHours = data.horizon_hours || horizonHours;
    const smsHours = data.sms_span_hours ?? data.sms_hours ?? 0;
    const rmHours = data.rm_span_hours ?? data.rm_hours ?? 0;
    const loadFactor = data.load_factor || '—';

    setText('scheduleFeasible', feasible ? 'Yes' : 'No');
    setText('scheduleDuration', duration + 'h');
    setText('scheduleSmsHours', smsHours + 'h');
    setText('scheduleRmHours', rmHours + 'h');
    setMetricTone('scheduleFeasibleCard', feasible ? 'success' : 'danger');
    setMetricTone('scheduleDurationCard', feasible ? 'success' : (data.horizon_exceeded ? 'danger' : 'warn'));
    setMetricTone('scheduleSmsCard', num(smsHours) > returnedHorizonHours ? 'warn' : 'info');
    setMetricTone('scheduleRmCard', num(rmHours) > returnedHorizonHours ? 'warn' : 'info');

    const statusColor = feasible ? '#10b981' : '#ef4444';
    const bottleneck = data.bottleneck || '—';

    // Store schedule rows for gantt modal
    const scheduleRows = data.schedule_rows || [];
    state.lastScheduleRows = scheduleRows;

    // Mark heats with their scheduling status (SCHEDULED vs NOT_SCHEDULED)
    const scheduledHeatIds = new Set(scheduleRows.map(r => r.Heat_ID).filter(Boolean));
    if (state.heatBatches) {
      state.heatBatches.forEach(heat => {
        heat.scheduling_status = scheduledHeatIds.has(heat.heat_id) ? 'SCHEDULED' : 'NOT_SCHEDULED';
      });
    }

    qs('schedulerContent').innerHTML = `
      <div style="display:flex;align-items:center;gap:2rem;padding:.5rem 0">
        <div style="font-size:1.8rem;font-weight:800;color:${statusColor}">${feasible ? '✓ FEASIBLE' : '✗ INFEASIBLE'}</div>
        <div style="display:flex;gap:2rem;font-size:.85rem">
          <div><div style="color:var(--text-soft);font-size:.7rem">Duration</div><div style="font-weight:700">${duration}h</div></div>
          <div><div style="color:var(--text-soft);font-size:.7rem">Horizon</div><div style="font-weight:700">${returnedHorizonHours}h</div></div>
          <div><div style="color:var(--text-soft);font-size:.7rem">Load</div><div style="font-weight:700">${loadFactor}</div></div>
          <div><div style="color:var(--text-soft);font-size:.7rem">SMS span</div><div style="font-weight:700">${smsHours}h</div></div>
          <div><div style="color:var(--text-soft);font-size:.7rem">RM span</div><div style="font-weight:700">${rmHours}h</div></div>
          <div><div style="color:var(--text-soft);font-size:.7rem">Bottleneck</div><div style="font-weight:700;color:${statusColor}">${escapeHtml(bottleneck)}</div></div>
        </div>
        <div style="font-size:.75rem;color:var(--text-soft);flex:1">${data.message}<br><span style="font-size:.68rem;color:var(--text-faint)">Workload: SMS ${num(data.sms_hours || 0).toFixed(1)}h · RM ${num(data.rm_hours || 0).toFixed(1)}h</span></div>
      </div>
    `;
    qs('schedulerContent').style.display = '';

    // Show remediation panel always (for what-if scenarios) - style changes based on feasibility
    const remPanel = qs('remediationPanel');
    if(remPanel){
      remPanel.style.display = '';
      // Change styling based on feasibility
      if(feasible){
        remPanel.style.background = '#f0fdf4';  // light green
        remPanel.style.borderColor = '#86efac';
        remPanel.style.color = '#166534';
        remPanel.querySelector('span').textContent = 'Feasible — explore what-if scenarios:';
        remPanel.style.backgroundColor = 'rgba(134, 239, 172, 0.1)';
      }

      // Calculate resource utilization
      if(!feasible && scheduleRows && scheduleRows.length){
        const byRes = {};
        scheduleRows.forEach(row => {
          const res = row.Resource_ID || 'UNKNOWN';
          if(!byRes[res]) byRes[res] = 0;
          byRes[res] += num(row.Duration_Hrs) || 0;
        });

        const resBreakdown = Object.entries(byRes)
          .map(([res, hours]) => ({
            res,
            hours,
            util: (hours / horizonHours) * 100,
            over: hours > horizonHours
          }))
          .sort((a,b) => b.util - a.util);

        // Show resource utilization alert if any over 100%
        const overCap = resBreakdown.filter(r => r.over);
        if(overCap.length > 0){
          const alertHtml = `
            <div style="margin-bottom:1rem;padding:.6rem;background:#fee2e2;border:1px solid #fecaca;border-radius:.3rem">
              <div style="font-weight:600;color:#991b1b;font-size:.8rem;margin-bottom:.4rem">⚠ Resource Overload Detected</div>
              <div style="font-size:.75rem;color:#7f1d1d">
                ${overCap.map(r => `
                  <div style="display:flex;justify-content:space-between;margin-bottom:.2rem">
                    <span><strong>${escapeHtml(r.res)}</strong></span>
                    <span>${r.hours.toFixed(0)}h / ${horizonHours}h (${r.util.toFixed(0)}%)</span>
                  </div>
                `).join('')}
              </div>
              <div style="font-size:.7rem;color:#991b1b;margin-top:.4rem">💡 Try: Add parallel resources OR narrow priority window to reduce load</div>
            </div>
          `;
          remPanel.style.display = '';
          remPanel.insertAdjacentHTML('afterbegin', alertHtml);
        }
      }

      if(!feasible && data.by_grade){
        const breakdown = Object.entries(data.by_grade).sort((a,b)=>b[1]-a[1]);
        qs('remGradeBreakdown').innerHTML = breakdown.map(([g,n])=>`
          <div style="display:flex;justify-content:space-between;padding:.15rem 0;border-bottom:1px solid var(--border-soft)">
            <span>${escapeHtml(g)}</span>
            <span style="font-weight:600">${n} heats · ${(n*2)}h est.</span>
          </div>`).join('');
      }
    }

    setPipelineStageStatus('ps-schedule', feasible ? 'done' : 'warn',
      data.message,
      [{v: duration+'h', l:'duration'}, {v: loadFactor, l:'load'}]
    );
    stageExpand('ps-schedule');

    // Enable release if feasible
    if(feasible) await loadReleaseBoard();

    setText('schedulerSimulateBtn', 'Simulate');
  } catch(e) {
    setPipelineStageStatus('ps-schedule', 'error', 'Simulation failed');
    setText('schedulerSimulateBtn', 'Simulate');
    throw e;
  }
}


async function loadReleaseBoard(){
  if(!state.planningOrders || !state.planningOrders.length){
    qs('releaseBoard').innerHTML = '<tr><td colspan="8" style="text-align:center;color:var(--text-soft)">Complete pipeline first.</td></tr>';
    setPipelineStageStatus('ps-release', 'pending', 'Complete stages 1-4 first');
    return;
  }

  // Only show POs that passed feasibility check (have heats in schedule)
  const feasibleHeatIds = new Set(state.lastScheduleRows?.map(r => r.Heat_ID) || []);
  const feasiblePos = (state.planningOrders || []).filter(po => {
    // A PO is feasible if at least one of its heats is in the schedule
    const poHeats = po.heats || [];
    return poHeats.length === 0 || poHeats.some(h => feasibleHeatIds.has(h.heat_id || h.id));
  });

  const totalMT = feasiblePos.reduce((sum, po) => sum + num(po.total_qty_mt), 0);
  const totalSOs = feasiblePos.reduce((sum, po) => sum + (po.selected_so_ids?.length || 0), 0);

  if(feasiblePos.length === 0){
    qs('releaseBoard').innerHTML = '<tr><td colspan="8" style="text-align:center;color:var(--text-faint)">No feasible POs yet. Run Feasibility Check first.</td></tr>';
    setText('releaseReady', '0');
    setText('releaseMT', '0');
    setText('releaseSOs', '0');
    return;
  }

  setText('releaseReady', feasiblePos.length);
  setText('releaseMT', totalMT.toFixed(0));
  setText('releaseSOs', totalSOs);
  setText('releaseStatus', 'Ready');

  qs('releaseBoard').innerHTML = feasiblePos.map(po => {
    const isReleased = po.planner_status === 'RELEASED';
    const status = isReleased ? '✓ RELEASED' : '⚪ FEASIBLE';
    const statusColor = isReleased ? '#16a34a' : '#0f172a';
    const rowStyle = isReleased ? 'opacity:0.6;background:rgba(16,163,74,.05)' : 'background:rgba(34,197,94,.02)';

    return `<tr style="${rowStyle}">
    <td style="text-align:center"><input type="checkbox" class="release-po-checkbox" data-po="${escapeHtml(po.po_id)}" ${isReleased ? 'disabled' : ''} style="width:1.2rem;height:1.2rem;cursor:pointer" /></td>
    <td><strong>${escapeHtml(po.po_id)}</strong><span style="margin-left:.4rem;font-size:.7rem;color:${statusColor};font-weight:600">${status}</span></td>
    <td style="font-size:.7rem">${escapeHtml(po.selected_so_ids?.slice(0,3).join(', ') || '—')}${(po.selected_so_ids?.length||0)>3?' +'+((po.selected_so_ids?.length||0)-3):''}</td>
    <td>${num(po.total_qty_mt).toFixed(0)}</td>
    <td>${escapeHtml(po.grade_family)}</td>
    <td>${po.heats_required || 0}</td>
    <td><span class="badge green">OK</span></td>
    <td><button class="btn ghost" style="padding:.2rem .5rem;font-size:.75rem;${isReleased ? 'opacity:0.4;cursor:not-allowed' : ''}" ${isReleased ? 'disabled' : ''} onclick="releaseSinglePO('${escapeHtml(po.po_id)}')">Release</button></td>
  </tr>`;
  }).join('');

  qs('releaseApproveBtn').disabled = false;
  setPipelineStageStatus('ps-release', 'done',
    feasiblePos.length + ' POs ready to release',
    [{v: feasiblePos.length, l:'POs'}, {v: totalSOs, l:'SOs'}, {v: totalMT.toFixed(0)+'MT', l:'total'}]
  );
  stageExpand('ps-release');
}

async function refreshPlanningOrdersAfterRelease() {
  const refreshed = await apiFetch('/api/aps/planning/orders').catch(() => ({ planning_orders: [] }));
  state.planningOrders = refreshed.planning_orders || [];
  refreshSimulationGradeOptions();

  updatePlanningKPIs();
  renderDashboard();
}

async function releaseSinglePO(poId){
  try {
    qs('chipPipelineText').textContent = '⏳ Releasing planning order ' + poId + '...';
    qs('chipPipeline').className = 'chip info';

    await apiFetch('/api/aps/planning/release', {
      method: 'POST',
      body: JSON.stringify({po_ids: [poId]})
    });

    await refreshPlanningOrdersAfterRelease();
    await loadReleaseBoard();
/*     renderDashboard(); */

    qs('chipPipelineText').textContent = '✓ Planning order ' + poId + ' released successfully';
    qs('chipPipeline').className = 'chip success';
  } catch(e) {
    qs('chipPipelineText').textContent = '✗ Release failed: ' + e.message;
    qs('chipPipeline').className = 'chip danger';
  }
}

async function releaseSelectedPOs(){
  const checked = Array.from(qs('releaseBoard').querySelectorAll('input[type="checkbox"]:checked')).map(el => el.dataset.po);
  if(!checked.length){
    qs('chipPipelineText').textContent = '⚠ Select at least one planning order to release';
    qs('chipPipeline').className = 'chip warn';
    return;
  }

  try {
    qs('chipPipelineText').textContent = '⏳ Releasing ' + checked.length + ' planning order' + (checked.length > 1 ? 's' : '') + '...';
    qs('chipPipeline').className = 'chip info';

    await apiFetch('/api/aps/planning/release', {
      method: 'POST',
      body: JSON.stringify({po_ids: checked})
    });

    await refreshPlanningOrdersAfterRelease();
    await loadReleaseBoard();
/*     renderDashboard(); */

    qs('chipPipelineText').textContent = '✓ Released ' + checked.length + ' planning order' + (checked.length > 1 ? 's' : '') + ' successfully';
    qs('chipPipeline').className = 'chip success';
  } catch(e) {
    qs('chipPipelineText').textContent = '✗ Release failed: ' + e.message;
    qs('chipPipeline').className = 'chip danger';
  }
}

async function releaseAllSelected(){
  const checked = Array.from(document.querySelectorAll('.release-po-checkbox:checked')).map(el => el.dataset.po);
  if(!checked.length){
    qs('chipPipelineText').textContent = '⚠ Select at least one planning order to release';
    qs('chipPipeline').className = 'chip warn';
    return;
  }

  if(!confirm(`Release ${checked.length} planning order${checked.length > 1 ? 's' : ''}?`)) return;

  try {
    qs('chipPipelineText').textContent = '⏳ Releasing ' + checked.length + ' order' + (checked.length > 1 ? 's' : '') + '...';
    qs('chipPipeline').className = 'chip info';

    await apiFetch('/api/aps/planning/release', {
      method: 'POST',
      body: JSON.stringify({po_ids: checked})
    });

    await refreshPlanningOrdersAfterRelease();
    await loadReleaseBoard();
/*     renderDashboard(); */

    qs('chipPipelineText').textContent = '✓ Released ' + checked.length + ' planning order' + (checked.length > 1 ? 's' : '') + ' successfully';
    qs('chipPipeline').className = 'chip success';
  } catch(e) {
    qs('chipPipelineText').textContent = '✗ Release failed: ' + e.message;
    qs('chipPipeline').className = 'chip danger';
  }
}

function releaseSelectAll(){
  qs('releaseSelectAllCheckbox').checked = true;
  document.querySelectorAll('.release-po-checkbox').forEach(cb => cb.checked = true);
}

function releaseClearAll(){
  qs('releaseSelectAllCheckbox').checked = false;
  document.querySelectorAll('.release-po-checkbox').forEach(cb => cb.checked = false);
}

function releaseToggleSelectAll(){
  const isChecked = qs('releaseSelectAllCheckbox').checked;
  document.querySelectorAll('.release-po-checkbox').forEach(cb => cb.checked = isChecked);
}


function setSORollingMode(soId, mode) {
  const cleanSoId = String(soId || '').trim();
  const cleanMode = String(mode || '').trim().toUpperCase() === 'COLD' ? 'COLD' : 'HOT';

  if (!cleanSoId) return;

  if (!state.soRollingOverrides) {
    state.soRollingOverrides = {};
  }

  state.soRollingOverrides[cleanSoId] = cleanMode;

  const so = (state.poolOrders || []).find(o => String(o.so_id || '').trim() === cleanSoId);
  if (so) {
    so.rolling_mode = cleanMode;
  }

  const affectedPOs = (state.planningOrders || []).filter(po =>
    (po.selected_so_ids || []).some(id => String(id || '').trim() === cleanSoId)
  );

  affectedPOs.forEach(po => {
    const poModes = (po.selected_so_ids || []).map(id => {
      const linkedSo = (state.poolOrders || []).find(o => String(o.so_id || '').trim() === String(id || '').trim());
      return String(linkedSo?.rolling_mode || state.soRollingOverrides[String(id || '').trim()] || 'HOT').toUpperCase();
    });

    po.rolling_mode = poModes.includes('COLD') ? 'COLD' : 'HOT';
  });

  renderPlanningOrderPool();
  renderPlanningBoard();
  updatePlanningKPIs();
}

function toggleHoldSO(soId) {
  const cleanSoId = String(soId || '').trim();
  if (!cleanSoId) return;

  const so = (state.poolOrders || []).find(o => String(o.so_id || '').trim() === cleanSoId);
  if (!so) {
    alert('Sales order not found.');
    return;
  }

  if (so._originalStatus == null) {
    so._originalStatus = so.status || 'OPEN';
  }

  const willHold = !so._held;
  so._held = willHold;
  so.status = willHold ? 'HELD' : so._originalStatus;

  if (willHold) {
    const cb = document.querySelector(`.pool-so-check[data-so="${CSS.escape(cleanSoId)}"]`);
    if (cb) cb.checked = false;
  }

  const affectedPOs = (state.planningOrders || []).filter(po =>
    (po.selected_so_ids || []).some(id => String(id || '').trim() === cleanSoId)
  );

  affectedPOs.forEach(po => {
    const linkedSOs = (po.selected_so_ids || []).map(id =>
      (state.poolOrders || []).find(o => String(o.so_id || '').trim() === String(id || '').trim())
    ).filter(Boolean);

    const activeSOs = linkedSOs.filter(x => !x._held);
    const allHeld = linkedSOs.length > 0 && activeSOs.length === 0;

    po.planner_status = allHeld ? 'HOLD' : (po.planner_status === 'HOLD' ? 'PROPOSED' : po.planner_status);
    po.total_qty_mt = activeSOs.reduce((sum, x) => sum + num(x.qty_mt || 0), 0);

    const activeModes = activeSOs.map(x =>
      String(x.rolling_mode || state.soRollingOverrides[String(x.so_id || '').trim()] || 'HOT').toUpperCase()
    );
    po.rolling_mode = activeModes.includes('COLD') ? 'COLD' : 'HOT';

    const heatSize = Number(qs('heatSizeMt')?.value || state.config?.heat_size_mt || 50);
    po.heats_required = po.total_qty_mt > 0 ? Math.ceil(po.total_qty_mt / Math.max(heatSize, 1)) : 0;
  });

  renderPlanningOrderPool();
  renderPlanningBoard();
  updatePlanningKPIs();
  updateSOPoolSelectionCount();
  updatePOToolbarButtons();
}


// Event listeners for planning workflow

qs('poolSearch')?.addEventListener('input', () => { renderPlanningOrderPool(); updateSOPoolSelectionCount(); });
qs('poolGrade')?.addEventListener('change', () => { renderPlanningOrderPool(); updateSOPoolSelectionCount(); });
qs('poolPriority')?.addEventListener('change', () => { renderPlanningOrderPool(); updateSOPoolSelectionCount(); });
qs('poolOrderType')?.addEventListener('change', () => { renderPlanningOrderPool(); updateSOPoolSelectionCount(); });
qs('poolRollingMode')?.addEventListener('change', () => { renderPlanningOrderPool(); updateSOPoolSelectionCount(); });
qs('poRollingMode')?.addEventListener('change', () => renderPlanningBoard());

// Pool checkbox handlers
qs('poolSelectAll')?.addEventListener('change', (e) => {
  const checked = e.target.checked;
  document.querySelectorAll('.pool-so-check').forEach(cb => cb.checked = checked);
  updateSOPoolSelectionCount();
});
qs('poolSelectAllBtn')?.addEventListener('click', () => {
  document.querySelectorAll('.pool-so-check').forEach(cb => cb.checked = true);
  qs('poolSelectAll').checked = true;
  updateSOPoolSelectionCount();
});
qs('poolClearAllBtn')?.addEventListener('click', () => {
  document.querySelectorAll('.pool-so-check').forEach(cb => cb.checked = false);
  qs('poolSelectAll').checked = false;
  updateSOPoolSelectionCount();
});
document.addEventListener('change', (e) => {
  if (e.target.classList.contains('pool-so-check')) {
    const allChecked = [...document.querySelectorAll('.pool-so-check')].every(c => c.checked);
    const someChecked = [...document.querySelectorAll('.pool-so-check')].some(c => c.checked);
    const selectAllCb = qs('poolSelectAll');
    if (selectAllCb) {
      selectAllCb.checked = allChecked;
      selectAllCb.indeterminate = someChecked && !allChecked;
    }
    updateSOPoolSelectionCount();
  }
});

// PO checkbox handlers and toolbar button state management
qs('poSelectAll')?.addEventListener('change', (e) => {
  const checked = e.target.checked;
  document.querySelectorAll('.po-check').forEach(cb => cb.checked = checked);
  updatePOToolbarButtons();
});
document.addEventListener('change', (e) => {
  if (e.target.classList.contains('po-check')) {
    const allChecked = [...document.querySelectorAll('.po-check')].every(c => c.checked);
    const someChecked = [...document.querySelectorAll('.po-check')].some(c => c.checked);
    const selectAllCb = qs('poSelectAll');
    if (selectAllCb) {
      selectAllCb.checked = allChecked;
      selectAllCb.indeterminate = someChecked && !allChecked;
    }
    updatePOToolbarButtons();
  }
  if (e.target.classList.contains('po-so-split-all')) {
    const poId = e.target.dataset.po;
    const checked = e.target.checked;
    document.querySelectorAll(`.po-so-split-check[data-po="${poId}"]`).forEach(cb => cb.checked = checked);
  }
});

function updatePOToolbarButtons() {
  const checkedCount = document.querySelectorAll('.po-check:checked').length;
  const totalCount = document.querySelectorAll('.po-check').length;
  const mergeBtn = qs('mergeSelectedBtn');
  const freezeBtn = qs('freezeSelectedBtn');
  const countBadge = qs('poSelectionCount');

  if (mergeBtn) mergeBtn.disabled = checkedCount < 3;
  if (freezeBtn) freezeBtn.disabled = checkedCount === 0;
  if (countBadge) countBadge.textContent = checkedCount === 0 ? '0 selected' : `${checkedCount} selected`;
}

function updateSOPoolSelectionCount() {
  const checkedCount = document.querySelectorAll('.pool-so-check:checked').length;
  const totalCount = document.querySelectorAll('.pool-so-check').length;
  const countBadge = qs('poolSelectionCount');
  if (countBadge) {
    countBadge.textContent = checkedCount === 0 ? '0 selected' : `${checkedCount} of ${totalCount} selected`;
  }
}

function togglePODetail(poId) {
  const detailRow = qs('po-detail-' + poId);
  if (detailRow) {
    detailRow.style.display = detailRow.style.display === 'none' ? 'table-row' : 'none';
  }
}

async function freezePO(poId) {
  try {
    const data = await apiFetch('/api/aps/planning/orders/update', {
      method: 'POST',
      body: JSON.stringify({action: 'freeze', po_id: poId})
    });
    if (data && data.planning_orders) {
      state.planningOrders = data.planning_orders;
      refreshSimulationGradeOptions();
      renderPlanningBoard();
      updatePOToolbarButtons();
    }
  } catch (e) {
    alert('Failed to freeze PO: ' + e.message);
  }
}

async function freezeSelectedPOs() {
  const poIds = [...document.querySelectorAll('.po-check:checked')].map(cb => cb.dataset.po);
  if (!poIds.length) return;
  try {
    for (const poId of poIds) {
      await freezePO(poId);
    }
  } catch (e) {
    alert('Failed to freeze POs: ' + e.message);
  }
}

async function splitPO(poId) {
  const checkedSos = [...document.querySelectorAll(`.po-so-split-check[data-po="${poId}"]:checked`)].map(cb => cb.dataset.so);
  if (!checkedSos.length) {
    alert('Select at least one SO to move.');
    return;
  }
  try {
    // Build split_map: keep unchecked in original PO, move checked to new PO
    const po = state.planningOrders?.find(p => p.po_id === poId);
    if (!po) return;

    const uncheckedSos = po.selected_so_ids.filter(so => !checkedSos.includes(so));
    const newPoId = poId + '-SPLIT';

    const splitMap = {};
    if (uncheckedSos.length > 0) {
      splitMap[poId] = uncheckedSos;
    }
    splitMap[newPoId] = checkedSos;

    const data = await apiFetch('/api/aps/planning/orders/update', {
      method: 'POST',
      body: JSON.stringify({action: 'split', source_po_id: poId, split_map: splitMap})
    });
    if (data && data.planning_orders) {
      state.planningOrders = data.planning_orders;
      refreshSimulationGradeOptions();
      renderPlanningBoard();
      updatePOToolbarButtons();
    }
  } catch (e) {
    alert('Failed to split PO: ' + e.message);
  }
}

async function mergeSelectedPOs() {
  const poIds = [...document.querySelectorAll('.po-check:checked')].map(cb => cb.dataset.po);
  if (poIds.length < 2) {
    alert('Select at least 2 POs to merge.');
    return;
  }
  try {
    // Merge all into the first PO
    const targetPoId = poIds[0];
    const sourcePoIds = poIds.slice(1);
    const data = await apiFetch('/api/aps/planning/orders/update', {
      method: 'POST',
      body: JSON.stringify({action: 'merge', source_po_ids: sourcePoIds, target_po_id: targetPoId})
    });
    if (data && data.planning_orders) {
      state.planningOrders = data.planning_orders;
      refreshSimulationGradeOptions();
      renderPlanningBoard();
      updatePOToolbarButtons();
    }
  } catch (e) {
    alert('Failed to merge POs: ' + e.message);
  }
}

// ===== MATERIAL CHECK FUNCTIONS =====
function checkMaterialForSO(soId) {
  const so = state.poolOrders?.find(o => String(o.so_id || o.SO_ID || '').trim() === String(soId || '').trim());
  const sku = String(so?.sku_id || so?.SKU_ID || '').trim();
  if (!so || !sku) {
    alert('SO not found or missing SKU');
    return;
  }

  const qty = num(so.qty_mt || so.Qty_MT || so.Order_Qty_MT || 0);
  const items = _planningMaterialItemsFromSkuQtyMap({ [sku]: qty });
  if (!items.length) {
    alert('SO has no positive quantity to explode.');
    return;
  }

  openMaterialPanel(`Material for SO-${soId} (${sku})`, items);
}

function checkMaterialForPO(poId) {
  const po = state.planningOrders?.find(p => p.po_id === poId);
  if (!po) {
    alert('PO not found');
    return;
  }

  const skuQtyMap = {};
  (po.selected_so_ids || []).forEach(soId => {
    const so = state.poolOrders?.find(o => String(o.so_id || o.SO_ID || '').trim() === String(soId || '').trim());
    const sku = String(so?.sku_id || so?.SKU_ID || '').trim();
    if (!sku) return;
    skuQtyMap[sku] = (skuQtyMap[sku] || 0) + num(so.qty_mt || so.Qty_MT || so.Order_Qty_MT || 0);
  });

  const items = _planningMaterialItemsFromSkuQtyMap(skuQtyMap);
  if (!items.length) {
    alert('No SKUs found for this PO');
    return;
  }

  const title = `Materials for PO-${poId} (${items.map(item => item.sku_id).join(', ')})`;
  openMaterialPanel(title, items);
}


function _planningMaterialItemsFromSkuQtyMap(skuQtyMap) {
  return Object.entries(skuQtyMap || {})
    .map(([skuId, qty]) => ({
      sku_id: String(skuId || '').trim(),
      qty_mt: Number(num(qty || 0).toFixed(3))
    }))
    .filter(item => item.sku_id && item.qty_mt > 0);
}

/* ============================================================================
   PLANNING MATERIAL PANEL
   Selection-scoped netted BOM tree with lazy child rendering.
   ============================================================================ */

function closeMaterialPanel() {
  const panel = qs('planningMaterialPanel');
  if (!panel) return;
  state.planningMaterialRequestId += 1;
  panel.style.display = 'none';
  qs('planningBomTree').innerHTML = '';
  qs('planningBomDetail').innerHTML = '';
  setText('materialCheckTitle', 'Material Requirements');
  state.planningMaterialView = null;
}

function showFullBOMForSelectedSOs() {
  const checkedSoIds = [...document.querySelectorAll('.pool-so-check:checked')]
    .map(el => String(el.dataset.so || '').trim())
    .filter(Boolean);

  if (!checkedSoIds.length) {
    alert('Select at least one sales order first.');
    return;
  }

  const selectedSOs = (state.poolOrders || []).filter(o =>
    checkedSoIds.includes(String(o.so_id || o.SO_ID || '').trim())
  );

  const skuQtyMap = {};
  selectedSOs.forEach(so => {
    const sku = String(so.sku_id || so.SKU_ID || '').trim();
    if (!sku) return;
    skuQtyMap[sku] = (skuQtyMap[sku] || 0) + num(so.qty_mt || so.Qty_MT || so.Order_Qty_MT || 0);
  });

  const items = _planningMaterialItemsFromSkuQtyMap(skuQtyMap);
  if (!items.length) {
    alert('No SKU IDs found for the selected sales orders.');
    return;
  }

  openMaterialPanel(`Materials for ${checkedSoIds.length} selected SO(s)`, items);
}



/* ---------- internal helpers ---------- */

function _planningMaterialNormalizeRow(row) {
  const flowType = String(row.Flow_Type || row.flow_type || 'INPUT').trim().toUpperCase();
  return {
    raw: row,
    root_sku: String(row.Root_SKU || row.root_sku || '').trim(),
    sku_id: String(row.SKU_ID || row.sku_id || '').trim(),
    parent_sku: String(row.Parent_SKU || row.parent_sku || '').trim(),
    required_qty: num(row.Gross_Req ?? row.Required_Qty ?? row.required_qty ?? 0),
    available_qty: num(row.Available ?? row.Available_Before ?? row.available_before ?? 0),
    net_req: num(row.Net_Req ?? row.net_req ?? 0),
    produced_qty: num(row.Produced_Qty ?? row.produced_qty ?? 0),
    bom_level: Math.max(0, Math.trunc(num(row.BOM_Level ?? row.bom_level ?? 0))),
    flow_type: flowType
  };
}

function _planningMaterialStatus(item) {
  const req = num(item.required_qty || 0);
  const prod = num(item.produced_qty || 0);
  const net = num(item.net_req || 0);
  const avail = num(item.available_qty || 0);

  if (item.flow_type === 'BYPRODUCT') return { label: 'BYPRODUCT', color: 'var(--text-faint)', tone: 'muted' };
  if (req <= 0) return { label: 'ZERO', color: 'var(--text-faint)', tone: 'muted' };
  if (net <= 1e-6) return { label: 'COVERED', color: 'var(--success)', tone: 'ok' };
  if (avail > 1e-6 || prod > 1e-6) return { label: 'PARTIAL', color: 'var(--warning)', tone: 'warn' };
  return { label: 'SHORT', color: 'var(--danger)', tone: 'bad' };
}

function _planningMaterialRootStatus(node) {
  const subtreeRows = _planningMaterialFlattenRows(node, true);
  if (!subtreeRows.length) return { label: 'ROOT', color: 'var(--text-faint)', tone: 'muted' };

  const totalReq = subtreeRows.reduce((sum, row) => sum + num(row.required_qty || 0), 0);
  const totalNet = subtreeRows.reduce((sum, row) => sum + num(row.net_req || 0), 0);
  if (totalNet <= 1e-6) return { label: 'COVERED', color: 'var(--success)', tone: 'ok' };
  if (totalNet < totalReq) return { label: 'PARTIAL', color: 'var(--warning)', tone: 'warn' };
  return { label: 'SHORT', color: 'var(--danger)', tone: 'bad' };
}

function _planningMaterialChildrenMap(rows) {
  const byParent = {};
  rows.forEach(row => {
    const parent = String(row.parent_sku || '').trim();
    const root = String(row.root_sku || '').trim();
    if (!parent || !root) return;
    const key = `${root}::${parent}`;
    if (!byParent[key]) byParent[key] = [];
    byParent[key].push(row);
  });

  Object.keys(byParent).forEach(parent => {
    byParent[parent].sort((a, b) => {
      const aLevel = num(a.bom_level, 999);
      const bLevel = num(b.bom_level, 999);
      if (aLevel !== bLevel) return aLevel - bLevel;
      return String(a.sku_id || '').localeCompare(String(b.sku_id || ''));
    });
  });

  return byParent;
}

function _planningMaterialBuildTreeNode(rootSku, parentSku, byParent, rootQtyMap, visitedPath = [], incomingRow = null) {
  const cleanSku = String(parentSku || '').trim() || 'UNKNOWN';
  const path = visitedPath.length ? [...visitedPath, cleanSku] : ['ROOT', cleanSku];
  const cycleDetected = visitedPath.includes(cleanSku);
  const parentKey = `${rootSku}::${cleanSku}`;
  const childRows = cycleDetected ? [] : (byParent[parentKey] || []);

  const node = {
    key: path.join('>'),
    root_sku: rootSku,
    sku_id: cleanSku,
    incoming_row: incomingRow,
    required_qty: incomingRow ? null : num(rootQtyMap[cleanSku] || 0),
    children: [],
    childrenLoaded: false,
    cycleDetected
  };

  node.children = childRows.map(row => _planningMaterialBuildTreeNode(
    rootSku,
    row.sku_id,
    byParent,
    rootQtyMap,
    [...visitedPath, cleanSku],
    row
  ));

  return node;
}

function _planningMaterialIndexTree(node, index = {}) {
  index[node.key] = node;
  (node.children || []).forEach(child => _planningMaterialIndexTree(child, index));
  return index;
}

function _planningMaterialFlattenRows(node, includeDescendants = false) {
  let rows = [];
  if (node.incoming_row) rows.push(node.incoming_row);

  if (includeDescendants) {
    (node.children || []).forEach(child => {
      rows = rows.concat(_planningMaterialFlattenRows(child, true));
    });
  }
  return rows;
}

function _planningMaterialNodeQty(node) {
  if (node.incoming_row) return num(node.incoming_row.required_qty || 0);
  return num(node.required_qty || 0);
}

function _planningMaterialCreateTreeElement(node, level = 0) {
  const hasChildren = Array.isArray(node.children) && node.children.length > 0;
  const status = node.incoming_row ? _planningMaterialStatus(node.incoming_row) : _planningMaterialRootStatus(node);
  const padLeft = 0.55 + (level * 0.9);

  const itemEl = document.createElement('div');
  itemEl.className = 'bom-tree-item';
  itemEl.dataset.nodeKey = node.key;

  const rowEl = document.createElement('div');
  rowEl.className = 'bom-tree-node';
  rowEl.dataset.nodeKey = node.key;
  rowEl.style.paddingLeft = `${padLeft}rem`;
  rowEl.addEventListener('click', () => selectPlanningMaterialNode(node.key));

  const toggleEl = document.createElement('div');
  toggleEl.className = `bom-tree-toggle ${hasChildren ? 'collapsed' : 'leaf'}`;
  if (hasChildren) {
    toggleEl.addEventListener('click', event => {
      event.stopPropagation();
      togglePlanningMaterialNode(toggleEl);
    });
  }

  const iconEl = document.createElement('div');
  iconEl.className = 'bom-tree-icon';
  iconEl.textContent = level === 0 ? '📦' : '⚙';

  const labelEl = document.createElement('span');
  labelEl.textContent = node.sku_id;

  const qtyEl = document.createElement('span');
  qtyEl.style.marginLeft = '.5rem';
  qtyEl.style.fontSize = '.65rem';
  qtyEl.style.color = 'var(--text-faint)';
  qtyEl.textContent = `${_planningMaterialNodeQty(node).toFixed(1)} MT`;

  const badgeEl = document.createElement('span');
  badgeEl.style.marginLeft = 'auto';
  badgeEl.style.fontSize = '.65rem';
  badgeEl.style.color = status.color;
  badgeEl.style.fontWeight = '700';
  badgeEl.textContent = status.label;

  rowEl.append(toggleEl, iconEl, labelEl, qtyEl, badgeEl);
  itemEl.appendChild(rowEl);

  if (hasChildren) {
    const childrenEl = document.createElement('div');
    childrenEl.className = 'bom-tree-children';
    childrenEl.dataset.level = String(level + 1);
    itemEl.appendChild(childrenEl);
  }

  return itemEl;
}

function _planningMaterialPopulateChildren(container, node, level) {
  if (!container || !node || node.childrenLoaded || !node.children.length) return;

  const frag = document.createDocumentFragment();
  node.children.forEach(child => {
    frag.appendChild(_planningMaterialCreateTreeElement(child, level));
  });
  container.replaceChildren(frag);
  node.childrenLoaded = true;
}

function renderPlanningMaterialTree(roots) {
  const treeEl = qs('planningBomTree');
  if (!treeEl) return;

  treeEl.innerHTML = '';
  if (!roots.length) {
    treeEl.innerHTML = '<div style="padding:.5rem;color:var(--text-soft);font-size:.75rem">No material tree available for this selection.</div>';
    return;
  }

  const frag = document.createDocumentFragment();
  roots.forEach(root => {
    frag.appendChild(_planningMaterialCreateTreeElement(root, 0));
  });
  treeEl.appendChild(frag);
}

function togglePlanningMaterialNode(toggleEl) {
  const itemEl = toggleEl.closest('.bom-tree-item');
  const view = state.planningMaterialView;
  if (!itemEl || !view?.nodeIndex) return;

  const nodeKey = itemEl.dataset.nodeKey;
  const node = view.nodeIndex[nodeKey];
  const children = Array.from(itemEl.children).find(child => child.classList?.contains('bom-tree-children'));
  if (!node || !children || !node.children.length) return;

  if (!node.childrenLoaded) {
    _planningMaterialPopulateChildren(children, node, num(children.dataset.level || 1));
  }

  const isOpen = children.classList.contains('open');
  children.classList.toggle('open', !isOpen);
  toggleEl.classList.toggle('collapsed', isOpen);
  toggleEl.classList.toggle('expanded', !isOpen);
}

function selectPlanningMaterialNode(nodeKey) {
  const view = state.planningMaterialView;
  if (!view || !view.nodeIndex || !view.nodeIndex[nodeKey]) return;

  view.selectedNodeKey = nodeKey;

  document.querySelectorAll('#planningBomTree .bom-tree-node').forEach(n => {
    n.classList.toggle('selected', n.dataset.nodeKey === nodeKey);
  });

  const node = view.nodeIndex[nodeKey];
  renderPlanningMaterialDetail(node);
}

function renderPlanningMaterialDetail(node) {
  const detailEl = qs('planningBomDetail');
  if (!detailEl || !node) return;

  const incoming = node.incoming_row || null;
  const childRows = (node.children || []).map(c => c.incoming_row).filter(Boolean);
  const subtreeRows = _planningMaterialFlattenRows(node, true);
  const totalChildReq = childRows.reduce((sum, row) => sum + num(row.required_qty || 0), 0);
  const totalSubtreeNet = subtreeRows.reduce((sum, row) => sum + num(row.net_req || 0), 0);

  let html = `
    <div class="bom-detail-header">
      <div class="bom-detail-title">${escapeHtml(node.sku_id)}</div>
      <div class="bom-detail-subtitle">${
        incoming
          ? `Required by ${escapeHtml(String(incoming.parent_sku || '—'))}`
          : 'Selected root material / finished good'
      }</div>
    </div>
  `;

  if (incoming) {
    const req = num(incoming.required_qty || 0);
    const prod = num(incoming.produced_qty || 0);
    const avail = num(incoming.available_qty || 0);
    const net = num(incoming.net_req || 0);
    const status = _planningMaterialStatus(incoming);

    html += `
      <div class="bom-detail-stats">
        <div class="bom-detail-stat">
          <div class="bom-detail-stat-label">Required</div>
          <div class="bom-detail-stat-value">${req.toFixed(1)} MT</div>
        </div>
        <div class="bom-detail-stat">
          <div class="bom-detail-stat-label">Available</div>
          <div class="bom-detail-stat-value">${avail.toFixed(1)} MT</div>
        </div>
        <div class="bom-detail-stat">
          <div class="bom-detail-stat-label">Net Req</div>
          <div class="bom-detail-stat-value">${net.toFixed(1)} MT</div>
        </div>
        <div class="bom-detail-stat">
          <div class="bom-detail-stat-label">Status</div>
          <div class="bom-detail-stat-value" style="color:${status.color}">${escapeHtml(status.label)}</div>
        </div>
      </div>
    `;
  } else {
    html += `
      <div class="bom-detail-stats">
        <div class="bom-detail-stat">
          <div class="bom-detail-stat-label">Selected demand</div>
          <div class="bom-detail-stat-value">${num(node.required_qty || 0).toFixed(1)} MT</div>
        </div>
        <div class="bom-detail-stat">
          <div class="bom-detail-stat-label">Immediate children</div>
          <div class="bom-detail-stat-value">${childRows.length}</div>
        </div>
        <div class="bom-detail-stat">
          <div class="bom-detail-stat-label">Immediate gross</div>
          <div class="bom-detail-stat-value">${totalChildReq.toFixed(1)} MT</div>
        </div>
        <div class="bom-detail-stat">
          <div class="bom-detail-stat-label">Subtree net</div>
          <div class="bom-detail-stat-value">${totalSubtreeNet.toFixed(1)} MT</div>
        </div>
      </div>
    `;
  }

  if (childRows.length) {
    html += `
      <div style="margin-top:1rem">
        <div class="bom-section-title">Immediate Child Materials</div>
        <div style="overflow:auto;border:1px solid var(--border);border-radius:.3rem">
          <table class="table">
            <thead>
              <tr>
                <th>SKU</th>
                <th>Required</th>
                <th>Available</th>
                <th>Produced</th>
                <th>Net Req</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              ${childRows.map(row => {
                const st = _planningMaterialStatus(row);
                return `
                  <tr>
                    <td><strong>${escapeHtml(String(row.sku_id || ''))}</strong></td>
                    <td>${num(row.required_qty || 0).toFixed(1)} MT</td>
                    <td>${num(row.available_qty || 0).toFixed(1)} MT</td>
                    <td>${num(row.produced_qty || 0).toFixed(1)} MT</td>
                    <td>${num(row.net_req || 0).toFixed(1)} MT</td>
                    <td><span style="color:${st.color};font-weight:700">${escapeHtml(st.label)}</span></td>
                  </tr>
                `;
              }).join('')}
            </tbody>
          </table>
        </div>
      </div>
    `;
  } else {
    html += `
      <div style="margin-top:1rem;padding:.75rem;border:1px solid var(--border);border-radius:.3rem;color:var(--text-soft);font-size:.8rem">
        No lower-level child materials under this node.
      </div>
    `;
  }

  detailEl.innerHTML = html;
}

async function openMaterialPanel(title, items) {
  const panel = qs('planningMaterialPanel');
  if (!panel) return;

  setText('materialCheckTitle', title || 'Material Requirements');
  panel.style.display = 'flex';

  const skuQtyMap = {};
  (items || []).forEach(item => {
    const sku = String(item?.sku_id || '').trim();
    const qty = num(item?.qty_mt || item?.required_qty || 0);
    if (!sku || qty <= 0) return;
    skuQtyMap[sku] = (skuQtyMap[sku] || 0) + qty;
  });

  const cleanItems = _planningMaterialItemsFromSkuQtyMap(skuQtyMap);
  if (!cleanItems.length) {
    qs('planningBomTree').innerHTML =
      '<div style="padding:.5rem;color:var(--text-soft);font-size:.75rem">No root SKUs selected.</div>';
    qs('planningBomDetail').innerHTML = '';
    return;
  }

  const requestId = ++state.planningMaterialRequestId;
  qs('planningBomTree').innerHTML =
    '<div style="padding:.5rem;color:var(--text-soft);font-size:.75rem">Loading selection BOM…</div>';
  qs('planningBomDetail').innerHTML = '';

  try {
    const payload = await apiFetch('/api/aps/bom/tree', {
      method: 'POST',
      body: JSON.stringify({ items: cleanItems })
    });
    if (requestId !== state.planningMaterialRequestId) return;

    const rootQtyMap = {};
    (payload.roots || cleanItems).forEach(item => {
      const sku = String(item?.sku_id || '').trim();
      const qty = num(item?.required_qty ?? item?.qty_mt ?? 0);
      if (!sku || qty <= 0) return;
      rootQtyMap[sku] = (rootQtyMap[sku] || 0) + qty;
    });

    const rows = (payload.net_bom || [])
      .map(_planningMaterialNormalizeRow)
      .filter(row => row.sku_id && row.required_qty > 1e-6 && row.flow_type !== 'BYPRODUCT');

    const byParent = _planningMaterialChildrenMap(rows);
    const roots = Object.keys(rootQtyMap).map(rootSku => _planningMaterialBuildTreeNode(rootSku, rootSku, byParent, rootQtyMap));
    const nodeIndex = {};
    roots.forEach(root => _planningMaterialIndexTree(root, nodeIndex));

    state.planningMaterialView = {
      title: title || 'Material Requirements',
      roots,
      nodeIndex,
      selectedNodeKey: roots[0]?.key || null,
      summary: payload.summary || {},
      structureErrors: payload.structure_errors || []
    };

    renderPlanningMaterialTree(roots);
    if (roots[0]?.key) {
      selectPlanningMaterialNode(roots[0].key);
      const firstToggle = qs('planningBomTree')?.querySelector('.bom-tree-toggle.collapsed');
      if (firstToggle) togglePlanningMaterialNode(firstToggle);
    }
  } catch (e) {
    if (requestId !== state.planningMaterialRequestId) return;
    state.planningMaterialView = null;
    qs('planningBomTree').innerHTML =
      `<div style="padding:.5rem;color:var(--danger);font-size:.75rem">Failed to load selection BOM: ${escapeHtml(e.message)}</div>`;
    qs('planningBomDetail').innerHTML = '';
  }
}

qs('pipelineRunBtn')?.addEventListener('click', runFullPipeline);

qs('pipelineExpandAllBtn')?.addEventListener('click', () => {
  const ids = ['ps-pool', 'ps-propose', 'ps-heats', 'ps-schedule', 'ps-release'];
  const anyCollapsed = ids.some((id) => qs(id + '-body')?.classList.contains('collapsed'));

  ids.forEach((id) => {
    if (anyCollapsed) stageExpand(id);
    else stageCollapse(id);
  });

  qs('pipelineExpandAllBtn').textContent = anyCollapsed ? 'Collapse All' : 'Expand All';
});

qs('heatDeriveBtn')?.addEventListener('click', deriveHeatBatches);
qs('schedulerSimulateBtn')?.addEventListener('click', simulateSchedule);
qs('releaseApproveBtn')?.addEventListener('click', releaseSelectedPOs);

document.addEventListener('DOMContentLoaded', async ()=>{
  qs('horizonSelect').value = state.horizon;
  const d = new Date(Date.now() + state.horizon*86400000);
  qs('ctpDate').value = d.toISOString().slice(0,10);

  // Show loading bar
  const bar = qs('loadingBar');
  bar.style.width = '10%';
  bar.style.opacity = '1';

  const healthPromise = checkHealth();
  const startupTasks = [
    loadConfig(),
    loadOrdersOnly(),
    loadPlanningOrderPool(),
    loadSkusForCtp(),
    loadApplicationState({ deferHeavy: true })
  ];

  await Promise.allSettled(startupTasks);
  bar.style.width = '85%';

  await loadReleaseBoard();
  bar.style.width = '100%';

  await healthPromise.catch(() => {});

  setTimeout(()=>{ bar.style.opacity = '0'; }, 300);
});
