// Synchronous startup before CSS/Blazor: only the theme choice is cached, never account data.
window.fitnessTheme = (() => {
    const key = 'fitnessapp.theme';
    function apply(value) {
        const theme = value === 'light' ? 'light' : 'dark';
        document.documentElement.dataset.theme = theme;
        document.querySelector('meta[name="theme-color"]').content = theme === 'light' ? '#f4f6f9' : '#101317';
        document.querySelector('link[rel="manifest"]').href = theme === 'light' ? 'manifest-light.webmanifest' : 'manifest.webmanifest';
        try { localStorage.setItem(key, theme); } catch { /* Private/restricted storage: use memory. */ }
    }
    let cached;
    try { cached = localStorage.getItem(key); } catch { /* Default to dark. */ }
    apply(cached);
    return { apply, current: () => document.documentElement.dataset.theme };
})();
