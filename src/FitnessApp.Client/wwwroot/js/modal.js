window.fitnessModal = (() => {
    const states = new Map();
    const modalStack = [];
    let lockCount = 0;
    let previousBodyOverflow = "";
    let previousDocumentOverflow = "";

    const focusableSelector = [
        "a[href]",
        "button:not([disabled])",
        "input:not([disabled])",
        "select:not([disabled])",
        "textarea:not([disabled])",
        "[tabindex]:not([tabindex='-1'])"
    ].join(",");

    function focusableElements(dialog) {
        return [...dialog.querySelectorAll(focusableSelector)]
            .filter(element => !element.hasAttribute("hidden") && element.getClientRects().length > 0);
    }

    function lockScrolling() {
        if (lockCount++ !== 0) return;
        previousBodyOverflow = document.body.style.overflow;
        previousDocumentOverflow = document.documentElement.style.overflow;
        document.body.style.overflow = "hidden";
        document.documentElement.style.overflow = "hidden";
    }

    function unlockScrolling() {
        if (lockCount === 0 || --lockCount !== 0) return;
        document.body.style.overflow = previousBodyOverflow;
        document.documentElement.style.overflow = previousDocumentOverflow;
    }

    function isTopmost(key) {
        return modalStack.at(-1) === key;
    }

    function removeFromStack(key) {
        const index = modalStack.lastIndexOf(key);
        if (index !== -1) modalStack.splice(index, 1);
    }

    function activate(key, dialog, dotNetReference, initialFocusSelector) {
        if (!dialog || states.has(key)) return;

        const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        const onKeyDown = event => {
            if (!isTopmost(key)) return;

            if (event.key === "Escape") {
                event.preventDefault();
                dotNetReference.invokeMethodAsync("RequestDismiss").catch(() => {});
                return;
            }

            if (event.key !== "Tab") return;
            const elements = focusableElements(dialog);
            if (elements.length === 0) {
                event.preventDefault();
                dialog.focus();
                return;
            }

            const first = elements[0];
            const last = elements[elements.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            }
            else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        };

        states.set(key, { opener, onKeyDown });
        modalStack.push(key);
        lockScrolling();
        document.addEventListener("keydown", onKeyDown, true);

        requestAnimationFrame(() => {
            if (!states.has(key) || !isTopmost(key)) return;
            let initialFocus = null;
            if (initialFocusSelector) {
                try {
                    initialFocus = dialog.querySelector(initialFocusSelector);
                }
                catch {
                    initialFocus = null;
                }
            }
            (initialFocus || focusableElements(dialog)[0] || dialog).focus();
        });
    }

    function deactivate(key) {
        const state = states.get(key);
        if (!state) return;

        const wasTopmost = isTopmost(key);
        document.removeEventListener("keydown", state.onKeyDown, true);
        states.delete(key);
        removeFromStack(key);
        unlockScrolling();
        if (wasTopmost && state.opener?.isConnected) state.opener.focus();
    }

    return { activate, deactivate };
})();
