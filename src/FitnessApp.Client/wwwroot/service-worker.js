importScripts('/js/push-state.js');
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
// Serialize display and local logout acknowledgements so logout also closes an in-flight display.
let displayWork = Promise.resolve();
function serializeDisplay(action) {
    const next = displayWork.then(action, action);
    displayWork = next.catch(() => {});
    return next;
}
self.addEventListener('message', event => {
    if (event.data?.type !== 'clear-push-device') return;
    event.waitUntil(serializeDisplay(async () => {
        await fitnessPushState.write(null);
        for (const notification of await self.registration.getNotifications()) notification.close();
        event.ports[0]?.postMessage({ cleared: true });
    }));
});
// No fetch handler or caches: private APIs, tokens and offline writes are never stored here.
self.addEventListener('push', event => event.waitUntil(serializeDisplay(async () => {
    let payload = {};
    try { payload = event.data?.json() || {}; } catch { }
    let state = null;
    try { state = await fitnessPushState.read(); } catch { }
    const recipientMatches = state?.binding && state.binding === payload.binding;
    const detailed = recipientMatches && state.previews === true;
    const path = recipientMatches && /^\/notifikationer\/[a-f0-9-]{36}$/i.test(payload.path || '')
        ? payload.path : '/indstillinger/notifikationer';
    // Even malformed or stale-recipient push always displays a visible generic notification.
    await self.registration.showNotification(detailed && typeof payload.title === 'string' ? payload.title : 'FitnessApp', {
        body: detailed && typeof payload.body === 'string' ? payload.body : 'Åbn FitnessApp for at se dine beskeder.',
        icon: '/icons/icon-192.png', badge: '/icons/badge-96.png',
        tag: recipientMatches && /^[a-f0-9-]{36}$/i.test(payload.id || '') ? payload.id : 'fitness-message',
        data: { path, binding: recipientMatches ? state.binding : null }
    });
})));
self.addEventListener('notificationclick', event => {
    event.notification.close();
    event.waitUntil((async () => {
        let state = null;
        try { state = await fitnessPushState.read(); } catch { }
        const data = event.notification.data || {};
        const path = state?.binding && state.binding === data.binding && /^\/notifikationer\/[a-f0-9-]{36}$/i.test(data.path || '')
            ? data.path : '/indstillinger/notifikationer';
        const url = new URL(path, self.location.origin).href;
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        const existing = windows.find(client => new URL(client.url).origin === self.location.origin);
        if (existing) { await existing.navigate(url); await existing.focus(); }
        else await self.clients.openWindow(url);
    })());
});
