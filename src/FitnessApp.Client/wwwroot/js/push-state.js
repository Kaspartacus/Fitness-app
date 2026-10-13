// Shared by window and service worker. This store contains no identity, credentials or fitness data.
self.fitnessPushState = {
    async database() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open('fitness-push-device', 1);
            request.onupgradeneeded = () => request.result.createObjectStore('device');
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(new Error('Device storage unavailable'));
        });
    },
    async installation() {
        const db = await this.database();
        try { return await new Promise((resolve, reject) => {
            // Atomic read/create across tabs. This random ID is not an account or authentication token.
            const tx = db.transaction('device', 'readwrite');
            const store = tx.objectStore('device');
            const request = store.get('installation');
            let id;
            request.onsuccess = () => {
                id = request.result || crypto.randomUUID().replace(/-/g, '');
                if (!request.result) store.put(id, 'installation');
            };
            tx.oncomplete = () => resolve(id);
            tx.onerror = () => reject(new Error('Device storage unavailable'));
            tx.onabort = () => reject(new Error('Device storage unavailable'));
        }); } finally { db.close(); }
    },
    async read() {
        const db = await this.database();
        try { return await new Promise((resolve, reject) => {
            const request = db.transaction('device').objectStore('device').get('active');
            request.onsuccess = () => resolve(request.result || null);
            request.onerror = () => reject(new Error('Device storage unavailable'));
        }); } finally { db.close(); }
    },
    async write(value) {
        const db = await this.database();
        try { await new Promise((resolve, reject) => {
            const tx = db.transaction('device', 'readwrite');
            if (value) tx.objectStore('device').put(value, 'active');
            else tx.objectStore('device').delete('active');
            tx.oncomplete = resolve;
            tx.onerror = () => reject(new Error('Device storage unavailable'));
            tx.onabort = () => reject(new Error('Device storage unavailable'));
        }); } finally { db.close(); }
    }
};
