(function () {
  const states = new WeakMap();
  const SNAP_MINUTES = 15;
  const EDGE_SCROLL_PX = 48;
  const EDGE_SCROLL_SPEED = 14;

  function initialize(root, dotnet) {
    if (!root || states.has(root)) return;
    const state = { dotnet, drag: null, guide: null, scrollTimer: null };

    function currentLane(clientX, clientY) {
      const lane = document.elementFromPoint(clientX, clientY)?.closest?.('[data-resource-id]');
      return lane && root.contains(lane) ? lane : null;
    }

    // Snaps a pointer position to the nearest 15-minute grid line within a lane's time window,
    // returning both the ratio (for staging the move) and the pixel x to draw the guide at.
    function snap(grid, clientX) {
      const rect = grid.getBoundingClientRect();
      if (rect.width <= 0) return null;
      const start = new Date(grid.dataset.windowStart).getTime();
      const end = new Date(grid.dataset.windowEnd).getTime();
      const totalMinutes = (end - start) / 60000;
      if (!(totalMinutes > 0)) return null;
      const rawRatio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
      const rawMinutes = rawRatio * totalMinutes;
      const snappedMinutes = Math.round(rawMinutes / SNAP_MINUTES) * SNAP_MINUTES;
      const ratio = Math.max(0, Math.min(1, snappedMinutes / totalMinutes));
      return { ratio, x: rect.left + ratio * rect.width };
    }

    function clearGuide() {
      state.guide?.remove();
      state.guide = null;
    }

    function clearLaneHighlight() {
      root.querySelectorAll('.aps-lane-drop-target').forEach(el => el.classList.remove('aps-lane-drop-target'));
    }

    function stopAutoScroll() {
      if (state.scrollTimer) { clearInterval(state.scrollTimer); state.scrollTimer = null; }
    }

    // Keeps a drag usable on a horizon wider than the viewport: without this, a target off-screen to
    // either side is simply unreachable, since the pointer can't drag the container and the block at
    // the same time.
    function autoScroll(clientX) {
      const scroller = root.querySelector('[data-gantt-scroll]');
      if (!scroller) return;
      const rect = scroller.getBoundingClientRect();
      stopAutoScroll();
      let direction = 0;
      if (clientX < rect.left + EDGE_SCROLL_PX) direction = -1;
      else if (clientX > rect.right - EDGE_SCROLL_PX) direction = 1;
      if (direction === 0) return;
      state.scrollTimer = setInterval(() => { scroller.scrollLeft += direction * EDGE_SCROLL_SPEED; }, 16);
    }

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
      if (!state.drag.active) {
        if (Math.abs(dx) + Math.abs(dy) < 4) return;
        state.drag.active = true;
        state.drag.block.classList.add('aps-operation-dragging');
      }
      Object.assign(state.drag.block.style, { transform: `translate(${dx}px, ${dy}px) scale(1.03)`, zIndex: '60', opacity: '.85' });
      document.body.style.cursor = 'grabbing';
      event.preventDefault();

      const lane = currentLane(event.clientX, event.clientY);
      const grid = lane?.querySelector?.('.aps-time-grid');
      if (lane && grid) {
        if (lane !== state.drag.lastLane) {
          clearLaneHighlight();
          lane.classList.add('aps-lane-drop-target');
          state.drag.lastLane = lane;
        }
        const snapped = snap(grid, event.clientX);
        if (snapped) {
          if (!state.guide) {
            state.guide = document.createElement('div');
            state.guide.className = 'aps-snap-guide';
            grid.appendChild(state.guide);
          } else if (state.guide.parentElement !== grid) {
            grid.appendChild(state.guide);
          }
          const gridRect = grid.getBoundingClientRect();
          state.guide.style.left = `${snapped.x - gridRect.left}px`;
        }
      } else {
        clearLaneHighlight();
        clearGuide();
        state.drag.lastLane = null;
      }
      autoScroll(event.clientX);
    };

    const up = async event => {
      const drag = state.drag;
      if (!drag) return;
      state.drag = null;
      drag.block.classList.remove('aps-operation-dragging');
      Object.assign(drag.block.style, { transform: '', zIndex: '', opacity: '' });
      document.body.style.cursor = '';
      clearLaneHighlight();
      clearGuide();
      stopAutoScroll();
      if (!drag.active) return;

      const lane = currentLane(event.clientX, event.clientY);
      const grid = lane?.querySelector?.('.aps-time-grid');
      if (!lane || !grid) return;
      const snapped = snap(grid, event.clientX);
      if (!snapped) return;
      await dotnet.invokeMethodAsync('StageDraggedMove', drag.planningKey, lane.dataset.resourceId, snapped.ratio);
    };

    root.addEventListener('pointerdown', down);
    root.addEventListener('pointermove', move);
    root.addEventListener('pointerup', up);
    root.addEventListener('pointercancel', up);
    state.cleanup = () => {
      root.removeEventListener('pointerdown', down);
      root.removeEventListener('pointermove', move);
      root.removeEventListener('pointerup', up);
      root.removeEventListener('pointercancel', up);
      stopAutoScroll();
    };
    states.set(root, state);
  }

  function dispose(root) { const state = states.get(root); state?.cleanup?.(); states.delete(root); }
  window.apsPlanningWorkbench = { initialize, dispose };
})();
