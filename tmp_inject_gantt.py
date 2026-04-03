import sys

with open(r'c:\Users\bhadk\Documents\APS\ui_design\index.html', 'r', encoding='utf-8') as f:
    text = f.read()

gantt_js = """
// ── Dashboard Campaign Gantt ──────────────────────────────────────────────────
function renderDashCampGantt() {
  const container = document.getElementById('dash-camp-gantt');
  if (!container) return;
  const jobs = (_state.gantt || []).filter(j => j.Planned_Start && j.Planned_End && j.Campaign);
  
  if (!jobs.length) {
    container.innerHTML = '<div style="text-align:center;color:var(--grey-500);padding:2rem">No Campaign Schedule data available</div>';
    return;
  }

  // Aggregate by Campaign
  const cMap = {};
  jobs.forEach(j => {
    const c = j.Campaign;
    if (!cMap[c]) cMap[c] = { start: new Date('2099-01-01'), end: new Date('1970-01-01'), mt: 0, jobs: [] };
    const s = new Date(j.Planned_Start), e = new Date(j.Planned_End);
    if (!isNaN(s) && s < cMap[c].start) cMap[c].start = s;
    if (!isNaN(e) && e > cMap[c].end) cMap[c].end = e;
    cMap[c].mt += parseFloat(j.Qty_MT) || 0;
    cMap[c].jobs.push(j);
  });

  const campaigns = Object.keys(cMap).map(k => ({
    id: k,
    start: cMap[k].start,
    end: cMap[k].end,
    mt: cMap[k].mt,
    jobs: cMap[k].jobs
  })).sort((a,b) => a.start - b.start);

  if(!campaigns.length) return;

  const tMin = new Date(Math.min(...campaigns.map(c => c.start)));
  const tMax = new Date(Math.max(...campaigns.map(c => c.end)));
  const span = tMax - tMin || 1;

  let html = `<div style="position:relative;width:100%;height:100%;min-width:30rem;display:flex;flex-direction:column;gap:0.5rem">`;
  
  // Header with timeline info
  html += `<div style="display:flex;justify-content:space-between;border-bottom:0.0625rem solid var(--grey-200);padding-bottom:0.25rem;font-size:0.625rem;color:var(--grey-500);text-transform:uppercase;font-weight:700">
             <span>${fmtDate(tMin.toISOString())}</span>
             <span>Campaign Execution Path</span>
             <span>${fmtDate(tMax.toISOString())}</span>
           </div>`;

  campaigns.forEach((c, idx) => {
    const left = ((c.start - tMin) / span) * 100;
    const width = Math.max(0.5, ((c.end - c.start) / span) * 100);
    const color = `hsl(${(idx * 137.5) % 360}, 75%, 60%)`;
    
    const durH = ((c.end - c.start) / 3600000).toFixed(1);

    html += `
    <div style="display:flex;align-items:center;min-height:1.75rem;position:relative;margin-bottom:0.125rem">
      <div style="width:4.5rem;font-size:0.6875rem;font-weight:700;color:var(--navy);white-space:nowrap;overflow:hidden;text-overflow:ellipsis" title="${c.id}">${c.id}</div>
      <div style="flex:1;position:relative;height:100%;background:rgba(0,0,0,0.02);border-radius:0.125rem">
        <div style="position:absolute;left:${left}%;width:${width}%;height:1.25rem;top:0.25rem;background:${color};border-radius:0.125rem;opacity:0.85;box-shadow:0 0.125rem 0.25rem rgba(0,0,0,0.08);display:flex;align-items:center;padding:0 0.25rem;overflow:hidden;white-space:nowrap" title="${c.id} | ${c.mt.toFixed(0)} MT | ${durH} hrs">
           <span style="font-size:0.55rem;color:#fff;font-weight:800;text-shadow:0 0.0625rem 0.125rem rgba(0,0,0,0.3)">${c.mt.toFixed(0)} MT</span>
        </div>
      </div>
    </div>`;
  });

  html += '</div>';
  container.innerHTML = html;
}
"""

if 'function renderDashCampGantt()' not in text:
    target = "window.addEventListener('DOMContentLoaded'"
    if target in text:
        text = text.replace(target, gantt_js + '\n' + target)
        with open(r'c:\Users\bhadk\Documents\APS\ui_design\index.html', 'w', encoding='utf-8') as f:
            f.write(text)
        print("Successfully injected function definition into index.html")
    else:
        print("Error: Could not find DOMContentLoaded event hook to inject before.")
else:
    print("Function already exists in index.html")
