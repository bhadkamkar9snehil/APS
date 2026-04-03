import re

with open(r'c:\Users\bhadk\Documents\APS\ui_design\index.html', 'r', encoding='utf-8') as f:
    text = f.read()

# Define the new responsive buildTputChart function
new_func = """function buildTputChart(totalMt, totalHeats) {
  const el = document.getElementById('tput-chart');
  if(!el) return;
  // Generate synthetic per-day distribution from total
  const sms = Math.round((totalMt||3000)/14);
  const rm  = Math.round(sms*0.85);
  const days = Array.from({length:14},(_,i)=>[Math.round(sms*(0.8+Math.random()*.4)),Math.round(rm*(0.8+Math.random()*.4))]);
  const max = Math.max(...days.map(d=>d[0]+d[1]));
  
  // Clean up and construct flex responsive bars
  el.innerHTML = '';
  el.style.cssText = 'display:flex;align-items:flex-end;gap:0.1875rem;height:100%;min-height:5rem;padding-top:0.5rem';
  
  days.forEach(([s,r]) => {
    const col = document.createElement('div');
    col.style.cssText = 'flex:1;display:flex;flex-direction:column;justify-content:flex-end;height:100%;gap:0.0625rem';
    
    // Use relative percentage heights to constrain within the parent container's dynamic boundaries
    col.innerHTML = `
      <div style="width:100%;height:${(s/max)*95}%;background:var(--eaf);border-radius:0.125rem 0.125rem 0 0;min-height:0.25rem" title="SMS: ${s} MT"></div>
      <div style="width:100%;height:${(r/max)*95}%;background:var(--rm);border-radius:0 0 0.125rem 0.125rem;min-height:0.25rem" title="RM: ${r} MT"></div>
    `;
    el.appendChild(col);
  });
}"""

# Use regex to replace the old function block safely
# It starts with "function buildTputChart(" and ends with the next "function "
start_idx = text.find('function buildTputChart(')
next_func_idx = text.find('function ', start_idx + 20)

if start_idx != -1 and next_func_idx != -1:
    old_func_block = text[start_idx:next_func_idx].strip()
    text = text[:start_idx] + new_func + '\n\n' + text[next_func_idx:]
    with open(r'c:\Users\bhadk\Documents\APS\ui_design\index.html', 'w', encoding='utf-8') as f:
        f.write(text)
    print("Fixed buildTputChart successfully.")
else:
    print("Could not locate boundaries for buildTputChart.")
