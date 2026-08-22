(function () {
  const states = new WeakMap();
  const EDGE_SCROLL_PX = 48;
  const EDGE_SCROLL_SPEED = 18;
  const PREFERENCES_KEY = 'aps.gantt.preferences.v1';

  function initialize(root, dotnet) {
    if (!root || states.has(root)) return;
    const state = { dotnet, drag: null, pan: null, split: null, guide: null, autoScrollFrame: null, metricsFrame: null, lastRows: '' };

    function preferences() {
      try { return JSON.parse(localStorage.getItem(PREFERENCES_KEY) || '{}'); }
      catch { return {}; }
    }

    function requestMetrics() {
      if (state.metricsFrame) return;
      state.metricsFrame = requestAnimationFrame(async () => {
        state.metricsFrame = null;
        const timeline = root.querySelector('[data-gantt-timeline]');
        const scroller = root.querySelector('[data-gantt-scroll]');
        if (!timeline || !scroller) return;
        const rowHeight = parseFloat(getComputedStyle(timeline).getPropertyValue('--aps-gantt-row-height')) || 60;
        const headerHeight = 56;
        const firstRow = Math.max(0, Math.floor(Math.max(0, scroller.scrollTop - headerHeight) / rowHeight));
        const visibleRows = Math.max(1, Math.ceil(scroller.clientHeight / rowHeight));
        const rowKey = `${firstRow}:${visibleRows}`;
        if (rowKey !== state.lastRows) {
          state.lastRows = rowKey;
          await dotnet.invokeMethodAsync('SetVisibleRowRange', firstRow, visibleRows);
        }
        await dotnet.invokeMethodAsync('UpdateViewportMetrics', timeline.clientWidth, root.clientWidth);
      });
    }

    function currentLane(clientX, clientY) {
      const lane = document.elementFromPoint(clientX, clientY)?.closest?.('[data-resource-id]');
      return lane && root.contains(lane) ? lane : null;
    }

    function snap(grid, clientX, drag) {
      const rect = grid.getBoundingClientRect();
      if (rect.width <= 0) return null;
      const start = new Date(grid.dataset.windowStart).getTime();
      const end = new Date(grid.dataset.windowEnd).getTime();
      const duration = end - start;
      if (!(duration > 0)) return null;
      const rawRatio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
      let candidate = start + rawRatio * duration - drag.durationMs * drag.grabRatio;
      const increments = { Hour: 60, ThirtyMinutes: 30, FifteenMinutes: 15, FiveMinutes: 5 };
      if (drag.snapMode === 'ShiftBoundary') {
        const boundaries = (grid.dataset.shiftBoundaries || '').split(',').filter(Boolean)
          .map(value => new Date(value).getTime()).filter(Number.isFinite);
        if (!boundaries.length) return { unavailable: true };
        candidate = boundaries.reduce((nearest, value) => Math.abs(value - candidate) < Math.abs(nearest - candidate) ? value : nearest);
      } else if (increments[drag.snapMode]) {
        const incrementMs = increments[drag.snapMode] * 60000;
        const day = new Date(candidate);
        const dayStart = Date.UTC(day.getUTCFullYear(), day.getUTCMonth(), day.getUTCDate());
        candidate = dayStart + Math.round((candidate - dayStart) / incrementMs) * incrementMs;
      }
      const ratio = (candidate - start) / duration;
      return { candidate, iso: new Date(candidate).toISOString(), ratio, x: rect.left + ratio * rect.width };
    }

    function clearGuide() {
      state.guide?.remove();
      state.guide = null;
    }

    function clearLaneHighlight() {
      root.querySelectorAll('.aps-lane-drop-eligible,.aps-lane-drop-ineligible,.aps-lane-drop-checking')
        .forEach(el => el.classList.remove('aps-lane-drop-eligible', 'aps-lane-drop-ineligible', 'aps-lane-drop-checking'));
    }

    function stopAutoScroll() {
      if (state.autoScrollFrame) cancelAnimationFrame(state.autoScrollFrame);
      state.autoScrollFrame = null;
    }

    function autoScroll(clientX, clientY) {
      const scroller = root.querySelector('[data-gantt-scroll]');
      if (!scroller) return;
      const rect = scroller.getBoundingClientRect();
      stopAutoScroll();
      const edgeVelocity = (value, low, high) => value < low + EDGE_SCROLL_PX
        ? -Math.min(1, (low + EDGE_SCROLL_PX - value) / EDGE_SCROLL_PX)
        : value > high - EDGE_SCROLL_PX
          ? Math.min(1, (value - high + EDGE_SCROLL_PX) / EDGE_SCROLL_PX)
          : 0;
      const velocityX = edgeVelocity(clientX, rect.left, rect.right);
      const velocityY = edgeVelocity(clientY, rect.top, rect.bottom);
      if (!velocityX && !velocityY) return;
      const tick = () => {
        scroller.scrollLeft += velocityX * EDGE_SCROLL_SPEED;
        scroller.scrollTop += velocityY * EDGE_SCROLL_SPEED;
        state.autoScrollFrame = requestAnimationFrame(tick);
      };
      state.autoScrollFrame = requestAnimationFrame(tick);
    }

    function setFeedback(drag, text, tone) {
      if (!drag.feedback) {
        drag.feedback = document.createElement('div');
        drag.feedback.className = 'aps-drag-feedback';
        document.body.appendChild(drag.feedback);
      }
      drag.feedback.dataset.tone = tone;
      drag.feedback.textContent = text;
      drag.feedback.style.left = `${drag.lastX + 14}px`;
      drag.feedback.style.top = `${drag.lastY + 16}px`;
    }

    function activateDrag(drag) {
      drag.active = true;
      drag.ghost = drag.block.cloneNode(true);
      drag.ghost.removeAttribute('id');
      drag.ghost.classList.add('aps-operation-ghost');
      Object.assign(drag.ghost.style, {
        position: 'fixed', left: `${drag.rect.left}px`, top: `${drag.rect.top}px`,
        width: `${drag.rect.width}px`, height: `${drag.rect.height}px`, zIndex: '1000', pointerEvents: 'none'
      });
      document.body.appendChild(drag.ghost);
      drag.block.classList.add('aps-operation-source-dragging');
      document.body.style.cursor = 'grabbing';
    }

    function cleanupDrag(drag) {
      drag?.ghost?.remove();
      drag?.feedback?.remove();
      drag?.block?.classList.remove('aps-operation-source-dragging');
      document.body.style.cursor = '';
      clearLaneHighlight();
      clearGuide();
      stopAutoScroll();
    }

    const down = event => {
      if (event.button !== 0) return;
      const splitter = event.target.closest('[data-gantt-splitter]');
      if (splitter && root.contains(splitter)) {
        state.split = { host: splitter.parentElement, startX: event.clientX, startWidth: parseFloat(getComputedStyle(splitter.parentElement).getPropertyValue('--aps-gantt-grid-width')) || 320 };
        splitter.setPointerCapture?.(event.pointerId);
        document.body.style.cursor = 'col-resize';
        event.preventDefault();
        return;
      }
      const block = event.target.closest('.aps-operation');
      if (!block || !root.contains(block)) {
        const grid = event.target.closest('.aps-time-grid');
        if (grid && root.contains(grid)) {
          state.pan = { grid, startX: event.clientX, dx: 0 };
          grid.setPointerCapture?.(event.pointerId);
        }
        return;
      }
      if (block.dataset.dragProtected === 'true') return;
      const rect = block.getBoundingClientRect();
      state.drag = {
        block,
        planningKey: block.dataset.planningKey,
        startX: event.clientX,
        startY: event.clientY,
        lastX: event.clientX,
        lastY: event.clientY,
        rect,
        grabRatio: Math.max(0, Math.min(1, (event.clientX - rect.left) / Math.max(1, rect.width))),
        grabY: event.clientY - rect.top,
        durationMs: Number(block.dataset.durationMs),
        eligibleResources: new Set((block.dataset.eligibleResources || '').split(',').filter(Boolean)),
        snapMode: block.dataset.snapMode || 'FifteenMinutes',
        frozen: block.dataset.frozen === 'true'
      };
      block.setPointerCapture?.(event.pointerId);
    };

    const move = event => {
      if (state.split) {
        const available = root.clientWidth;
        const width = Math.max(220, Math.min(available * .45, state.split.startWidth + event.clientX - state.split.startX));
        state.split.width = width;
        state.split.host.style.setProperty('--aps-gantt-grid-width', `${width}px`);
        event.preventDefault();
        return;
      }
      if (state.pan) {
        state.pan.dx = event.clientX - state.pan.startX;
        if (Math.abs(state.pan.dx) > 4) document.body.style.cursor = 'grabbing';
        event.preventDefault();
        return;
      }
      if (!state.drag) return;
      const dx = event.clientX - state.drag.startX, dy = event.clientY - state.drag.startY;
      state.drag.lastX = event.clientX;
      state.drag.lastY = event.clientY;
      if (!state.drag.active) {
        if (Math.abs(dx) + Math.abs(dy) < 4) return;
        activateDrag(state.drag);
      }
      Object.assign(state.drag.ghost.style, {
        left: `${event.clientX - state.drag.rect.width * state.drag.grabRatio}px`,
        top: `${event.clientY - state.drag.grabY}px`
      });
      event.preventDefault();

      const lane = currentLane(event.clientX, event.clientY);
      const grid = lane?.querySelector?.('.aps-time-grid');
      if (lane && grid) {
        const eligible = state.drag.eligibleResources.has(lane.dataset.resourceId);
        if (lane !== state.drag.lastLane) {
          clearLaneHighlight();
          lane.classList.add(eligible ? 'aps-lane-drop-eligible' : 'aps-lane-drop-ineligible');
          state.drag.lastLane = lane;
        }
        state.drag.lastEligible = eligible;
        const snapped = eligible ? snap(grid, event.clientX, state.drag) : null;
        state.drag.lastCandidate = snapped;
        if (eligible && snapped && !snapped.unavailable) {
          if (!state.guide) {
            state.guide = document.createElement('div');
            state.guide.className = 'aps-snap-guide';
            grid.appendChild(state.guide);
          } else if (state.guide.parentElement !== grid) {
            grid.appendChild(state.guide);
          }
          const gridRect = grid.getBoundingClientRect();
          state.guide.style.left = `${snapped.x - gridRect.left}px`;
          const start = new Date(snapped.candidate);
          const end = new Date(snapped.candidate + state.drag.durationMs);
          const deltaMinutes = Math.round((snapped.candidate - new Date(state.drag.block.dataset.operationStart).getTime()) / 60000);
          setFeedback(state.drag, `${start.toISOString().slice(11,16)}–${end.toISOString().slice(11,16)} · ${deltaMinutes >= 0 ? '+' : ''}${deltaMinutes} min${state.drag.frozen ? ' · override required' : ''}`, state.drag.frozen ? 'warning' : 'eligible');
        } else if (eligible && snapped?.unavailable) {
          clearGuide();
          state.drag.lastCandidate = null;
          setFeedback(state.drag, 'Shift calendar unavailable for resource', 'ineligible');
        } else {
          clearGuide();
          setFeedback(state.drag, 'Resource not eligible', 'ineligible');
        }
      } else {
        clearLaneHighlight();
        clearGuide();
        state.drag.lastLane = null;
        state.drag.lastEligible = false;
        state.drag.lastCandidate = null;
        setFeedback(state.drag, 'Move over an eligible resource lane', 'neutral');
      }
      autoScroll(event.clientX, event.clientY);
    };

    const up = async event => {
      if (state.split) {
        const split = state.split;
        state.split = null;
        document.body.style.cursor = '';
        await dotnet.invokeMethodAsync('SetGridWidth', split.width ?? split.startWidth, root.clientWidth);
        requestMetrics();
        return;
      }
      if (state.pan) {
        const pan = state.pan;
        state.pan = null;
        document.body.style.cursor = '';
        const width = pan.grid.getBoundingClientRect().width;
        if (Math.abs(pan.dx) > 4 && width > 0) await dotnet.invokeMethodAsync('PanViewport', -pan.dx / width);
        return;
      }
      const drag = state.drag;
      if (!drag) return;
      state.drag = null;
      cleanupDrag(drag);
      if (!drag.active) return;

      const lane = currentLane(event.clientX, event.clientY);
      const grid = lane?.querySelector?.('.aps-time-grid');
      if (!lane || !grid || !drag.eligibleResources.has(lane.dataset.resourceId)) return;
      const snapped = snap(grid, event.clientX, drag);
      if (!snapped || snapped.unavailable) return;
      await dotnet.invokeMethodAsync('StageDraggedMove', drag.planningKey, lane.dataset.resourceId, snapped.iso);
    };

    const cancel = () => {
      if (!state.drag) return;
      const drag = state.drag;
      state.drag = null;
      cleanupDrag(drag);
    };

    const keydown = event => { if (event.key === 'Escape') cancel(); };
    const operationKeydown = event => {
      if (event.target.closest?.('.aps-operation') && ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key))
        event.preventDefault();
    };
    const fullscreenChanged = () => dotnet.invokeMethodAsync('FullscreenChanged', document.fullscreenElement === root);

    const wheel = event => {
      if (!event.ctrlKey || !event.target.closest('[data-gantt-timeline]')) return;
      const timeline = root.querySelector('[data-gantt-timeline]');
      if (!timeline) return;
      const rect = timeline.getBoundingClientRect();
      if (rect.width <= 0) return;
      event.preventDefault();
      const ratio = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width));
      const direction = event.deltaY > 0 ? 1 : -1;
      dotnet.invokeMethodAsync('ZoomAt', direction, ratio);
    };

    const scroller = root.querySelector('[data-gantt-scroll]');
    const resizeObserver = new ResizeObserver(requestMetrics);
    resizeObserver.observe(root);
    if (scroller) scroller.addEventListener('scroll', requestMetrics, { passive: true });

    root.addEventListener('pointerdown', down);
    root.addEventListener('pointermove', move);
    root.addEventListener('pointerup', up);
    root.addEventListener('pointercancel', cancel);
    root.addEventListener('wheel', wheel, { passive: false });
    root.addEventListener('keydown', operationKeydown);
    window.addEventListener('keydown', keydown);
    window.addEventListener('blur', cancel);
    document.addEventListener('fullscreenchange', fullscreenChanged);
    dotnet.invokeMethodAsync('ApplyGanttPreferences', JSON.stringify(preferences()), root.clientWidth).then(requestMetrics);
    state.cleanup = () => {
      root.removeEventListener('pointerdown', down);
      root.removeEventListener('pointermove', move);
      root.removeEventListener('pointerup', up);
      root.removeEventListener('pointercancel', cancel);
      root.removeEventListener('wheel', wheel);
      root.removeEventListener('keydown', operationKeydown);
      window.removeEventListener('keydown', keydown);
      window.removeEventListener('blur', cancel);
      document.removeEventListener('fullscreenchange', fullscreenChanged);
      if (scroller) scroller.removeEventListener('scroll', requestMetrics);
      resizeObserver.disconnect();
      if (state.metricsFrame) cancelAnimationFrame(state.metricsFrame);
      stopAutoScroll();
    };
    states.set(root, state);
  }

  function savePreference(key, value) {
    const current = (() => { try { return JSON.parse(localStorage.getItem(PREFERENCES_KEY) || '{}'); } catch { return {}; } })();
    current[key] = value;
    localStorage.setItem(PREFERENCES_KEY, JSON.stringify(current));
  }

  function dispose(root) { const state = states.get(root); state?.cleanup?.(); states.delete(root); }
  function focusOperation(planningKey) {
    const escaped = window.CSS?.escape ? window.CSS.escape(planningKey) : planningKey.replace(/["\\]/g, '\\$&');
    document.querySelector(`.aps-operation[data-planning-key="${escaped}"]`)?.focus();
  }
  function focusContextMenu() { document.querySelector('[data-gantt-context-menu] [role="menuitem"]')?.focus(); }
  function copyText(value) { return navigator.clipboard.writeText(value); }
  async function toggleFullscreen(root) {
    if (document.fullscreenElement === root) await document.exitFullscreen();
    else if (root?.requestFullscreen) await root.requestFullscreen();
    else throw new Error('Fullscreen is not supported by this host.');
  }
  window.apsPlanningWorkbench = { initialize, dispose, savePreference, focusOperation, focusContextMenu, copyText, toggleFullscreen };
})();
