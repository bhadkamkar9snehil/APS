(() => {
    const storageKey = "aps.appearance.v1";
    const defaultPreference = { version: 1, mode: 0, accent: { kind: 0, customHex: null } };
    const modeNames = ["system", "light", "dark"];
    const accentNames = ["amber", "violet", "forest", "brick", "plum", "olive", "custom"];
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    let mediaHandler = null;

    function validHex(value) {
        return typeof value === "string" && /^#[0-9a-fA-F]{6}$/.test(value);
    }

    function normalize(value) {
        if (!value || value.version !== 1 || !Number.isInteger(value.mode) || value.mode < 0 || value.mode > 2 ||
            !value.accent || !Number.isInteger(value.accent.kind) || value.accent.kind < 0 || value.accent.kind > 6)
            return structuredClone(defaultPreference);

        if (value.accent.kind === 6 && !validHex(value.accent.customHex))
            return structuredClone(defaultPreference);

        return {
            version: 1,
            mode: value.mode,
            accent: {
                kind: value.accent.kind,
                customHex: value.accent.kind === 6 ? value.accent.customHex.toUpperCase() : null
            }
        };
    }

    function load() {
        try {
            return normalize(JSON.parse(localStorage.getItem(storageKey)));
        } catch {
            return structuredClone(defaultPreference);
        }
    }

    function resolveTheme(mode) {
        if (mode === 1) return "light";
        if (mode === 2) return "dark";
        return media.matches ? "dark" : "light";
    }

    function foregroundFor(hex) {
        const channels = [1, 3, 5].map(index => parseInt(hex.slice(index, index + 2), 16) / 255);
        const linear = channels.map(value => value <= 0.04045 ? value / 12.92 : Math.pow((value + 0.055) / 1.055, 2.4));
        const luminance = 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
        const darkContrast = (luminance + 0.05) / 0.0569;
        const lightContrast = 1.05 / (luminance + 0.05);
        return darkContrast >= lightContrast ? "#171411" : "#FFFFFF";
    }

    function apply(value) {
        const preference = normalize(value);
        const root = document.documentElement;
        const effectiveTheme = resolveTheme(preference.mode);

        root.dataset.theme = effectiveTheme;
        root.dataset.themeMode = modeNames[preference.mode];
        root.dataset.accent = accentNames[preference.accent.kind];
        root.style.colorScheme = effectiveTheme;

        if (preference.accent.kind === 6) {
            root.style.setProperty("--aps-accent-custom", preference.accent.customHex);
            root.style.setProperty("--aps-accent-custom-foreground", foregroundFor(preference.accent.customHex));
        } else {
            root.style.removeProperty("--aps-accent-custom");
            root.style.removeProperty("--aps-accent-custom-foreground");
        }

        try { localStorage.setItem(storageKey, JSON.stringify(preference)); } catch { }
        return preference;
    }

    function bootstrap() {
        apply(load());
    }

    function initialize() {
        dispose();
        apply(load());
        mediaHandler = () => {
            const current = load();
            if (current.mode === 0) apply(current);
        };
        media.addEventListener("change", mediaHandler);
    }

    function setMode(mode) {
        apply({ ...load(), mode });
    }

    function dispose() {
        if (mediaHandler) media.removeEventListener("change", mediaHandler);
        mediaHandler = null;
    }

    window.apsTheme = { apply, bootstrap, dispose, initialize, load, setMode };
})();
