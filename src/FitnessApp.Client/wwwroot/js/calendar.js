window.fitnessCalendar = (() => {
    const navigationKeys = new Set(["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"]);

    function bindKeyboardNavigation(container) {
        if (!(container instanceof HTMLElement) || container.dataset.calendarKeyboardBound === "true") return;

        container.dataset.calendarKeyboardBound = "true";
        container.addEventListener("keydown", event => {
            if (navigationKeys.has(event.key) && event.target instanceof Element &&
                event.target.closest("[data-calendar-day]")) {
                event.preventDefault();
            }
        });
    }

    function focus(element) {
        if (element instanceof HTMLElement && !element.disabled) element.focus();
    }

    return { bindKeyboardNavigation, focus };
})();
