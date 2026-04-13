const API = 'http://localhost:5000';
const MASTER_KEYS = {config:'Key',resources:'Resource_ID',routing:'SKU_ID',queue:'From_Operation',changeover:'From \\ To',skus:'SKU_ID',bom:'BOM_ID',inventory:'SKU_ID','campaign-config':'Grade',scenarios:'Parameter'};
const MASTER_LABELS = {config:'Config',resources:'Resource Master',routing:'Routing',queue:'Queue Times',changeover:'Changeover Matrix',skus:'SKU Master',bom:'BOM',inventory:'Inventory','campaign-config':'Campaign Config',scenarios:'Scenarios'};
const state = {
  orders:[], campaigns:[], capacity:[], scenario_metrics:[], scenarios:[], bomGross:[], bomNet:[],
  scenarioOutput:[], masterAudit:null,
  ctpLastResult:null,
  bomStructureErrors:[], bomFeasible:true,
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
  ganttZoom: {
    executionTimeline: 1,
    executionPlant: 1,
    modal: 1
  },
  materialMode: 'campaign',
  releaseReadyCount: 0,
  topSummaryCollapsed: false,
  executionDetailPinned: false,
  // Planner-set rolling mode overrides for individual SOs (so_id → 'HOT'|'COLD')
  soRollingOverrides: {}
};

