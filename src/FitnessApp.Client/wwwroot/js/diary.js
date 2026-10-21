window.fitnessDiary = (() => {
    const instances = new Map();
    function stop(key) {
        const old = instances.get(key);
        if (!old) return;
        clearTimeout(old.timer);
        for (const type of ['pointerdown', 'keydown', 'input', 'scroll']) old.root.removeEventListener(type, old.activity, true);
        window.removeEventListener('pagehide', old.hide);
        window.removeEventListener('pageshow', old.show);
        instances.delete(key);
    }
    function start(key, root, callback) {
        stop(key);
        const state = { root, timer: null, sent: 0, locked: false };
        const lock = () => {
            if (state.locked) return;
            state.locked = true;
            // Hide content synchronously, including when the runtime or network is unavailable.
            root.hidden = true;
            for (const field of root.querySelectorAll('textarea, input')) field.value = '';
            callback.invokeMethodAsync('IdleLock').catch(() => {});
        };
        const arm = () => { clearTimeout(state.timer); state.timer = setTimeout(lock, 15 * 60 * 1000); };
        state.activity = event => {
            if (!event.isTrusted || document.visibilityState !== 'visible' || state.locked) return;
            arm();
            if (Date.now() - state.sent < 30000) return;
            state.sent = Date.now();
            callback.invokeMethodAsync('DiaryActivity').catch(lock);
        };
        state.hide = lock;
        state.show = event => { if (event.persisted) lock(); };
        for (const type of ['pointerdown', 'keydown', 'input', 'scroll']) root.addEventListener(type, state.activity, true);
        window.addEventListener('pagehide', state.hide);
        window.addEventListener('pageshow', state.show);
        instances.set(key, state); arm();
    }
    return { start, stop };
})();
