export function attach(editor, dotNet) {
    const events = new AbortController();
    const options = { signal: events.signal };
    let drag;
    let moving = false;
    let animationFrame = 0;

    const rowOf = (element) => element?.closest('[data-exercise-index]');
    const indexOf = (row) => Number(row.dataset.exerciseIndex);
    const handleOf = (element) => element.closest('[data-exercise-handle]:not(:disabled)');

    async function move(source, targetIndex, handle) {
        if (moving || targetIndex === indexOf(source) ||
            !editor.querySelector(`[data-exercise-index="${targetIndex}"]`)) {
            return;
        }
        moving = true;
        try {
            await dotNet.invokeMethodAsync('MoveExercise', indexOf(source), targetIndex);
            if (handle.isConnected) {
                handle.focus({ preventScroll: true });
                handle.scrollIntoView({ block: 'nearest' });
            }
        } finally {
            moving = false;
        }
    }

    function findTarget() {
        const row = rowOf(document.elementFromPoint(drag.x, drag.y));
        const target = row && row !== drag.source && editor.contains(row) ? row : null;
        if (target !== drag.target) {
            drag.target?.classList.remove('is-drop-target');
            target?.classList.add('is-drop-target');
            drag.target = target;
        }
    }

    function scrollNearEdge() {
        if (!drag?.started) return;
        const delta = drag.y < 64 ? -12 : drag.y > window.innerHeight - 64 ? 12 : 0;
        if (delta) {
            window.scrollBy(0, delta);
            findTarget();
        }
        animationFrame = requestAnimationFrame(scrollNearEdge);
    }

    function clearDrag() {
        if (!drag) return;
        drag.source.classList.remove('is-reordering');
        drag.target?.classList.remove('is-drop-target');
        cancelAnimationFrame(animationFrame);
        drag = undefined;
    }

    editor.addEventListener('pointerdown', (event) => {
        const handle = handleOf(event.target);
        if (!handle || !event.isPrimary || event.button !== 0 || moving || drag) return;
        const source = rowOf(handle);
        drag = { handle, source, pointerId: event.pointerId, startX: event.clientX, startY: event.clientY };
        handle.setPointerCapture(event.pointerId);
    }, options);

    editor.addEventListener('pointermove', (event) => {
        if (drag?.pointerId !== event.pointerId) return;
        drag.x = event.clientX;
        drag.y = event.clientY;
        if (!drag.started) {
            if (Math.hypot(drag.x - drag.startX, drag.y - drag.startY) < 5) return;
            drag.started = true;
            drag.source.classList.add('is-reordering');
            animationFrame = requestAnimationFrame(scrollNearEdge);
        }
        findTarget();
    }, options);

    editor.addEventListener('pointerup', (event) => {
        if (drag?.pointerId !== event.pointerId) return;
        const { source, target, handle } = drag;
        clearDrag();
        if (target) void move(source, indexOf(target), handle);
    }, options);

    for (const eventName of ['pointercancel', 'lostpointercapture']) {
        editor.addEventListener(eventName, (event) => {
            if (drag?.pointerId === event.pointerId) clearDrag();
        }, options);
    }

    editor.addEventListener('keydown', (event) => {
        const handle = handleOf(event.target);
        if (!handle || drag || !['ArrowUp', 'ArrowDown'].includes(event.key)) return;
        event.preventDefault();
        const row = rowOf(handle);
        const targetIndex = indexOf(row) + (event.key === 'ArrowUp' ? -1 : 1);
        void move(row, targetIndex, handle);
    }, options);

    return {
        dispose() {
            events.abort();
            clearDrag();
        },
    };
}