function qs(id){ return document.getElementById(id); }
function parseDate(v){
  if(!v) return new Date(NaN);
  if(v instanceof Date) return new Date(v.getTime());
  if(typeof v === 'number') return new Date(v);
  const raw = String(v).trim();
  let d = new Date(raw);
  if(!Number.isNaN(d.getTime())) return d;
  d = new Date(raw.replace(' ', 'T'));
  return d;
}
function setText(id,v){ const el=qs(id); if(el) el.textContent = v; }
function chipToneClass(tone = 'neutral') {
  const semantic = {
    success: 'status-success',
    warn: 'status-warning',
    danger: 'status-danger',
    info: 'status-info',
    neutral: 'status-neutral'
  };
  const legacy = tone === 'neutral' ? '' : tone;
  return ['chip', legacy, 'status-token', semantic[tone] || semantic.neutral].filter(Boolean).join(' ');
}
function setChipTone(id, tone = 'neutral') {
  const el = qs(id);
  if (!el) return;
  el.className = chipToneClass(tone);
}
function escapeHtml(v){ return String(v ?? '').replace(/[&<>"']/g, m => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m])); }
function fmtDate(v){ if(!v) return '—'; const d=parseDate(v); if(Number.isNaN(d.getTime())) return String(v); return d.toLocaleDateString('en-GB',{day:'2-digit',month:'short'}).replace(' ','-'); }
function fmtDateTime(v){ if(!v) return '—'; const d=parseDate(v); if(Number.isNaN(d.getTime())) return String(v); const date = d.toLocaleDateString('en-GB',{day:'2-digit',month:'short'}); const time = d.toLocaleTimeString('en-GB',{hour:'2-digit',minute:'2-digit',hour12:false}); return `${date} ${time}`; }
function num(v, fallback=0){ const n = Number(v); return Number.isFinite(n) ? n : fallback; }
function upper(v){ return String(v || '').trim().toUpperCase(); }
function escapeAttr(v){ return escapeHtml(v).replace(/"/g, '&quot;'); }
function badgeForStatus(status){
  const s = String(status || '').toUpperCase();
  if (s.includes('HOLD')) return '<span class="badge status-token status-warning amber">'+escapeHtml(status || 'HOLD')+'</span>';
  if (s.includes('LATE') || s==='CRITICAL' || s==='BLOCKED') return '<span class="badge status-token status-danger red">'+escapeHtml(status || 'LATE')+'</span>';
  if (s.includes('RUN')) return '<span class="badge status-token status-info blue">'+escapeHtml(status || 'RUNNING')+'</span>';
  return '<span class="badge status-token status-success green">'+escapeHtml(status || 'RELEASED')+'</span>';
}

function fmtHours(value, digits = 1){
  const v = num(value, NaN);
  if (!Number.isFinite(v)) return '—';
  return `${v.toFixed(digits)}h`;
}

function ctpDecisionMeta(decisionClass){
  const key = upper(decisionClass);
  const map = {
    PROMISE_CONFIRMED_STOCK_ONLY: { label: 'Stock-only promise', tone: 'success', detail: 'Demand is covered from net finished stock.' },
    PROMISE_CONFIRMED_MERGED: { label: 'Merged campaign promise', tone: 'success', detail: 'Request fits into an existing committed campaign.' },
    PROMISE_CONFIRMED_NEW_CAMPAIGN: { label: 'New campaign promise', tone: 'success', detail: 'Request is feasible with a new production campaign.' },
    PROMISE_HEURISTIC_ONLY: { label: 'Heuristic promise', tone: 'warn', detail: 'Promise is based on degraded/heuristic schedule basis.' },
    PROMISE_LATER_DATE: { label: 'Later-date promise', tone: 'warn', detail: 'Full quantity is feasible but misses the requested date.' },
    PROMISE_SPLIT_REQUIRED: { label: 'Split required', tone: 'warn', detail: 'Only partial quantity is feasible by requested date.' },
    CANNOT_PROMISE_MATERIAL: { label: 'Material block', tone: 'danger', detail: 'Material shortages block full promise.' },
    CANNOT_PROMISE_CAPACITY: { label: 'Capacity block', tone: 'danger', detail: 'Finite schedule cannot complete within due window.' },
    CANNOT_PROMISE_POLICY_ONLY: { label: 'Policy-only block', tone: 'danger', detail: 'Policy constraints block the promise.' },
    CANNOT_PROMISE_INVENTORY_TRUST: { label: 'Inventory trust block', tone: 'danger', detail: 'Inventory lineage confidence is below policy threshold.' },
    CANNOT_PROMISE_MASTER_DATA: { label: 'Master-data block', tone: 'danger', detail: 'Routing/resource/master issues block a reliable promise.' },
    CANNOT_PROMISE_MIXED_BLOCKERS: { label: 'Mixed blockers', tone: 'danger', detail: 'Multiple blockers are active (material/capacity/policy).' }
  };
  return map[key] || {
    label: key ? key.replace(/_/g, ' ') : 'Decision unavailable',
    tone: '',
    detail: 'Decision class was not provided by the API.'
  };
}

function ctpLineageMeta(status){
  const key = upper(status);
  if (key === 'AUTHORITATIVE_SNAPSHOT_CHAIN' || key === 'NO_COMMITTED_CAMPAIGNS') {
    return { label: 'Authoritative', tone: 'success', detail: 'Inventory came from verified snapshots/current stock baseline.' };
  }
  if (key === 'RECOMPUTED_FROM_CONSUMPTION') {
    return { label: 'Recomputed', tone: 'warn', detail: 'Inventory was reconstructed from committed consumption.' };
  }
  if (key === 'CONSERVATIVE_BLEND') {
    return { label: 'Conservative Blend', tone: 'warn', detail: 'Inventory uses conservative minimum between recomputed and snapshot values.' };
  }
  return { label: key ? key.replace(/_/g, ' ') : 'Unknown', tone: '', detail: 'Inventory lineage status is unavailable.' };
}

function signedDelta(value, digits = 1, suffix = ''){
  if (!Number.isFinite(value)) return '—';
  const sign = value > 0 ? '+' : '';
  return `${sign}${value.toFixed(digits)}${suffix}`;
}

function toneStatusClasses(tone){
  if (tone === 'danger') return 'status-danger red';
  if (tone === 'warn') return 'status-warning amber';
  if (tone === 'success') return 'status-success green';
  return 'status-info blue';
}
function initGanttTooltips(container){
  if(!container) return;
  const targets = container.querySelectorAll('[data-gantt-tooltip]');
  if(!targets.length) return;

  let tooltip = container.querySelector('.gantt-tooltip');
  if(!tooltip){
    tooltip = document.createElement('div');
    tooltip.className = 'gantt-tooltip';
    tooltip.style.cssText = 'position:fixed;z-index:10001;max-width:22rem;padding:.55rem .7rem;background:rgba(15,23,42,.96);color:#fff;border-radius:.5rem;font-size:.74rem;line-height:1.35;box-shadow:0 10px 30px rgba(15,23,42,.22);pointer-events:none;opacity:0;transform:translateY(4px);transition:opacity .12s ease, transform .12s ease;white-space:normal';
    document.body.appendChild(tooltip);
  }

  const show = (event) => {
    tooltip.innerHTML = event.currentTarget.dataset.ganttTooltip || '';
    tooltip.style.opacity = '1';
    tooltip.style.transform = 'translateY(0)';
  };
  const move = (event) => {
    const pad = 14;
    const rect = tooltip.getBoundingClientRect();
    let left = event.clientX + pad;
    let top = event.clientY - 12;
    if(left + rect.width > window.innerWidth - 12) left = event.clientX - rect.width - pad;
    if(top + rect.height > window.innerHeight - 12) top = window.innerHeight - rect.height - 12;
    if(top < 12) top = 12;
    tooltip.style.left = left + 'px';
    tooltip.style.top = top + 'px';
  };
  const hide = () => {
    tooltip.style.opacity = '0';
    tooltip.style.transform = 'translateY(4px)';
  };

  targets.forEach(el => {
    el.addEventListener('mouseenter', show);
    el.addEventListener('mousemove', move);
    el.addEventListener('mouseleave', hide);
  });
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
function setTopActionContext(page) {
  const ctrlDefault = qs('ctrl-default');
  const ctrlPlanning = qs('ctrl-planning');
  const navbarActions = document.querySelector('.navbar-actions');
  const contextSlot = qs('navbarContextSlot');
  const actionPatternByPage = {
    dashboard: { mode: 'default' },
    planning: { mode: 'planning' },
    bom: { mode: 'context', contextClass: 'ctx-bom' },
    material: { mode: 'context', contextClass: 'ctx-material' },
    execution: { mode: 'context', contextClass: 'ctx-execution' },
    capacity: { mode: 'default' },
    ctp: { mode: 'context', contextClass: 'ctx-ctp' },
    scenarios: { mode: 'context', contextClass: 'ctx-scenarios' },
    master: { mode: 'context', contextClass: 'ctx-master' }
  };
  const pagePattern = actionPatternByPage[page] || actionPatternByPage.dashboard;
  const showDefaultActions = pagePattern.mode === 'default';
  const showPlanningActions = pagePattern.mode === 'planning';
  const activeContextClass = pagePattern.mode === 'context' ? pagePattern.contextClass : '';

  if (ctrlDefault) ctrlDefault.style.display = showDefaultActions ? '' : 'none';
  if (ctrlPlanning) ctrlPlanning.style.display = showPlanningActions ? '' : 'none';

  if (!contextSlot) return;
  contextSlot.querySelectorAll('.tab-context-actions').forEach((el) => {
    el.classList.remove('is-active');
  });

  if (activeContextClass) {
    const activeContext = contextSlot.querySelector(`.${activeContextClass}`);
    if (activeContext) activeContext.classList.add('is-active');
  }

  if (navbarActions) {
    navbarActions.classList.toggle('has-context', Boolean(activeContextClass));
  }
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
    const active = el.dataset.page === page;
    el.classList.toggle('active', active);
    el.setAttribute('aria-selected', active ? 'true' : 'false');
    el.tabIndex = active ? 0 : -1;
  });

  setTopActionContext(page);
  syncMaterialModeControl();
  renderTopSummaryForPage(page);

  if (page === 'material') initSplitResizer('materialDivider');
  if (page === 'bom') initSplitResizer('bomDivider');
  if (page === 'capacity') renderCapacity();
  syncExecutionDetailPanel();
}

function switchExecView(view){
  document.querySelectorAll('.exec-view').forEach(el=>el.classList.remove('active-view'));
  qs('exec-view-'+view).classList.add('active-view');
  document.querySelectorAll('[data-exec-view]').forEach(b=>{
    const active = b.dataset.execView===view;
    b.classList.toggle('active', active);
    b.setAttribute('aria-selected', active ? 'true' : 'false');
    b.tabIndex = active ? 0 : -1;
  });
  if (view === 'timeline') {
    renderDispatch();
  } else if (view === 'gantt') {
    renderSchedule();
  } else if (view === 'campaigns') {
    renderCampaigns();
    refreshExecutionTopbarMeta(view);
  } else {
    refreshExecutionTopbarMeta(view);
  }
  syncExecutionBarSelectionVisuals();
  syncExecutionDetailPanel();
}

function clampZoomValue(value){
  return Math.max(0.6, Math.min(2.6, Number(value) || 1));
}

function getZoomValue(key, fallback = 1){
  if(!state.ganttZoom) state.ganttZoom = { executionTimeline: 1, executionPlant: 1, modal: 1 };
  state.ganttZoom[key] = clampZoomValue(state.ganttZoom[key] ?? fallback);
  return state.ganttZoom[key];
}

function setZoomLabel(id, value){
  const el = qs(id);
  if(el) el.textContent = `${Math.round(clampZoomValue(value) * 100)}%`;
}

function getTimelineScalePreset(zoom){
  const z = clampZoomValue(zoom);
  if (z <= 0.8) return { bucketHours: 24, bucketPx: 78, scaleLabel: '1 day buckets' };
  if (z <= 1.2) return { bucketHours: 12, bucketPx: 92, scaleLabel: '12 hr buckets' };
  if (z <= 1.8) return { bucketHours: 6, bucketPx: 108, scaleLabel: '6 hr buckets' };
  return { bucketHours: 3, bucketPx: 124, scaleLabel: '3 hr buckets' };
}

function floorTimeToBucket(timestamp, bucketHours){
  const d = parseDate(timestamp);
  if (Number.isNaN(d.getTime())) return Date.now();
  d.setMinutes(0, 0, 0);
  d.setHours(Math.floor(d.getHours() / bucketHours) * bucketHours);
  return d.getTime();
}

function ceilTimeToBucket(timestamp, bucketHours){
  const bucketMs = bucketHours * 3600000;
  const floor = floorTimeToBucket(timestamp, bucketHours);
  if (timestamp <= floor) return floor + bucketMs;
  return floor + Math.ceil((timestamp - floor) / bucketMs) * bucketMs;
}

function formatTimelineBucketLabel(timestamp, bucketHours){
  const d = parseDate(timestamp);
  if (Number.isNaN(d.getTime())) return { primary: '—', secondary: '' };
  const primaryDate = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  if (bucketHours >= 24) {
    return {
      primary: primaryDate,
      secondary: d.toLocaleDateString('en-US', { weekday: 'short' })
    };
  }
  return {
    primary: primaryDate,
    secondary: d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false })
  };
}

function buildTimelineScale(starts, ends, zoom, fallbackDays = 14, minWidth = 960){
  const startTimes = (starts || []).map(v => parseDate(v).getTime()).filter(Number.isFinite);
  const endTimes = (ends || []).map(v => parseDate(v).getTime()).filter(Number.isFinite);
  const preset = getTimelineScalePreset(zoom);
  const bucketMs = preset.bucketHours * 3600000;
  const minTime = startTimes.length ? Math.min(...startTimes) : Date.now();
  const fallbackEnd = minTime + fallbackDays * 86400000;
  const maxTime = endTimes.length ? Math.max(...endTimes) : fallbackEnd;
  const startMs = floorTimeToBucket(minTime, preset.bucketHours);
  let endMs = ceilTimeToBucket(maxTime, preset.bucketHours);
  if (endMs <= startMs) endMs = startMs + bucketMs;
  const bucketCount = Math.max(1, Math.ceil((endMs - startMs) / bucketMs));
  const contentWidth = Math.max(minWidth, bucketCount * preset.bucketPx);
  const buckets = Array.from({ length: bucketCount }, (_, index) => {
    const ts = startMs + index * bucketMs;
    return {
      index,
      ts,
      left: index * preset.bucketPx,
      label: formatTimelineBucketLabel(ts, preset.bucketHours)
    };
  });
  return {
    ...preset,
    bucketMs,
    startMs,
    endMs,
    spanMs: endMs - startMs,
    bucketCount,
    contentWidth,
    buckets
  };
}

function timelineBarMetrics(start, end, scale, minWidth = 6){
  const s = parseDate(start).getTime();
  const e = parseDate(end).getTime();
  if (!Number.isFinite(s) || !Number.isFinite(e)) return null;
  const left = Math.max(0, ((s - scale.startMs) / scale.bucketMs) * scale.bucketPx);
  const width = Math.max(minWidth, ((Math.max(e, s + 1) - s) / scale.bucketMs) * scale.bucketPx);
  return {
    left,
    width: Math.min(width, Math.max(minWidth, scale.contentWidth - left))
  };
}

function setScaleLabel(id, text){
  const el = qs(id);
  if (el) el.textContent = text;
}

function syncExecutionZoomButtonState(target){
  const key = target === 'plant' ? 'executionPlant' : 'executionTimeline';
  const zoom = getZoomValue(key, 1);
  const minReached = zoom <= 0.6001;
  const maxReached = zoom >= 2.5999;
  document.querySelectorAll(`[data-exec-zoom-target="${target}"]`).forEach((btn) => {
    const direction = btn.dataset.execZoomDir;
    const disabled = direction === 'out' ? minReached : maxReached;
    btn.disabled = disabled;
    btn.classList.toggle('is-disabled', disabled);
    btn.setAttribute('aria-disabled', disabled ? 'true' : 'false');
  });
}

function refreshExecutionZoomLabels(){
  const timelineScale = getTimelineScalePreset(getZoomValue('executionTimeline', 1));
  const plantScale = getTimelineScalePreset(getZoomValue('executionPlant', 1));
  setZoomLabel('timelineZoomLabel', getZoomValue('executionTimeline', 1));
  setZoomLabel('plantZoomLabel', getZoomValue('executionPlant', 1));
  setScaleLabel('timelineScaleLabel', timelineScale.scaleLabel);
  setScaleLabel('plantScaleLabel', plantScale.scaleLabel);
  syncExecutionZoomButtonState('timeline');
  syncExecutionZoomButtonState('plant');
}

function syncExecutionDetailPanel(){
  const panel = qs('campaignDetailsPanel');
  const toggle = qs('execDetailToggle');
  const onExecutionPage = qs('page-execution')?.classList.contains('active');
  const show = onExecutionPage && Boolean(state.executionDetailPinned);
  if(panel) panel.classList.toggle('open', show);
  if(toggle) {
    toggle.textContent = show ? 'Hide Detail' : 'Show Detail';
    toggle.classList.toggle('is-active', show);
  }
}

function toggleExecutionDetailPanel(){
  state.executionDetailPinned = !state.executionDetailPinned;
  const content = qs('campaignDetailsContent');
  if(state.executionDetailPinned && !state.selectedCampaign && content){
    content.innerHTML = executionDetailEmptyMarkup();
  }
  syncExecutionBarSelectionVisuals();
  syncExecutionDetailPanel();
}

function closeExecutionDetailPanel(){
  state.executionDetailPinned = false;
  syncExecutionBarSelectionVisuals();
  syncExecutionDetailPanel();
}

function currentExecutionView(){
  return (document.querySelector('.exec-view.active-view')?.id || '').replace('exec-view-', '') || 'timeline';
}

function stepExecutionZoom(target, delta){
  if(target === 'timeline'){
    state.ganttZoom.executionTimeline = clampZoomValue(getZoomValue('executionTimeline', 1) + delta);
    refreshExecutionZoomLabels();
    renderDispatch();
    return;
  }
  if(target === 'plant'){
    state.ganttZoom.executionPlant = clampZoomValue(getZoomValue('executionPlant', 1) + delta);
    refreshExecutionZoomLabels();
    renderSchedule();
  }
}

const EXEC_OPERATION_FALLBACK_ORDER = ['BF', 'SMS', 'EAF', 'LRF', 'VD', 'CCM', 'RM'];

function normalizeOperationName(rawOp) {
  const op = upper(rawOp || '');
  if (!op) return '';
  if (op.includes('BLAST') || op.startsWith('BF')) return 'BF';
  if (op.includes('EAF') || op.startsWith('SMS_EAF')) return 'EAF';
  if (op.includes('LRF')) return 'LRF';
  if (op === 'VD' || op.includes('VACUUM')) return 'VD';
  if (op.includes('CCM') || op.includes('CAST')) return 'CCM';
  if (op === 'SMS' || op.startsWith('SMS')) return 'SMS';
  if (op.startsWith('RM') || op.includes('ROLLING')) return 'RM';
  return op.split(/[\s_-]+/)[0] || op;
}

function _ridToOp(rid){
  return normalizeOperationName(rid) || '—';
}

function parseOrderedConfigList(value) {
  if (Array.isArray(value)) {
    return value.map(v => String(v || '').trim()).filter(Boolean);
  }
  const text = String(value || '').trim();
  if (!text) return [];
  return text.split(/[>,|;/,\n]+/).map(v => v.trim()).filter(Boolean);
}

function executionOperationOrder() {
  const configuredOrder = parseOrderedConfigList(
    state.config?.operation_order ||
    state.config?.operation_sequence ||
    state.config?.routing_sequence ||
    state.config?.OPERATION_ORDER ||
    state.config?.OPERATION_SEQUENCE ||
    state.config?.ROUTING_SEQUENCE
  ).map(normalizeOperationName).filter(Boolean);

  const routingRows = Array.isArray(state.master?.routing) ? state.master.routing : [];
  const routingOrder = routingRows
    .map((row) => ({
      seq: num(row?.Sequence ?? row?.Op_Seq ?? row?.sequence ?? row?.op_seq, NaN),
      op: normalizeOperationName(row?.Operation ?? row?.operation ?? row?.Operation_Group ?? row?.operation_group)
    }))
    .filter(entry => entry.op && Number.isFinite(entry.seq))
    .sort((a, b) => a.seq - b.seq)
    .map(entry => entry.op);

  const merged = [];
  const pushUnique = (op) => {
    const normalized = normalizeOperationName(op);
    if (!normalized || merged.includes(normalized)) return;
    merged.push(normalized);
  };
  configuredOrder.forEach(pushUnique);
  routingOrder.forEach(pushUnique);
  EXEC_OPERATION_FALLBACK_ORDER.forEach(pushUnique);
  return merged;
}

function executionOperationRank(op) {
  const orderedOps = executionOperationOrder();
  const normalized = normalizeOperationName(op);
  const idx = orderedOps.indexOf(normalized);
  return idx === -1 ? 999 : idx;
}

function configuredPlantOrderFromConfig() {
  return parseOrderedConfigList(
    state.config?.plant_order ||
    state.config?.plant_sequence ||
    state.config?.PLANT_ORDER ||
    state.config?.PLANT_SEQUENCE
  ).map(v => upper(v));
}

function inferPlantFromResource(resourceId){
  const rid = String(resourceId || '').trim().toUpperCase();
  if (!rid) return 'SHARED';
  if (rid.startsWith('RM')) return 'RM';
  if (rid.startsWith('BF')) return 'BF';
  if (rid.startsWith('EAF') || rid.startsWith('LRF') || rid.startsWith('VD') || rid.startsWith('CCM') || rid.startsWith('SMS')) return 'SMS';
  return 'SHARED';
}

function plantForJob(job){
  const raw = String(job?.Plant || job?.plant || '').trim();
  if (raw && raw.toUpperCase() !== 'SHARED') return raw.toUpperCase();
  return inferPlantFromResource(job?.Resource_ID || job?.resource_id || '');
}
function renderCapacityBars(){
  const rows = [...(state.capacity || [])].sort((a,b)=>num(b['Utilisation_%'] || b.utilisation) - num(a['Utilisation_%'] || a.utilisation));
  const barsEl = qs('capacityBars');
  if(!barsEl) return;
  if(!rows.length){
    barsEl.innerHTML = '<div class="empty-state">No capacity rows loaded yet. Next: run Feasibility Check or Run Schedule.</div>';
    return;
  }

  barsEl.innerHTML = rows.map(r=>{
    const util = Math.round(num(r['Utilisation_%'] || r.utilisation || 0));
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
}

// ---- Pipeline stage accordion ----
function toggleStage(id){
  const body = qs(id + '-body');
  const chevron = qs(id + '-chevron');
  const isCollapsed = body.classList.contains('collapsed');
  body.classList.toggle('collapsed', !isCollapsed);
  chevron.classList.toggle('collapsed', !isCollapsed);
}

function setPipelineStageStatus(id, status, meta, kpis, options = {}){
  // status: pending | running | done | warn | error | blocked
  const badge = qs(id + '-badge');
  const metaEl = qs(id + '-meta');
  const kpisEl = qs(id + '-kpis');
  const stage = qs(id);
  const labels = {pending:'PENDING',running:'RUNNING…',done:'DONE',warn:'CHECK',error:'ERROR',blocked:'BLOCKED'};
  const semantic = {
    pending: 'status-neutral',
    running: 'status-info',
    done: 'status-success',
    warn: 'status-warning',
    error: 'status-danger',
    blocked: 'status-neutral'
  };
  if (badge) {
    badge.className = ['ps-badge', 'status-token', status, semantic[status] || 'status-neutral'].join(' ');
    badge.textContent = labels[status] || status.toUpperCase();
  }
  if (stage) {
    stage.className = ['panel', 'pipeline-stage', status === 'pending' ? '' : status].filter(Boolean).join(' ');
    stage.dataset.stageStatus = status;
  }
  if(meta && metaEl) metaEl.textContent = meta;
  if(kpis && kpisEl){
    kpisEl.innerHTML = kpis.map(k=>`<span><strong>${k.v}</strong> ${k.l}</span>`).join('');
  }
  // if (!options.skipGuideRefresh) refreshPlanningWorkflowGuide();
}

function stageExpand(id){ const b=qs(id+'-body'),c=qs(id+'-chevron'); b.classList.remove('collapsed'); c.classList.remove('collapsed'); }
function stageCollapse(id){ const b=qs(id+'-body'),c=qs(id+'-chevron'); b.classList.add('collapsed'); c.classList.add('collapsed'); }

function planningWorkflowSnapshot() {
  const sim = latestSimResult();
  const simFeasible = Boolean(sim && sim.feasible);
  const poolCount = (state.poolOrders || []).length;
  const poCount = (state.planningOrders || []).length;
  const heatCount = (state.heatBatches || []).length;
  const releasedCount = (state.planningOrders || []).filter(po => upper(po.planner_status) === 'RELEASED').length;
  const releaseReady = Math.max(0, num(state.releaseReadyCount || 0));
  const hasSim = Boolean(sim);

  const stageStatus = {
    pool: poolCount > 0 ? 'done' : 'pending',
    propose: poCount > 0 ? 'done' : (poolCount > 0 ? 'pending' : 'blocked'),
    heats: heatCount > 0 ? 'done' : (poCount > 0 ? 'pending' : 'blocked'),
    simulate: hasSim ? (simFeasible ? 'done' : 'warn') : (heatCount > 0 ? 'pending' : 'blocked'),
    release: releasedCount > 0
      ? 'done'
      : (!hasSim ? (heatCount > 0 ? 'pending' : 'blocked') : (!simFeasible ? 'blocked' : (releaseReady > 0 ? 'pending' : 'warn')))
  };

  let next = 'Load open sales orders in Stage 1 to begin the pipeline.';
  if (poolCount > 0) next = 'Select SOs and run Propose Orders in Stage 2.';
  if (poCount > 0) next = 'Run Derive in Stage 3 to generate heat batches.';
  if (heatCount > 0) next = 'Run Feasibility Check in Stage 4 before release.';
  if (hasSim && !simFeasible) next = 'Simulation is not feasible. Adjust horizon, resources, or filters and run Stage 4 again.';
  if (hasSim && simFeasible && releaseReady <= 0 && releasedCount <= 0) next = 'No release-ready POs found yet. Review simulation details and planning-order mix.';
  if (hasSim && simFeasible && releaseReady > 0 && releasedCount <= 0) next = 'Release feasible planning orders in Stage 5.';
  if (releasedCount > 0) next = 'Pipeline complete. Continue dispatch from Execution.';

  return {
    poolCount,
    poCount,
    heatCount,
    releasedCount,
    releaseReady,
    hasSim,
    simFeasible,
    stageStatus,
    canPropose: poolCount > 0,
    canDerive: poCount > 0,
    canSimulate: heatCount > 0,
    canRelease: hasSim && simFeasible && releaseReady > 0,
    next
  };
}

function syncPlanningActionAvailability(snapshot = planningWorkflowSnapshot()) {
  const setButtonState = (btnId, canRun, stageId, blockedTitle, readyTitle) => {
    const btn = qs(btnId);
    if (!btn) return;
    const stageRunning = qs(stageId)?.classList.contains('running');
    const disabled = stageRunning || !canRun;
    btn.disabled = disabled;
    btn.classList.toggle('is-ready', !disabled);
    btn.title = disabled ? blockedTitle : readyTitle;
    btn.setAttribute('aria-disabled', disabled ? 'true' : 'false');
  };

  setButtonState(
    'planningProposeBtn',
    snapshot.canPropose,
    'ps-propose',
    'Load order pool first (Stage 1).',
    'Group selected sales orders into planning orders.'
  );
  setButtonState(
    'heatDeriveBtn',
    snapshot.canDerive,
    'ps-heats',
    'Propose planning orders first (Stage 2).',
    'Derive heat batches from current planning orders.'
  );
  setButtonState(
    'schedulerSimulateBtn',
    snapshot.canSimulate,
    'ps-schedule',
    'Derive heat batches first (Stage 3).',
    'Run feasibility check for current heat plan.'
  );
  setButtonState(
    'releaseApproveBtn',
    snapshot.canRelease,
    'ps-release',
    snapshot.simFeasible
      ? 'No release-ready feasible planning orders yet.'
      : 'Run a feasible simulation first (Stage 4).',
    'Release selected feasible planning orders to Execution.'
  );
}

// function renderPlanningWorkflowGuide(snapshot = planningWorkflowSnapshot()) {
//   const trackEl = qs('planningWorkflowTrack');
//   const nextEl = qs('planningWorkflowNext');
//   if (trackEl) {
//     const stageRows = [
//       { key: 'pool', label: '1. Pool', meta: `${snapshot.poolCount} SOs` },
//       { key: 'propose', label: '2. Propose', meta: `${snapshot.poCount} POs` },
//       { key: 'heats', label: '3. Heats', meta: `${snapshot.heatCount} heats` },
//       { key: 'simulate', label: '4. Simulate', meta: snapshot.hasSim ? (snapshot.simFeasible ? 'Feasible' : 'Check') : 'Pending' },
//       { key: 'release', label: '5. Release', meta: `${snapshot.releasedCount} released` }
//     ];
//     trackEl.innerHTML = stageRows.map((stage) => {
//       const status = snapshot.stageStatus[stage.key] || 'pending';
//       const statusLabel = status === 'done'
//         ? 'Done'
//         : status === 'running'
//           ? 'Running'
//           : status === 'warn'
//             ? 'Check'
//             : status === 'blocked'
//               ? 'Blocked'
//               : 'Pending';
//       return `<div class="planning-workflow-step ${status}">
//         <span class="planning-workflow-dot" aria-hidden="true"></span>
//         <span class="planning-workflow-copy">
//           <span class="planning-workflow-step-label">${escapeHtml(stage.label)}</span>
//           <span class="planning-workflow-step-meta">${escapeHtml(statusLabel)} · ${escapeHtml(stage.meta)}</span>
//         </span>
//       </div>`;
//     }).join('');
//   }
//   if (nextEl) nextEl.textContent = snapshot.next;
// }

// function refreshPlanningWorkflowGuide() {
//   const snapshot = planningWorkflowSnapshot();
//   const stageIdMap = {
//     pool: 'ps-pool',
//     propose: 'ps-propose',
//     heats: 'ps-heats',
//     simulate: 'ps-schedule',
//     release: 'ps-release'
//   };
//   Object.entries(stageIdMap).forEach(([key, stageId]) => {
//     const stageEl = qs(stageId);
//     if (!stageEl || stageEl.classList.contains('running')) return;
//     const status = snapshot.stageStatus[key] || 'pending';
//     setPipelineStageStatus(stageId, status, undefined, undefined, { skipGuideRefresh: true });
//   });
//   syncPlanningActionAvailability(snapshot);
//   renderPlanningWorkflowGuide(snapshot);
// }

async function runFullPipeline(){
  const btn = qs('pipelineRunBtn');
  btn.textContent = '⏳ Running…';
  btn.disabled = true;

  qs('pipelineStatusBadge').style.background = '#dbeafe';
  qs('pipelineStatusBadge').style.color = '#2563eb';
  qs('pipelineStatusBadge').textContent = 'Running…';

  const pipelineChip = qs('chipPipeline');
  pipelineChip.style.display = '';
  setChipTone('chipPipeline', 'info');
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

    // Pre-run BOM scoped to current planning selection so totals match planner intent.
    setText('chipPipelineText', '⏳ Running BOM explosion...');
    await runBomForPlanningOrders();

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

    setChipTone('chipPipeline', 'success');
    setText('chipPipelineText', '✓ Pipeline ready to release');
  } catch(e) {
    qs('pipelineStatusBadge').style.background = '#fee2e2';
    qs('pipelineStatusBadge').style.color = '#dc2626';
    qs('pipelineStatusBadge').textContent = 'Error';

    setChipTone('chipPipeline', 'danger');
    setText('chipPipelineText', '✗ Pipeline error: ' + e.message);

    console.error('Pipeline error:', e);
  }

  btn.textContent = '▶ Run Pipeline';
  btn.disabled = false;
  // refreshPlanningWorkflowGuide();
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

    topTabs.addEventListener('keydown', (e) => {
      const navKeys = ['ArrowRight', 'ArrowLeft', 'Home', 'End'];
      if (!navKeys.includes(e.key)) return;
      const current = e.target.closest('[data-page]');
      if (!current) return;

      const tabs = [...topTabs.querySelectorAll('[data-page]')];
      if (!tabs.length) return;
      const currentIndex = tabs.indexOf(current);
      if (currentIndex < 0) return;

      let nextIndex = currentIndex;
      if (e.key === 'ArrowRight') nextIndex = (currentIndex + 1) % tabs.length;
      if (e.key === 'ArrowLeft') nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
      if (e.key === 'Home') nextIndex = 0;
      if (e.key === 'End') nextIndex = tabs.length - 1;

      const nextTab = tabs[nextIndex];
      if (!nextTab) return;
      e.preventDefault();
      nextTab.focus();
      activatePage(nextTab.dataset.page);
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
  restoreTopSummaryPreference();
  restoreMaterialModePreference();
  const activePage = document.querySelector('.page.active')?.id?.replace('page-', '') || 'dashboard';
  setTopActionContext(activePage);
  renderTopSummaryForPage(activePage);
  refreshExecutionZoomLabels();
  syncExecutionDetailPanel();
}

initAppUi();
async function checkHealth(){
  try{
    const d = await apiFetch('/api/health?quick=1');
    setChipTone('chipApi', 'success');
    setText('chipApiText', d.workbook_ok ? 'API + workbook connected' : 'API up, workbook issue');
  }catch(e){
    setChipTone('chipApi', 'danger');
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
  el.className = ['kpi-card', 'kpi-card--sub', 'metric', tone].filter(Boolean).join(' ');
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

function activePageKey() {
  return document.querySelector('.page.active')?.id?.replace('page-', '') || 'dashboard';
}

function normalizeMaterialMode(mode) {
  const key = String(mode || '').trim().toLowerCase();
  if (key === 'po' || key === 'planning_order' || key === 'planning-order' || key === 'planning_orders') return 'po';
  if (key === 'heat' || key === 'heats') return 'heat';
  return 'campaign';
}

function materialDetailLevel() {
  const payloadLevel = normalizeMaterialMode(state.material?.detail_level || '');
  if (payloadLevel !== 'campaign' || state.material?.detail_level) return payloadLevel;
  return normalizeMaterialMode(state.materialMode);
}

function materialEntityLabel(level = materialDetailLevel(), { plural = true } = {}) {
  const labels = {
    campaign: plural ? 'Campaigns' : 'Campaign',
    po: plural ? 'Planning Orders' : 'Planning Order',
    heat: plural ? 'Heats' : 'Heat'
  };
  return labels[normalizeMaterialMode(level)] || labels.campaign;
}

function materialEntityId(entity) {
  return String(
    entity?.entity_id ||
    entity?.heat_id ||
    entity?.planning_order_id ||
    entity?.campaign_id ||
    entity?.id ||
    ''
  ).trim();
}

function materialPlanUrl(mode = state.materialMode) {
  return '/api/aps/material/plan?detail_level=' + encodeURIComponent(normalizeMaterialMode(mode));
}

function syncMaterialModeControl() {
  const sel = qs('materialModeSelect');
  if (!sel) return;
  sel.value = normalizeMaterialMode(state.materialMode);
}

function persistMaterialModePreference() {
  try {
    localStorage.setItem('aps.materialMode', normalizeMaterialMode(state.materialMode));
  } catch (_) {}
}

function restoreMaterialModePreference() {
  try {
    const saved = localStorage.getItem('aps.materialMode');
    state.materialMode = normalizeMaterialMode(saved || state.materialMode);
  } catch (_) {
    state.materialMode = 'campaign';
  }
  syncMaterialModeControl();
}

function setTopSummaryCard(slot, card) {
  const cfg = card || {};
  const tone = cfg.tone || '';
  setText(`summaryLabel${slot}`, cfg.label ?? '—');
  setText(`summaryValue${slot}`, cfg.value ?? '—');
  setText(`summarySub${slot}`, cfg.sub ?? '—');
  const cardEl = qs(`summaryCard${slot}`);
  if (cardEl) {
    cardEl.className = ['kpi-card', 'kpi-card--summary', 'metric', tone].filter(Boolean).join(' ');
    cardEl.hidden = !card;
  }
}

function setExecutionKpiCard(slot, card) {
  const cfg = card || {};
  const tone = cfg.tone || '';
  setText(`executionKpiLabel${slot}`, cfg.label ?? '—');
  setText(`executionKpiValue${slot}`, cfg.value ?? '—');
  setText(`executionKpiSub${slot}`, cfg.sub ?? '—');
  const cardEl = qs(`executionKpiCard${slot}`);
  if (cardEl) {
    cardEl.className = ['kpi-card', 'kpi-card--sub', 'metric', tone].filter(Boolean).join(' ');
  }
}

function syncTopSummaryState() {
  const panel = qs('topSummary');
  const toggle = qs('topSummaryToggle');
  if (panel) panel.classList.toggle('is-collapsed', Boolean(state.topSummaryCollapsed));
  if (toggle) {
    const collapsed = Boolean(state.topSummaryCollapsed);
    const label = collapsed ? 'Expand KPI row' : 'Collapse KPI row';
    toggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
    toggle.setAttribute('aria-label', label);
    toggle.setAttribute('title', label);
    toggle.innerHTML = collapsed
      ? '<i class="fa-solid fa-chevron-down" aria-hidden="true"></i>'
      : '<i class="fa-solid fa-chevron-up" aria-hidden="true"></i>';
  }
}

function restoreTopSummaryPreference() {
  try {
    state.topSummaryCollapsed = localStorage.getItem('aps.topSummaryCollapsed') === 'true';
  } catch (_) {
    state.topSummaryCollapsed = false;
  }
  syncTopSummaryState();
}

function toggleTopSummary() {
  state.topSummaryCollapsed = !state.topSummaryCollapsed;
  try {
    localStorage.setItem('aps.topSummaryCollapsed', String(state.topSummaryCollapsed));
  } catch (_) {}
  syncTopSummaryState();
}

function materialCampaignsForSummary() {
  const detailLevel = materialDetailLevel();
  const matPlan = (state.material && typeof state.material === 'object') ? state.material : {};
  const materialCampaigns = matPlan.campaigns || [];
  if (materialCampaigns.length) return materialCampaigns;
  if (detailLevel !== 'campaign') return [];
  return (state.campaigns || []).map(c => ({
    campaign_id: c.campaign_id || c.Campaign_ID || '',
    required_qty: num(c.total_mt || c.Total_MT),
    shortage_qty: (c.shortages || []).reduce((a, s) => a + num(s.qty), 0),
    make_convert_qty: num(c.make_convert_qty || 0),
    material_status: c.material_status || c.Material_Status || '',
    release_status: c.release_status || c.Release_Status || ''
  }));
}

function bomSummarySnapshot() {
  const summary = (state.bomSummary && typeof state.bomSummary === 'object') ? state.bomSummary : {};
  const grouped = Array.isArray(state.bomGrouped) ? state.bomGrouped : [];

  const totalLines = num(summary.total_sku_lines || 0);
  const coveredLines = num(summary.covered_lines || 0);
  const partialLines = num(summary.partial_lines || 0);
  const shortLines = num(summary.short_lines || 0);
  const grossReq = num(summary.total_gross_req || 0);
  const netReq = num(summary.total_net_req || 0);
  const coveragePct = totalLines ? (coveredLines / totalLines) * 100 : 0;
  const plantCount = grouped.length;

  let atRiskMt = 0;
  for (const plant of grouped) {
    for (const mt of (plant.material_types || [])) {
      for (const row of (mt.rows || [])) {
        const status = String(row.status || '').toUpperCase();
        if (status === 'SHORT' || status === 'PARTIAL SHORT') {
          atRiskMt += num(row.net_req || 0);
        }
      }
    }
  }

  return {
    totalLines,
    coveredLines,
    partialLines,
    shortLines,
    grossReq,
    netReq,
    coveragePct,
    plantCount,
    atRiskMt
  };
}

function topSummaryCardsForPage(page = activePageKey(), summary = state.overview || {}, cachedKpis = null) {
  const kpis = cachedKpis || deriveAppKpis(summary);
  const planStatus = derivePlanStatus();
  const ot = kpis.onTimePct == null ? '—' : `${kpis.onTimePct.toFixed(1)}%`;
  const simSub = kpis.sim
    ? (kpis.sim.horizon_exceeded
      ? `Needs ${num(kpis.sim.total_duration_hours || 0).toFixed(1)}h vs ${num(kpis.sim.horizon_hours || 0).toFixed(1)}h horizon`
      : (kpis.sim.message || kpis.solverStatus))
    : (kpis.solverStatus || 'delivery rate');

  if (page === 'dashboard') {
    const lateTone = kpis.lateCount > 0 ? 'danger' : 'success';
    const holdTone = kpis.heldCount > 0 ? 'warn' : 'success';
    const utilTone = kpis.maxUtil == null ? '' : (kpis.maxUtil > 100 ? 'danger' : kpis.maxUtil > 85 ? 'warn' : 'success');
    return [
      { label: 'Plan Status', value: planStatus.value, sub: planStatus.sub, tone: planStatus.tone },
      { label: 'Planning Orders', value: String(kpis.planningOrderCount || 0), sub: `${kpis.releasedCount} released`, tone: '' },
      { label: 'On-Time', value: ot, sub: simSub, tone: kpis.onTimePct != null && kpis.onTimePct < 85 ? 'danger' : 'success' },
      { label: 'Late / Hold', value: `${kpis.lateCount}/${kpis.heldCount}`, sub: 'Late campaigns / on hold', tone: kpis.lateCount > 0 ? lateTone : holdTone },
      { label: 'Peak Utilisation', value: kpis.maxUtil == null ? '—' : `${kpis.maxUtil}%`, sub: kpis.capacityLabel || 'Most constrained resource', tone: utilTone }
    ];
  }

  if (page === 'planning') {
    const poolCount = (state.poolOrders && state.poolOrders.length) ? state.poolOrders.length : num(summary.orders_total || summary.open_orders || 0);
    const windowCount = Array.isArray(state.windowSOs) ? state.windowSOs.length : 0;
    const releaseReady = Array.isArray(state.releaseQueue) ? state.releaseQueue.length : 0;
    return [
      { label: 'Order Pool', value: String(poolCount || 0), sub: `${kpis.urgentPoolCount || 0} urgent`, tone: 'info' },
      { label: 'Window SOs', value: String(windowCount), sub: `${state.planningWindow || 7}-day planning window`, tone: '' },
      { label: 'Proposed Lots', value: String(kpis.planningOrderCount || 0), sub: `${kpis.totalMt.toFixed(0)} MT in scope`, tone: '' },
      { label: 'Heat Batches', value: String(kpis.totalHeats || 0), sub: `${kpis.releasedHeats || 0} released`, tone: 'success' },
      { label: 'Release Ready', value: String(releaseReady), sub: releaseReady ? 'Feasible POs waiting approval' : 'No feasible queue yet', tone: releaseReady ? 'success' : 'warn' }
    ];
  }

  if (page === 'material') {
    const campaigns = materialCampaignsForSummary();
    const scopeLabel = materialEntityLabel(materialDetailLevel(), { plural: true });
    const scopeSub = materialDetailLevel() === 'campaign' ? 'Material review scope' : 'Material review entities';
    const riskTagged = campaigns.map(c => materialRiskKey(c));
    const readyCount = riskTagged.filter(r => r === 'ready').length;
    const convertCount = riskTagged.filter(r => r === 'convert').length;
    const heldCount = riskTagged.filter(r => r === 'held').length;
    const shortCount = riskTagged.filter(r => r === 'short').length;
    return [
      { label: `Total ${scopeLabel}`, value: String(campaigns.length || 0), sub: scopeSub, tone: 'info' },
      { label: 'Ready', value: String(readyCount), sub: 'Can release now', tone: 'success' },
      { label: 'Needs Convert', value: String(convertCount), sub: 'Make/convert action', tone: 'warn' },
      { label: 'Held', value: String(heldCount), sub: 'Manual hold status', tone: '' },
      { label: 'Short', value: String(shortCount), sub: 'Blocking shortages', tone: 'danger' }
    ];
  }

  if (page === 'execution') {
    const rows = state.lastScheduleRows && state.lastScheduleRows.length ? state.lastScheduleRows : (state.gantt || []);
    const inFlightOps = rows.length;
    const lateTone = kpis.lateCount > 0 ? 'danger' : 'success';
    const holdTone = kpis.heldCount > 0 ? 'warn' : 'success';
    return [
      { label: 'Released', value: String(kpis.releasedCount || 0), sub: `${kpis.releasedMt.toFixed(0)} MT released`, tone: 'success' },
      { label: 'In Flight', value: String(inFlightOps), sub: 'Scheduled operations', tone: 'info' },
      { label: 'Late', value: String(kpis.lateCount || 0), sub: kpis.lateCount ? 'Campaigns behind due date' : 'No late campaigns', tone: lateTone },
      { label: 'On Hold', value: String(kpis.heldCount || 0), sub: kpis.heldCount ? 'Manual hold present' : 'No held campaigns', tone: holdTone },
      { label: 'On-Time', value: ot, sub: simSub, tone: kpis.onTimePct != null && kpis.onTimePct < 85 ? 'danger' : 'success' }
    ];
  }

  if (page === 'bom') {
    const s = bomSummarySnapshot();
    const shortTone = s.shortLines > 0 ? 'danger' : 'success';
    const riskTone = s.atRiskMt > 0 ? 'warn' : 'success';
    return [
      { label: 'BOM Lines', value: String(s.totalLines || 0), sub: `${s.plantCount || 0} plants in scope`, tone: 'info' },
      { label: 'Coverage', value: `${s.coveragePct.toFixed(0)}%`, sub: `${s.coveredLines} covered · ${s.partialLines} partial`, tone: 'success' },
      { label: 'Short', value: String(s.shortLines || 0), sub: s.shortLines > 0 ? 'Blocking shortages' : 'No blocking shortages', tone: shortTone },
      { label: 'Gross Req', value: `${(s.grossReq / 1000).toFixed(1)}k`, sub: `${s.grossReq.toFixed(1)} MT total`, tone: '' },
      { label: 'Net / At Risk', value: `${(s.netReq / 1000).toFixed(1)}k`, sub: `${s.atRiskMt.toFixed(1)} MT at risk`, tone: riskTone }
    ];
  }

  if (page === 'capacity') {
    const rows = [...(state.capacity || [])];
    const sorted = rows.sort((a, b) =>
      num(b['Utilisation_%'] || b.utilisation || 0) - num(a['Utilisation_%'] || a.utilisation || 0)
    );
    const avgUtil = sorted.length
      ? sorted.reduce((a, r) => a + num(r['Utilisation_%'] || r.utilisation || 0), 0) / sorted.length
      : null;
    const overloaded = sorted.filter(r => num(r['Utilisation_%'] || r.utilisation || 0) > 100).length;
    const slack = sorted.filter(r => num(r['Utilisation_%'] || r.utilisation || 0) < 60).length;
    const peak = sorted.length ? Math.round(num(sorted[0]['Utilisation_%'] || sorted[0].utilisation || 0)) : null;
    const peakTone = peak == null ? '' : (peak > 100 ? 'danger' : peak > 85 ? 'warn' : 'success');
    return [
      { label: 'Bottleneck', value: sorted.length ? String(sorted[0].Resource_ID || sorted[0].resource_id || '—') : '—', sub: 'Most constrained resource', tone: 'danger' },
      { label: 'Avg Utilisation', value: avgUtil == null ? '—' : `${avgUtil.toFixed(1)}%`, sub: 'Across all resources', tone: '' },
      { label: 'Overloaded', value: String(overloaded), sub: 'Above 100%', tone: 'warn' },
      { label: 'Available Slack', value: String(slack), sub: 'Below 60%', tone: 'success' },
      { label: 'Peak Utilisation', value: peak == null ? '—' : `${peak}%`, sub: 'Current peak load', tone: peakTone }
    ];
  }

  if (page === 'ctp') {
    const requests = state.ctpRequests || [];
    const outputByRequest = new Map((state.ctpOutput || []).map(out => [
      String(out.Request_ID || out.request_id || ''),
      out
    ]));
    let feasible = 0;
    let infeasible = 0;
    let pending = 0;
    const latenessDays = [];

    requests.forEach((req) => {
      const requestId = String(req.Request_ID || req.request_id || '');
      const out = outputByRequest.get(requestId) || {};
      const isFeasible = out.Feasible ?? out.feasible ?? out.Plant_Completion_Feasible ?? out.plant_completion_feasible;
      if (isFeasible === true) feasible += 1;
      else if (isFeasible === false) infeasible += 1;
      else pending += 1;

      const margin = Number(out.Lateness_Days ?? out.lateness_days ?? out.Margin_Days ?? out.margin_days);
      if (Number.isFinite(margin)) latenessDays.push(margin);
    });

    const avgMargin = latenessDays.length
      ? latenessDays.reduce((sum, v) => sum + v, 0) / latenessDays.length
      : null;

    return [
      { label: 'Requests', value: String(requests.length), sub: 'CTP checks logged', tone: 'info' },
      { label: 'Feasible', value: String(feasible), sub: 'Can meet requested date', tone: 'success' },
      { label: 'Not Feasible', value: String(infeasible), sub: infeasible ? 'Needs replanning or date shift' : 'No infeasible decisions', tone: infeasible ? 'danger' : 'success' },
      { label: 'Pending', value: String(pending), sub: pending ? 'Awaiting output rows' : 'All requests evaluated', tone: pending ? 'warn' : 'success' },
      {
        label: 'Avg Margin',
        value: avgMargin == null ? '—' : `${avgMargin.toFixed(1)}d`,
        sub: avgMargin == null ? 'No lateness data yet' : (avgMargin > 0 ? 'Average lateness vs request' : 'On-time/early on average'),
        tone: avgMargin == null ? '' : (avgMargin > 0 ? 'warn' : 'success')
      }
    ];
  }

  return topSummaryCardsForPage('dashboard', summary, kpis);
}

function renderTopSummaryForPage(page = activePageKey(), summary = state.overview || {}, cachedKpis = null) {
  const cards = topSummaryCardsForPage(page, summary, cachedKpis);
  const grid = qs('summaryGrid');
  for (let idx = 0; idx < 5; idx += 1) {
    setTopSummaryCard(idx + 1, cards[idx] || null);
  }
  if (grid) {
    grid.style.setProperty('--summary-count', String(Math.max(1, Math.min(5, cards.length || 1))));
  }
  renderTabStatusContext(page, summary, cachedKpis);
}

function hydrateSummary(summary){
  const kpis = deriveAppKpis(summary);
  renderTopSummaryForPage(activePageKey(), summary, kpis);

  const solverStatus = upper(kpis.solverStatus || '');
  const solverTone = solverStatus.includes('OPTIMAL')
    ? 'success'
    : (solverStatus.includes('FEASIBLE') || solverStatus.includes('RUN') || solverStatus.includes('PROCESS')) ? 'info' : 'warn';
  setChipTone('chipSolver', solverTone);
  setChipTone('chipHeld', num(kpis.heldCount) > 0 ? 'warn' : 'success');
  setChipTone('chipLate', num(kpis.lateCount) > 0 ? 'danger' : 'success');

  setText('chipSolverText', 'Solver ' + (kpis.solverStatus || '—'));
  setText('chipHeldText', `Hold ${kpis.heldCount || 0}`);
  setText('chipLateText', `Late ${kpis.lateCount || 0}`);

  const planTone = derivePlanStatus().tone;
  updateStatusBarProgress(kpis.progressPct, planTone);
}

function updateStatusBarProgress(progressPct = null, tone = 'info') {
  const progress = progressPct == null ? deriveAppKpis().progressPct : Math.max(0, Math.min(100, num(progressPct)));
  const progressBar = qs('statusFooterProgressFill');
  if (progressBar) {
    const mappedTone = tone === 'danger' ? 'danger' : tone === 'warn' ? 'warn' : tone === 'success' ? 'success' : 'info';
    progressBar.className = `status-footer-progress-fill tone-${mappedTone}`;
    progressBar.style.width = progress + '%';
  }
}

function renderTabStatusContext(page = activePageKey(), summary = state.overview || {}, cachedKpis = null) {
  const slot = qs('statusTabContext');
  if (!slot) return;

  const kpis = cachedKpis || deriveAppKpis(summary);
  const chips = [];
  const pushChip = (tone, label, value = '', note = '') => {
    const lead = value ? `<span class="status-chip-label">${escapeHtml(label)}</span><span class="status-chip-value">${escapeHtml(value)}</span>` : `<span class="status-chip-value">${escapeHtml(label)}</span>`;
    const tail = note ? `<span class="status-chip-note">${escapeHtml(note)}</span>` : '';
    chips.push(`<div class="${chipToneClass(tone)} status-chip status-chip--context"><span class="dot"></span><span class="status-chip-copy">${lead}${tail}</span></div>`);
  };

  if (page === 'planning') {
    const completed = Object.values(kpis.stageState || {}).filter(Boolean).length;
    const sim = latestSimResult();
    const simLabel = sim ? (sim.feasible ? 'Feasible' : (sim.horizon_exceeded ? 'Exceeded' : 'At Risk')) : 'Pending';
    const simTone = sim ? (sim.feasible ? 'success' : (sim.horizon_exceeded ? 'danger' : 'warn')) : 'info';
    pushChip(completed >= 4 ? 'success' : 'info', 'Pipeline', `${completed}/5`, 'stages ready');
    pushChip(kpis.releasedCount > 0 ? 'success' : 'warn', 'Released', String(kpis.releasedCount || 0), 'planning orders');
    pushChip(simTone, 'Simulation', simLabel);
    pushChip('info', 'Artifacts', 'API', 'Workbook writeback is explicit');
  } else if (page === 'bom') {
    const shortLines = num(state.bomSummary?.short_lines || 0);
    const structureErrors = Array.isArray(state.bomStructureErrors) ? state.bomStructureErrors.length : 0;
    const netReq = num(state.bomSummary?.net_requirement_mt || state.bomSummary?.net_req_mt || 0);
    pushChip(shortLines > 0 ? 'warn' : 'success', 'Short Lines', String(shortLines));
    pushChip(structureErrors > 0 ? 'danger' : 'success', 'Structure', structureErrors > 0 ? `${structureErrors}` : 'Clean', structureErrors > 0 ? 'issues' : '');
    pushChip('info', 'Net Req', `${netReq.toFixed(1)} MT`);
    pushChip('info', 'Scope', 'Total Plan', 'Use Material tab for release blockers');
  } else if (page === 'material') {
    const entities = materialCampaignsForSummary();
    const shortCount = entities.filter(entity => materialRiskKey(entity) === 'short').length;
    const convertCount = entities.filter(entity => materialRiskKey(entity) === 'convert').length;
    const readyCount = entities.filter(entity => materialRiskKey(entity) === 'ready').length;
    pushChip(shortCount > 0 ? 'danger' : 'success', 'Short', String(shortCount), materialEntityLabel(materialDetailLevel(), { plural: true }));
    pushChip(convertCount > 0 ? 'warn' : 'success', 'Convert', String(convertCount), 'need processing');
    pushChip(readyCount > 0 ? 'success' : 'info', 'Ready', String(readyCount));
  } else if (page === 'execution') {
    const rows = state.lastScheduleRows && state.lastScheduleRows.length ? state.lastScheduleRows : (state.gantt || []);
    const selectedCampaign = String(state.selectedCampaign || '').trim();
    pushChip(rows.length ? 'info' : 'warn', 'Ops', String(rows.length), 'scheduled');
    pushChip(kpis.lateCount > 0 ? 'danger' : 'success', 'Late', String(kpis.lateCount || 0), 'campaigns');
    pushChip(selectedCampaign ? 'info' : 'neutral', 'Selection', selectedCampaign || 'None');
  } else if (page === 'capacity') {
    const peak = kpis.maxUtil == null ? null : Number(kpis.maxUtil);
    const overloaded = (state.capacity || []).filter(r => num(r['Utilisation_%'] || r.utilisation || 0) > 100).length;
    const peakTone = peak != null && peak > 100 ? 'danger' : peak != null && peak > 85 ? 'warn' : 'success';
    const selectedCampaign = String(state.selectedCampaign || '').trim();
    pushChip(peakTone, 'Peak Util', peak == null ? '—' : `${peak}%`);
    pushChip(overloaded > 0 ? 'danger' : 'success', 'Overloaded', String(overloaded), 'resources');
    pushChip(selectedCampaign ? 'info' : 'neutral', 'Execution', selectedCampaign || 'Unpinned', selectedCampaign ? 'campaign selected' : 'select in Execution');
  } else if (page === 'ctp') {
    const requests = state.ctpRequests || [];
    const outputs = state.ctpOutput || [];
    const feasibleCount = outputs.filter(out =>
      out.Feasible === true ||
      out.feasible === true ||
      out.Plant_Completion_Feasible === true ||
      out.plant_completion_feasible === true
    ).length;
    const pendingCount = Math.max(requests.length - outputs.length, 0);
    pushChip(requests.length ? 'info' : 'warn', 'Requests', String(requests.length));
    pushChip(feasibleCount > 0 ? 'success' : 'warn', 'Feasible', String(feasibleCount));
    pushChip(pendingCount > 0 ? 'warn' : 'success', 'Pending', String(pendingCount));
  } else if (page === 'master') {
    pushChip('info', 'Sections', String((state.master && Object.keys(state.master).length) || 0), 'loaded');
  } else if (page === 'scenarios') {
    pushChip('info', 'Scenarios', String((state.scenarios || []).length), 'saved');
  } else {
    const planStatus = derivePlanStatus();
    pushChip(planStatus.tone === 'danger' ? 'danger' : planStatus.tone === 'warn' ? 'warn' : 'success', 'Plan', planStatus.value);
    pushChip('info', 'Planning Orders', String(kpis.planningOrderCount || 0));
    pushChip(num(kpis.heldCount) > 0 ? 'warn' : 'success', 'Hold', String(kpis.heldCount || 0));
  }

  slot.innerHTML = chips.join('');
}

// ──────────────────────────────────────────────
//  DASHBOARD RENDERERS
// ──────────────────────────────────────────────

function renderDashboard() {
  renderDashboardPipeline();
  renderDashboardAlerts();
  renderBottleneck(state.overview || {});
  renderDashboardConstraints();
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
      <div class="dash-row-copy">
        <div class="dash-row-title">${escapeHtml(s.label)}</div>
        <div class="dash-row-sub">${escapeHtml(s.detail)}</div>
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
      <div class="dash-cap-row-head">
        <div class="dash-cap-resource">
          <div class="dash-cap-dot" style="background:${plantColor}"></div>
          <span class="dash-cap-resource-id">${escapeHtml(rid)}</span>
        </div>
        <span class="dash-cap-util" style="color:${barColor}">${util}%</span>
      </div>
      <div class="dash-cap-bar-bg">
        <div class="dash-cap-bar" style="width:${Math.min(util,100)}%;background:${barColor}"></div>
      </div>
      <div class="dash-cap-op-meta">
        <span>${escapeHtml(String(r.Operation_Group || r.Operation || _ridToOp(rid)))}</span>
        <span>${demand.toFixed(1)}h / ${avail.toFixed(1)}h</span>
      </div>
    </div>`;
  }).join('') : '<div class="dashboard-empty dashboard-empty--spacious">Run Schedule or Feasibility Check to see utilisation.</div>';
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
    qs('dashboardInventoryBody').innerHTML = '<div class="dashboard-empty">Load Master Data to see inventory.</div>';
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
    qs('dashboardInventoryBody').innerHTML = '<div class="dashboard-empty dashboard-empty--success"><div class="dashboard-empty-icon">✓</div>All materials above safety stock.</div>';
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
        <div class="dash-mat-detail">${m.qty} MT on hand · need ${m.min} MT</div>
      </div>
      <span class="dash-mat-tag" style="background:${tagBg};color:${tagColor}">${m.status}</span>
    </div>`;
  }).join('');
}

function renderDashActivePOs() {
  const pos = state.planningOrders || [];
  if (!pos.length) {
    qs('dashActivePOs').innerHTML = '<div class="dashboard-empty">No POs yet. Run Propose Orders.</div>';
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
        <div class="dash-po-id">${escapeHtml(po.po_id)}</div>
        <div class="dash-po-meta">${escapeHtml(grade)} · ${soCount} SOs · ${heats} heats${dueWindow ? ` · ${escapeHtml(dueWindow)}` : ''}</div>
      </div>
      <div class="dash-po-side">
        <div class="dash-po-mt">${mt} MT</div>
        <div class="dash-po-status" style="color:${badgeColor}">${(po.planner_status||'PROP').substring(0,4)}</div>
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
      const cidLiteral = JSON.stringify(String(cid));
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
          <button class="btn success" style="font-size:.72rem;padding:.25rem .55rem;${isReleased?'opacity:.4;cursor:not-allowed':''}" ${isReleased?'disabled':''} onclick="releaseSinglePO('${escapeHtml(cid)}')">Release</button>
          <button class="btn danger" style="font-size:.72rem;padding:.25rem .55rem;${!isReleased?'opacity:.4;cursor:not-allowed':''}" ${!isReleased?'disabled':''} onclick="unreleasePlanningOrder('${escapeHtml(cid)}')">Unrelease</button>
          <button class="btn warn" style="font-size:.72rem;padding:.25rem .55rem" onclick="updateCampaignStatus('${escapeHtml(cid)}','Release_Status','MATERIAL HOLD')">Hold</button>
          <button class="btn ghost" style="font-size:.72rem;padding:.25rem .55rem" onclick='focusCampaignInExecution(${cidLiteral})'>Focus</button>
        </td>
      </tr>`;
    }).join('')}
    </tbody>
  </table></div>`;
}
function renderSchedule(){
  const wrap = qs('scheduleTimeline');
  const jobs = getExecutionJobs();
  if(!jobs.length){
    wrap.innerHTML = executionEmptyStateMarkup(
      'Plant timeline is empty',
      'Run Schedule from the top action bar to populate plant-wise execution rows.',
      'warning'
    );
    refreshExecutionTopbarMeta('gantt');
    return;
  }
  const zoom = getZoomValue('executionPlant', 1);
  setZoomLabel('plantZoomLabel', zoom);
  const scale = buildTimelineScale(
    jobs.map(j => j.Planned_Start),
    jobs.map(j => j.Planned_End),
    zoom,
    14,
    1120
  );
  setScaleLabel('plantScaleLabel', scale.scaleLabel);

  // Group by plant first, then resource, and keep routing sequence order.
  const byPlant = {};
  jobs.forEach(job => {
    const s = parseDate(job.Planned_Start);
    const e = parseDate(job.Planned_End);
    if (isNaN(s) || isNaN(e)) return;
    const plant = plantForJob(job);
    const rid = String(job.Resource_ID || '?').trim() || '?';
    if (!byPlant[plant]) byPlant[plant] = {};
    if (!byPlant[plant][rid]) byPlant[plant][rid] = [];
    byPlant[plant][rid].push(job);
  });

  const axisHtml = scale.buckets.map(bucket => `
    <div class="gantt-day" style="width:${scale.bucketPx}px;min-width:${scale.bucketPx}px">
      <div>${bucket.label.primary}</div>
      <small>${bucket.label.secondary}</small>
    </div>
  `).join('');
  let html = `<div class="gantt-hdr">
    <div class="gantt-res-col" style="font-weight:700">Resource</div>
    <div class="gantt-days" style="width:${scale.contentWidth}px;min-width:${scale.contentWidth}px">${axisHtml}</div>
  </div>`;

  const orderedPlants = getExecutionPlantOrder(jobs);
  orderedPlants.forEach((plant, plantIndex) => {
    const resources = byPlant[plant] || {};
    const orderedResources = Object.keys(resources).sort((a, b) => {
      const aOp = _ridToOp(a);
      const bOp = _ridToOp(b);
      const safeA = executionOperationRank(aOp);
      const safeB = executionOperationRank(bOp);
      if (safeA !== safeB) return safeA - safeB;
      return String(a).localeCompare(String(b));
    });

    html += `<div class="gantt-row gantt-plant-band" style="${plantIndex ? 'border-top:1px solid var(--border);' : ''}">
      <div class="gantt-res-lbl"><div class="rid">${escapeHtml(plant)} Plant</div><div class="rop">Routing sequence view</div></div>
      <div class="gantt-tl" style="width:${scale.contentWidth}px;min-width:${scale.contentWidth}px;background-size:${scale.bucketPx}px 100%"></div>
    </div>`;

    orderedResources.forEach(rid => {
      const ops = resources[rid];
      const bars = ops.map(op => {
        const s = parseDate(op.Planned_Start);
        const e = parseDate(op.Planned_End);
        if (isNaN(s) || isNaN(e) || s > e) return '';
        const metrics = timelineBarMetrics(s, e, scale, 8);
        if (!metrics) return '';
        const campaign = String(op.Campaign || '').trim() || 'Campaign';
        const campaignLiteral = JSON.stringify(campaign);
        const cls = ganttClassFromOperation(_ridToOp(rid));
        const label = metrics.width >= 86 ? escapeHtml(campaign) : '';
        const selectedCls = executionBarSelectionClasses(campaign);
        const tooltip = executionBarTooltip(op, rid);
        return `<div class="gantt-job ${cls}${selectedCls}" data-campaign="${escapeAttr(campaign)}" data-gantt-tooltip="${escapeAttr(tooltip)}" style="left:${metrics.left.toFixed(1)}px;width:${metrics.width.toFixed(1)}px;cursor:pointer;min-width:6px" onclick='selectCampaignFromGantt(${campaignLiteral})' onkeydown='handleExecutionBarKey(event, ${campaignLiteral})' role="button" tabindex="0" aria-label="Inspect ${escapeAttr(campaign)} in plant timeline">${label}</div>`;
      }).join('');
      html += `<div class="gantt-row">
        <div class="gantt-res-lbl"><div class="rid">${escapeHtml(rid)}</div><div class="rop">${escapeHtml(_ridToOp(rid))}</div></div>
        <div class="gantt-tl" style="width:${scale.contentWidth}px;min-width:${scale.contentWidth}px;background-size:${scale.bucketPx}px 100%">${bars}</div>
      </div>`;
    });
  });

  wrap.innerHTML = `<div style="min-width:${scale.contentWidth + 180}px">${html}</div>`;
  initGanttTooltips(wrap);
  syncExecutionBarSelectionVisuals();
  refreshExecutionTopbarMeta('gantt');
}

function renderDashboardConstraints() {
  const box = qs('dashboardConstraintBody');
  if (!box) return;

  const summary = state.overview || {};
  const kpis = deriveAppKpis(summary);
  const sim = latestSimResult();
  const matSummary = (state.material && state.material.summary) ? state.material.summary : {};
  const totalRequired = num(matSummary['Total Required Qty'] || 0);
  const shortageQty = num(matSummary['Shortage Qty'] || 0);

  const horizonLoad = sim ? Math.max(0, (num(sim.total_duration_hours || 0) / Math.max(num(sim.horizon_hours || 1), 1)) * 100) : 0;
  const bottleneckUtil = kpis.maxUtil == null ? 0 : num(kpis.maxUtil);
  const latenessPressure = kpis.onTimePct == null ? 0 : Math.max(0, 100 - num(kpis.onTimePct));
  const shortagePressure = totalRequired > 0 ? Math.max(0, Math.min((shortageQty / totalRequired) * 100, 100)) : 0;
  const holdPressure = kpis.planningOrderCount > 0 ? Math.max(0, Math.min((num(kpis.heldCount) / Math.max(num(kpis.planningOrderCount), 1)) * 100, 100)) : 0;
  const releaseReadiness = kpis.planningOrderCount > 0 ? Math.max(0, Math.min((num(kpis.releasedCount) / Math.max(num(kpis.planningOrderCount), 1)) * 100, 100)) : 0;

  const pressureTone = (pct) => {
    if (pct >= 85) return 'danger';
    if (pct >= 60) return 'warn';
    return 'success';
  };

  const readinessTone = releaseReadiness >= 70 ? 'success' : releaseReadiness >= 40 ? 'warn' : 'danger';

  const rows = [
    {
      label: 'Horizon load',
      value: `${horizonLoad.toFixed(0)}%`,
      pct: Math.min(horizonLoad, 100),
      sub: sim ? `${num(sim.total_duration_hours || 0).toFixed(1)}h vs ${num(sim.horizon_hours || 0).toFixed(1)}h` : 'Run feasibility to compute',
      tone: pressureTone(horizonLoad),
      color: horizonLoad >= 100 ? 'red' : horizonLoad >= 85 ? 'gold' : 'green'
    },
    {
      label: 'Bottleneck',
      value: kpis.maxUtil == null ? '—' : `${bottleneckUtil.toFixed(0)}%`,
      pct: Math.min(bottleneckUtil, 100),
      sub: kpis.capacityLabel || 'No capacity data',
      tone: pressureTone(bottleneckUtil),
      color: bottleneckUtil >= 100 ? 'red' : bottleneckUtil >= 85 ? 'gold' : 'green'
    },
    {
      label: 'Lateness',
      value: `${latenessPressure.toFixed(0)}%`,
      pct: Math.min(latenessPressure, 100),
      sub: `${kpis.lateCount || 0} late campaigns`,
      tone: pressureTone(latenessPressure),
      color: latenessPressure >= 80 ? 'red' : latenessPressure >= 55 ? 'gold' : 'green'
    },
    {
      label: 'Material short',
      value: `${shortagePressure.toFixed(0)}%`,
      pct: Math.min(shortagePressure, 100),
      sub: `${shortageQty.toFixed(1)} MT short`,
      tone: pressureTone(shortagePressure),
      color: shortagePressure >= 35 ? 'red' : shortagePressure >= 12 ? 'gold' : 'green'
    },
    {
      label: 'Hold pressure',
      value: `${holdPressure.toFixed(0)}%`,
      pct: Math.min(holdPressure, 100),
      sub: `${kpis.heldCount || 0} held`,
      tone: pressureTone(holdPressure),
      color: holdPressure >= 35 ? 'red' : holdPressure >= 15 ? 'gold' : 'green'
    },
    {
      label: 'Release ready',
      value: `${releaseReadiness.toFixed(0)}%`,
      pct: Math.min(releaseReadiness, 100),
      sub: `${kpis.releasedCount || 0}/${kpis.planningOrderCount || 0} released`,
      tone: readinessTone,
      color: releaseReadiness >= 70 ? 'green' : releaseReadiness >= 40 ? 'gold' : 'red'
    }
  ];

  const highRisk = rows.filter(row => row.label !== 'Release ready' && (row.tone === 'danger' || row.tone === 'warn')).length;
  setText('dashboardConstraintMeta', highRisk ? `${highRisk} under pressure` : 'Stable');

  box.innerHTML = `<div class="dashboard-constraint-grid">${
    rows.map(row => `
      <div class="dashboard-constraint-row">
        <div class="dashboard-constraint-head">
          <div class="dashboard-constraint-label">${escapeHtml(row.label)}</div>
          <div class="dashboard-constraint-value ${row.tone}">${escapeHtml(row.value)}</div>
        </div>
        <div class="mini-track"><div class="mini-bar ${row.color}" style="left:0;width:${Math.min(Math.max(num(row.pct), 0), 100)}%"></div></div>
        <div class="dashboard-constraint-sub">${escapeHtml(row.sub)}</div>
      </div>
    `).join('')
  }</div>`;
}

function ganttClassFromOperation(op) {
  const n = normalizeOperationName(op);
  if (n === 'EAF') return 'eaf';
  if (n === 'LRF') return 'lrf';
  if (n === 'VD') return 'vd';
  if (n === 'CCM') return 'ccm';
  if (n === 'RM') return 'rm';
  return 'eaf';
}

function getExecutionPlantOrder(jobs) {
  const plants = [...new Set((jobs || []).map(j => plantForJob(j)).filter(Boolean))];
  const explicitOrder = configuredPlantOrderFromConfig();
  const byPlantOps = new Map(plants.map(plant => [plant, []]));
  for (const job of (jobs || [])) {
    const plant = plantForJob(job);
    if (!byPlantOps.has(plant)) continue;
    const op = normalizeOperationName(job?.Operation || _ridToOp(job?.Resource_ID || job?.resource_id));
    if (op) byPlantOps.get(plant).push(op);
  }

  const plantRoutingRank = (plant) => {
    const ops = byPlantOps.get(plant) || [];
    if (!ops.length) return 999;
    return ops.reduce((best, op) => Math.min(best, executionOperationRank(op)), 999);
  };

  return plants.sort((a, b) => {
    const ua = String(a).toUpperCase();
    const ub = String(b).toUpperCase();
    if (explicitOrder.length) {
      const ia = explicitOrder.indexOf(ua);
      const ib = explicitOrder.indexOf(ub);
      const sa = ia === -1 ? 999 : ia;
      const sb = ib === -1 ? 999 : ib;
      if (sa !== sb) return sa - sb;
    }
    const routeA = plantRoutingRank(ua);
    const routeB = plantRoutingRank(ub);
    if (routeA !== routeB) return routeA - routeB;
    return ua.localeCompare(ub);
  });
}

function executionDetailEmptyMarkup() {
  return `<div class="execution-detail-empty-state">
    <div class="execution-detail-empty-title">Execution detail</div>
    <div class="execution-detail-empty-copy">Select a bar from equipment or plant timeline to pin details for campaign, due date, heat count, linked sales orders, and material context.</div>
  </div>`;
}

function executionEmptyStateMarkup(title, copy, tone = 'neutral') {
  return `<div class="execution-empty-state execution-empty-state--${escapeHtml(tone)}">
    <div class="execution-empty-title">${escapeHtml(title)}</div>
    <div class="execution-empty-copy">${escapeHtml(copy)}</div>
  </div>`;
}

function executionBarSelectionClasses(campaignId){
  const selectedCampaign = String(state.selectedCampaign || '').trim();
  const campaign = String(campaignId || '').trim();
  if (!selectedCampaign || !campaign || selectedCampaign !== campaign) return '';
  return state.executionDetailPinned ? ' is-selected is-pinned' : ' is-selected';
}

function syncExecutionBarSelectionVisuals(){
  const selectedCampaign = String(state.selectedCampaign || '').trim();
  const hasSelection = Boolean(selectedCampaign);
  const pinned = Boolean(state.executionDetailPinned);
  document.querySelectorAll('.eq-gantt-bar[data-campaign], .gantt-job[data-campaign]').forEach((bar) => {
    const campaign = String(bar.dataset.campaign || '').trim();
    const isSelected = hasSelection && campaign === selectedCampaign;
    bar.classList.toggle('is-selected', isSelected);
    bar.classList.toggle('is-pinned', isSelected && pinned);
    bar.classList.toggle('is-muted', hasSelection && !isSelected);
  });
}

function executionBarTooltip(job, resourceId, flags = []){
  const campaign = String(job?.Campaign || job?.campaign_id || '').trim() || 'Campaign';
  const rid = String(resourceId || job?.Resource_ID || job?.resource_id || '—').trim() || '—';
  const op = normalizeOperationName(job?.Operation || job?.operation || _ridToOp(rid)) || 'Operation';
  const jobId = String(job?.Job_ID || job?.job_id || '').trim();
  const start = fmtDateTime(job?.Planned_Start || job?.planned_start);
  const end = fmtDateTime(job?.Planned_End || job?.planned_end);
  const flagLine = flags.length ? `<br>${escapeHtml(flags.join(' · '))}` : '';
  return `<strong>${escapeHtml(campaign)}</strong><br>${escapeHtml(op)} · ${escapeHtml(rid)}${jobId ? `<br>Job ${escapeHtml(jobId)}` : ''}<br>${escapeHtml(start)} to ${escapeHtml(end)}${flagLine}`;
}

function handleExecutionBarKey(event, campaignId){
  if (!event) return;
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault();
    selectCampaignFromGantt(campaignId);
  }
}

function getExecutionJobs() {
  const simulated = state.lastScheduleRows || [];
  if (state.lastSimResult && simulated.length) return simulated;
  const persisted = state.gantt || [];
  if (persisted.length) return persisted;
  return simulated;
}

function openExecutionGanttMode(mode) {
  if (mode === 'equipment') {
    switchExecView('timeline');
    return;
  }
  if (mode === 'plant') {
    switchExecView('gantt');
    return;
  }

  const jobs = getExecutionJobs();
  if (!jobs.length) {
    alert('No schedule data available for this view. Run Schedule first.');
    return;
  }

  if (mode === 'so') {
    showGanttModal('Sales Orders - Scheduled Timeline', jobs, 'so');
    return;
  }
  if (mode === 'po') {
    showGanttModal('Planning Orders - Operation Timeline', jobs, 'po');
    return;
  }
  showGanttModal('Global Plant-Campaign Timeline', jobs, 'global');
}

function updateExecutionCapacityMeta(resourceIds = []) {
  const loadEl = qs('executionMetaPlantLoad');
  const bottleneckEl = qs('executionMetaBottleneck');
  if (!loadEl || !bottleneckEl) return;

  const normalized = [...new Set((resourceIds || []).map(r => String(r || '').trim()).filter(Boolean))];
  if (!normalized.length) {
    loadEl.textContent = 'Load —';
    bottleneckEl.textContent = 'Bottleneck —';
    return;
  }

  const resourceSet = new Set(normalized);
  const capRows = (state.capacity || [])
    .map(row => ({
      rid: String(row.Resource_ID || row.resource_id || '').trim(),
      util: num(row['Utilisation_%'] || row.utilisation, NaN)
    }))
    .filter(row => resourceSet.has(row.rid) && Number.isFinite(row.util));

  if (!capRows.length) {
    loadEl.textContent = 'Load n/a';
    bottleneckEl.textContent = 'Bottleneck n/a';
    return;
  }

  const avgUtil = Math.round(capRows.reduce((sum, row) => sum + row.util, 0) / capRows.length);
  const overCount = capRows.filter(row => row.util > 100).length;
  const bottleneck = capRows.slice().sort((a, b) => b.util - a.util)[0];
  loadEl.textContent = `${avgUtil}% avg util${overCount ? ` · ${overCount} over` : ''}`;
  bottleneckEl.textContent = `Bottleneck ${bottleneck.rid} ${Math.round(bottleneck.util)}%`;
}

function renderExecutionKpis() {
  const jobs = getExecutionJobs();
  const resources = new Set(jobs.map(j => String(j.Resource_ID || j.resource_id || '').trim()).filter(Boolean));
  const kpis = deriveAppKpis();
  const exceptionCount = num(kpis.lateCount) + num(kpis.heldCount);
  const exceptionTone = exceptionCount > 0 ? 'danger' : 'success';
  const bottleneckTone = kpis.maxUtil == null ? '' : (kpis.maxUtil > 100 ? 'danger' : kpis.maxUtil > 85 ? 'warn' : 'success');

  setExecutionKpiCard(1, {
    label: 'Released Lots',
    value: String(kpis.releasedCount || 0),
    sub: `${num(kpis.releasedMt).toFixed(0)} MT released`,
    tone: 'success'
  });
  setExecutionKpiCard(2, {
    label: 'In Flight Ops',
    value: String(jobs.length || 0),
    sub: `${resources.size || 0} resources active`,
    tone: 'info'
  });
  setExecutionKpiCard(3, {
    label: 'Exceptions',
    value: `${kpis.lateCount || 0}/${kpis.heldCount || 0}`,
    sub: `${exceptionCount} late + hold flags`,
    tone: exceptionTone
  });
  setExecutionKpiCard(4, {
    label: 'Bottleneck',
    value: kpis.maxUtil == null ? '—' : `${Math.round(num(kpis.maxUtil))}%`,
    sub: kpis.capacityLabel || 'Peak resource',
    tone: bottleneckTone
  });
}

function refreshExecutionTopbarMeta(view = null) {
  // KPI rendering removed: only top summary strip shown per design rule
  // renderExecutionKpis();
  const activeView = view || (document.querySelector('.exec-view.active-view')?.id || '').replace('exec-view-', '') || 'timeline';
  const sourceJobs = getExecutionJobs();
  const jobs = activeView === 'timeline' ? (state.executionLastFilteredJobs || sourceJobs) : sourceJobs;
  const resources = new Set(jobs.map(j => String(j.Resource_ID || j.resource_id || '').trim()).filter(Boolean));
  const plants = new Set(jobs.map(j => plantForJob(j)).filter(Boolean));
  const topbarCopy = qs('executionTopbarCopy');
  const boardStatus = qs('executionBoardStatus');
  const filters = state.dispatchFilters || {};
  const filterBits = [];
  if (filters.campaignFilter) filterBits.push(`Campaign ${filters.campaignFilter}`);
  if (filters.gradeFilter) filterBits.push(`Grade ${filters.gradeFilter}`);
  if (filters.plantFilter) filterBits.push(`Plant ${filters.plantFilter}`);
  const filterLabel = filterBits.length ? ` · Filters: ${filterBits.join(', ')}` : '';

  if (!jobs.length && sourceJobs.length && activeView === 'timeline') {
    updateExecutionCapacityMeta([]);
    if (topbarCopy) topbarCopy.textContent = 'No operations match current timeline filters. Clear or adjust Campaign / Grade / Plant filters.';
    if (boardStatus) boardStatus.textContent = '0 rows in filtered timeline';
    return;
  }

  if (!jobs.length) {
    updateExecutionCapacityMeta([]);
    if (topbarCopy) topbarCopy.textContent = 'No scheduled operations loaded. Run Schedule from the top action bar.';
    if (boardStatus) boardStatus.textContent = 'Awaiting schedule data';
    return;
  }

  updateExecutionCapacityMeta([...resources]);

  if (activeView === 'timeline') {
    const constraints = state.executionConstraintSummary || {};
    const constraintBits = [];
    if (constraints.held) constraintBits.push(`${constraints.held} held`);
    if (constraints.late) constraintBits.push(`${constraints.late} late`);
    if (constraints.bottleneck) constraintBits.push(`${constraints.bottleneck} bottleneck bars`);
    const constraintLabel = constraintBits.length ? ` · ${constraintBits.join(' · ')}` : '';
    if (topbarCopy) topbarCopy.textContent = `Equipment timeline (resource lanes) · ${jobs.length} operations across ${resources.size} resources${filterLabel}`;
    if (boardStatus) boardStatus.textContent = state.selectedCampaign ? `Inspecting ${state.selectedCampaign}` : `${resources.size} resources · ${jobs.length} ops${constraintLabel}`;
    return;
  }
  if (activeView === 'campaigns') {
    if (topbarCopy) topbarCopy.textContent = `Released-lot list synced to equipment and plant timelines · ${jobs.length} scheduled operations`;
    if (boardStatus) boardStatus.textContent = `${resources.size} resources in active schedule`;
    return;
  }
  if (topbarCopy) topbarCopy.textContent = `Plant timeline view · ${jobs.length} operations across ${resources.size} resources in ${plants.size || 1} plants`;
  if (boardStatus) boardStatus.textContent = 'Plant sequence focus';
}

function selectCampaignFromGantt(campaignId, options = {}) {
  const normalizedCampaign = String(campaignId || '').trim();
  if (!normalizedCampaign) return;
  state.selectedCampaign = normalizedCampaign;
  state.executionDetailPinned = true;

  const panel = qs('campaignDetailsPanel');
  const content = qs('campaignDetailsContent');
  const clearBtn = qs('clearCampaignBtn');

  if (panel) panel.classList.add('open');
  if (content) {
    content.innerHTML = `<div style="padding:1rem"><strong>${escapeHtml(normalizedCampaign)}</strong><br><small>Campaign details loading...</small></div>`;
  }
  if (clearBtn) {
    const filters = state.dispatchFilters || {};
    const hasFilters = Boolean(filters.campaignFilter || filters.gradeFilter || filters.plantFilter);
    clearBtn.style.display = (options.showClearFilter || hasFilters) ? 'inline-flex' : 'none';
  }

  activatePage('execution');
  const targetView = options.view || currentExecutionView();
  switchExecView(targetView);
  syncExecutionBarSelectionVisuals();
  syncExecutionDetailPanel();
  renderCampaignDetails();
}

function clearCampaignSelection(){
  state.selectedCampaign = null;
  state.executionDetailPinned = false;
  state.dispatchFilters = { campaignFilter: '', gradeFilter: '', plantFilter: '' };
  const clearBtn = document.getElementById('clearCampaignBtn');
  if(clearBtn) clearBtn.style.display = 'none';
  const contentDiv = qs('campaignDetailsContent');
  if(contentDiv) contentDiv.innerHTML = executionDetailEmptyMarkup();
  const campSel = qs('dispatchFilterCampaign');
  const gradeSel = qs('dispatchFilterGrade');
  const plantSel = qs('dispatchFilterPlant');
  if(campSel) campSel.value = '';
  if(gradeSel) gradeSel.value = '';
  if(plantSel) plantSel.value = '';
  syncExecutionDetailPanel();
  const activeView = currentExecutionView();
  if (activeView === 'gantt') renderSchedule();
  if (activeView === 'timeline') renderDispatch();
  syncExecutionBarSelectionVisuals();
  refreshExecutionTopbarMeta(activeView);
}

function focusCampaignInExecution(campaignId){
  const normalizedCampaign = String(campaignId || '').trim();
  if (!normalizedCampaign) return;
  state.dispatchFilters = {
    ...(state.dispatchFilters || {}),
    campaignFilter: normalizedCampaign
  };
  const campSel = qs('dispatchFilterCampaign');
  if (campSel) campSel.value = normalizedCampaign;
  const clearBtn = qs('clearCampaignBtn');
  if (clearBtn) clearBtn.style.display = 'inline-flex';
  selectCampaignFromGantt(normalizedCampaign, { view: 'timeline', showClearFilter: true });
}
function togglePlant(plantId){
  const body = document.getElementById(plantId);
  if(body) body.style.display = body.style.display === 'none' ? 'block' : 'none';
  const header = body?.previousElementSibling;
  if(header) header.querySelector('span').textContent = body.style.display === 'none' ? '▶' : '▼';
}
function renderCampaignDetails(){
  const contentDiv = qs('campaignDetailsContent');
  if(!state.selectedCampaign){
    if (contentDiv) contentDiv.innerHTML = executionDetailEmptyMarkup();
    syncExecutionDetailPanel();
    return;
  }
  const campaign = (state.campaigns || []).find(c=>(c.campaign_id || c.Campaign_ID) === state.selectedCampaign);
  if(!campaign){
    contentDiv.innerHTML = '<div style="padding:1rem;color:var(--text-faint)">Campaign not found</div>';
    syncExecutionDetailPanel();
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
  syncExecutionDetailPanel();
}
function toggleOrderItem(el){
  el.classList.toggle('expanded');
}
function filterDispatchByDropdown(){
  const campaignFilter = qs('dispatchFilterCampaign').value;
  const gradeFilter = qs('dispatchFilterGrade').value;
  const plantFilter = qs('dispatchFilterPlant').value;

  state.dispatchFilters = { campaignFilter, gradeFilter, plantFilter };
  const clearBtn = qs('clearCampaignBtn');
  if (clearBtn) clearBtn.style.display = (campaignFilter || gradeFilter || plantFilter) ? 'inline-flex' : 'none';
  renderDispatch();
}
function populateDispatchFilters(){
  const sourceJobs = getExecutionJobs();
  const campaigns = [...new Set(sourceJobs.map(j=>j.Campaign || j.campaign_id).filter(Boolean))].sort();
  const grades = [...new Set(sourceJobs.map(j=>j.Grade || '').filter(Boolean))].sort();
  const plants = getExecutionPlantOrder(sourceJobs);
  const filters = state.dispatchFilters || {};

  const campSel = qs('dispatchFilterCampaign');
  const gradeSel = qs('dispatchFilterGrade');
  const plantSel = qs('dispatchFilterPlant');

  campSel.innerHTML = '<option value="">All Campaigns</option>' + campaigns.map(c=>`<option value="${escapeHtml(c)}">${escapeHtml(c)}</option>`).join('');
  gradeSel.innerHTML = '<option value="">All Grades</option>' + grades.map(g=>`<option value="${escapeHtml(g)}">${escapeHtml(g)}</option>`).join('');
  plantSel.innerHTML = '<option value="">All Plants</option>' + plants.map(p=>`<option value="${escapeHtml(p)}">${escapeHtml(p)}</option>`).join('');
  campSel.value = filters.campaignFilter || '';
  gradeSel.value = filters.gradeFilter || '';
  plantSel.value = filters.plantFilter || '';
}
function renderDispatch(){
  const grid = qs('dispatchGrid');
  const sourceJobs = getExecutionJobs();
  if(!sourceJobs.length){
    grid.innerHTML = executionEmptyStateMarkup(
      'Execution timeline is empty',
      'Run Schedule from the top action bar to load execution operations.',
      'warning'
    );
    state.executionLastFilteredJobs = [];
    state.executionConstraintSummary = { held: 0, late: 0, bottleneck: 0 };
    syncExecutionDetailPanel();
    refreshExecutionTopbarMeta('timeline');
    return;
  }
  let jobs = [...sourceJobs];
  const zoom = getZoomValue('executionTimeline', 1);
  setZoomLabel('timelineZoomLabel', zoom);
  const filters = state.dispatchFilters || {};

  if(filters.campaignFilter) jobs = jobs.filter(j=>(j.Campaign||j.campaign_id)===filters.campaignFilter);
  if(filters.gradeFilter) jobs = jobs.filter(j=>(j.Grade||'')===filters.gradeFilter);
  if(filters.plantFilter) jobs = jobs.filter(j=>plantForJob(j)===filters.plantFilter);

  if(!jobs.length){
    grid.innerHTML = executionEmptyStateMarkup(
      'No operations match filters',
      'Clear or adjust Campaign, Grade, or Plant filters to restore timeline rows.',
      'neutral'
    );
    state.executionLastFilteredJobs = [];
    state.executionConstraintSummary = { held: 0, late: 0, bottleneck: 0 };
    syncExecutionDetailPanel();
    refreshExecutionTopbarMeta('timeline');
    return;
  }
  const scale = buildTimelineScale(
    jobs.map(j => j.Planned_Start),
    jobs.map(j => j.Planned_End),
    zoom,
    14,
    1080
  );
  setScaleLabel('timelineScaleLabel', scale.scaleLabel);

  // Summary values for topbar status/meta
  const byResource = {};
  jobs.forEach(j=>{ const r=j.Resource_ID||'?'; if(!byResource[r]) byResource[r]=[]; byResource[r].push(j); });
  const campaignStatusMap = new Map((state.campaigns || []).map(c => [
    String(c.campaign_id || c.Campaign_ID || '').trim(),
    upper(c.release_status || c.Release_Status || c.Status || '')
  ]));
  const overloadedResources = new Set(
    (state.capacity || [])
      .filter(c => num(c['Utilisation_%'] || c.utilisation) > 100)
      .map(c => String(c.Resource_ID || c.resource_id || '').trim())
      .filter(Boolean)
  );
  const visibleHeld = new Set();
  const visibleLate = new Set();
  let visibleBottleneckBars = 0;

  // Build rows — one flat row per resource, individual job bars
  const LABEL_W = 90;
  const axisHtml = scale.buckets.map(bucket => `
    <div style="width:${scale.bucketPx}px;min-width:${scale.bucketPx}px;padding:.16rem .25rem;border-right:1px solid var(--border-soft);font-size:.63rem;color:var(--text-faint);line-height:1.2">
      <div style="font-weight:800;color:var(--text)">${bucket.label.primary}</div>
      <div>${bucket.label.secondary}</div>
    </div>
  `).join('');
  let html = `<div style="display:table;min-width:${scale.contentWidth + LABEL_W + 20}px;border-collapse:collapse">`;

  // Header axis row
  html += `<div style="display:table-row">
    <div style="display:table-cell;width:${LABEL_W}px;vertical-align:middle;padding-right:6px"></div>
    <div style="display:table-cell;vertical-align:top;padding-bottom:4px">
      <div style="display:flex;width:${scale.contentWidth}px;min-width:${scale.contentWidth}px;background:var(--panel-muted);border-radius:3px;border:1px solid var(--border-soft);overflow:hidden">${axisHtml}</div>
    </div>
  </div>`;

  const orderedResources = Object.keys(byResource).sort((a, b) => {
    const aOp = _ridToOp(a);
    const bOp = _ridToOp(b);
    const safeA = executionOperationRank(aOp);
    const safeB = executionOperationRank(bOp);
    if (safeA !== safeB) return safeA - safeB;
    return String(a).localeCompare(String(b));
  });

  orderedResources.forEach(rid=>{
    const list = byResource[rid];
    const utilRow = (state.capacity||[]).find(c=>(c.Resource_ID||c.resource_id)===rid);
    const util = Math.round(num(utilRow&&(utilRow['Utilisation_%']||utilRow.utilisation)));
    const pillCls = util>100?'color:#ef4444;font-weight:700':util>85?'color:#f59e0b;font-weight:700':'color:#22c55e;font-weight:700';

    // Individual job bars
    const bars = list.map(j=>{
      const s = parseDate(j.Planned_Start);
      const e = parseDate(j.Planned_End);
      if (Number.isNaN(s.getTime()) || Number.isNaN(e.getTime())) return '';
      const metrics = timelineBarMetrics(s, e, scale, 5);
      if (!metrics) return '';
      const op = (j.Operation||'').toUpperCase();
      const cls = ['EAF','LRF','VD','CCM','RM'].includes(op)?op.toLowerCase():'eaf';
      const camp = String(j.Campaign || '').trim();
      const campaignLiteral = JSON.stringify(camp);
      const status = campaignStatusMap.get(camp) || '';
      const isHeld = status.includes('HOLD');
      const isLate = status.includes('LATE');
      const isBottleneck = overloadedResources.has(String(rid).trim());
      if (isHeld) visibleHeld.add(camp);
      if (isLate) visibleLate.add(camp);
      if (isBottleneck) visibleBottleneckBars += 1;
      const flagClasses = [
        isHeld ? 'flag-held' : '',
        isLate ? 'flag-late' : '',
        isBottleneck ? 'flag-bottleneck' : ''
      ].filter(Boolean).join(' ');
      const flags = [];
      if (isHeld) flags.push('HELD');
      if (isLate) flags.push('LATE');
      if (isBottleneck) flags.push('BOTTLENECK');
      const selectedCls = executionBarSelectionClasses(camp);
      const label = metrics.width >= 72 ? escapeHtml(camp) : '';
      const tooltip = executionBarTooltip(j, rid, flags);
      return `<div class="eq-gantt-bar ${cls} ${flagClasses}${selectedCls}" data-campaign="${escapeAttr(camp)}" data-gantt-tooltip="${escapeAttr(tooltip)}" style="position:absolute;left:${metrics.left.toFixed(1)}px;width:${metrics.width.toFixed(1)}px;top:1px;bottom:1px;border-radius:2px;cursor:pointer;min-width:2px;display:flex;align-items:center;justify-content:center;font-size:.6rem;font-weight:800;color:rgba(255,255,255,.92);overflow:hidden;white-space:nowrap;padding:0 .18rem" onclick='selectCampaignFromGantt(${campaignLiteral})' onkeydown='handleExecutionBarKey(event, ${campaignLiteral})' role="button" tabindex="0" aria-label="Inspect ${escapeAttr(camp)} in equipment timeline">${label}</div>`;
    }).join('');

    html += `<div style="display:table-row">
      <div style="display:table-cell;width:${LABEL_W}px;vertical-align:middle;padding:.25rem .4rem .25rem 0;border-top:1px solid var(--border-soft)">
        <div style="font-size:.75rem;font-weight:700;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${escapeHtml(rid)}</div>
        <div style="font-size:.62rem;${pillCls}">${util}%</div>
      </div>
      <div style="display:table-cell;vertical-align:middle;padding:.25rem 0;border-top:1px solid var(--border-soft)">
        <div style="position:relative;width:${scale.contentWidth}px;min-width:${scale.contentWidth}px;height:34px;background:var(--panel-soft);border-radius:3px;border:1px solid var(--border-soft);overflow:hidden;background-image:repeating-linear-gradient(90deg, transparent, transparent ${Math.max(scale.bucketPx - 1, 1)}px, #edf2f7 ${Math.max(scale.bucketPx - 1, 1)}px, #edf2f7 ${scale.bucketPx}px)">${bars}</div>
      </div>
    </div>`;
  });

  html += '</div>';
  grid.innerHTML = html;
  initGanttTooltips(grid);
  syncExecutionBarSelectionVisuals();
  const clearBtn = qs('clearCampaignBtn');
  const hasFilters = Boolean(filters.campaignFilter || filters.gradeFilter || filters.plantFilter);
  if (clearBtn) clearBtn.style.display = hasFilters ? 'inline-flex' : 'none';
  state.executionLastFilteredJobs = jobs;
  state.executionConstraintSummary = {
    held: visibleHeld.size,
    late: visibleLate.size,
    bottleneck: visibleBottleneckBars
  };
  refreshExecutionTopbarMeta('timeline');
  populateDispatchFilters();
  if(state.selectedCampaign) {
    renderCampaignDetails();
  } else {
    const contentDiv = qs('campaignDetailsContent');
    if (contentDiv && !contentDiv.innerHTML.trim()) contentDiv.innerHTML = executionDetailEmptyMarkup();
    syncExecutionDetailPanel();
  }
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

function fmtMaterialKpiQty(value) {
  if (value == null || Number.isNaN(Number(value))) return '—';
  return `${num(value).toFixed(1)} MT`;
}

function materialDetailAvailable(campaign) {
  return Boolean(
    (campaign?.materials && campaign.materials.length) ||
    (campaign?.detail_rows && campaign.detail_rows.length) ||
    (campaign?.plants && campaign.plants.length)
  );
}

function materialRiskKey(campaign) {
  const shortQty = num(campaign?.shortage_qty);
  const convertQty = num(campaign?.make_convert_qty);
  const status = String(campaign?.material_status || campaign?.release_status || '').toUpperCase();
  if (shortQty > 0) return 'short';
  if (status.includes('HOLD')) return 'held';
  if (convertQty > 0 || status.includes('PARTIAL') || status.includes('LOW') || status.includes('CONVERT')) return 'convert';
  return 'ready';
}

function materialRiskRank(campaign) {
  const ranks = { short: 0, held: 1, convert: 2, ready: 3 };
  return ranks[materialRiskKey(campaign)] ?? 4;
}

function materialPriorityRank(entity) {
  const priority = upper(entity?.priority || entity?.Priority || '');
  const ranks = { URGENT: 0, HIGH: 1, NORMAL: 2, LOW: 3 };
  return ranks[priority] ?? 4;
}

function materialDueTimestamp(entity) {
  const dueWindow = entity?.due_window;
  const dueEnd = Array.isArray(dueWindow)
    ? (dueWindow[1] || dueWindow[0] || '')
    : (entity?.due_end || entity?.due_date || entity?.Due_Date || '');
  const d = parseDate(dueEnd);
  return Number.isNaN(d.getTime()) ? Number.POSITIVE_INFINITY : d.getTime();
}

function materialDueLabel(entity) {
  const dueWindow = entity?.due_window;
  const dueEnd = Array.isArray(dueWindow)
    ? (dueWindow[1] || dueWindow[0] || '')
    : (entity?.due_end || entity?.due_date || entity?.Due_Date || '');
  const d = parseDate(dueEnd);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short' }).replace(' ', '-');
}

function materialReleaseUrgencyRank(entity) {
  const status = upper(entity?.release_status || entity?.planner_status || '');
  if (status.includes('RELEASED')) return 4;
  const dueTs = materialDueTimestamp(entity);
  if (Number.isFinite(dueTs)) {
    const hrsToDue = (dueTs - Date.now()) / 3600000;
    if (hrsToDue <= 24) return 0;
    if (hrsToDue <= 72) return 1;
    if (hrsToDue <= 168) return 2;
  }
  return 3;
}

function materialVerdictConfig(campaign, entityLabel = 'campaign') {
  const riskKey = materialRiskKey(campaign);
  const entity = String(entityLabel || 'campaign').toLowerCase();
  if (riskKey === 'short') {
    return {
      tone: 'danger',
      label: 'Release Blocked',
      copy: `Critical shortages remain. Resolve short items before this ${entity} can release.`
    };
  }
  if (riskKey === 'held') {
    return {
      tone: 'warn',
      label: 'Manual Hold',
      copy: `Material is not the only issue here. Clear the hold reason and confirm ${entity} readiness before release.`
    };
  }
  if (riskKey === 'convert') {
    return {
      tone: 'info',
      label: 'Convert Before Release',
      copy: `Inventory is close, but make/convert work must complete before this ${entity} is truly ready.`
    };
  }
  return {
    tone: 'success',
    label: 'Ready To Release',
    copy: `Coverage is in place. Final checks are operational, not material-blocking for this ${entity}.`
  };
}

function materialRecommendedActions(campaign, plants = [], entityLabel = 'campaign') {
  const riskKey = materialRiskKey(campaign);
  const shortQty = num(campaign?.shortage_qty);
  const convertQty = num(campaign?.make_convert_qty);
  const entity = String(entityLabel || 'campaign').toLowerCase();
  const highestRiskPlant = [...plants]
    .sort((a, b) => num(b.shortage_qty) - num(a.shortage_qty) || num(b.make_convert_qty) - num(a.make_convert_qty))[0];

  if (riskKey === 'short') {
    return [
      {
        title: 'Expedite short material',
        meta: `${shortQty.toFixed(1)} MT still uncovered${highestRiskPlant ? ` · Focus ${highestRiskPlant.plant}` : ''}`,
        icon: 'bolt',
        tone: 'danger'
      },
      {
        title: 'Check substitute route',
        meta: 'Review alternate SKU or plant source before changing release date',
        icon: 'right-left',
        tone: 'warn'
      },
      {
        title: `Keep ${entity} on hold`,
        meta: `Do not release this ${entity} until short items have a confirmed recovery path`,
        icon: 'hand',
        tone: 'neutral'
      }
    ];
  }

  if (riskKey === 'held') {
    return [
      {
        title: 'Resolve hold reason',
        meta: `Manual review is blocking ${entity} release more than material availability`,
        icon: 'circle-exclamation',
        tone: 'warn'
      },
      {
        title: 'Reconfirm material reservation',
        meta: 'Preserve covered stock while the hold is being cleared',
        icon: 'boxes-stacked',
        tone: 'info'
      }
    ];
  }

  if (riskKey === 'convert') {
    return [
      {
        title: 'Launch make / convert task',
        meta: `${convertQty.toFixed(1)} MT depends on conversion`,
        icon: 'industry',
        tone: 'info'
      },
      {
        title: 'Stage downstream release',
        meta: `Queue ${entity} release once convert confirmation is received`,
        icon: 'forward',
        tone: 'success'
      }
    ];
  }

  return [
    {
      title: 'Reserve covered inventory',
      meta: 'Lock the available stock to avoid cross-campaign contention',
      icon: 'boxes-stacked',
      tone: 'success'
    },
    {
      title: `Release ${entity}`,
      meta: `Material position is clean; proceed with ${entity} execution sequencing`,
      icon: 'play',
      tone: 'success'
    }
  ];
}

function renderMaterial(){
  // state.material is {summary:{}, campaigns:[...]} — not an array
  const matPlan = (state.material && typeof state.material === 'object') ? state.material : {};
  const activeDetailLevel = normalizeMaterialMode(matPlan.detail_level || state.materialMode);
  state.materialMode = activeDetailLevel;
  syncMaterialModeControl();
  let camps = matPlan.campaigns || [];

  // Fallback: derive from state.campaigns if material plan not populated
  if(!camps.length && activeDetailLevel === 'campaign'){
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
    const scopeLabel = materialEntityLabel(activeDetailLevel, { plural: true });
    const scopeNoun = materialEntityLabel(activeDetailLevel, { plural: false }).toLowerCase();
    qs('materialDetailContent').innerHTML = `<div class="material-detail-empty">No material status data available for ${scopeLabel.toLowerCase()} yet. Refresh material status after running planning or schedule to populate ${scopeNoun}-level readiness.</div>`;
    qs('materialTree').innerHTML = '';
    setText('materialTreeHeader', `${scopeLabel} Material Risk`);
    return;
  }

  setText('materialTreeHeader', `${materialEntityLabel(activeDetailLevel, { plural: true })} Material Risk`);
  const riskTagged = camps.map(c => ({ ...c, _riskKey: materialRiskKey(c) }));

  buildMaterialTree(riskTagged);
}

function buildMaterialTree(camps){
  state.materialCampaigns = [...camps].sort((a,b)=>
    materialRiskRank(a) - materialRiskRank(b) ||
    num(b.shortage_qty) - num(a.shortage_qty) ||
    (materialDueTimestamp(a) - materialDueTimestamp(b)) ||
    (materialPriorityRank(a) - materialPriorityRank(b)) ||
    (materialReleaseUrgencyRank(a) - materialReleaseUrgencyRank(b)) ||
    num(b.make_convert_qty) - num(a.make_convert_qty) ||
    num(b.required_qty) - num(a.required_qty)
  );
  const tree = qs('materialTree');
  const riskGroups = [
    { key: 'short', label: 'Short', icon: '!', color: 'var(--danger)' },
    { key: 'held', label: 'Held', icon: 'H', color: 'var(--warning)' },
    { key: 'convert', label: 'Needs Convert', icon: '~', color: 'var(--warning)' },
    { key: 'ready', label: 'Ready', icon: '✓', color: 'var(--success)' }
  ];

  const indexed = state.materialCampaigns.map((camp, idx) => ({ camp, idx }));
  const grouped = riskGroups.map(group => ({
    ...group,
    items: indexed.filter(item => materialRiskKey(item.camp) === group.key)
  })).filter(group => group.items.length > 0);

  tree.innerHTML = grouped.map(group => {
    const rows = group.items.map(({ camp, idx }) => {
      const shortQty = num(camp.shortage_qty);
      const convertQty = num(camp.make_convert_qty);
      const label = group.key === 'short'
        ? `${shortQty.toFixed(1)} MT short`
        : (group.key === 'held'
          ? 'On hold'
          : (group.key === 'convert' ? `${convertQty.toFixed(1)} MT convert` : 'Ready'));
      const cid = materialEntityId(camp);
      const qty = num(camp.required_qty).toFixed(0);
      const due = materialDueLabel(camp);
      const priority = upper(camp.priority || camp.Priority || '');
      const metaParts = [
        camp.grade || '—',
        `${qty} MT`,
        label,
        priority ? `P:${priority}` : '',
        due ? `Due ${due}` : ''
      ].filter(Boolean);
      return '<div class="tree-item">'+
        '<div class="tree-node campaign" onclick="selectMaterialCampaign(this,'+idx+')" data-campaign="'+escapeHtml(cid)+'">'+
          '<div class="tree-toggle leaf"></div>'+
          '<div class="tree-node-icon" style="color:'+group.color+'">'+group.icon+'</div>'+
          '<div class="material-tree-copy">'+
            '<div class="material-tree-title">'+escapeHtml(cid)+'</div>'+
            '<div class="material-tree-meta">'+escapeHtml(metaParts.join(' · '))+'</div>'+
          '</div>'+
        '</div>'+
      '</div>';
    }).join('');

    return `<div class="material-risk-group">
      <div class="material-risk-head">
        <span class="material-risk-title">${escapeHtml(group.label)}</span>
        <span class="material-risk-count">${group.items.length}</span>
      </div>
      ${rows}
    </div>`;
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
  const cid       = materialEntityId(campaign) || 'Unknown';
  const grade     = campaign.grade || '—';
  const reqQty    = num(campaign.required_qty);
  const shortQty  = num(campaign.shortage_qty);
  const coveredQty= num(campaign.inventory_covered_qty);
  const convertQty= num(campaign.make_convert_qty);
  const matStatus = campaign.material_status || '';
  const releaseStatus = campaign.release_status || 'OPEN';
  const materials = [...(campaign.materials || campaign.detail_rows || [])].sort((a,b)=>
    num(b.shortage_qty || b.required_qty || b.gross_required_qty) - num(a.shortage_qty || a.required_qty || a.gross_required_qty)
  );
  const plants = (campaign.plants && campaign.plants.length)
    ? campaign.plants
    : Object.values(materials.reduce((acc, row) => {
        const plant = row.plant || 'Unassigned';
        if (!acc[plant]) {
          acc[plant] = {
            plant,
            required_qty: 0,
            inventory_covered_qty: 0,
            make_convert_qty: 0,
            shortage_qty: 0,
            rows: []
          };
        }
        acc[plant].required_qty += num(row.required_qty || row.gross_required_qty);
        acc[plant].inventory_covered_qty += num(row.inventory_covered_qty);
        acc[plant].make_convert_qty += num(row.make_convert_qty);
        acc[plant].shortage_qty += num(row.shortage_qty);
        acc[plant].rows.push(row);
        return acc;
      }, {}));
  const coveragePct = reqQty > 0 ? Math.max(0, Math.min(100, ((reqQty - shortQty) / reqQty) * 100)) : 100;
  const plantCards = plants.sort((a,b)=>num(b.shortage_qty || 0) - num(a.shortage_qty || 0) || num(b.required_qty || 0) - num(a.required_qty || 0));
  const hasDetail = materialDetailAvailable(campaign);

  // Shortages can be in campaign.shortages (array) or campaign.material_shortages (object)
  const shortages = campaign.shortages && campaign.shortages.length > 0
    ? campaign.shortages
    : Object.entries(campaign.material_shortages || {}).map(([sku_id, qty])=>({sku_id, qty}));

  const topMaterials = [...materials]
    .sort((a,b)=>num(b.required_qty || b.gross_required_qty) - num(a.required_qty || a.gross_required_qty))
    .slice(0, 6);
  const riskKey = materialRiskKey(campaign);
  const entityLabel = materialEntityLabel(materialDetailLevel(), { plural: false });
  const verdict = materialVerdictConfig(campaign, entityLabel);
  const recommendedActions = materialRecommendedActions(campaign, plantCards, entityLabel);
  const blockerRows = shortages.length
    ? shortages.map(item => ({
        sku: item.sku_id || item.material_sku || '—',
        plant: item.plant || 'Multiple plants',
        qty: num(item.qty || item.Required_Qty || item.required_qty || item.gross_required_qty),
        note: 'Short'
      }))
    : materials
        .filter(row => num(row.shortage_qty) > 0 || num(row.make_convert_qty) > 0)
        .sort((a, b) => num(b.shortage_qty || b.make_convert_qty) - num(a.shortage_qty || a.make_convert_qty))
        .slice(0, 6)
        .map(row => ({
          sku: row.material_sku || row.sku_id || '—',
          plant: row.plant || '—',
          qty: num(row.shortage_qty) || num(row.make_convert_qty),
          note: num(row.shortage_qty) > 0 ? 'Short' : 'Convert'
        }));
  const blockerTable = blockerRows.length
    ? blockerRows.map(row => `
        <div class="material-blocker-row ${row.note === 'Short' ? 'danger' : 'warn'}">
          <div>
            <div class="material-blocker-sku">${escapeHtml(row.sku)}</div>
            <div class="material-blocker-meta">${escapeHtml(row.plant)} · ${escapeHtml(row.note)}</div>
          </div>
          <div class="material-blocker-qty">${row.qty.toFixed(1)} MT</div>
        </div>
      `).join('')
    : `<div class="material-blocker-empty">No immediate blockers. Material risk is currently clear.</div>`;

  const materialRows = materials.length ? materials.map(row => {
    const required = num(row.required_qty || row.gross_required_qty);
    const available = num(row.available_before);
    const covered = num(row.inventory_covered_qty);
    const convert = num(row.make_convert_qty);
    const shortage = num(row.shortage_qty);
    const status = String(row.status || row.type || 'COVERED').toUpperCase();
    const tone = shortage > 0 ? 'short' : convert > 0 ? 'partial' : 'covered';
    return `<tr>
      <td style="font-weight:700">${escapeHtml(row.material_sku || row.sku_id || '')}</td>
      <td>${escapeHtml(row.material_name || row.sku_id || '—')}</td>
      <td>${escapeHtml(row.plant || '—')}</td>
      <td>${escapeHtml(row.material_type || 'Material')}</td>
      <td style="text-align:right">${required.toFixed(1)}</td>
      <td style="text-align:right">${available.toFixed(1)}</td>
      <td style="text-align:right;color:var(--success);font-weight:700">${covered.toFixed(1)}</td>
      <td style="text-align:right;color:${convert > 0 ? 'var(--warning)' : 'var(--text-soft)'};font-weight:700">${convert.toFixed(1)}</td>
      <td style="text-align:right;color:${shortage > 0 ? 'var(--danger)' : 'var(--text-soft)'};font-weight:700">${shortage.toFixed(1)}</td>
      <td><span class="bom-item-badge ${tone}">${escapeHtml(status)}</span></td>
    </tr>`;
  }).join('') : `<tr><td colspan="10">${hasDetail ? 'No material rows available for this campaign.' : 'Detailed material line netting is not loaded for this campaign yet.'}</td></tr>`;

  let html = `<div class="material-detail-shell">
    <div class="material-decision-grid">
      <div class="material-decision-card material-decision-card--${verdict.tone}">
        <div class="material-detail-hero">
          <div class="material-detail-hero-copy">
            <div class="material-detail-title">${escapeHtml(cid)}</div>
            <div class="material-detail-subtitle">${escapeHtml(grade)} · ${escapeHtml(releaseStatus)} · ${escapeHtml(matStatus || 'OK')}</div>
            <div class="material-detail-pills">
              <span class="material-detail-pill">${materials.length} material SKU(s)</span>
              <span class="material-detail-pill">${plantCards.length} plant bucket(s)</span>
              <span class="material-detail-pill ${shortQty > 0 ? 'danger' : 'success'}">${shortQty > 0 ? `${shortQty.toFixed(1)} MT short` : 'Fully covered'}</span>
            </div>
          </div>
          <div class="material-coverage-gauge">
            <div class="material-coverage-value">${coveragePct.toFixed(0)}%</div>
            <div class="material-coverage-label">coverage</div>
          </div>
        </div>
        <div class="material-verdict-row">
          <div>
            <div class="material-verdict-kicker">Release Verdict</div>
            <div class="material-verdict-title">${escapeHtml(verdict.label)}</div>
            <div class="material-verdict-copy">${escapeHtml(verdict.copy)}</div>
          </div>
          <div class="material-summary-grid material-summary-grid--detail">
            <div class="material-detail-stat tone-success">
              <div class="material-detail-stat-label">Required</div>
              <div class="material-detail-stat-value">${reqQty.toFixed(1)} MT</div>
            </div>
            <div class="material-detail-stat tone-info">
              <div class="material-detail-stat-label">Stock Covered</div>
              <div class="material-detail-stat-value">${coveredQty.toFixed(1)} MT</div>
            </div>
            <div class="material-detail-stat tone-warn">
              <div class="material-detail-stat-label">Make / Convert</div>
              <div class="material-detail-stat-value">${convertQty.toFixed(1)} MT</div>
            </div>
            <div class="material-detail-stat ${shortQty > 0 ? 'tone-danger' : 'tone-success'}">
              <div class="material-detail-stat-label">Short</div>
              <div class="material-detail-stat-value">${shortQty.toFixed(1)} MT</div>
            </div>
            <div class="material-detail-stat">
              <div class="material-detail-stat-label">Status</div>
              <div class="material-detail-stat-value material-detail-stat-text">${escapeHtml(matStatus || 'OK')}</div>
            </div>
          </div>
        </div>
      </div>
      <div class="material-decision-card material-decision-card--action">
        <div class="material-section-head">
          <div>
            <div class="material-section-title">Next Actions</div>
            <div class="material-section-sub">The fastest moves to unblock or safely release this campaign.</div>
          </div>
        </div>
        <div class="material-recommendation-list">
          ${recommendedActions.map(action => `
            <div class="material-recommendation-row tone-${escapeHtml(action.tone)}">
              <div class="material-recommendation-icon"><i class="fa-solid fa-${escapeHtml(action.icon)}" aria-hidden="true"></i></div>
              <div>
                <div class="material-recommendation-title">${escapeHtml(action.title)}</div>
                <div class="material-recommendation-meta">${escapeHtml(action.meta)}</div>
              </div>
            </div>
          `).join('')}
        </div>
      </div>
    </div>
    <div class="material-detail-layout">
      <div class="material-detail-main">
        <div class="material-section">
          <div class="material-section-head">
            <div>
              <div class="material-section-title">Release Blockers</div>
              <div class="material-section-sub">${blockerRows.length ? 'Resolve these first before worrying about the full material table.' : 'Nothing critical is blocking release from a material point of view.'}</div>
            </div>
          </div>
          <div class="material-blocker-list">${blockerTable}</div>
        </div>
        <div class="material-section">
          <div class="material-section-head">
            <div>
              <div class="material-section-title">Supporting Material Lines</div>
              <div class="material-section-sub">Required, stock draw, conversion, and shortage by SKU.</div>
            </div>
          </div>
          <div class="material-table-wrap">
            <table class="table material-detail-table">
              <thead>
                <tr><th>SKU</th><th>Name</th><th>Plant</th><th>Type</th><th style="text-align:right">Req MT</th><th style="text-align:right">Avail</th><th style="text-align:right">Stock</th><th style="text-align:right">Convert</th><th style="text-align:right">Short</th><th>Status</th></tr>
              </thead>
              <tbody>${materialRows}</tbody>
            </table>
          </div>
        </div>
      </div>
      <div class="material-detail-side">
        <div class="material-section">
          <div class="material-section-head">
            <div class="material-section-title">Plant Coverage</div>
            <div class="material-section-sub">How the load splits across plant buckets</div>
          </div>
          <div class="material-plant-list">${plantCards.length ? plantCards.map(plant => {
            const required = num(plant.required_qty);
            const short = num(plant.shortage_qty);
            const covered = num(plant.inventory_covered_qty);
            const convert = num(plant.make_convert_qty);
            const pct = required > 0 ? Math.max(0, Math.min(100, ((required - short) / required) * 100)) : 100;
            return `<div class="material-plant-card ${short > 0 ? 'risk' : 'good'}">
              <div class="material-plant-card-top">
                <div>
                  <div class="material-plant-name">${escapeHtml(plant.plant || 'Unassigned')}</div>
                  <div class="material-plant-meta">${required.toFixed(1)} MT required</div>
                </div>
                <div class="material-plant-pct">${pct.toFixed(0)}%</div>
              </div>
              <div class="dash-cap-bar-bg"><div class="dash-cap-bar" style="width:${pct}%;background:${short > 0 ? 'var(--danger)' : 'var(--success)'}"></div></div>
              <div class="material-plant-breakdown">
                <span>${covered.toFixed(1)} stock</span>
                <span>${convert.toFixed(1)} convert</span>
                <span>${short.toFixed(1)} short</span>
              </div>
            </div>`;
          }).join('') : `<div class="material-side-empty">${hasDetail ? 'No plant buckets for this campaign.' : 'Plant coverage becomes available when detailed material rows are loaded.'}</div>`}</div>
        </div>
        <div class="material-section">
          <div class="material-section-head">
            <div class="material-section-title">${shortages.length ? 'Shortage Focus' : 'Top Material Draw'}</div>
            <div class="material-section-sub">${shortages.length ? 'Immediate gaps to resolve before release' : 'Largest requirements driving this campaign'}</div>
          </div>
          <div class="material-side-list">${(shortages.length ? shortages : topMaterials).map(item => {
            const sku = item.sku_id || item.material_sku || '';
            const qty = num(item.qty || item.Required_Qty || item.required_qty || item.gross_required_qty);
            const meta = shortages.length
              ? `${qty.toFixed(1)} MT short`
              : `${qty.toFixed(1)} MT required · ${escapeHtml(item.plant || '—')}`;
            return `<div class="material-side-row ${shortages.length ? 'risk' : ''}">
              <div>
                <div class="material-side-name">${escapeHtml(sku)}</div>
                <div class="material-side-meta">${meta}</div>
              </div>
              <div class="material-side-qty">${qty.toFixed(1)}</div>
            </div>`;
          }).join('') || `<div class="material-side-empty">${hasDetail ? 'No material actions needed.' : 'Top material draw becomes available when detailed material rows are loaded.'}</div>`}</div>
        </div>
      </div>
    </div>
  </div>`;

  qs('materialDetailContent').innerHTML = html;
}

function setCapacityDiagnosticCard(slot, card) {
  const cfg = card || {};
  const tone = cfg.tone || '';
  setText(`capacityDiagLabel${slot}`, cfg.label ?? '—');
  setText(`capacityDiagValue${slot}`, cfg.value ?? '—');
  setText(`capacityDiagSub${slot}`, cfg.sub ?? '—');
  const cardEl = qs(`capacityDiagCard${slot}`);
  if (cardEl) {
    cardEl.className = ['kpi-card', 'kpi-card--sub', 'metric', tone].filter(Boolean).join(' ');
  }
}

function renderCapacityDiagnostics(rows = []) {
  const safeRows = Array.isArray(rows) ? rows : [];
  const overloaded = safeRows.filter(r => num(r['Utilisation_%'] || r.utilisation) > 100);
  const high = safeRows.filter(r => {
    const util = num(r['Utilisation_%'] || r.utilisation);
    return util > 85 && util <= 100;
  });
  const healthy = safeRows.length - overloaded.length - high.length;
  const peak = safeRows[0] || null;

  const totalDemand = safeRows.reduce((sum, r) => sum + num(r.Demand_Hrs || r.demand_hrs), 0);
  const totalProcess = safeRows.reduce((sum, r) => sum + num(r.Process_Hrs || r.process_hrs), 0);
  const totalSetup = safeRows.reduce((sum, r) => sum + num(r.Setup_Hrs || r.setup_hrs), 0);
  const totalChangeover = safeRows.reduce((sum, r) => sum + num(r.Changeover_Hrs || r.changeover_hrs), 0);
  const totalTasks = safeRows.reduce((sum, r) => sum + num(r.Task_Count || r.task_count), 0);
  const totalAvail = safeRows.reduce((sum, r) => sum + num(r.Avail_Hrs_14d || r.avail_hrs), 0);
  const headroom = totalAvail - totalDemand;
  const headroomTone = headroom < 0 ? 'danger' : headroom < 40 ? 'warn' : 'success';
  const mixSub = totalDemand > 0
    ? `P ${((totalProcess / totalDemand) * 100).toFixed(0)}% · S ${((totalSetup / totalDemand) * 100).toFixed(0)}% · C ${((totalChangeover / totalDemand) * 100).toFixed(0)}%`
    : 'No scheduled load mix yet';

  setCapacityDiagnosticCard(1, {
    label: 'Peak Resource',
    value: peak ? String(peak.Resource_ID || peak.resource_id || '—') : '—',
    sub: peak ? `${Math.round(num(peak['Utilisation_%'] || peak.utilisation))}% utilisation` : 'No capacity rows',
    tone: peak ? (num(peak['Utilisation_%'] || peak.utilisation) > 100 ? 'danger' : num(peak['Utilisation_%'] || peak.utilisation) > 85 ? 'warn' : 'success') : ''
  });
  setCapacityDiagnosticCard(2, {
    label: 'Overloaded',
    value: String(overloaded.length),
    sub: overloaded.length ? 'Need relief or resequencing' : 'No hard overload',
    tone: overloaded.length ? 'danger' : 'success'
  });
  setCapacityDiagnosticCard(3, {
    label: 'Load Mix',
    value: `${fmtHours(totalProcess, 0)} / ${fmtHours(totalSetup, 0)} / ${fmtHours(totalChangeover, 0)}`,
    sub: mixSub,
    tone: totalChangeover > totalProcess * 0.25 ? 'warn' : (high.length ? 'warn' : 'success')
  });
  setCapacityDiagnosticCard(4, {
    label: 'Headroom / Tasks',
    value: `${headroom >= 0 ? '+' : ''}${headroom.toFixed(1)}h`,
    sub: `${totalTasks} tasks · ${Math.max(healthy, 0)} healthy resources`,
    tone: headroomTone
  });

  const narrativeEl = qs('capacityNarrative');
  if (narrativeEl) {
    const bottleneck = peak ? `${peak.Resource_ID || peak.resource_id || 'peak resource'} at ${Math.round(num(peak['Utilisation_%'] || peak.utilisation))}%` : 'No bottleneck';
    const pressure = overloaded.length
      ? `${overloaded.length} overloaded resource(s) need relief`
      : high.length
        ? `${high.length} high-utilisation resource(s); watch sequencing`
        : 'No overload pressure detected';
    narrativeEl.textContent = `${bottleneck}. ${pressure}. Demand ${totalDemand.toFixed(1)}h vs available ${totalAvail.toFixed(1)}h.`;
    narrativeEl.className = `notice ${overloaded.length ? 'danger' : high.length ? 'warn' : 'success'}`;
  }
}

function renderCapacityExecutionContext(rows = []) {
  const metaEl = qs('capacityExecutionMeta');
  const chipsEl = qs('capacityExecutionChips');
  if (!metaEl || !chipsEl) return;

  const safeRows = Array.isArray(rows) ? rows : [];
  if (!safeRows.length) {
    metaEl.textContent = 'No capacity map available. Run Feasibility Check to load resource pressure.';
    chipsEl.innerHTML = '<div class="empty-state">Capacity context appears after schedule/capacity rows are generated.</div>';
    return;
  }

  const selectedCampaign = String(state.selectedCampaign || '').trim();
  const scheduleRows = Array.isArray(state.lastScheduleRows) && state.lastScheduleRows.length
    ? state.lastScheduleRows
    : (state.gantt || []);
  const selectedOps = selectedCampaign
    ? scheduleRows.filter((row) => String(row.Campaign || row.campaign_id || '').trim() === selectedCampaign)
    : [];
  const selectedResources = [...new Set(selectedOps.map((row) => String(row.Resource_ID || row.resource_id || '').trim()).filter(Boolean))];

  if (selectedCampaign && selectedResources.length) {
    metaEl.textContent = `${selectedCampaign}: ${selectedResources.length} execution resources mapped to current capacity load.`;
    const chips = selectedResources.map((rid) => {
      const capRow = safeRows.find((row) => String(row.Resource_ID || row.resource_id || '').trim() === rid);
      if (!capRow) {
        return `<div class="capacity-execution-chip"><span class="badge status-token status-neutral">${escapeHtml(rid)}</span><span class="capacity-execution-chip-sub">not in capacity map</span></div>`;
      }
      const util = Math.round(num(capRow['Utilisation_%'] || capRow.utilisation));
      const tone = util > 100 ? 'danger' : util > 85 ? 'warn' : 'success';
      return `<div class="capacity-execution-chip"><span class="badge status-token ${toneStatusClasses(tone)}">${escapeHtml(rid)} ${util}%</span><span class="capacity-execution-chip-sub">${escapeHtml(capRow.Operation_Group || capRow.Operation || _ridToOp(capRow.Resource_ID || capRow.resource_id))}</span></div>`;
    });
    chipsEl.innerHTML = chips.join('');
    return;
  }

  if (selectedCampaign) {
    metaEl.textContent = `${selectedCampaign} is selected in Execution, but no scheduled operations are visible for it in the current timeline scope.`;
  } else {
    metaEl.textContent = 'No campaign pinned from Execution. Capacity is showing top bottlenecks across the active plan.';
  }

  const bottlenecks = safeRows.slice(0, 4).map((row) => {
    const util = Math.round(num(row['Utilisation_%'] || row.utilisation));
    const tone = util > 100 ? 'danger' : util > 85 ? 'warn' : 'success';
    const rid = String(row.Resource_ID || row.resource_id || '—');
    const op = String(row.Operation_Group || row.Operation || _ridToOp(row.Resource_ID || row.resource_id) || 'Operation');
    return `<div class="capacity-execution-chip"><span class="badge status-token ${toneStatusClasses(tone)}">${escapeHtml(rid)} ${util}%</span><span class="capacity-execution-chip-sub">${escapeHtml(op)}</span></div>`;
  });
  chipsEl.innerHTML = bottlenecks.join('');
}

function renderCapacity(){
  const rows = [...(state.capacity||[])].sort((a,b)=>num(b['Utilisation_%'] || b.utilisation) - num(a['Utilisation_%'] || a.utilisation));
  const body = qs('capacityBody');
  renderCapacityBars();
  // KPI rendering removed: only top summary strip shown per design rule
  // renderCapacityDiagnostics(rows);
  renderCapacityExecutionContext(rows);
  if(!rows.length){ body.innerHTML = '<tr><td colspan="11">No capacity rows loaded. Run Feasibility Check to generate the map.</td></tr>'; return; }
  body.innerHTML = rows.map(r=>{
    const util = Math.round(num(r['Utilisation_%'] || r.utilisation));
    const demand = num(r.Demand_Hrs || r.demand_hrs);
    const process = num(r.Process_Hrs || r.process_hrs);
    const setup = num(r.Setup_Hrs || r.setup_hrs);
    const changeover = num(r.Changeover_Hrs || r.changeover_hrs);
    const tasks = num(r.Task_Count || r.task_count);
    const avail = num(r.Avail_Hrs_14d || r.avail_hrs);
    const processPct = demand > 0 ? (process / demand) * 100 : 0;
    const setupPct = demand > 0 ? (setup / demand) * 100 : 0;
    const changeoverPct = demand > 0 ? (changeover / demand) * 100 : 0;
    return '<tr>'+
      '<td><strong>'+escapeHtml(r.Resource_ID || r.resource_id || '—')+'</strong></td>'+
      '<td>'+escapeHtml(r.Operation_Group || r.Operation || _ridToOp(r.Resource_ID || r.resource_id))+'</td>'+
      '<td>'+escapeHtml(demand.toFixed(1))+'h</td>'+
      '<td>'+escapeHtml(process.toFixed(1))+'h</td>'+
      '<td>'+escapeHtml(setup.toFixed(1))+'h</td>'+
      '<td>'+escapeHtml(changeover.toFixed(1))+'h</td>'+
      '<td>'+escapeHtml(String(tasks))+'</td>'+
      '<td>'+escapeHtml(avail.toFixed(1))+'h</td>'+
      '<td>'+util+'%</td>'+
      '<td>'+badgeForStatus(r.Status || (util > 100 ? 'Overloaded' : util > 85 ? 'High' : 'OK'))+'</td>'+
      '<td><details class="capacity-burden-details"><summary>Split</summary><div class="capacity-burden-line">Process <strong>'+process.toFixed(1)+'h</strong> · '+processPct.toFixed(0)+'%</div><div class="capacity-burden-line">Setup <strong>'+setup.toFixed(1)+'h</strong> · '+setupPct.toFixed(0)+'%</div><div class="capacity-burden-line">Changeover <strong>'+changeover.toFixed(1)+'h</strong> · '+changeoverPct.toFixed(0)+'%</div><div class="capacity-burden-line">Tasks <strong>'+String(tasks)+'</strong></div></details></td>'+
    '</tr>';
  }).join('');
}

function normalizeScenarioMetricRow(row) {
  const metrics = row || {};
  const scenario = String(
    metrics.scenario
    || metrics.Scenario
    || metrics.name
    || metrics.Name
    || ''
  ).trim();
  const campaigns = num(metrics.campaigns || metrics.Campaigns || 0);
  const released = num(metrics.released_campaigns || metrics.Released_Campaigns || metrics.released || 0);
  const held = num(
    metrics.held_campaigns
    || metrics.Held_Campaigns
    || Math.max(campaigns - released, 0)
  );
  const onTimePct = num(metrics.on_time_pct || metrics.On_Time_Pct || metrics.on_time, NaN);
  const totalHeats = num(metrics.total_heats || metrics.Total_Heats || metrics.heats || 0);
  const throughputMtDay = num(metrics.throughput_mt_day || metrics.Throughput_MT_Day, NaN);
  const fallbackPlannedMt = num(metrics.planned_mt || metrics.total_mt || metrics.Total_MT, NaN);
  const horizonDays = Math.max(num(state.simConfig?.horizon_days || state.horizon || 14, 14), 1);
  const plannedMt = Number.isFinite(throughputMtDay) ? throughputMtDay * horizonDays : fallbackPlannedMt;
  let utilisationMap = metrics.utilisation || metrics.Utilisation || metrics.utilization || {};
  if (typeof utilisationMap === 'string') {
    try { utilisationMap = JSON.parse(utilisationMap); } catch { utilisationMap = {}; }
  }
  const bottleneck = String(metrics.bottleneck || metrics.Bottleneck || '—').trim() || '—';
  const bottleneckLoad = Number.isFinite(num(utilisationMap?.[bottleneck], NaN))
    ? num(utilisationMap[bottleneck], NaN)
    : num(metrics.bottleneck_load_pct || metrics.Bottleneck_Load_Pct, NaN);
  const releaseReadyPct = campaigns > 0 ? (released / campaigns) * 100 : NaN;
  return {
    scenario,
    plannedMt,
    totalHeats,
    onTimePct,
    heldCampaigns: held,
    bottleneck,
    bottleneckLoad,
    releaseReadyPct,
    campaigns,
    released
  };
}

function scenarioDeltaCell(value, baselineValue, suffix = '', digits = 1) {
  const curr = num(value, NaN);
  if (!Number.isFinite(curr)) return '—';
  const baseline = num(baselineValue, NaN);
  if (!Number.isFinite(baseline)) return `${curr.toFixed(digits)}${suffix}`;
  const delta = curr - baseline;
  const toneClass = delta > 0 ? 'delta-up' : delta < 0 ? 'delta-down' : 'delta-flat';
  return `<div class="scenario-value-cell"><span class="scenario-value-main">${curr.toFixed(digits)}${suffix}</span><span class="scenario-value-delta ${toneClass}">${signedDelta(delta, digits, suffix)}</span></div>`;
}

function renderScenarios(){
  const items = state.scenarios || [];
  const metricRows = (state.scenarioOutput || []).map(normalizeScenarioMetricRow).filter(row => row.scenario);
  const baseline = metricRows.find(row => upper(row.scenario) === 'BASELINE') || metricRows[0] || null;
  const cardBox = qs('scenarioCards');
  const baselineNote = qs('scenarioBaselineNote');
  const deltaBody = qs('scenarioDeltaBody');
  const tableBody = qs('scenarioTableBody');
  if (baselineNote) {
    baselineNote.textContent = baseline
      ? `Baseline = ${baseline.scenario}. Deltas shown against this row.`
      : 'No scenario metrics available yet. Run scenario experiments to compare deltas.';
  }

  if (metricRows.length) {
    const top = metricRows.slice(0, 3);
    cardBox.innerHTML = top.map((row) => `
      <div class="panel card">
        <div class="panel-body card-body">
          <div class="metric-label">Scenario</div>
          <div class="metric-value">${escapeHtml(row.scenario)}</div>
          <div class="metric-sub">
            ${Number.isFinite(row.onTimePct) ? `${row.onTimePct.toFixed(1)}% on-time` : 'On-time N/A'}
            · ${Number.isFinite(row.plannedMt) ? `${row.plannedMt.toFixed(0)} MT planned` : 'MT N/A'}
          </div>
          <div style="margin-top:.75rem;display:flex;gap:.45rem;flex-wrap:wrap">
            <button class="btn primary" onclick="applyScenarioByName('${escapeAttr(row.scenario)}')">Apply</button>
          </div>
        </div>
      </div>
    `).join('');
  } else {
    const top = items.slice(0,3);
    cardBox.innerHTML = top.map((r, idx)=>{
      const key = Object.keys(r)[0];
      const value = r[key];
      return '<div class="panel card"><div class="panel-body card-body">'+
        '<div class="metric-label">Scenario</div><div class="metric-value">'+escapeHtml(key || ('Scenario ' + (idx+1)))+'</div>'+
        '<div class="metric-sub">'+escapeHtml(value || 'Workbook-backed scenario row')+'</div>'+
        '<div style="margin-top:.75rem;display:flex;gap:.45rem;flex-wrap:wrap"><button class="btn primary" onclick="applyScenarioByName(\''+escapeHtml(key || '')+'\')">Apply</button><button class="btn" onclick="editScenario(\''+escapeHtml(key || '')+'\')">Edit</button></div>'+
      '</div></div>';
    }).join('') || '<div class="notice">No scenario rows available.</div>';
  }

  if (deltaBody) {
    if (!metricRows.length) {
      deltaBody.innerHTML = '<tr><td colspan="7">No scenario output metrics available yet.</td></tr>';
    } else {
      const base = baseline || metricRows[0];
      deltaBody.innerHTML = metricRows.map((row) => `
        <tr>
          <td><strong>${escapeHtml(row.scenario)}</strong></td>
          <td>${scenarioDeltaCell(row.plannedMt, base?.plannedMt, ' MT', 0)}</td>
          <td>${scenarioDeltaCell(row.totalHeats, base?.totalHeats, '', 0)}</td>
          <td>${scenarioDeltaCell(row.onTimePct, base?.onTimePct, '%', 1)}</td>
          <td>${scenarioDeltaCell(row.heldCampaigns, base?.heldCampaigns, '', 0)}</td>
          <td>${escapeHtml(row.bottleneck || '—')}${Number.isFinite(row.bottleneckLoad) ? `<div class="scenario-inline-note">${row.bottleneckLoad.toFixed(1)}%</div>` : ''}</td>
          <td>${scenarioDeltaCell(row.releaseReadyPct, base?.releaseReadyPct, '%', 1)}</td>
        </tr>
      `).join('');
    }
  }

  tableBody.innerHTML = items.map(r=>{
    const key = Object.keys(r)[0] || '—';
    const value = r[key];
    return '<tr><td><strong>'+escapeHtml(key)+'</strong></td><td>'+escapeHtml(value)+'</td><td><button class="btn" onclick="applyScenarioByName(\''+escapeHtml(key)+'\')">Apply</button> <button class="btn" onclick="editScenario(\''+escapeHtml(key)+'\')">Edit</button> <button class="btn danger" onclick="deleteScenario(\''+escapeHtml(key)+'\')">Delete</button></td></tr>';
  }).join('') || '<tr><td colspan="3">No scenarios returned.</td></tr>';
}
function renderCtpResultPanel(result){
  const box = qs('ctpResult');
  if (!box) return;
  if (!result || typeof result !== 'object') {
    box.textContent = 'No request yet.';
    return;
  }
  const feasible = Boolean(result.feasible ?? result.plant_completion_feasible ?? result.exact_requested_date_feasible);
  const decision = ctpDecisionMeta(result.decision_class || result.decision_family);
  const lineage = ctpLineageMeta(result.inventory_lineage_status || result.lineage_status);
  const confidence = upper(result.promise_confidence || '');
  const altCount = Array.isArray(result.alternatives) ? result.alternatives.length : 0;
  const feasibleBadge = `<span class="badge status-token ${toneStatusClasses(feasible ? 'success' : 'danger')}">${feasible ? 'Feasible' : 'Not Feasible'}</span>`;
  const decisionBadge = `<span class="badge status-token ${toneStatusClasses(decision.tone)}">${escapeHtml(decision.label)}</span>`;
  const lineageBadge = `<span class="badge status-token ${toneStatusClasses(lineage.tone)}">${escapeHtml(lineage.label)}</span>`;

  box.innerHTML = `
    <div class="ctp-result-shell">
      <div class="ctp-result-row">
        ${feasibleBadge}
        ${decisionBadge}
        ${lineageBadge}
      </div>
      <div class="ctp-result-copy">Earliest completion: <strong>${escapeHtml(fmtDateTime(result.promised_completion_date || result.earliest_completion || result.earliest_delivery))}</strong></div>
      <div class="ctp-result-copy">Margin/Lateness: <strong>${escapeHtml(String(result.lateness_days ?? '—'))}</strong> day(s)</div>
      <div class="ctp-result-copy">Confidence: <strong>${escapeHtml(confidence || '—')}</strong> · Alternatives: <strong>${escapeHtml(String(altCount))}</strong></div>
      <div class="ctp-result-copy">${escapeHtml(result.narrative || decision.detail)}</div>
    </div>
  `;
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
      earliest: out.Earliest_Delivery || out.earliest_delivery || out.Promised_Completion_Date || out.promised_completion_date || '',
      margin: out.Lateness_Days ?? out.lateness_days ?? '—',
      decisionClass: out.Decision_Class || out.decision_class || out.decision_family || '',
      lineageStatus: out.Inventory_Lineage_Status || out.inventory_lineage_status || '',
      feasible: out.Feasible ?? out.feasible ?? out.Plant_Completion_Feasible ?? out.plant_completion_feasible
    };
  });
  if (state.ctpLastResult && state.ctpLastResult._request) {
    merged.unshift({
      sku: state.ctpLastResult._request.sku_id,
      qty: state.ctpLastResult._request.qty_mt,
      requested: state.ctpLastResult._request.requested_date,
      earliest: state.ctpLastResult.promised_completion_date || state.ctpLastResult.earliest_completion || state.ctpLastResult.earliest_delivery,
      margin: state.ctpLastResult.lateness_days ?? '—',
      decisionClass: state.ctpLastResult.decision_class || state.ctpLastResult.decision_family || '',
      lineageStatus: state.ctpLastResult.inventory_lineage_status || '',
      feasible: state.ctpLastResult.feasible ?? state.ctpLastResult.plant_completion_feasible
    });
  }
  body.innerHTML = merged.slice(0, 12).map(r=>{
    const decision = ctpDecisionMeta(r.decisionClass);
    const lineage = ctpLineageMeta(r.lineageStatus);
    return '<tr>'+
      '<td>'+escapeHtml(r.sku)+'</td>'+
      '<td>'+escapeHtml(r.qty)+'</td>'+
      '<td>'+escapeHtml(fmtDate(r.requested))+'</td>'+
      '<td>'+escapeHtml(fmtDate(r.earliest))+'</td>'+
      '<td>'+escapeHtml(r.margin)+'</td>'+
      '<td><span class="badge status-token '+toneStatusClasses(decision.tone)+'">'+escapeHtml(decision.label)+'</span></td>'+
      '<td><span class="badge status-token '+toneStatusClasses(lineage.tone)+'">'+escapeHtml(lineage.label)+'</span></td>'+
      '<td>'+badgeForStatus(r.feasible === true ? 'FEASIBLE' : r.feasible === false ? 'NOT FEASIBLE' : '—')+'</td>'+
    '</tr>';
  }).join('') || '<tr><td colspan="8">No CTP history yet.</td></tr>';
}

function setMasterAuditCard(slot, cfg){
  const card = cfg || {};
  setText(`masterAuditValue${slot}`, card.value ?? '—');
  setText(`masterAuditSub${slot}`, card.sub ?? '—');
  const cardEl = qs(`masterAuditCard${slot}`);
  if (cardEl) cardEl.className = ['kpi-card', 'kpi-card--sub', 'metric', card.tone || ''].filter(Boolean).join(' ');
}

function renderMasterAudit(){
  const payload = state.masterAudit || {};
  const health = payload.health_summary || {};
  if (!payload || !payload.health_summary) {
    setMasterAuditCard(1, { value: 'Pending', sub: 'Master audit not loaded', tone: 'info' });
    setMasterAuditCard(2, { value: '—', sub: 'No audit yet', tone: '' });
    setMasterAuditCard(3, { value: '—', sub: 'No audit yet', tone: '' });
    setMasterAuditCard(4, { value: '—', sub: 'No audit yet', tone: '' });
    const issuesEl = qs('masterAuditIssues');
    if (issuesEl) {
      issuesEl.textContent = 'Run refresh to load master-data audit checks.';
      issuesEl.className = 'notice master-audit-notice';
    }
    return;
  }
  const checks = Array.isArray(health.checks) ? health.checks : [];
  const critical = num(health.critical_count || 0);
  const warning = num(health.warning_count || 0);
  const ok = num(health.ok_count || 0);
  const conflicts = num(payload.config_duplicates?.conflict_count || 0);

  const healthTone = critical > 0 ? 'danger' : warning > 0 ? 'warn' : 'success';
  const healthLabel = critical > 0 ? 'At Risk' : warning > 0 ? 'Needs Review' : 'Healthy';
  setMasterAuditCard(1, {
    value: healthLabel,
    sub: `${checks.length} checks from workbook + runtime validation`,
    tone: healthTone
  });
  setMasterAuditCard(2, {
    value: String(critical),
    sub: critical > 0 ? 'Immediate fixes required' : 'No critical findings',
    tone: critical > 0 ? 'danger' : 'success'
  });
  setMasterAuditCard(3, {
    value: String(warning),
    sub: warning > 0 ? 'Warnings to review' : `${ok} checks clean`,
    tone: warning > 0 ? 'warn' : 'success'
  });
  setMasterAuditCard(4, {
    value: String(conflicts),
    sub: conflicts > 0 ? 'Algorithm_Config vs Config mismatch' : 'Config sheets aligned',
    tone: conflicts > 0 ? 'warn' : 'success'
  });

  const issuesEl = qs('masterAuditIssues');
  if (!issuesEl) return;
  const notable = checks.filter(check => ['critical', 'warning'].includes(String(check.level || '').toLowerCase()));
  if (!notable.length) {
    issuesEl.textContent = 'Master-data audit looks healthy. No critical or warning findings right now.';
    issuesEl.className = 'notice master-audit-notice success';
    return;
  }
  const top = notable.slice(0, 4).map((check) => {
    const title = check.title || check.code || 'Check';
    const count = Number.isFinite(num(check.count, NaN)) && num(check.count, NaN) > 0 ? ` (${num(check.count)})` : '';
    const detail = check.detail ? `: ${check.detail}` : '';
    return `${title}${count}${detail}`;
  });
  issuesEl.textContent = top.join(' • ');
  issuesEl.className = `notice master-audit-notice ${critical > 0 ? 'danger' : 'warn'}`;
}

function renderMaster(){
  // KPI rendering removed: only top summary strip shown per design rule
  // renderMasterAudit();
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
  // Ensure material mode preference is restored before any data fetches
  restoreMaterialModePreference();
  const [overview, campaigns, releaseQueue, gantt, capacity, material, dispatch, planningOrders] = await Promise.all([
    apiFetch('/api/aps/dashboard/overview').catch(()=>null),
    apiFetch('/api/aps/campaigns/list').catch(()=>({items:[]})),
    apiFetch('/api/aps/campaigns/release-queue').catch(()=>({items:[]})),
    apiFetch('/api/aps/schedule/gantt').catch(()=>({jobs:[]})),
    apiFetch('/api/aps/capacity/map').catch(()=>({items:[]})),
    apiFetch(materialPlanUrl()).catch(()=>({summary:{}, campaigns:[], detail_level: normalizeMaterialMode(state.materialMode)})),
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
    const [scenarios, scenarioOutput, ctpReqs, ctpOut, master, masterAudit] = await Promise.all([
      apiFetch('/api/aps/scenarios/list').catch(()=>({items:[]})),
      apiFetch('/api/aps/scenarios/output').catch(()=>({items:[]})),
      apiFetch('/api/aps/ctp/requests').catch(()=>({items:[]})),
      apiFetch('/api/aps/ctp/output').catch(()=>({items:[]})),
      apiFetch('/api/aps/masterdata').catch(()=>({})),
      apiFetch('/api/masterdata/audit').catch(()=>null)
    ]);

    state.scenarios = scenarios.items || [];
    state.scenarioOutput = scenarioOutput.items || [];
    state.ctpRequests = ctpReqs.items || [];
    state.ctpOutput = ctpOut.items || [];
    state.master = master || {};
    state.masterAudit = masterAudit || null;
    if (!state.ctpLastResult && state.ctpOutput.length) {
      state.ctpLastResult = state.ctpOutput[0];
    }

    renderDashboard();
    renderSchedule();
    renderDispatch();
    renderCapacity();
    renderScenarios();
    renderCtpResultPanel(state.ctpLastResult);
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
        horizon: Number(state.horizon || 14),
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
    state.ctpLastResult = { ...d, _request: payload };
    renderCtpResultPanel(state.ctpLastResult);
    const reqs = await apiFetch('/api/aps/ctp/requests').catch(()=>({items:[]}));
    const outs = await apiFetch('/api/aps/ctp/output').catch(()=>({items:[]}));
    state.ctpRequests = reqs.items || [];
    state.ctpOutput = outs.items || [];
    renderCTPHistory();
  }catch(e){
    state.ctpLastResult = null;
    qs('ctpResult').textContent = 'CTP failed: ' + e.message;
  }
  finally{ btn.innerHTML = old; btn.disabled = false; }
}

function normalizeBomStructureErrors(rawErrors, fallbackMessage = '') {
  const normalized = [];
  const rows = Array.isArray(rawErrors) ? rawErrors : [];
  rows.forEach((err) => {
    if (!err) return;
    if (typeof err === 'string') {
      normalized.push({ type: 'STRUCTURE', path: '', message: err });
      return;
    }
    const type = String(err.type || err.code || 'STRUCTURE').trim() || 'STRUCTURE';
    const path = String(err.path || err.node || '').trim();
    const detail = String(err.message || err.reason || err.details || '').trim();
    normalized.push({ type, path, message: detail });
  });
  if (!normalized.length && fallbackMessage) {
    normalized.push({ type: 'BOM', path: '', message: String(fallbackMessage) });
  }
  return normalized;
}

function renderBomStructureAlert() {
  const errors = normalizeBomStructureErrors(state.bomStructureErrors || []);
  if (!errors.length && state.bomFeasible !== false) return '';
  const title = errors.length
    ? `BOM structure warnings (${errors.length})`
    : 'BOM is not currently feasible';
  const rows = errors.length
    ? `<div class="bom-structure-alert-list">${errors.slice(0, 6).map((err) => `
        <div class="bom-structure-alert-item">
          <span class="bom-structure-alert-type">${escapeHtml(err.type || 'STRUCTURE')}</span>
          <span class="bom-structure-alert-copy">${escapeHtml(
            err.path ? `${err.path}${err.message ? ` — ${err.message}` : ''}` : (err.message || 'Structure issue')
          )}</span>
        </div>
      `).join('')}</div>`
    : `<div class="bom-structure-alert-copy">Run BOM explosion after correcting master data links.</div>`;
  return `
    <div class="bom-structure-alert">
      <div class="bom-structure-alert-title">${escapeHtml(title)}</div>
      ${rows}
    </div>
  `;
}

async function runBom() {
  const btn = qs('bomExplodeBtn');
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
    state.bomStructureErrors = normalizeBomStructureErrors(d.structure_errors || []);
    state.bomFeasible = d.feasible !== false;

    renderBomSummary();
    renderBomGrouped();
    renderTopSummaryForPage(activePageKey());
  } catch (e) {
    state.bomSummary = {};
    state.bomGrouped = [];
    state.bomFlat = [];
    state.bomGross = [];
    state.bomNet = [];
    state.bomStructureErrors = normalizeBomStructureErrors([], e.message || 'BOM explosion failed');
    state.bomFeasible = false;
    const detail = qs('bomDetailContent');
    if (detail) {
      detail.innerHTML = `${renderBomStructureAlert()}<div class="bom-detail-empty">BOM Explosion failed: ${escapeHtml(e.message)}</div>`;
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
  if (activePageKey() === 'bom') {
    renderTopSummaryForPage('bom');
  }
}

async function runBomForPlanningOrders() {
  const selectedPos = state.planningOrders || [];
  if (!selectedPos.length) {
    await runBom();
    return;
  }

  const skuQtyMap = {};
  selectedPos.forEach(po => {
    (po.selected_so_ids || []).forEach(soId => {
      const so = (state.poolOrders || []).find(row => String(row.so_id || '').trim() === String(soId || '').trim());
      const sku = String(so?.sku_id || '').trim();
      const qty = num(so?.qty_mt || 0);
      if (!sku || qty <= 0) return;
      skuQtyMap[sku] = (skuQtyMap[sku] || 0) + qty;
    });
  });

  const items = _planningMaterialItemsFromSkuQtyMap(skuQtyMap);
  if (!items.length) {
    await runBom();
    return;
  }

  try {
    const d = await apiFetch('/api/aps/bom/tree', {
      method: 'POST',
      body: JSON.stringify({ items })
    });

    state.bomGrouped = d.grouped_bom || [];
    state.bomSummary = d.summary || {};
    state.bomFlat = d.net_bom || [];
    state.bomGross = d.gross_bom || [];
    state.bomNet = d.net_bom || [];
    state.bomStructureErrors = normalizeBomStructureErrors(d.structure_errors || []);
    state.bomFeasible = d.feasible !== false;
    renderBomSummary();
    renderBomGrouped();
    renderTopSummaryForPage(activePageKey());
  } catch (e) {
    state.bomStructureErrors = normalizeBomStructureErrors([], e.message || 'Planning BOM explosion failed');
    state.bomFeasible = false;
    const detail = qs('bomDetailContent');
    if (detail) {
      detail.innerHTML = `${renderBomStructureAlert()}<div class="bom-detail-empty">Planning BOM failed: ${escapeHtml(e.message)}</div>`;
    }
    throw e;
  }
}

function bomStatusBadgeClass(status){
  const m = {'COVERED': 'covered', 'PARTIAL SHORT': 'partial', 'SHORT': 'short', 'BYPRODUCT': 'byproduct'};
  return m[status] || 'short';
}

function renderBomGrouped(){
  if(!state.bomGrouped || !state.bomGrouped.length){
    const diagnostics = renderBomStructureAlert();
    qs('bomDetailContent').innerHTML = `${diagnostics}<div class="bom-detail-empty">No BOM data yet. Next: use the top-right Explode action for the active plan.</div>`;
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
    qs('bomDetailContent').innerHTML = '<div class="bom-detail-empty">No stage selected. Next: pick a production stage from the left tree to inspect detail.</div>';
    return;
  }

  const allRows = materialType
    ? (materialType.rows || [])
    : (plantData.material_types || []).flatMap(mt => mt.rows || []);
  const totalGross = materialType ? (materialType.gross_req || 0) : (plantData.gross_req || 0);
  const totalNet = materialType ? (materialType.net_req || 0) : (plantData.net_req || 0);
  const totalProduced = materialType ? (materialType.produced_qty || 0) : (plantData.produced_qty || 0);
  const covered = allRows.filter(r => r.status === 'COVERED').length;
  const short = allRows.filter(r => r.status === 'SHORT').length;
  const partial = allRows.filter(r => r.status === 'PARTIAL SHORT').length;
  const blockedMt = allRows
    .filter(r => r.status === 'SHORT' || r.status === 'PARTIAL SHORT')
    .reduce((sum, r) => sum + num(r.net_req || 0), 0);
  const stageCount = (plantData.material_types || []).length;

  let html = '<div class="bom-detail-shell">';
  html += renderBomStructureAlert();
  html += '<div class="bom-detail-header bom-detail-hero">';
  html += '<div class="bom-detail-hero-copy">';
  html += '<div class="bom-detail-title">'+escapeHtml(plant)+'</div>';
  html += '<div class="bom-detail-subtitle">'+escapeHtml(materialType ? `${materialType.material_type} total-plan material stage` : 'Plant total-plan material overview')+'</div>';
  html += '<div style="font-size: 0.75rem; color: var(--text-soft); margin-top: 0.5rem; line-height: 1.4;">'+
    '<div>'+escapeHtml(
      materialType
        ? `${allRows.length} items · ${covered} covered · ${partial} partial · ${short} short`
        : `${stageCount} stages · ${allRows.length} items · ${covered} covered · ${partial} partial · ${short} short`
    )+'</div>'+
    '<div style="margin-top: 0.25rem; color: var(--text-faint);">BOM reports total-plan requirement; release blockers live in the Material tab.</div>'+
  '</div>';
  html += '<div style="display: flex; gap: 2rem; margin-top: 1rem; flex-wrap: wrap; align-items: baseline;">'+
    '<div><div style="font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.045em; font-weight: 600; color: var(--text-faint); margin-bottom: 0.25rem;">Gross Req</div><div style="font-size: 1.3rem; font-weight: 700; color: var(--text);">'+totalGross.toFixed(1)+'<span style="font-size: 0.85rem; font-weight: 500; margin-left: 0.3rem;">MT</span></div></div>'+
    '<div><div style="font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.045em; font-weight: 600; color: var(--text-faint); margin-bottom: 0.25rem;">Produced</div><div style="font-size: 1.3rem; font-weight: 700; color: var(--text);">'+totalProduced.toFixed(1)+'<span style="font-size: 0.85rem; font-weight: 500; margin-left: 0.3rem;">MT</span></div></div>'+
    '<div><div style="font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.045em; font-weight: 600; color: var(--text-faint); margin-bottom: 0.25rem;">Net Req</div><div style="font-size: 1.3rem; font-weight: 700; color: var(--text);">'+totalNet.toFixed(1)+'<span style="font-size: 0.85rem; font-weight: 500; margin-left: 0.3rem;">MT</span></div></div>'+
    '<div><div style="font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.045em; font-weight: 600; color: var(--text-faint); margin-bottom: 0.25rem;">'+(materialType ? 'Blocked' : 'At Risk')+'</div><div style="font-size: 1.3rem; font-weight: 700; color: var(--text);">'+blockedMt.toFixed(1)+'<span style="font-size: 0.85rem; font-weight: 500; margin-left: 0.3rem;">MT</span></div></div>'+
  '</div>';
  html += '</div></div>';

  if(!materialType) {
    html += '<div class="bom-detail-section">';
    html += '<div class="bom-detail-section-head"><div><div class="bom-detail-section-title">Material stages</div><div class="bom-detail-section-sub">Choose a stage to inspect covered, partial, and short lines with condensed material cards.</div></div></div>';
    html += '<div class="bom-stage-grid">'+
      (plantData.material_types || []).map(mt => {
        const mtRows = mt.rows || [];
        const mtCovered = mtRows.filter(r => r.status === 'COVERED').length;
        const mtShort = mtRows.filter(r => r.status === 'SHORT').length;
        const mtPartial = mtRows.filter(r => r.status === 'PARTIAL SHORT').length;
        return '<button type="button" class="bom-stage-card '+(mtShort ? 'danger' : mtPartial ? 'warn' : 'success')+'" onclick="selectBomMaterialType(this,'+escapeHtml(JSON.stringify({plant:plant,materialType:mt}).replace(/'/g,"&#39;"))+')" data-plant="'+escapeHtml(plant)+'" data-type="'+escapeHtml(mt.material_type)+'">'+
          '<div class="bom-stage-card-top"><div><div class="bom-stage-card-title">'+escapeHtml(mt.material_type)+'</div><div class="bom-stage-card-sub">'+escapeHtml(`${mt.row_count || mtRows.length} items`)+'</div></div><span class="bom-item-badge '+(mtShort ? 'short' : mtPartial ? 'partial' : 'covered')+'">'+escapeHtml(mtShort ? 'Short' : mtPartial ? 'Partial' : 'Covered')+'</span></div>'+
          '<div class="bom-stage-card-stats">'+
            '<div><span class="bom-stage-stat-label">Gross</span><span class="bom-stage-stat-value">'+num(mt.gross_req || 0).toFixed(1)+' MT</span></div>'+
            '<div><span class="bom-stage-stat-label">Net</span><span class="bom-stage-stat-value">'+num(mt.net_req || 0).toFixed(1)+' MT</span></div>'+
            '<div><span class="bom-stage-stat-label">Mix</span><span class="bom-stage-stat-value">'+escapeHtml(`${mtCovered}/${mtPartial}/${mtShort}`)+'</span></div>'+
          '</div>'+
        '</button>';
      }).join('')+
    '</div></div>';
  } else {
    const sortedRows = allRows.slice().sort((a, b) => {
      const rank = row => row.status === 'SHORT' ? 0 : row.status === 'PARTIAL SHORT' ? 1 : row.status === 'COVERED' ? 2 : 3;
      const toneDiff = rank(a) - rank(b);
      if (toneDiff) return toneDiff;
      return num(b.net_req || 0) - num(a.net_req || 0);
    });
    html += '<div class="bom-detail-section">';
    html += '<div class="bom-detail-section-head"><div><div class="bom-detail-section-title">'+escapeHtml(materialType.material_type)+' item detail</div><div class="bom-detail-section-sub">Condensed cards highlight where coverage is complete versus where net requirement remains exposed.</div></div></div>';
    html += '<div class="bom-item-grid">'+
      sortedRows.map(r => '<div class="bom-item-card '+(r.status === 'SHORT' ? 'danger' : r.status === 'PARTIAL SHORT' ? 'warn' : 'success')+'">'+
        '<div class="bom-item-card-top">'+
          '<div><div class="bom-item-card-title">'+escapeHtml(r.sku_id)+'</div><div class="bom-item-card-sub">'+escapeHtml(r.parent_skus || 'No parent mapping')+'</div></div>'+
          '<div class="bom-item-card-badges"><span class="bom-item-badge '+bomStatusBadgeClass(r.status)+'">'+escapeHtml(r.status)+'</span><span class="bom-item-badge '+String(r.flow_type || '').toLowerCase()+'">'+escapeHtml(r.flow_type || 'INPUT')+'</span></div>'+
        '</div>'+
        '<div class="bom-item-card-metrics">'+
          '<div><div class="bom-item-label">Gross Req</div><div class="bom-item-value">'+num(r.gross_req || 0).toFixed(1)+' MT</div></div>'+
          '<div><div class="bom-item-label">Available</div><div class="bom-item-value">'+num(r.available_before || 0).toFixed(1)+' MT</div></div>'+
          '<div><div class="bom-item-label">Net Req</div><div class="bom-item-value">'+num(r.net_req || 0).toFixed(1)+' MT</div></div>'+
        '</div>'+
      '</div>').join('')+
    '</div></div>';
  }

  html += '</div>';
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
qs('materialRefreshBtn')?.addEventListener('click', refreshMaterialPlan);
qs('materialModeSelect')?.addEventListener('change', async (e) => {
  const nextMode = normalizeMaterialMode(e.target.value);
  if (nextMode === state.materialMode) return;
  state.materialMode = nextMode;
  persistMaterialModePreference();
  await refreshMaterialPlan();
});
qs('bomExplodeBtn')?.addEventListener('click', runBom);
hookBomFilters();
qs('scenariosRefreshBtn')?.addEventListener('click', loadApplicationState);
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
  renderTopSummaryForPage(activePageKey());
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
    // refreshPlanningWorkflowGuide();
  } catch(e) {
    console.error('Failed to load order pool:', e);
    setPipelineStageStatus('ps-pool', 'error', 'Load failed');
    qs('poolBody').innerHTML = '<tr><td colspan="8" style="text-align:center;color:var(--text-soft)">Error: ' + e.message + '</td></tr>';
    // refreshPlanningWorkflowGuide();
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

  const filteredUrgent = filtered.filter((so) => upper(so.priority) === 'URGENT').length;
  const filteredHeld = filtered.filter((so) => Boolean(so._held)).length;
  const defaultSelected = filtered.filter((so) => !so._held).length;
  // KPI rendering removed: only top summary strip shown per design rule
  // setText('poolTotalCount', String(filtered.length));
  // setText('poolUrgentCount', String(filteredUrgent));
  // setText('poolHeldCount', String(filteredHeld));
  // setText('poolSelectedCount', String(defaultSelected));

  poolBody.innerHTML = filtered.map((so) => {
    const isHeld = so._held;
    const rm = state.soRollingOverrides[so.so_id] || so.rolling_mode || 'HOT';
    return `<tr class="pool-row ${isHeld ? 'is-held' : ''}" onclick="if(event.target.type !== 'checkbox' && !event.target.closest('button') && !event.target.closest('select')) checkMaterialForSO('${escapeHtml(so.so_id)}')">
      <td><input type="checkbox" class="pool-so-check" data-so="${escapeHtml(so.so_id)}" ${isHeld ? '' : 'checked'} ${isHeld ? 'disabled' : ''} onclick="event.stopPropagation()"></td>
      <td>${escapeHtml(so.so_id)}</td>
      <td class="pool-sku-cell">${escapeHtml(so.sku_id || '—')}</td>
      <td>${escapeHtml(so.customer_id)}</td>
      <td>${escapeHtml(so.grade)}</td>
      <td>${so.section_mm ? `${so.section_mm}mm` : '—'}</td>
      <td>${num(so.qty_mt).toFixed(0)}</td>
      <td>${fmtDate(so.due_date)}</td>
      <td><span class="badge ${so.priority === 'URGENT' ? 'red' : so.priority === 'HIGH' ? 'amber' : 'blue'}">${escapeHtml(so.priority)}</span></td>
      <td><span class="table-pill table-pill--order-type">${escapeHtml(so.order_type || 'MTO')}</span></td>
      <td onclick="event.stopPropagation()">
        <select class="sel sel-inline sel-inline--rolling ${rm==='HOT' ? 'is-hot' : 'is-cold'}" onchange="setSORollingMode('${escapeHtml(so.so_id)}',this.value);this.classList.toggle('is-hot', this.value==='HOT');this.classList.toggle('is-cold', this.value!=='HOT')">
          <option value="HOT" ${rm==='HOT'?'selected':''}>HOT</option>
          <option value="COLD" ${rm==='COLD'?'selected':''}>COLD</option>
        </select>
      </td>
      <td>${isHeld ? '<span class="badge amber">HELD</span>' : escapeHtml(so.status)}</td>
      <td class="table-action-cell table-action-cell--inline">
        <button class="btn ghost btn-xs" onclick="event.stopPropagation();checkMaterialForSO('${escapeHtml(so.so_id)}')">Mat</button>
        <button class="btn ${isHeld ? 'primary' : 'warn'} btn-xs" onclick="event.stopPropagation();toggleHoldSO('${escapeHtml(so.so_id)}')">${isHeld ? 'Unhold' : 'Hold'}</button>
      </td>
    </tr>`;
  }).join('') || `
    <tr>
      <td colspan="13" style="text-align:center;color:var(--text-soft)">No orders match the current filters. Next: clear one filter or widen the window.</td>
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

  updateSOPoolSelectionCount();
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

    if (checkedSoIds.length === 0) {
      throw new Error('Select at least one sales order before proposing planning orders.');
    }

    const data = await apiFetch('/api/aps/planning/orders/propose', {
      method: 'POST',
      body: JSON.stringify({ days: windowDays, so_ids: checkedSoIds })
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
        <td><input type="checkbox" class="po-so-split-check" data-po="${escapeHtml(po.po_id)}" data-so="${escapeHtml(soId)}" ${po.planner_status === 'RELEASED' ? 'disabled' : ''}></td>
        <td>${escapeHtml(soId)}</td>
        <td>${so.qty_mt ? num(so.qty_mt).toFixed(0) : '—'}</td>
        <td>${so.due_date ? fmtDate(so.due_date) : '—'}</td>
        <td>${so.priority || '—'}</td>
      </tr>`;
    }).join('');

    const isReleased = po.planner_status === 'RELEASED';
    const rowStyle = isReleased ? 'opacity:.6;background:rgba(107,114,207,.02)' : '';

    return `<tr class="po-row ${isReleased ? 'is-released' : ''}" id="po-row-${escapeHtml(po.po_id)}" onclick="togglePODetail('${escapeHtml(po.po_id)}')" style="${rowStyle}">
      <td onclick="event.stopPropagation()"><input type="checkbox" class="po-check" data-po="${escapeHtml(po.po_id)}" ${isReleased ? 'disabled' : ''}></td>
      <td><strong>${escapeHtml(po.po_id)}</strong>${isReleased ? ' ✓' : ''}</td>
      <td>${escapeHtml(soIds.join(', ') || '—')}</td>
      <td style="font-size:.75rem;color:var(--text-soft)">${escapeHtml(skuIds || '—')}</td>
      <td>${num(po.total_qty_mt).toFixed(0)}</td>
      <td>${escapeHtml(po.grade_family)}</td>
      <td>${po.due_window ? po.due_window[0] + ' to ' + po.due_window[1] : '—'}</td>
      <td>${po.heats_required || 0}</td>
      <td><span class="table-pill table-pill--rolling ${po.rolling_mode === 'HOT' ? 'is-hot' : 'is-cold'}">${escapeHtml(po.rolling_mode || 'HOT')}</span></td>
      <td><span class="badge ${statusBadgeClass(po.planner_status)}">${escapeHtml(po.planner_status)}</span></td>
      <td class="table-action-cell table-action-cell--inline" onclick="event.stopPropagation()"><button class="btn ghost ps-action btn-xs" onclick="toggleFreezePO('${escapeHtml(po.po_id)}')" ${isReleased ? 'disabled' : ''} title="${po.planner_status === 'FROZEN' ? 'Click to unfreeze' : 'Click to freeze'}">${po.planner_status === 'FROZEN' ? 'Unfreeze' : 'Freeze'}</button><button class="btn ghost ps-action btn-xs" onclick="checkMaterialForPO('${escapeHtml(po.po_id)}')">Mat</button></td>
    </tr>
    <tr class="po-detail-row" id="po-detail-${escapeHtml(po.po_id)}" style="display:none">
      <td colspan="11">
        <div style="padding:.5rem 1rem;background:var(--panel-soft);border-top:1px solid var(--border-soft)">
          <table class="table">
            <thead><tr>
              <th style="width:2rem"><input type="checkbox" class="po-so-split-all" data-po="${escapeHtml(po.po_id)}" ${isReleased ? 'disabled' : ''}></th>
              <th>SO ID</th><th>Qty MT</th><th>Due Date</th><th>Priority</th>
            </tr></thead>
            <tbody>
              ${soRows}
            </tbody>
          </table>
          ${!isReleased ? `<button class="btn ghost btn-xs" style="margin-top:.5rem" onclick="splitPO('${escapeHtml(po.po_id)}')">Split checked</button>` : '<div style="font-size:.75rem;color:var(--text-soft);padding:.5rem 0">This PO is released and cannot be modified.</div>'}
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

  qs('planningBoard').innerHTML = html || '<tr><td colspan="11" style="text-align:center;color:var(--text-soft)">No planning orders proposed yet. Next: select SOs in Stage 1 and run Propose.</td></tr>';
  // refreshPlanningWorkflowGuide();
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
  // refreshPlanningWorkflowGuide();
}

// SO Due Date Gantt - based on due dates (no schedule simulation needed)
function modalDayWidth(base){
  return Math.max(56, Math.round(base * getZoomValue('modal', 1)));
}

function renderSODueGantt(soList){
  if(!soList || !soList.length) return '<div style="padding:2rem;text-align:center;color:var(--text-soft)">No SOs selected.</div>';

  const bySO = {};
  const dueStarts = [];
  const dueEnds = [];

  soList.forEach(so => {
    const soId = so.so_id;
    const dueDate = parseDate(so.due_date);

    if(Number.isNaN(dueDate.getTime())) return;

    if(!bySO[soId]) bySO[soId] = so;
    dueStarts.push(dueDate.getTime() - 86400000);
    dueEnds.push(dueDate.getTime() + 86400000);
  });

  if(!dueStarts.length || Object.keys(bySO).length === 0)
    return '<div style="padding:2rem;text-align:center;color:var(--text-soft)">No valid due dates.</div>';

  const scale = buildTimelineScale(dueStarts, dueEnds, getZoomValue('modal', 1), 10, 1080);
  const soIds = Object.keys(bySO).sort();
  const ganttWidth = scale.contentWidth;

  let html = `
    <div style="border:1px solid var(--border);border-radius:.3rem;overflow:hidden;background:var(--panel);font-size:.75rem">
      <!-- Header - FROZEN (sticky) -->
      <div style="display:flex;border-bottom:2px solid var(--border);position:sticky;top:0;background:var(--panel);z-index:11;box-shadow:0 2px 4px rgba(0,0,0,.08)">
        <div style="width:11rem;flex-shrink:0;padding:.5rem;font-weight:600;border-right:1px solid var(--border)">SO | SKU | Qty</div>
        <div style="display:flex;width:${ganttWidth}px;background:var(--panel)">
          ${scale.buckets.map(bucket => `<div style="width:${scale.bucketPx}px;min-width:${scale.bucketPx}px;padding:.28rem;text-align:center;font-size:.68rem;border-right:1px solid var(--border-soft);color:var(--text-soft);background:var(--panel);line-height:1.2"><div style="font-weight:700;color:var(--text)">${bucket.label.primary}</div><div>${bucket.label.secondary}</div></div>`).join('')}
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
              const dueDate = parseDate(so.due_date).getTime();
              const dueLeft = Math.max(0, ((dueDate - scale.startMs) / scale.bucketMs) * scale.bucketPx);
              const qty = num(so.qty_mt || 0).toFixed(0);
              const skuId = so.sku_id ? escapeHtml(so.sku_id.substring(0, 12)) : '—';
              const markerWidth = Math.max(40, scale.bucketPx * 0.72);

              return `
                <div style="display:flex;border-bottom:1px solid var(--border-soft);align-items:center">
                  <div style="width:11rem;flex-shrink:0;padding:.5rem;border-right:1px solid var(--border);min-height:2.2rem;display:flex;flex-direction:column;justify-content:center">
                    <div style="font-weight:600;font-size:.8rem">${soId}</div>
                    <div style="font-size:.7rem;color:var(--text-soft);margin-top:.15rem">${skuId}</div>
                    <div style="font-size:.7rem;color:var(--text-soft)">${qty}MT</div>
                  </div>
                  <div style="position:relative;width:${ganttWidth}px;height:2.4rem">
                    ${scale.buckets.map(bucket => `<div style="position:absolute;left:${bucket.left}px;width:${scale.bucketPx}px;height:100%;border-right:1px solid var(--border-soft);opacity:.12"></div>`).join('')}
                    <div style="position:absolute;left:${dueLeft.toFixed(1)}px;width:${markerWidth.toFixed(1)}px;top:.5rem;bottom:.5rem;background:${priorityColor};opacity:.85;border-radius:.2rem;border:2px solid ${priorityColor};display:flex;align-items:center;justify-content:center;font-size:.65rem;color:#fff;font-weight:700;white-space:nowrap;padding:0 .2rem" title="${so.due_date}">DUE</div>
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

  const bySO = {};
  const plantColor = {BF: '#3b82f6', SMS: '#f97316', RM: '#8b5cf6'};
  const starts = [];
  const ends = [];

  scheduleRows.forEach(row => {
    const campaign = row.Campaign || '';
    const soMatch = campaign.match(/SO-\d+/);
    const so = soMatch ? soMatch[0] : null;
    if(!so) return; // Skip if no valid SO

    if(!bySO[so]) bySO[so] = [];
    bySO[so].push(row);

    const start = new Date(row.Planned_Start);
    const end = new Date(row.Planned_End);
    if(!isNaN(start)) starts.push(start.getTime());
    if(!isNaN(end)) ends.push(end.getTime());
  });

  if(!starts.length || Object.keys(bySO).length === 0)
    return '<div style="padding:2rem;text-align:center;color:var(--text-soft)">No valid schedule data.</div>';

  const scale = buildTimelineScale(starts, ends, getZoomValue('modal', 1), 7, 980);
  const soList = Object.keys(bySO).sort();
  const ganttWidth = scale.contentWidth;

  let html = `
    <div style="border:1px solid var(--border);border-radius:.3rem;overflow:hidden;background:var(--panel);font-size:.75rem">
      <!-- Header -->
      <div style="display:flex;border-bottom:2px solid var(--border);position:sticky;top:0;background:var(--panel);z-index:10">
        <div style="width:8rem;flex-shrink:0;padding:.5rem;font-weight:600;border-right:1px solid var(--border)">SO</div>
        <div style="display:flex;width:${ganttWidth}px">
          ${scale.buckets.map(bucket => `<div style="width:${scale.bucketPx}px;min-width:${scale.bucketPx}px;padding:.28rem;text-align:center;font-size:.68rem;border-right:1px solid var(--border-soft);color:var(--text-soft);line-height:1.2"><div style="font-weight:700;color:var(--text)">${bucket.label.primary}</div><div>${bucket.label.secondary}</div></div>`).join('')}
        </div>
      </div>

      <!-- SO rows by plant -->
      ${['BF', 'SMS', 'RM'].map(plant => {
        const sosInPlant = soList.filter(so => bySO[so].some(o => plantForJob(o) === plant));
        if(sosInPlant.length === 0) return '';

        return `
          <div style="border-bottom:1px solid var(--border)">
            <div style="padding:.4rem;background:${plantColor[plant]};color:#fff;font-weight:600;font-size:.8rem">${plant}</div>
            ${sosInPlant.map(so => {
              const ops = bySO[so].filter(o => plantForJob(o) === plant);
              return `
                <div style="display:flex;border-bottom:1px solid var(--border-soft)">
                  <div style="width:8rem;flex-shrink:0;padding:.5rem;border-right:1px solid var(--border);font-weight:600">${so}</div>
                  <div style="position:relative;width:${ganttWidth}px;height:1.8rem">
                    ${scale.buckets.map(bucket => `<div style="position:absolute;left:${bucket.left}px;width:${scale.bucketPx}px;height:100%;border-right:1px solid var(--border-soft);opacity:.1"></div>`).join('')}
                    ${ops.map(op => {
                      const metrics = timelineBarMetrics(op.Planned_Start, op.Planned_End, scale, 8);
                      if (!metrics) return '';
                      return `<div style="position:absolute;left:${metrics.left.toFixed(1)}px;width:${metrics.width.toFixed(1)}px;top:.3rem;bottom:.3rem;background:${plantColor[plant]};opacity:.85;border-radius:.2rem;border:1px solid ${plantColor[plant]};display:flex;align-items:center;justify-content:center;font-size:.6rem;color:#fff;font-weight:600" title="${fmtDateTime(op.Planned_Start)} → ${fmtDateTime(op.Planned_End)}"></div>`;
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

  const byPO = {};
  const opPalette = {
    BF:  { fill: '#3b82f6', text: '#ffffff' },
    EAF: { fill: '#f97316', text: '#ffffff' },
    LRF: { fill: '#f59e0b', text: '#111827' },
    VD:  { fill: '#64748b', text: '#ffffff' },
    CCM: { fill: '#10b981', text: '#ffffff' },
    RM:  { fill: '#8b5cf6', text: '#ffffff' },
    DEF: { fill: '#94a3b8', text: '#ffffff' }
  };
  const laneTop = { BF: 10, EAF: 10, LRF: 34, VD: 34, CCM: 58, RM: 58 };
  const rowHeight = 96;
  const starts = [];
  const ends = [];

  const fmtGanttDay = (ts) => {
    const d = parseDate(ts);
    if(Number.isNaN(d.getTime())) return '—';
    return d.toLocaleDateString('en-GB', { weekday:'short', day:'2-digit', month:'short' }).replace(',', '');
  };
  const fmtGanttTick = (ts) => {
    const d = parseDate(ts);
    if(Number.isNaN(d.getTime())) return '';
    return d.toLocaleTimeString('en-GB', { hour:'2-digit', minute:'2-digit', hour12:false });
  };

  scheduleRows.forEach(row => {
    const campaign = row.Campaign || '';
    const poMatch = campaign.match(/PO-\d+/);
    const po = poMatch ? poMatch[0] : 'Unknown';

    if(!byPO[po]) byPO[po] = [];
    byPO[po].push(row);

    const start = parseDate(row.Planned_Start);
    const end = parseDate(row.Planned_End);
    if(!Number.isNaN(start.getTime())) starts.push(start.getTime());
    if(!Number.isNaN(end.getTime())) ends.push(end.getTime());
  });

  if(!starts.length) return '<div style="padding:1rem;color:var(--text-soft)">No valid time data.</div>';

  const scale = buildTimelineScale(starts, ends, getZoomValue('modal', 1), 7, 1040);
  const poList = Object.keys(byPO).sort();
  const ganttWidth = scale.contentWidth;

  let html = `
    <div style="border:1px solid var(--border);border-radius:.4rem;overflow:hidden;background:var(--panel)">
      <div style="display:flex;border-bottom:2px solid var(--border);position:sticky;top:0;background:var(--panel);z-index:2">
        <div style="width:14rem;flex-shrink:0;padding:.7rem .85rem;font-weight:800;border-right:1px solid var(--border);font-size:.76rem;background:#fbfcfe">Planning Order</div>
        <div style="display:flex;width:${ganttWidth}px">
          ${scale.buckets.map(bucket => `
            <div style="width:${scale.bucketPx}px;min-width:${scale.bucketPx}px;padding:.35rem .2rem;text-align:center;font-size:.69rem;border-right:1px solid var(--border-soft);color:var(--text-soft);line-height:1.25">
              <div style="font-weight:700;color:var(--text)">${fmtGanttDay(bucket.ts)}</div>
              <div>${fmtGanttTick(bucket.ts)}</div>
            </div>
          `).join('')}
        </div>
      </div>

      ${poList.map((po, idx) => {
        const ops = byPO[po].slice().sort((a,b)=>parseDate(a.Planned_Start) - parseDate(b.Planned_Start));
        const totalHours = ops.reduce((sum, o) => sum + num(o.Duration_Hrs || 0), 0);
        const families = [...new Set(ops.map(o => _ridToOp(o.Resource_ID || o.resource_id)).filter(Boolean))];
        const poStart = Math.min(...ops.map(op => parseDate(op.Planned_Start).getTime()).filter(Number.isFinite));
        const poEnd = Math.max(...ops.map(op => parseDate(op.Planned_End).getTime()).filter(Number.isFinite));
        const poMetrics = timelineBarMetrics(poStart, poEnd, scale, 18);
        const familyChips = families.map(family => {
          const palette = opPalette[family] || opPalette.DEF;
          return `<span data-gantt-tooltip="${escapeAttr(`<strong>${family}</strong><br>Operation family used in this PO timeline.`)}" style="display:inline-flex;align-items:center;gap:.28rem;padding:.12rem .38rem;border-radius:.8rem;background:rgba(148,163,184,.08);font-size:.64rem;font-weight:700;color:var(--text-soft);cursor:help"><span style="width:.42rem;height:.42rem;border-radius:999px;background:${palette.fill};display:inline-block"></span>${family}</span>`;
        }).join('');
        const rowBg = idx % 2 === 0 ? '#ffffff' : '#fcfdff';

        return `
          <div style="display:flex;border-bottom:1px solid var(--border-soft);background:${rowBg}">
            <div style="width:14rem;flex-shrink:0;padding:.8rem .85rem;border-right:1px solid var(--border);display:flex;flex-direction:column;justify-content:center;gap:.35rem;background:${rowBg}">
              <div style="font-size:.9rem;font-weight:900;letter-spacing:-.01em">${po}</div>
              <div style="display:flex;flex-wrap:wrap;gap:.28rem">${familyChips || '<span style="font-size:.68rem;color:var(--text-soft)">Scheduled ops</span>'}</div>
              <div style="font-size:.68rem;color:var(--text-soft)">Start ${fmtDateTime(poStart)} · End ${fmtDateTime(poEnd)}</div>
              <div style="font-size:.72rem;font-weight:700;color:var(--text)">${totalHours.toFixed(1)}h total</div>
            </div>
            <div style="position:relative;width:${ganttWidth}px;height:${rowHeight}px;background:linear-gradient(180deg,#fff,#fcfdff)">
              ${scale.buckets.map(bucket => `<div style="position:absolute;left:${bucket.left}px;width:${scale.bucketPx}px;height:100%;border-right:1px solid var(--border-soft);opacity:.18"></div>`).join('')}
              <div style="position:absolute;left:0;right:0;top:28px;border-top:1px dashed rgba(148,163,184,.16)"></div>
              <div style="position:absolute;left:0;right:0;top:52px;border-top:1px dashed rgba(148,163,184,.16)"></div>
              <div style="position:absolute;left:${poMetrics.left.toFixed(1)}px;width:${poMetrics.width.toFixed(1)}px;top:12px;height:66px;border-radius:.55rem;border:1px dashed rgba(15,23,42,.18);background:rgba(148,163,184,.08);box-shadow:inset 0 0 0 1px rgba(255,255,255,.35)"></div>
              ${ops.map(op => {
                const opStart = parseDate(op.Planned_Start);
                const opEnd = parseDate(op.Planned_End);
                const opMetrics = timelineBarMetrics(opStart, opEnd, scale, 8);
                if (!opMetrics) return '';
                const dur = num(op.Duration_Hrs || 0).toFixed(1);
                const family = _ridToOp(op.Resource_ID || op.resource_id);
                const palette = opPalette[family] || opPalette.DEF;
                const top = laneTop[family] ?? 30;
                const label = opMetrics.width > 44 ? family : '';
                return `
                  <div data-gantt-tooltip="${escapeAttr(`<strong>${po}</strong><br>${family} on ${escapeHtml(op.Resource_ID || '—')}<br>Duration: ${dur}h<br>Start: ${fmtDateTime(opStart)}<br>End: ${fmtDateTime(opEnd)}`)}" style="position:absolute;left:${opMetrics.left.toFixed(1)}px;width:${opMetrics.width.toFixed(1)}px;top:${top}px;height:20px;background:${palette.fill};opacity:.95;border-radius:.32rem;border:1px solid rgba(255,255,255,.5);display:flex;align-items:center;justify-content:center;font-size:.62rem;color:${palette.text};font-weight:800;overflow:hidden;white-space:nowrap;padding:0 .24rem;box-shadow:0 1px 2px rgba(15,23,42,.12);cursor:pointer" title="">${label}</div>
                `;
              }).join('')}
            </div>
          </div>
        `;
      }).join('')}
    </div>

    <div style="margin-top:1rem;display:flex;gap:.65rem;flex-wrap:wrap;justify-content:center;font-size:.75rem">
      <div style="padding:.45rem .7rem;border:1px solid var(--border);border-radius:.7rem;background:#fbfcfe"><span style="color:var(--text-soft)">Start:</span> <strong>${fmtDateTime(scale.startMs)}</strong></div>
      <div style="padding:.45rem .7rem;border:1px solid var(--border);border-radius:.7rem;background:#fbfcfe"><span style="color:var(--text-soft)">End:</span> <strong>${fmtDateTime(scale.endMs)}</strong></div>
      <div style="padding:.45rem .7rem;border:1px solid var(--border);border-radius:.7rem;background:#fbfcfe"><span style="color:var(--text-soft)">Total POs:</span> <strong>${poList.length}</strong></div>
      <div style="padding:.45rem .7rem;border:1px solid var(--border);border-radius:.7rem;background:#fbfcfe"><span style="color:var(--text-soft)">Scale:</span> <strong>${scale.scaleLabel}</strong></div>
    </div>
  `;

  return html;
}

// Heat/Equipment Gantt - per equipment with plant grouping
function renderEquipmentGantt(scheduleRows){
  if(!scheduleRows || !scheduleRows.length) return '<div style="padding:2rem;text-align:center;color:var(--text-soft)">No schedule data.</div>';

  const byEquip = {};
  const plantColor = {BF: '#3b82f6', SMS: '#f97316', RM: '#8b5cf6'};
  const starts = [];
  const ends = [];

  scheduleRows.forEach(row => {
    const equip = row.Resource_ID || 'Unknown';
    if(!byEquip[equip]) byEquip[equip] = [];
    byEquip[equip].push(row);

    const start = new Date(row.Planned_Start);
    const end = new Date(row.Planned_End);
    if(!isNaN(start)) starts.push(start.getTime());
    if(!isNaN(end)) ends.push(end.getTime());
  });

  if(!starts.length) return '<div style="padding:1rem;color:var(--text-soft)">No valid time data.</div>';

  const scale = buildTimelineScale(starts, ends, getZoomValue('modal', 1), 7, 980);
  const ganttWidth = scale.contentWidth;

  let html = `
    <div style="border:1px solid var(--border);border-radius:.3rem;overflow:hidden;background:var(--panel)">
      <!-- Header -->
      <div style="display:flex;border-bottom:2px solid var(--border);position:sticky;top:0;background:var(--panel)">
        <div style="width:9rem;flex-shrink:0;padding:.5rem;font-weight:600;border-right:1px solid var(--border);font-size:.75rem">Equipment | Plant</div>
        <div style="display:flex;width:${ganttWidth}px">
          ${scale.buckets.map(bucket => `<div style="width:${scale.bucketPx}px;min-width:${scale.bucketPx}px;padding:.28rem;text-align:center;font-size:.68rem;border-right:1px solid var(--border-soft);color:var(--text-soft);line-height:1.2"><div style="font-weight:700;color:var(--text)">${bucket.label.primary}</div><div>${bucket.label.secondary}</div></div>`).join('')}
        </div>
      </div>

      <!-- Equipment rows grouped by plant -->
      ${(() => {
        let html = '';
        const plantOrder = ['BF', 'SMS', 'RM'];

        plantOrder.forEach(plant => {
          const equipInPlant = Object.keys(byEquip).filter(equip => {
            return byEquip[equip].some(o => plantForJob(o) === plant);
          }).sort();

          if(equipInPlant.length === 0) return;

          html += `<div style="padding:.4rem;background:${plantColor[plant]};color:#fff;font-weight:600;font-size:.8rem;border-bottom:2px solid var(--border)">${plant} Plant</div>`;

          equipInPlant.forEach(equip => {
            const ops = byEquip[equip].filter(o => plantForJob(o) === plant);
            const color = plantColor[plant];
            const utilization = (ops.reduce((sum, o) => sum + (num(o.Duration_Hrs) || 0), 0) / Math.max((scale.spanMs / 3600000), 1) * 100).toFixed(0);

            html += `
              <div style="display:flex;border-bottom:1px solid var(--border-soft)">
                <div style="width:9rem;flex-shrink:0;padding:.5rem;border-right:1px solid var(--border);font-size:.75rem">
                  <div style="font-weight:600">${equip}</div>
                  <div style="font-size:.7rem;color:var(--text-soft);margin-top:.2rem">${utilization}% util</div>
                </div>
                <div style="position:relative;width:${ganttWidth}px;height:2rem">
                  <!-- Grid -->
                  ${scale.buckets.map(bucket => `<div style="position:absolute;left:${bucket.left}px;width:${scale.bucketPx}px;height:100%;border-right:1px solid var(--border-soft);opacity:.15"></div>`).join('')}

                  <!-- Bars -->
                  ${ops.map(op => {
                    const metrics = timelineBarMetrics(op.Planned_Start, op.Planned_End, scale, 8);
                    if (!metrics) return '';
                    const campaign = (op.Campaign || '').substring(0, 10);
                    return `
                      <div style="position:absolute;left:${metrics.left.toFixed(1)}px;width:${metrics.width.toFixed(1)}px;top:.3rem;bottom:.3rem;background:${color};opacity:.8;border-radius:.2rem;border:1px solid ${color};display:flex;align-items:center;justify-content:center;font-size:.65rem;color:#fff;font-weight:600;overflow:hidden" title="${campaign}">${campaign}</div>
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
      <div><span style="color:var(--text-soft)">Start:</span> <strong>${fmtDateTime(scale.startMs).substring(0, 16)}</strong></div>
      <div><span style="color:var(--text-soft)">End:</span> <strong>${fmtDateTime(scale.endMs).substring(0, 16)}</strong></div>
      <div><span style="color:var(--text-soft)">Total Equipment:</span> <strong>${Object.keys(byEquip).length}</strong></div>
    </div>
  `;

  return html;
}

function renderGlobalGantt(scheduleRows){
  if(!scheduleRows || !scheduleRows.length) return '<div style="padding:2rem;text-align:center;color:var(--text-soft)">No schedule data.</div>';

  const grouped = {};
  const plantColor = { BF: '#3b82f6', SMS: '#f97316', RM: '#8b5cf6', SHARED: '#64748b' };
  const starts = [];
  const ends = [];

  scheduleRows.forEach(row => {
    const plant = plantForJob(row);
    const campaign = String(row.Campaign || 'Unknown');
    const key = `${plant}::${campaign}`;
    if(!grouped[key]){
      grouped[key] = { plant, campaign, ops: 0, resources: new Set(), minStart: Infinity, maxEnd: -Infinity };
    }
    const start = parseDate(row.Planned_Start).getTime();
    const end = parseDate(row.Planned_End).getTime();
    if(!Number.isFinite(start) || !Number.isFinite(end)) return;
    grouped[key].ops += 1;
    grouped[key].resources.add(String(row.Resource_ID || ''));
    grouped[key].minStart = Math.min(grouped[key].minStart, start);
    grouped[key].maxEnd = Math.max(grouped[key].maxEnd, end);
    starts.push(start);
    ends.push(end);
  });

  if(!starts.length) return '<div style="padding:1rem;color:var(--text-soft)">No valid time data.</div>';

  const scale = buildTimelineScale(starts, ends, getZoomValue('modal', 1), 7, 1040);
  const ganttWidth = scale.contentWidth;

  const rows = Object.values(grouped)
    .filter(row => Number.isFinite(row.minStart) && Number.isFinite(row.maxEnd))
    .sort((a, b) => {
      const order = ['BF', 'SMS', 'RM', 'SHARED'];
      const ia = order.indexOf(a.plant);
      const ib = order.indexOf(b.plant);
      const sa = ia === -1 ? 999 : ia;
      const sb = ib === -1 ? 999 : ib;
      if(sa !== sb) return sa - sb;
      return a.campaign.localeCompare(b.campaign);
    });

  return `
    <div style="border:1px solid var(--border);border-radius:.4rem;overflow:hidden;background:var(--panel);font-size:.75rem">
      <div style="display:flex;border-bottom:2px solid var(--border);position:sticky;top:0;background:var(--panel);z-index:3">
        <div style="width:14rem;flex-shrink:0;padding:.6rem .75rem;font-weight:800;border-right:1px solid var(--border)">Plant / Campaign</div>
        <div style="display:flex;width:${ganttWidth}px">
          ${scale.buckets.map(bucket => `<div style="width:${scale.bucketPx}px;min-width:${scale.bucketPx}px;padding:.28rem;text-align:center;font-size:.68rem;border-right:1px solid var(--border-soft);color:var(--text-soft);line-height:1.2"><div style="font-weight:700;color:var(--text)">${bucket.label.primary}</div><div>${bucket.label.secondary}</div></div>`).join('')}
        </div>
      </div>
      ${rows.map((row, idx) => {
        const metrics = timelineBarMetrics(row.minStart, row.maxEnd, scale, 16);
        const color = plantColor[row.plant] || plantColor.SHARED;
        const bg = idx % 2 ? '#fcfdff' : '#ffffff';
        return `
          <div style="display:flex;border-bottom:1px solid var(--border-soft);background:${bg}">
            <div style="width:14rem;flex-shrink:0;padding:.55rem .75rem;border-right:1px solid var(--border);display:flex;flex-direction:column;gap:.18rem">
              <div style="font-weight:800">${escapeHtml(row.plant)}</div>
              <div style="font-size:.72rem">${escapeHtml(row.campaign)}</div>
              <div style="font-size:.66rem;color:var(--text-soft)">${row.ops} ops · ${row.resources.size} resources</div>
            </div>
            <div style="position:relative;width:${ganttWidth}px;height:2.3rem">
              ${scale.buckets.map(bucket => `<div style="position:absolute;left:${bucket.left}px;width:${scale.bucketPx}px;height:100%;border-right:1px solid var(--border-soft);opacity:.16"></div>`).join('')}
              <div style="position:absolute;left:${metrics.left.toFixed(1)}px;width:${metrics.width.toFixed(1)}px;top:.38rem;bottom:.38rem;background:${color};opacity:.84;border-radius:.24rem;border:1px solid ${color}"></div>
            </div>
          </div>
        `;
      }).join('')}
    </div>
  `;
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

  const content = document.createElement('div');
  content.style.cssText = 'background:var(--page-bg);border:1px solid var(--border);border-radius:.4rem;box-shadow:0 10px 40px rgba(0,0,0,.2);width:95%;height:92vh;overflow:auto;padding:1.25rem;display:flex;flex-direction:column';
  const modalZoom = getZoomValue('modal', 1);
  const modalScale = getTimelineScalePreset(modalZoom);
  content.innerHTML = `
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:1rem;flex-shrink:0;border-bottom:1px solid var(--border);padding-bottom:.8rem">
      <h3 style="margin:0;font-size:1rem;font-weight:600">${escapeHtml(title)}</h3>
      <div style="display:flex;align-items:center;gap:.4rem">
        <button type="button" class="btn ghost btn-compact" data-modal-zoom="-">-</button>
        <span id="modalZoomLabel" style="min-width:3.2rem;text-align:center;font-size:.75rem;color:var(--text-soft);font-weight:700">${Math.round(modalZoom * 100)}%</span>
        <span id="modalScaleLabel" style="display:inline-flex;align-items:center;height:1.5rem;padding:0 .55rem;border-radius:999px;background:var(--panel-soft);border:1px solid var(--border-soft);font-size:.68rem;color:var(--text-soft);font-weight:700">${modalScale.scaleLabel}</span>
        <button type="button" class="btn ghost btn-compact" data-modal-zoom="+">+</button>
        <button style="background:none;border:none;font-size:1.5rem;cursor:pointer;color:var(--text-soft);flex-shrink:0;padding:0;width:2rem;height:2rem;display:flex;align-items:center;justify-content:center">✕</button>
      </div>
    </div>
    <div style="flex:1;overflow:auto;min-height:0" id="modalGanttBody"></div>
  `;

  const renderBody = () => {
    const body = content.querySelector('#modalGanttBody');
    if(!body) return;
    let ganttContent = '';
    if(type === 'so') ganttContent = renderSOGantt(data);
    else if(type === 'so-due') ganttContent = renderSODueGantt(data);
    else if(type === 'po') ganttContent = renderPOGantt(data);
    else if(type === 'heat') ganttContent = renderEquipmentGantt(data);
    else if(type === 'global') ganttContent = renderGlobalGantt(data);
    body.innerHTML = ganttContent;
    initGanttTooltips(body);
    const z = content.querySelector('#modalZoomLabel');
    if(z) z.textContent = `${Math.round(getZoomValue('modal', 1) * 100)}%`;
    const s = content.querySelector('#modalScaleLabel');
    if(s) s.textContent = getTimelineScalePreset(getZoomValue('modal', 1)).scaleLabel;
  };

  const closeBtn = content.querySelector('button:last-of-type');
  closeBtn.addEventListener('click', () => modal.remove());
  const zoomOutBtn = content.querySelector('[data-modal-zoom="-"]');
  const zoomInBtn = content.querySelector('[data-modal-zoom="+"]');
  if(zoomOutBtn){
    zoomOutBtn.addEventListener('click', () => {
      state.ganttZoom.modal = clampZoomValue(getZoomValue('modal', 1) - 0.2);
      renderBody();
    });
  }
  if(zoomInBtn){
    zoomInBtn.addEventListener('click', () => {
      state.ganttZoom.modal = clampZoomValue(getZoomValue('modal', 1) + 0.2);
      renderBody();
    });
  }

  modal.appendChild(content);
  document.body.appendChild(modal);
  renderBody();

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
    const horizonUse = data.load_factor || '—';
    const overflowHours = num(data.overflow_hours || 0);
    const smsWorkload = num(data.sms_hours || 0).toFixed(1);
    const rmWorkload = num(data.rm_hours || 0).toFixed(1);

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
      <div class="scheduler-summary">
        <div class="scheduler-summary-inner">
          <div class="scheduler-summary-status" style="color:${statusColor}">${feasible ? '✓ FEASIBLE' : '✗ INFEASIBLE'}</div>
          <div class="scheduler-summary-metrics">
            <div class="scheduler-summary-metric"><div class="scheduler-summary-metric-label">Duration</div><div class="scheduler-summary-metric-value">${duration}h</div></div>
            <div class="scheduler-summary-metric"><div class="scheduler-summary-metric-label">Horizon</div><div class="scheduler-summary-metric-value">${returnedHorizonHours}h</div></div>
            <div class="scheduler-summary-metric"><div class="scheduler-summary-metric-label">Horizon use</div><div class="scheduler-summary-metric-value">${horizonUse}</div></div>
            <div class="scheduler-summary-metric"><div class="scheduler-summary-metric-label">SMS span</div><div class="scheduler-summary-metric-value">${smsHours}h</div></div>
            <div class="scheduler-summary-metric"><div class="scheduler-summary-metric-label">RM span</div><div class="scheduler-summary-metric-value">${rmHours}h</div></div>
            <div class="scheduler-summary-metric"><div class="scheduler-summary-metric-label">Bottleneck</div><div class="scheduler-summary-metric-value" style="color:${statusColor}">${escapeHtml(bottleneck)}</div></div>
          </div>
          <div class="scheduler-summary-message">
            <div class="scheduler-summary-title" style="color:${statusColor}">
              ${feasible
                ? `Finite schedule generated within selected horizon`
                : (data.horizon_exceeded
                  ? `Not feasible for selected horizon: need ${duration.toFixed(1)}h, but only ${returnedHorizonHours.toFixed(1)}h selected`
                  : `No feasible finite schedule was produced`)}
            </div>
            <div class="scheduler-summary-detail">
              ${feasible
                ? `The schedule fits within the selected horizon.`
                : (data.horizon_exceeded
                  ? `The issue is horizon overflow, not missing rows: the schedule exceeds the selected horizon by ${overflowHours.toFixed(1)}h.`
                  : escapeHtml(data.message || 'Finite schedule is infeasible with current planning orders / master data.'))}
            </div>
            <div class="scheduler-summary-workload">
              Workload: SMS ${smsWorkload}h · RM ${rmWorkload}h
            </div>
            <div class="scheduler-summary-note">
              Span = elapsed time from first start to last finish in that family. Workload = summed processing hours, so workload can be higher than span when lines run in parallel.
            </div>
          </div>
        </div>
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
      data.horizon_exceeded
        ? `Need ${duration.toFixed(1)}h vs ${returnedHorizonHours.toFixed(1)}h horizon`
        : data.message,
      [{v: duration+'h', l:'span'}, {v: horizonUse, l:'horizon use'}]
    );
    stageExpand('ps-schedule');

    // Enable release if feasible
    if(feasible) await loadReleaseBoard();
    else {
      state.releaseReadyCount = 0;
      setText('releaseStatus', 'Blocked');
      qs('releaseApproveBtn').disabled = true;
      // refreshPlanningWorkflowGuide();
    }

    setText('schedulerSimulateBtn', 'Simulate');
  } catch(e) {
    setPipelineStageStatus('ps-schedule', 'error', 'Simulation failed');
    state.releaseReadyCount = 0;
    qs('releaseApproveBtn').disabled = true;
    // refreshPlanningWorkflowGuide();
    setText('schedulerSimulateBtn', 'Simulate');
    throw e;
  }
}


async function loadReleaseBoard(){
  if(!state.planningOrders || !state.planningOrders.length){
    state.releaseReadyCount = 0;
    qs('releaseBoard').innerHTML = '<tr><td colspan="8" style="text-align:center;color:var(--text-soft)">Complete stages 1-4 first. Next: propose, derive, and simulate.</td></tr>';
    setText('releaseStatus', 'Pending');
    qs('releaseApproveBtn').disabled = true;
    setPipelineStageStatus('ps-release', 'pending', 'Complete stages 1-4 first');
    // refreshPlanningWorkflowGuide();
    return;
  }

  // Only show POs that passed feasibility check.
  // Prefer direct PO/campaign mapping from schedule rows, fallback to heat mapping.
  const scheduleRows = state.lastScheduleRows || [];
  const feasiblePoIds = new Set(
    scheduleRows
      .map(r => String(r.Campaign || r.campaign_id || '').trim())
      .filter(Boolean)
  );
  const feasibleHeatIds = new Set(
    scheduleRows
      .map(r => String(r.Heat_ID || r.heat_id || '').trim())
      .filter(Boolean)
  );
  const heatIdsByPo = {};
  (state.heatBatches || []).forEach(heat => {
    const poId = String(heat.planning_order_id || heat.po_id || '').trim();
    const heatId = String(heat.heat_id || heat.Heat_ID || '').trim();
    if (!poId || !heatId) return;
    if (!heatIdsByPo[poId]) heatIdsByPo[poId] = [];
    heatIdsByPo[poId].push(heatId);
  });
  const feasiblePos = (state.planningOrders || []).filter(po => {
    if (po.planner_status === 'RELEASED') return true;
    const poId = String(po.po_id || '').trim();
    if (feasiblePoIds.size > 0) return feasiblePoIds.has(poId);
    const poHeatIds = heatIdsByPo[poId] || [];
    return poHeatIds.some(heatId => feasibleHeatIds.has(heatId));
  });

  const totalMT = feasiblePos.reduce((sum, po) => sum + num(po.total_qty_mt), 0);
  const totalSOs = feasiblePos.reduce((sum, po) => sum + (po.selected_so_ids?.length || 0), 0);
  state.releaseReadyCount = feasiblePos.filter(po => po.planner_status !== 'RELEASED').length;

  if(feasiblePos.length === 0){
    qs('releaseBoard').innerHTML = '<tr><td colspan="8" style="text-align:center;color:var(--text-faint)">No feasible POs yet. Next: run Feasibility Check and resolve blockers.</td></tr>';
    setText('releaseReady', '0');
    setText('releaseMT', '0');
    setText('releaseSOs', '0');
    setText('releaseStatus', 'Waiting');
    qs('releaseApproveBtn').disabled = true;
    const sim = latestSimResult();
    setPipelineStageStatus(
      'ps-release',
      sim && sim.feasible ? 'warn' : 'pending',
      sim && sim.feasible ? 'No feasible POs in release queue' : 'Run Feasibility Check first',
      [{ v: 0, l: 'ready' }]
    );
    // refreshPlanningWorkflowGuide();
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
    <td style="text-align:center"><input type="checkbox" class="release-po-checkbox" data-po="${escapeHtml(po.po_id)}" ${isReleased ? 'disabled' : ''} /></td>
    <td><strong>${escapeHtml(po.po_id)}</strong><span style="margin-left:.4rem;font-size:.7rem;color:${statusColor};font-weight:600">${status}</span></td>
    <td style="font-size:.7rem">${escapeHtml(po.selected_so_ids?.slice(0,3).join(', ') || '—')}${(po.selected_so_ids?.length||0)>3?' +'+((po.selected_so_ids?.length||0)-3):''}</td>
    <td>${num(po.total_qty_mt).toFixed(0)}</td>
    <td>${escapeHtml(po.grade_family)}</td>
    <td>${po.heats_required || 0}</td>
    <td><span class="badge green">OK</span></td>
    <td class="table-action-cell table-action-cell--inline">
      <button class="btn ghost btn-xs" style="${isReleased ? 'opacity:0.4;cursor:not-allowed' : ''}" ${isReleased ? 'disabled' : ''} onclick="releaseSinglePO('${escapeHtml(po.po_id)}')">Release</button>
      <button class="btn danger btn-xs" style="${!isReleased ? 'opacity:0.4;cursor:not-allowed' : ''}" ${!isReleased ? 'disabled' : ''} onclick="unreleasePlanningOrder('${escapeHtml(po.po_id)}')">Unrelease</button>
    </td>
  </tr>`;
  }).join('');

  qs('releaseApproveBtn').disabled = state.releaseReadyCount === 0;
  setPipelineStageStatus('ps-release', 'done',
    feasiblePos.length + ' POs ready to release',
    [{v: feasiblePos.length, l:'POs'}, {v: totalSOs, l:'SOs'}, {v: totalMT.toFixed(0)+'MT', l:'total'}]
  );
  stageExpand('ps-release');
  // refreshPlanningWorkflowGuide();
}

async function releaseSinglePO(poId){
  try {
    qs('chipPipelineText').textContent = '⏳ Releasing planning order ' + poId + '...';
    setChipTone('chipPipeline', 'info');

    await apiFetch('/api/aps/planning/release', {
      method: 'POST',
      body: JSON.stringify({po_ids: [poId]})
    });

    await refreshReleaseCycleState();
/*     renderDashboard(); */

    qs('chipPipelineText').textContent = '✓ Planning order ' + poId + ' released successfully';
    setChipTone('chipPipeline', 'success');
  } catch(e) {
    qs('chipPipelineText').textContent = '✗ Release failed: ' + e.message;
    setChipTone('chipPipeline', 'danger');
  }
}

async function releaseSelectedPOs(){
  const checked = Array.from(qs('releaseBoard').querySelectorAll('input[type="checkbox"]:checked')).map(el => el.dataset.po);
  if(!checked.length){
    qs('chipPipelineText').textContent = '⚠ Select at least one planning order to release';
    setChipTone('chipPipeline', 'warn');
    return;
  }

  try {
    qs('chipPipelineText').textContent = '⏳ Releasing ' + checked.length + ' planning order' + (checked.length > 1 ? 's' : '') + '...';
    setChipTone('chipPipeline', 'info');

    await apiFetch('/api/aps/planning/release', {
      method: 'POST',
      body: JSON.stringify({po_ids: checked})
    });

    await refreshReleaseCycleState();
/*     renderDashboard(); */

    qs('chipPipelineText').textContent = '✓ Released ' + checked.length + ' planning order' + (checked.length > 1 ? 's' : '') + ' successfully';
    setChipTone('chipPipeline', 'success');
  } catch(e) {
    qs('chipPipelineText').textContent = '✗ Release failed: ' + e.message;
    setChipTone('chipPipeline', 'danger');
  }
}

async function releaseAllSelected(){
  const checked = Array.from(document.querySelectorAll('.release-po-checkbox:checked')).map(el => el.dataset.po);
  if(!checked.length){
    qs('chipPipelineText').textContent = '⚠ Select at least one planning order to release';
    setChipTone('chipPipeline', 'warn');
    return;
  }

  if(!confirm(`Release ${checked.length} planning order${checked.length > 1 ? 's' : ''}?`)) return;

  try {
    qs('chipPipelineText').textContent = '⏳ Releasing ' + checked.length + ' order' + (checked.length > 1 ? 's' : '') + '...';
    setChipTone('chipPipeline', 'info');

    await apiFetch('/api/aps/planning/release', {
      method: 'POST',
      body: JSON.stringify({po_ids: checked})
    });

    await refreshReleaseCycleState();
/*     renderDashboard(); */

    qs('chipPipelineText').textContent = '✓ Released ' + checked.length + ' planning order' + (checked.length > 1 ? 's' : '') + ' successfully';
    setChipTone('chipPipeline', 'success');
  } catch(e) {
    qs('chipPipelineText').textContent = '✗ Release failed: ' + e.message;
    setChipTone('chipPipeline', 'danger');
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
  setText('poolSelectedCount', checkedCount);
  // Disable Propose button when no SOs selected
  const proposeBtn = qs('planningProposeBtn');
  if (proposeBtn) {
    proposeBtn.disabled = checkedCount === 0;
    proposeBtn.title = checkedCount === 0 ? 'Select at least one Sales Order' : 'Propose selected SOs into Planning Orders';
  }
}

function togglePODetail(poId) {
  const detailRow = qs('po-detail-' + poId);
  if (detailRow) {
    detailRow.style.display = detailRow.style.display === 'none' ? 'table-row' : 'none';
  }
}

async function toggleFreezePO(poId) {
  const po = state.planningOrders?.find(p => p.po_id === poId);
  if (!po) return;
  const isFrozen = po.planner_status === 'FROZEN';
  const action = isFrozen ? 'unfreeze' : 'freeze';
  try {
    const data = await apiFetch('/api/aps/planning/orders/update', {
      method: 'POST',
      body: JSON.stringify({action: action, po_id: poId})
    });
    if (data && data.planning_orders) {
      state.planningOrders = data.planning_orders;
      refreshSimulationGradeOptions();
      renderPlanningBoard();
      updatePOToolbarButtons();
    }
  } catch (e) {
    alert('Failed to ' + action + ' PO: ' + e.message);
  }
}

async function freezePO(poId) {
  // Legacy function - now calls toggleFreezePO for backward compatibility
  return toggleFreezePO(poId);
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

  qs('pipelineExpandAllBtn').textContent = anyCollapsed ? 'Collapse' : 'Expand';
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
  // refreshPlanningWorkflowGuide();
  bar.style.width = '100%';

  await healthPromise.catch(() => {});

  setTimeout(()=>{ bar.style.opacity = '0'; }, 300);
});

async function refreshMaterialPlan(){
  const btn = qs('materialRefreshBtn');
  const old = btn ? btn.innerHTML : '';
  if (btn) {
    btn.innerHTML = '<span class="spinner"></span> Refreshing…';
    btn.disabled = true;
  }

  try {
    const material = await apiFetch(materialPlanUrl());
    state.material = material || { summary: {}, campaigns: [] };
    renderMaterial();
    renderTopSummaryForPage(activePageKey());
    qs('chipPipelineText').textContent = '✓ Material status refreshed';
    setChipTone('chipPipeline', 'success');
  } catch (e) {
    qs('chipPipelineText').textContent = '✗ Material refresh failed: ' + e.message;
    setChipTone('chipPipeline', 'danger');
  } finally {
    if (btn) {
      btn.innerHTML = old;
      btn.disabled = false;
    }
  }
}

async function unreleasePlanningOrder(poId){
  if(!poId) return;
  if(!confirm(`Return ${poId} from execution back to planning?`)) return;

  try {
    qs('chipPipelineText').textContent = '⏳ Unreleasing planning order ' + poId + '...';
    setChipTone('chipPipeline', 'info');

    await apiFetch('/api/aps/planning/unrelease', {
      method: 'POST',
      body: JSON.stringify({po_ids: [poId]})
    });

    await refreshReleaseCycleState();

    qs('chipPipelineText').textContent = '✓ Planning order ' + poId + ' returned to planning';
    setChipTone('chipPipeline', 'success');
  } catch(e) {
    qs('chipPipelineText').textContent = '✗ Unrelease failed: ' + e.message;
    setChipTone('chipPipeline', 'danger');
  }
}

async function refreshReleaseCycleState() {
  const [overview, campaigns, material, planningOrders] = await Promise.all([
    apiFetch('/api/aps/dashboard/overview').catch(()=>null),
    apiFetch('/api/aps/campaigns/list').catch(()=>({items:[]})),
    apiFetch(materialPlanUrl()).catch(()=>({summary:{}, campaigns:[], detail_level: normalizeMaterialMode(state.materialMode)})),
    apiFetch('/api/aps/planning/orders').catch(()=>({planning_orders:[]}))
  ]);

  state.overview = overview ? overview.summary || overview : null;
  state.lastSimResult = overview?.last_simulation || null;
  state.campaigns = campaigns.items || [];
  state.material = material || { summary: {}, campaigns: [] };
  state.planningOrders = planningOrders.planning_orders || [];
  refreshSimulationGradeOptions();

  hydrateSummary(state.overview || {});
  renderDashboard();
  renderPlanningBoard();
  renderCampaigns();
  renderMaterial();
  await loadReleaseBoard();
  // refreshPlanningWorkflowGuide();
}
