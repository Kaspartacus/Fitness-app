window.fitnessDiary = (() => {
    const instances = new Map();
    const inactivityLimit = 15 * 60 * 1000;
    function stop(key) {
        const old = instances.get(key);
        if (!old) return;
        old.stopped = true;
        clearTimeout(old.timer);
        for (const type of ['pointerdown', 'keydown', 'input', 'scroll']) old.root.removeEventListener(type, old.activity, true);
        window.removeEventListener('pagehide', old.hide);
        window.removeEventListener('pageshow', old.show);
        window.removeEventListener('focus', old.resume);
        document.removeEventListener('visibilitychange', old.resume);
        document.removeEventListener('resume', old.resume);
        instances.delete(key);
    }
    function start(key, root, callback) {
        stop(key);
        const state = { root, timer: null, sent: 0, locked: false, stopped: false, deadline: Date.now() + inactivityLimit };
        const lock = () => {
            if (state.locked || state.stopped) return;
            state.locked = true;
            clearTimeout(state.timer);
            // Hide content synchronously, including when the runtime or network is unavailable.
            root.hidden = true;
            for (const field of root.querySelectorAll('textarea, input')) field.value = '';
            callback.invokeMethodAsync('IdleLock').catch(() => {});
        };
        const checkDeadline = () => {
            if (state.locked || state.stopped) return true;
            if (Date.now() < state.deadline) return false;
            lock();
            return true;
        };
        const arm = () => {
            clearTimeout(state.timer);
            if (state.locked || state.stopped) return;
            state.timer = setTimeout(() => {
                if (!checkDeadline()) arm();
            }, Math.max(0, state.deadline - Date.now()));
        };
        state.activity = event => {
            if (!event.isTrusted || document.visibilityState !== 'visible' || !document.hasFocus() || checkDeadline()) return;
            state.deadline = Date.now() + inactivityLimit;
            arm();
            if (Date.now() - state.sent < 30000) return;
            state.sent = Date.now();
            callback.invokeMethodAsync('DiaryActivity').catch(lock);
        };
        state.hide = lock;
        state.resume = () => { checkDeadline(); };
        state.show = event => { if (event.persisted) lock(); else checkDeadline(); };
        for (const type of ['pointerdown', 'keydown', 'input', 'scroll']) root.addEventListener(type, state.activity, true);
        window.addEventListener('pagehide', state.hide);
        window.addEventListener('pageshow', state.show);
        window.addEventListener('focus', state.resume);
        document.addEventListener('visibilitychange', state.resume);
        document.addEventListener('resume', state.resume);
        instances.set(key, state); arm();
    }
    return { start, stop };
})();
