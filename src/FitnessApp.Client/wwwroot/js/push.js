(() => {
    const supported = () => isSecureContext && 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
    const ready = 'serviceWorker' in navigator && isSecureContext
        ? navigator.serviceWorker.register('/service-worker.js', { scope: '/', updateViaCache: 'none' })
        : Promise.resolve(null);
    ready.catch(() => {});
    window.fitnessPush = {
        async status() {
            const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
            const installed = matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
            if (!supported()) return { supported: false, ios, installed, permission: 'default', subscribed: false, binding: null };
            const registration = await ready;
            const state = await fitnessPushState.read();
            const subscription = registration ? await registration.pushManager.getSubscription() : null;
            return { supported: true, ios, installed, permission: Notification.permission, subscribed: !!subscription, binding: state?.binding || null };
        },
        async enable(publicKey, detailed) {
            // Call before the first await: Safari requires a direct user gesture.
            const permission = Notification.requestPermission();
            if (await permission !== 'granted') throw new Error('Permission was not granted');
            await ready;
            const registration = await navigator.serviceWorker.ready;
            let subscription = await registration.pushManager.getSubscription();
            let state = await fitnessPushState.read();
            if (subscription && !state) { await subscription.unsubscribe(); subscription = null; }
            const bytes = Uint8Array.from(atob(publicKey.replace(/-/g, '+').replace(/_/g, '/')), c => c.charCodeAt(0));
            subscription ||= await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: bytes });
            state ||= { binding: crypto.randomUUID().replace(/-/g, '') };
            state.previews = !!detailed;
            await fitnessPushState.write(state);
            const data = subscription.toJSON();
            return { endpoint: data.endpoint, p256dh: data.keys.p256dh, auth: data.keys.auth, binding: state.binding,
                installationId: await fitnessPushState.installation() };
        },
        async previews(enabled) {
            if (!supported()) return;
            const state = await fitnessPushState.read();
            if (state) await fitnessPushState.write({ ...state, previews: !!enabled });
            if (!enabled) {
                const registration = await ready;
                for (const notification of await registration?.getNotifications() || []) notification.close();
            }
        },
        async clear() {
            if (!('serviceWorker' in navigator) || !isSecureContext) return;
            // First revoke local recipient binding, even if unsubscribe/network later fails.
            await fitnessPushState.write(null);
            const registration = await ready.catch(() => null);
            if (!registration) return;
            if (registration.active) {
                await new Promise((resolve, reject) => {
                    const channel = new MessageChannel();
                    const timeout = setTimeout(() => { channel.port1.close(); reject(new Error('Device cleanup timed out')); }, 5000);
                    channel.port1.onmessage = () => { clearTimeout(timeout); channel.port1.close(); resolve(); };
                    registration.active.postMessage({ type: 'clear-push-device' }, [channel.port2]);
                });
            }
            for (const notification of await registration.getNotifications()) notification.close();
            const subscription = registration.pushManager ? await registration.pushManager.getSubscription() : null;
            // Local binding is already revoked. A network failure cannot restore it.
            if (subscription) { try { await subscription.unsubscribe(); } catch { } }
        }
    };
})();
