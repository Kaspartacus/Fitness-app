// Credentials exist only in HttpOnly cookies and transient response memory. This file never caches them.
window.fitnessSession = (() => {
    const marker = 'fitnessapp.logout-pending'; // Non-secret intent only; prevents restore after offline logout.
    const channel = typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel('fitnessapp-session') : null;
    let receiver;
    let lastActivity = 0;
    let generation = 0;
    const pendingLogout = () => localStorage.getItem(marker) === '1';
    const withLock = action => navigator.locks
        ? navigator.locks.request('fitnessapp-session-cookie', action)
        : Promise.reject(new Error('Secure session synchronization is unavailable.'));
    function changed() {
        generation++;
        channel?.postMessage('changed');
    }
    channel?.addEventListener('message', () => {
        generation++;
        receiver?.invokeMethodAsync('SessionChanged');
    });
    window.addEventListener('storage', event => {
        if (event.key === marker && event.newValue === '1') {
            generation++;
            receiver?.invokeMethodAsync('SessionChanged');
        }
    });
    function activity(event) {
        if (!event.isTrusted || document.visibilityState !== 'visible' || pendingLogout()) return;
        const now = Date.now();
        if (now - lastActivity < 60000) return;
        lastActivity = now;
        receiver?.invokeMethodAsync('UserActivity');
    }
    return {
        initialize(dotnet) {
            receiver = dotnet;
            document.addEventListener('visibilitychange', activity, { passive: true });
            for (const type of ['pointerdown', 'keydown', 'touchstart', 'wheel'])
                document.addEventListener(type, activity, { passive: true });
        },
        foregroundOpening: () => document.visibilityState === 'visible' && document.hasFocus() &&
            performance.getEntriesByType('navigation')[0]?.type !== 'reload',
        pendingLogout,
        suppress() { localStorage.setItem(marker, '1'); changed(); },
        request(path, body) {
            const requestedGeneration = generation;
            return withLock(async () => {
                if (requestedGeneration !== generation) return { status: 409, body: '' };
                if (path === 'refresh' && pendingLogout()) return { status: 401, body: '' };
                if (path === 'login') await window.fitnessPush.clear();
                const response = await fetch('/api/auth/' + path, {
                    method: 'POST', credentials: 'same-origin', cache: 'no-store',
                    headers: { 'Content-Type': 'application/json', 'X-FitnessApp-Session': '1' },
                    body: body ? JSON.stringify(body) : undefined
                });
                const result = { status: response.status, body: await response.text() };
                if (response.ok && path === 'login') {
                    // A logout/account transition can supersede login while fetch is in flight.
                    // Its persistent suppression intent must survive this stale response.
                    if (requestedGeneration !== generation) return { status: 409, body: '' };
                    localStorage.removeItem(marker);
                    changed();
                }
                if (response.ok && path === 'logout') {
                    localStorage.removeItem(marker);
                    changed();
                }
                return result;
            });
        }
    };
})();
