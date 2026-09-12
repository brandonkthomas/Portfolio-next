type ThemeSelection = "light" | "dark";

const themeStorageKey = "portfolio-theme";

function isThemeSelection(value: string | null): value is ThemeSelection {
    return value === "light" || value === "dark";
}

function readStoredTheme(): ThemeSelection | null {
    try {
        const value = localStorage.getItem(themeStorageKey);
        if (isThemeSelection(value)) {
            return value;
        }

        if (value !== null) {
            localStorage.removeItem(themeStorageKey);
        }

        return null;
    } catch {
        return null;
    }
}

function applyTheme(selection: ThemeSelection): void {
    document.documentElement.dataset.theme = selection;
}

// An explicit saved choice applies in the document head; otherwise CSS follows the OS theme.
const initialTheme = readStoredTheme();
if (initialTheme) {
    applyTheme(initialTheme);
}

function initializeThemeControl(): void {
    const button = document.querySelector<HTMLButtonElement>("[data-theme-button]");
    if (!button) {
        return;
    }

    const systemTheme = matchMedia("(prefers-color-scheme: dark)");
    const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
    const getSystemTheme = (): ThemeSelection => systemTheme.matches ? "dark" : "light";
    let pendingSelection = initialTheme ?? getSystemTheme();
    let transitionSequence = 0;
    let activeTransition: ViewTransition | null = null;

    const updateButton = (selection: ThemeSelection): void => {
        const label = selection[0].toUpperCase() + selection.slice(1);
        const nextSelection: ThemeSelection = selection === "light" ? "dark" : "light";
        const nextLabel = nextSelection[0].toUpperCase() + nextSelection.slice(1);
        button.setAttribute("aria-label", `Color theme: ${label}. Activate for ${nextLabel}.`);
    };

    const selectTheme = (selection: ThemeSelection): void => {
        applyTheme(selection);
        updateButton(selection);

        try {
            localStorage.setItem(themeStorageKey, selection);
        } catch {
            // The selected theme still applies for this page when storage is unavailable.
        }
    };

    const transitionToTheme = (selection: ThemeSelection): void => {
        const sequence = ++transitionSequence;
        const commitSelection = (): void => {
            if (sequence === transitionSequence) {
                selectTheme(selection);
            }
        };

        if (reducedMotion.matches || typeof document.startViewTransition !== "function") {
            activeTransition?.skipTransition();
            activeTransition = null;
            delete document.documentElement.dataset.themeTransition;
            commitSelection();
            return;
        }

        activeTransition?.skipTransition();
        delete document.documentElement.dataset.portfolioTransition;
        document.documentElement.dataset.themeTransition = "true";
        const transition = document.startViewTransition(commitSelection);
        activeTransition = transition;

        const clearTransitionState = (): void => {
            if (activeTransition === transition) {
                activeTransition = null;
                delete document.documentElement.dataset.themeTransition;
            }
        };
        void transition.finished.then(clearTransitionState, clearTransitionState);
    };

    updateButton(pendingSelection);

    button.addEventListener("click", () => {
        pendingSelection = pendingSelection === "light" ? "dark" : "light";
        transitionToTheme(pendingSelection);
    });

    systemTheme.addEventListener("change", () => {
        if (!isThemeSelection(document.documentElement.dataset.theme ?? null)) {
            pendingSelection = getSystemTheme();
            updateButton(pendingSelection);
        }
    });
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeThemeControl, { once: true });
} else {
    initializeThemeControl();
}
