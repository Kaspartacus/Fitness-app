const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const sourcePath = path.resolve(__dirname, '../../src/FitnessApp.Client/wwwroot/js/session.js');
const source = fs.readFileSync(sourcePath, 'utf8');
const logoutMarker = 'fitnessapp.logout-pending';

function deferred() {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
}

function response(status, body = '') {
    return { status, ok: status >= 200 && status < 300, text: async () => body };
}

function browserSession(fetch, storage = new Map()) {
    let lockTail = Promise.resolve();
    const window = { addEventListener() {}, fitnessPush: { clear: async () => {} } };
    const context = {
        window,
        fetch,
        localStorage: {
            getItem: key => storage.get(key) ?? null,
            setItem: (key, value) => storage.set(key, value),
            removeItem: key => storage.delete(key)
        },
        navigator: {
            locks: {
                request(_name, action) {
                    const result = lockTail.then(action);
                    lockTail = result.catch(() => {});
                    return result;
                }
            }
        },
        document: { visibilityState: 'visible', hasFocus: () => true, addEventListener() {} },
        performance: { getEntriesByType: () => [{ type: 'navigate' }] }
    };
    new vm.Script(source, { filename: sourcePath }).runInNewContext(context);
    return { session: window.fitnessSession, storage };
}

test('logout during in-flight login survives stale success and failed queued server logout', { timeout: 5000 }, async () => {
    const loginStarted = deferred();
    const loginReply = deferred();
    let logoutAttempts = 0;
    const browser = browserSession(async url => {
        if (url.endsWith('/login')) {
            loginStarted.resolve();
            return loginReply.promise;
        }
        assert.equal(url, '/api/auth/logout');
        logoutAttempts++;
        throw new TypeError('Synthetic offline logout');
    });

    const login = browser.session.request('login', { email: 'synthetic@example.test', password: 'synthetic' });
    await loginStarted.promise;
    browser.session.suppress();
    const logout = browser.session.request('logout');
    // Attach the rejection observer before releasing the lock held by login.
    const failedLogout = assert.rejects(logout, /Synthetic offline logout/);
    loginReply.resolve(response(200, 'synthetic login response'));

    assert.equal((await login).status, 409);
    await failedLogout;
    assert.equal(logoutAttempts, 1); // The newer logout still reaches the server.
    assert.equal(browser.session.pendingLogout(), true);
    assert.equal(browser.storage.get(logoutMarker), '1');

    let restorationRequests = 0;
    const reopened = browserSession(async () => {
        restorationRequests++;
        return response(200);
    }, browser.storage);
    assert.equal((await reopened.session.request('refresh', { userActivity: false })).status, 401);
    assert.equal(restorationRequests, 0);
    assert.equal(reopened.session.pendingLogout(), true);
});

test('current successful login clears old suppression and current logout clears its own intent', { timeout: 5000 }, async () => {
    const calls = [];
    const storage = new Map([[logoutMarker, '1']]);
    const browser = browserSession(async (url, options) => {
        calls.push(url);
        assert.equal(options.credentials, 'same-origin');
        assert.equal(options.cache, 'no-store');
        assert.equal(options.headers['X-FitnessApp-Session'], '1');
        return response(url.endsWith('/logout') ? 204 : 200, 'synthetic response');
    }, storage);

    assert.equal((await browser.session.request('login', { email: 'synthetic@example.test', password: 'synthetic' })).status, 200);
    assert.equal(browser.session.pendingLogout(), false);
    assert.equal(storage.has(logoutMarker), false);
    assert.equal((await browser.session.request('refresh', { userActivity: false })).status, 200);
    browser.session.suppress();
    assert.equal((await browser.session.request('logout')).status, 204);
    assert.equal(browser.session.pendingLogout(), false);
    assert.deepEqual(calls, ['/api/auth/login', '/api/auth/refresh', '/api/auth/logout']);
});
