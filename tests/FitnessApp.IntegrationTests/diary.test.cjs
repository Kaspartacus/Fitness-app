const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const sourcePath = path.resolve(__dirname, '../../src/FitnessApp.Client/wwwroot/js/diary.js');
const source = fs.readFileSync(sourcePath, 'utf8');
const minute = 60 * 1000;
const idleLimit = 15 * minute;

class EventTarget {
    listeners = new Map();
    addEventListener(type, listener) {
        if (!this.listeners.has(type)) this.listeners.set(type, new Set());
        this.listeners.get(type).add(listener);
    }
    removeEventListener(type, listener) { this.listeners.get(type)?.delete(listener); }
    emit(type, event = {}) {
        for (const listener of [...(this.listeners.get(type) ?? [])]) listener({ type, ...event });
    }
    listenerCount() { return [...this.listeners.values()].reduce((total, listeners) => total + listeners.size, 0); }
}

function browserDiary() {
    let now = 1000000;
    let focused = true;
    let nextTimer = 0;
    const timers = new Map();
    const history = new Map();
    const window = new EventTarget();
    const document = Object.assign(new EventTarget(), { visibilityState: 'visible', hasFocus: () => focused });
    const calls = [];
    const createRoot = () => Object.assign(new EventTarget(), {
        hidden: false,
        fields: [{ value: 'Synthetic private draft' }, { value: 'Synthetic code' }],
        querySelectorAll(selector) { assert.equal(selector, 'textarea, input'); return this.fields; }
    });
    const context = {
        window,
        document,
        Date: { now: () => now },
        setTimeout(action, delay) {
            const id = ++nextTimer;
            const timer = { action, due: now + delay };
            timers.set(id, timer); history.set(id, timer);
            return id;
        },
        clearTimeout: id => timers.delete(id)
    };
    new vm.Script(source, { filename: sourcePath }).runInNewContext(context);
    return {
        diary: window.fitnessDiary, window, document, calls, timers, createRoot,
        advance: elapsed => { now += elapsed; }, // Deliberately do not run timers: model a suspended browser.
        focus: value => { focused = value; },
        timer() { assert.equal(timers.size, 1); const [id, value] = [...timers][0]; return { id, ...value }; },
        fire(id) { timers.delete(id); history.get(id).action(); }
    };
}

function assertLocked(browser, root) {
    assert.equal(root.hidden, true);
    assert.deepEqual(root.fields.map(field => field.value), ['', '']);
    assert.equal(browser.calls.filter(method => method === 'IdleLock').length, 1);
}

test('first trusted input after a suspended deadline clears synchronously instead of renewing', () => {
    const browser = browserDiary(); const root = browser.createRoot();
    browser.diary.start('synthetic', root, { invokeMethodAsync: method => {
        browser.calls.push(method);
        // Clearing must happen before invoking the runtime, even if it never answers.
        assert.equal(root.hidden, true);
        assert.deepEqual(root.fields.map(field => field.value), ['', '']);
        return new Promise(() => {});
    } });
    const overdueTimer = browser.timer().id;
    browser.advance(idleLimit);
    root.emit('input', { isTrusted: true });
    assertLocked(browser, root);
    assert.deepEqual(browser.calls, ['IdleLock']);
    browser.window.emit('focus'); browser.document.emit('resume'); browser.fire(overdueTimer);
    assertLocked(browser, root);
    assert.equal(browser.timers.size, 0);
});

for (const [target, event] of [['document', 'visibilitychange'], ['document', 'resume'], ['window', 'focus'], ['window', 'pageshow']]) {
    test(`overdue ${event} clears before any new input`, () => {
        const browser = browserDiary(); const root = browser.createRoot();
        browser.diary.start('synthetic', root, { invokeMethodAsync: method => { browser.calls.push(method); return Promise.resolve(); } });
        browser.advance(idleLimit + minute);
        browser[target].emit(event, { persisted: false });
        assertLocked(browser, root);
        assert.deepEqual(browser.calls, ['IdleLock']);
    });
}

test('nonexpired visibility focus and resume checks never renew the inactivity deadline', () => {
    const browser = browserDiary(); const root = browser.createRoot();
    browser.diary.start('synthetic', root, { invokeMethodAsync: method => { browser.calls.push(method); return Promise.resolve(); } });
    const originalDeadline = browser.timer().due;
    browser.advance(10 * minute);
    browser.document.visibilityState = 'hidden'; browser.document.emit('visibilitychange');
    browser.document.visibilityState = 'visible'; browser.document.emit('visibilitychange');
    browser.document.emit('resume'); browser.window.emit('focus'); browser.window.emit('pageshow', { persisted: false });
    assert.equal(root.hidden, false); assert.deepEqual(browser.calls, []);
    assert.equal(browser.timer().due, originalDeadline);
    browser.advance(5 * minute); browser.fire(browser.timer().id);
    assertLocked(browser, root);
});

