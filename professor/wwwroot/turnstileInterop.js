window.turnstileInterop = (() => {
    let scriptLoad;

    function loadTurnstile() {
        if (window.turnstile) {
            return Promise.resolve();
        }

        if (!scriptLoad) {
            scriptLoad = new Promise((resolve, reject) => {
                const script = document.createElement("script");
                script.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
                script.async = true;
                script.defer = true;
                script.onload = resolve;
                script.onerror = () => reject(new Error("Turnstile script failed to load."));
                document.head.appendChild(script);
            });
        }

        return scriptLoad;
    }

    function notify(dotNetReference, token) {
        dotNetReference.invokeMethodAsync("ReceberTokenAsync", token).catch(() => {});
    }

    return {
        async render(container, siteKey, action, dotNetReference) {
            await loadTurnstile();
            return window.turnstile.render(container, {
                sitekey: siteKey,
                action,
                callback: token => notify(dotNetReference, token),
                "expired-callback": () => notify(dotNetReference, ""),
                "error-callback": () => notify(dotNetReference, "")
            });
        },

        reset(widgetId) {
            window.turnstile?.reset(widgetId);
        },

        remove(widgetId) {
            window.turnstile?.remove(widgetId);
        }
    };
})();
