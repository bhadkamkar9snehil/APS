(function () {
  const states = new WeakMap();
  function initialize(root, dotnet) {
    if (!root || states.has(root)) return;
    const state = { dotnet, drag: null };
    const down = event => {
      if (event.button !== 0) return;
      const block = event.target.closest('.aps-operation');
      if (!block || !root.contains(block)) return;
      state.drag = { block, planningKey: block.dataset.planningKey, startX: event.clientX, startY: event.clientY };
      block.setPointerCapture?.(event.pointerId);
    };
    const move = event => {
      if (!state.drag) return;
      const dx = event.clientX - state.drag.startX, dy = event.clientY - state.drag.startY;
      if (Math.abs(dx) + Math.abs(dy) < 4) return;
      Object.assign(state.drag.block.style, { transform: `translate(${dx}px, ${dy}px)`, zIndex: '60', opacity: '.72' });
      document.body.style.cursor = 'grabbing';
      event.preventDefault();
    };
    const up = async event => {
      const drag = state.drag;
      if (!drag) return;
      state.drag = null;
      Object.assign(drag.block.style, { transform: '', zIndex: '', opacity: '' });
      document.body.style.cursor = '';
      if (Math.abs(event.clientX - drag.startX) + Math.abs(event.clientY - drag.startY) < 4) return;
      const lane = document.elementFromPoint(event.clientX, event.clientY)?.closest?.('[data-resource-id]');
      const grid = lane?.querySelector?.('.aps-time-grid');
      if (!lane || !grid || !root.contains(lane)) return;
      const rect = grid.getBoundingClientRect();
      const ratio = rect.width > 0 ? Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width)) : 0;
      await dotnet.invokeMethodAsync('StageDraggedMove', drag.planningKey, lane.dataset.resourceId, ratio);
    };
    root.addEventListener('pointerdown', down);
    root.addEventListener('pointermove', move);
    root.addEventListener('pointerup', up);
    root.addEventListener('pointercancel', up);
    state.cleanup = () => { root.removeEventListener('pointerdown', down); root.removeEventListener('pointermove', move); root.removeEventListener('pointerup', up); root.removeEventListener('pointercancel', up); };
    states.set(root, state);
  }
  function dispose(root) { const state = states.get(root); state?.cleanup?.(); states.delete(root); }
  window.apsPlanningWorkbench = { initialize, dispose };
})();