test('trusted foreground activity renews before expiry and early or stale timers use the current deadline', () => {
    const browser = browserDiary(); const root = browser.createRoot();
    browser.diary.start('synthetic', root, { invokeMethodAsync: method => { browser.calls.push(method); return Promise.resolve(); } });
    const oldTimer = browser.timer();
    browser.advance(minute); root.emit('keydown', { isTrusted: true });
    assert.equal(browser.timer().due, oldTimer.due + minute);
    assert.deepEqual(browser.calls, ['DiaryActivity']);
    browser.fire(oldTimer.id); // A callback queued before clearTimeout must not lock a renewed view.
    assert.equal(root.hidden, false);
    assert.equal(browser.timer().due, oldTimer.due + minute);
    browser.advance(14 * minute); browser.fire(browser.timer().id); // Also model an early callback.
    assert.equal(root.hidden, false);
    assert.equal(browser.timer().due, oldTimer.due + minute);
    browser.advance(minute); browser.fire(browser.timer().id);
    assertLocked(browser, root);
});

test('untrusted hidden and unfocused activity cannot renew or notify the server', () => {
    const browser = browserDiary(); const root = browser.createRoot();
    browser.diary.start('synthetic', root, { invokeMethodAsync: method => { browser.calls.push(method); return Promise.resolve(); } });
    const deadline = browser.timer().due;
    browser.advance(14 * minute);
    root.emit('input', { isTrusted: false });
    browser.document.visibilityState = 'hidden'; root.emit('scroll', { isTrusted: true });
    browser.document.visibilityState = 'visible'; browser.focus(false); root.emit('pointerdown', { isTrusted: true });
    assert.equal(browser.timer().due, deadline); assert.deepEqual(browser.calls, []);
    browser.advance(minute); browser.fire(browser.timer().id);
    assertLocked(browser, root);
});

test('pagehide and bfcache pageshow still lock exactly once before the inactivity deadline', () => {
    for (const event of ['pagehide', 'pageshow']) {
        const browser = browserDiary(); const root = browser.createRoot();
        browser.diary.start('synthetic', root, { invokeMethodAsync: method => { browser.calls.push(method); return Promise.resolve(); } });
        browser.window.emit(event, { persisted: true });
        browser.window.emit(event, { persisted: true });
        assertLocked(browser, root);
        assert.equal(browser.timers.size, 0);
    }
});

test('stop and restart remove listeners and make callbacks from old timers inert', () => {
    const browser = browserDiary(); const first = browser.createRoot(); const second = browser.createRoot();
    const callback = { invokeMethodAsync: method => { browser.calls.push(method); return Promise.resolve(); } };
    browser.diary.start('synthetic', first, callback);
    const oldTimer = browser.timer().id;
    const queuedActivity = [...first.listeners.get('input')][0];
    const queuedPagehide = [...browser.window.listeners.get('pagehide')][0];
    browser.advance(minute); browser.diary.start('synthetic', second, callback);
    assert.equal(first.listenerCount(), 0);
    assert.equal(second.listenerCount(), 4);
    assert.equal(browser.window.listenerCount(), 3);
    assert.equal(browser.document.listenerCount(), 2);
    browser.advance(14 * minute); browser.fire(oldTimer);
    queuedActivity({ isTrusted: true }); queuedPagehide(); // Already-queued listeners outlive removal.
    assert.equal(first.hidden, false); assert.equal(second.hidden, false); assert.deepEqual(browser.calls, []);
    assert.equal(browser.timers.size, 1);
    const currentTimer = browser.timer().id;
    browser.diary.stop('synthetic');
    assert.equal(second.listenerCount(), 0); assert.equal(browser.window.listenerCount(), 0);
    assert.equal(browser.document.listenerCount(), 0); assert.equal(browser.timers.size, 0);
    browser.advance(idleLimit); browser.fire(currentTimer);
    first.emit('input', { isTrusted: true }); second.emit('input', { isTrusted: true });
    browser.window.emit('focus'); browser.document.emit('resume');
    assert.deepEqual(browser.calls, []);
    assert.equal(first.hidden, false); assert.equal(second.hidden, false);
});
