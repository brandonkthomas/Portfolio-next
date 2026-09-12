const portfolioLinkSelector = "a[data-portfolio-link]";
const portfolioMainSelector = "[data-portfolio-main]";
const historyStateKey = "portfolio-navigation";
const photoListSelector = "[data-photo-list]";

interface ShellContract {
    id: string;
    version: string;
}

interface PortfolioHistoryState {
    key: typeof historyStateKey;
    scrollX: number;
    scrollY: number;
}

interface FetchedPortfolioPage {
    document: Document;
    url: URL;
}

interface MetadataField {
    selector: string;
    attribute: "content" | "href";
}

interface NavigationLinkState {
    ariaCurrent: string | null;
    order: number;
}

type NavigationMode = "push" | "pop";
type PortfolioTransitionDirection = "forward" | "backward";

const metadataFields: readonly MetadataField[] = [
    { selector: 'meta[name="description"]', attribute: "content" },
    { selector: 'link[rel="canonical"]', attribute: "href" },
    { selector: 'meta[property="og:type"]', attribute: "content" },
    { selector: 'meta[property="og:title"]', attribute: "content" },
    { selector: 'meta[property="og:description"]', attribute: "content" },
    { selector: 'meta[property="og:url"]', attribute: "content" },
    { selector: 'meta[name="twitter:card"]', attribute: "content" },
    { selector: 'meta[name="twitter:title"]', attribute: "content" },
    { selector: 'meta[name="twitter:description"]', attribute: "content" }
];

/**
 * Progressively enhances the server-rendered photo list with shortest-column
 * placement while preserving its DOM order and native image loading behavior.
 */
function initializePhotoGrid(): () => void {
    const list = document.querySelector<HTMLOListElement>(photoListSelector);
    if (!list
        || CSS.supports("display", "grid-lanes")
        || !("ResizeObserver" in window)) {
        return () => undefined;
    }

    const items = Array.from(list.children).filter(
        (child): child is HTMLLIElement => child instanceof HTMLLIElement
    );
    if (items.length === 0) {
        return () => undefined;
    }

    let layoutFrame: number | null = null;
    let lastWidth = -1;

    const layout = (): void => {
        layoutFrame = null;

        const listWidth = list.getBoundingClientRect().width;
        if (listWidth <= 0) {
            return;
        }

        const styles = getComputedStyle(list);
        const requestedColumnCount = Number.parseInt(
            styles.getPropertyValue("--photo-column-count"),
            10
        );
        const columnCount = Math.max(
            1,
            Math.min(Number.isFinite(requestedColumnCount) ? requestedColumnCount : 1, items.length)
        );
        const parsedGap = Number.parseFloat(styles.columnGap);
        const gap = Number.isFinite(parsedGap) ? parsedGap : 0;
        const itemWidth = (listWidth - gap * (columnCount - 1)) / columnCount;
        const columnHeights = Array<number>(columnCount).fill(0);

        list.style.setProperty("--photo-item-width", `${itemWidth}px`);
        list.dataset.masonry = "true";
        const itemHeights = items.map((item) => item.getBoundingClientRect().height);

        items.forEach((item, itemIndex) => {
            let columnIndex = 0;
            for (let index = 1; index < columnHeights.length; index += 1) {
                if (columnHeights[index] < columnHeights[columnIndex]) {
                    columnIndex = index;
                }
            }

            const x = columnIndex * (itemWidth + gap);
            const y = columnHeights[columnIndex];
            item.style.setProperty("--photo-x", `${x}px`);
            item.style.setProperty("--photo-y", `${y}px`);
            columnHeights[columnIndex] += itemHeights[itemIndex] + gap;
        });

        list.style.blockSize = `${Math.max(...columnHeights) - gap}px`;
        lastWidth = listWidth;
    };

    const scheduleLayout = (): void => {
        if (layoutFrame === null) {
            layoutFrame = requestAnimationFrame(layout);
        }
    };

    const resizeObserver = new ResizeObserver((entries) => {
        const width = entries[0]?.contentRect.width ?? 0;
        if (Math.abs(width - lastWidth) > 0.5) {
            scheduleLayout();
        }
    });
    resizeObserver.observe(list);

    const images = Array.from(list.querySelectorAll<HTMLImageElement>("img"));
    for (const image of images) {
        image.addEventListener("load", scheduleLayout);
        image.addEventListener("error", scheduleLayout);
    }

    layout();

    return () => {
        if (layoutFrame !== null) {
            cancelAnimationFrame(layoutFrame);
        }

        resizeObserver.disconnect();
        for (const image of images) {
            image.removeEventListener("load", scheduleLayout);
            image.removeEventListener("error", scheduleLayout);
        }

        delete list.dataset.masonry;
        list.style.removeProperty("--photo-item-width");
        list.style.removeProperty("block-size");
        for (const item of items) {
            item.style.removeProperty("--photo-x");
            item.style.removeProperty("--photo-y");
        }
    };
}

/** Controls the mobile portfolio menu without changing link behavior. */
function initializeMenu(): () => void {
    const sidebar = document.querySelector<HTMLElement>("[data-sidebar]");
    const button = document.querySelector<HTMLButtonElement>("[data-menu-button]");
    const menu = document.querySelector<HTMLElement>("[data-menu]");

    if (!sidebar || !button || !menu) {
        return () => undefined;
    }

    const setOpen = (open: boolean): void => {
        button.setAttribute("aria-expanded", String(open));
        menu.dataset.open = String(open);
    };

    button.addEventListener("click", () => {
        setOpen(button.getAttribute("aria-expanded") !== "true");
    });

    menu.addEventListener("click", (event) => {
        if (event.target instanceof Element && event.target.closest("a")) {
            setOpen(false);
        }
    });

    document.addEventListener("click", (event) => {
        if (event.target instanceof Node && !sidebar.contains(event.target)) {
            setOpen(false);
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && button.getAttribute("aria-expanded") === "true") {
            setOpen(false);
            button.focus();
        }
    });

    matchMedia("(min-width: 68rem)").addEventListener("change", () => setOpen(false));
    return () => setOpen(false);
}

/** Reads the identity used to reject HTML from another or incompatible shell. */
function getShellContract(source: Document): ShellContract | null {
    const id = source.documentElement.dataset.portfolioShell;
    const version = source.documentElement.dataset.portfolioShellVersion;

    return id && version ? { id, version } : null;
}

/** Requires every allowlisted metadata value before the current document is changed. */
function validateMetadata(source: Document): void {
    if (!source.title) {
        throw new Error("The portfolio response has no document title.");
    }

    for (const field of metadataFields) {
        if (!source.head.querySelector(field.selector)?.hasAttribute(field.attribute)) {
            throw new Error(`The portfolio response is missing ${field.selector}.`);
        }
    }
}

/** Reads incoming active-link state and verifies the navigation contract is unchanged. */
function getNavigationState(source: Document): ReadonlyMap<string, NavigationLinkState> {
    const states = new Map<string, NavigationLinkState>();
    const orders = new Set<number>();
    let activeLinkCount = 0;

    source.querySelectorAll<HTMLAnchorElement>(portfolioLinkSelector).forEach((link) => {
        const key = link.dataset.navigationKey;
        const orderValue = link.dataset.navigationOrder;
        const order = Number(orderValue);
        const ariaCurrent = link.getAttribute("aria-current");
        if (!key
            || states.has(key)
            || orderValue === undefined
            || !/^\d+$/.test(orderValue)
            || !Number.isSafeInteger(order)
            || order < 0
            || (ariaCurrent !== null && ariaCurrent !== "page")
            || orders.has(order)) {
            throw new Error("The portfolio response has invalid navigation metadata.");
        }

        if (ariaCurrent === "page") {
            activeLinkCount += 1;
        }

        orders.add(order);
        states.set(key, {
            ariaCurrent,
            order
        });
    });

    if (states.size === 0 || activeLinkCount !== 1) {
        throw new Error("The portfolio response must have one active navigation link.");
    }

    return states;
}

/** Fetches and validates one complete portfolio document without executing its scripts. */
async function fetchPortfolioPage(
    requestedUrl: URL,
    expectedShell: ShellContract,
    signal: AbortSignal
): Promise<FetchedPortfolioPage> {
    const response = await fetch(requestedUrl.href, {
        credentials: "same-origin",
        headers: { Accept: "text/html" },
        signal
    });
    const contentType = response.headers.get("content-type")?.split(";", 1)[0]?.trim().toLowerCase();

    if (!response.ok || contentType !== "text/html") {
        throw new Error("The portfolio response is not successful HTML.");
    }

    const responseUrl = new URL(response.url);
    if (responseUrl.origin !== location.origin) {
        throw new Error("The portfolio response changed origin.");
    }

    const nextDocument = new DOMParser().parseFromString(await response.text(), "text/html");
    const nextShell = getShellContract(nextDocument);
    const nextMain = nextDocument.querySelector<HTMLElement>(portfolioMainSelector);

    if (!nextShell
        || nextShell.id !== expectedShell.id
        || nextShell.version !== expectedShell.version
        || !nextMain
        || !nextMain.dataset.portfolioView
        || nextMain.querySelector("script")) {
        throw new Error("The portfolio response has an incompatible shell.");
    }

    validateMetadata(nextDocument);
    const navigationState = getNavigationState(nextDocument);
    if (navigationState.get(nextMain.dataset.portfolioView)?.ariaCurrent !== "page") {
        throw new Error("The portfolio response view does not match its active navigation link.");
    }

    if (requestedUrl.hash) {
        responseUrl.hash = requestedUrl.hash;
    }

    return { document: nextDocument, url: responseUrl };
}

/** Replaces only portfolio-owned content, metadata, and active navigation state. */
function applyPortfolioPage(page: FetchedPortfolioPage, currentMain: HTMLElement): void {
    const nextMain = page.document.querySelector<HTMLElement>(portfolioMainSelector);
    if (!nextMain) {
        throw new Error("The portfolio response has no main region.");
    }

    const nextView = nextMain.dataset.portfolioView;
    if (!nextView) {
        throw new Error("The portfolio response has no view identity.");
    }

    const currentNavigationState = getNavigationState(document);
    const navigationState = getNavigationState(page.document);
    if (currentNavigationState.size !== navigationState.size) {
        throw new Error("The current portfolio navigation is incompatible.");
    }

    const navigationUpdates = Array.from(
        document.querySelectorAll<HTMLAnchorElement>(portfolioLinkSelector)
    ).map((link) => {
        const key = link.dataset.navigationKey;
        const currentState = key ? currentNavigationState.get(key) : undefined;
        const nextState = key ? navigationState.get(key) : undefined;
        if (!currentState || !nextState || currentState.order !== nextState.order) {
            throw new Error("The current portfolio navigation is incompatible.");
        }

        return { link, ariaCurrent: nextState.ariaCurrent };
    });

    const metadataUpdates = metadataFields.map((field) => {
        const currentElement = document.head.querySelector(field.selector);
        const nextElement = page.document.head.querySelector(field.selector);
        const value = nextElement?.getAttribute(field.attribute);

        if (!currentElement || value === null || value === undefined) {
            throw new Error(`The current portfolio metadata is missing ${field.selector}.`);
        }

        return { currentElement, field, value };
    });

    for (const update of navigationUpdates) {
        if (update.ariaCurrent) {
            update.link.setAttribute("aria-current", update.ariaCurrent);
        } else {
            update.link.removeAttribute("aria-current");
        }
    }

    for (const update of metadataUpdates) {
        update.currentElement.setAttribute(update.field.attribute, update.value);
    }

    document.title = page.document.title;
    currentMain.dataset.portfolioView = nextView;
    currentMain.replaceChildren(...Array.from(nextMain.childNodes));
}

/** Returns whether two URLs identify the same server-rendered document. */
function isSameDocument(left: URL, right: URL): boolean {
    return left.origin === right.origin
        && left.pathname === right.pathname
        && left.search === right.search;
}

/** Maps HTML-authored navigation order to forward or backward view motion. */
function getPortfolioTransitionDirection(
    currentView: string | undefined,
    nextView: string | undefined,
    navigationState: ReadonlyMap<string, NavigationLinkState>
): PortfolioTransitionDirection | null {
    const currentOrder = currentView ? navigationState.get(currentView)?.order : undefined;
    const nextOrder = nextView ? navigationState.get(nextView)?.order : undefined;

    if (currentOrder === undefined || nextOrder === undefined || currentOrder === nextOrder) {
        return null;
    }

    return nextOrder > currentOrder ? "forward" : "backward";
}

/** Identifies an ordinary primary-button activation without browser modifiers. */
function isOrdinaryActivation(event: MouseEvent, link: HTMLAnchorElement): boolean {
    return !event.defaultPrevented
        && event.button === 0
        && !event.metaKey
        && !event.ctrlKey
        && !event.shiftKey
        && !event.altKey
        && !link.hasAttribute("download")
        && (!link.target || link.target.toLowerCase() === "_self");
}

/** Limits enhancement to an ordinary unmodified activation of an opted-in link. */
function shouldEnhanceClick(event: MouseEvent, link: HTMLAnchorElement, renderedUrl: URL): boolean {
    if (!isOrdinaryActivation(event, link)) {
        return false;
    }

    const destination = new URL(link.href, location.href);
    return destination.origin === location.origin
        && (destination.protocol === "http:" || destination.protocol === "https:")
        && destination.href !== location.href
        && !isSameDocument(destination, renderedUrl);
}

/** Keeps an ordinary activation inert when its portfolio view is already rendered. */
function shouldIgnoreCurrentClick(event: MouseEvent, link: HTMLAnchorElement, renderedUrl: URL): boolean {
    if (!isOrdinaryActivation(event, link)
        || link.getAttribute("aria-current") !== "page") {
        return false;
    }

    const destination = new URL(link.href, location.href);
    return isSameDocument(destination, renderedUrl);
}

/** Stores scroll coordinates on the current browser-history entry. */
function createHistoryState(): PortfolioHistoryState {
    return {
        key: historyStateKey,
        scrollX: window.scrollX,
        scrollY: window.scrollY
    };
}

/** Accepts only navigation state written by this module. */
function readHistoryState(value: unknown): PortfolioHistoryState | null {
    if (!value || typeof value !== "object") {
        return null;
    }

    const candidate = value as Partial<PortfolioHistoryState>;
    return candidate.key === historyStateKey
        && Number.isFinite(candidate.scrollX)
        && Number.isFinite(candidate.scrollY)
        ? candidate as PortfolioHistoryState
        : null;
}

/** Focuses a destination while avoiding a permanent tabindex change. */
function focusTemporarily(element: HTMLElement): void {
    const hadTabIndex = element.hasAttribute("tabindex");
    if (!hadTabIndex) {
        element.setAttribute("tabindex", "-1");
        element.addEventListener("blur", () => element.removeAttribute("tabindex"), { once: true });
    }

    element.focus({ preventScroll: true });
}

/** Applies focus and scroll before a new navigation state is presented or captured. */
function settleNavigation(
    destination: URL,
    mode: NavigationMode,
    historyState: PortfolioHistoryState | null,
    currentMain: HTMLElement
): void {
    if (mode === "pop" && historyState) {
        currentMain.focus({ preventScroll: true });
        window.scrollTo(historyState.scrollX, historyState.scrollY);
        return;
    }

    let hashTarget: HTMLElement | null = null;
    if (destination.hash) {
        try {
            hashTarget = document.getElementById(decodeURIComponent(destination.hash.slice(1)));
        } catch {
            hashTarget = document.getElementById(destination.hash.slice(1));
        }
    }

    if (hashTarget) {
        focusTemporarily(hashTarget);
        hashTarget.scrollIntoView();
    } else {
        currentMain.focus({ preventScroll: true });
        window.scrollTo(0, 0);
    }
}

/** Enables progressive navigation only when the current document exposes the shell contract. */
function initializePortfolioNavigation(closeMenu: () => void): void {
    const shell = getShellContract(document);
    const currentMain = document.querySelector<HTMLElement>(portfolioMainSelector);
    if (!shell || !currentMain) {
        return;
    }

    const navigationState = getNavigationState(document);

    let activeController: AbortController | null = null;
    let navigationSequence = 0;
    let renderedUrl = new URL(location.href);
    let scrollFrame: number | null = null;
    let disposeCurrentView = initializePhotoGrid();
    let activeViewTransition: ViewTransition | null = null;

    const applyPageWithTransition = async (
        page: FetchedPortfolioPage,
        sequence: number,
        destination: URL,
        mode: NavigationMode,
        historyState: PortfolioHistoryState | null
    ): Promise<void> => {
        const nextMain = page.document.querySelector<HTMLElement>(portfolioMainSelector);
        const direction = getPortfolioTransitionDirection(
            currentMain.dataset.portfolioView,
            nextMain?.dataset.portfolioView,
            navigationState
        );
        const replacePage = (): void => {
            if (sequence !== navigationSequence) {
                return;
            }

            disposeCurrentView();
            applyPortfolioPage(page, currentMain);
            disposeCurrentView = initializePhotoGrid();
            settleNavigation(destination, mode, historyState, currentMain);
        };

        if (!direction
            || matchMedia("(prefers-reduced-motion: reduce)").matches
            || typeof document.startViewTransition !== "function") {
            replacePage();
            return;
        }

        activeViewTransition?.skipTransition();
        delete document.documentElement.dataset.themeTransition;
        document.documentElement.dataset.portfolioTransition = direction;
        const transition = document.startViewTransition(replacePage);
        activeViewTransition = transition;

        const clearTransitionState = (): void => {
            if (activeViewTransition === transition) {
                activeViewTransition = null;
                delete document.documentElement.dataset.portfolioTransition;
            }
        };
        void transition.finished.then(clearTransitionState, clearTransitionState);
        await transition.updateCallbackDone;
    };

    if ("scrollRestoration" in history) {
        history.scrollRestoration = "manual";
    }
    history.replaceState(createHistoryState(), "", location.href);

    const persistScrollPosition = (): void => {
        history.replaceState(createHistoryState(), "", location.href);
    };

    const cancelPendingScrollSave = (): void => {
        if (scrollFrame !== null) {
            cancelAnimationFrame(scrollFrame);
            scrollFrame = null;
        }
    };

    window.addEventListener("scroll", () => {
        if (scrollFrame !== null) {
            return;
        }

        scrollFrame = requestAnimationFrame(() => {
            scrollFrame = null;
            persistScrollPosition();
        });
    }, { passive: true });

    window.addEventListener("pagehide", persistScrollPosition);
    window.addEventListener("hashchange", () => {
        renderedUrl = new URL(location.href);
    });

    const navigate = async (
        destination: URL,
        mode: NavigationMode,
        historyState: PortfolioHistoryState | null
    ): Promise<void> => {
        activeController?.abort();
        activeViewTransition?.skipTransition();
        const controller = new AbortController();
        const sequence = ++navigationSequence;
        activeController = controller;
        currentMain.setAttribute("aria-busy", "true");

        try {
            const page = await fetchPortfolioPage(destination, shell, controller.signal);
            if (sequence !== navigationSequence) {
                return;
            }

            if (mode === "push") {
                cancelPendingScrollSave();
                persistScrollPosition();
            }

            closeMenu();
            await applyPageWithTransition(page, sequence, page.url, mode, historyState);
            if (sequence !== navigationSequence) {
                return;
            }

            if (mode === "push") {
                history.pushState(
                    { key: historyStateKey, scrollX: 0, scrollY: 0 } satisfies PortfolioHistoryState,
                    "",
                    page.url
                );
            }

            renderedUrl = page.url;
        } catch (error: unknown) {
            const aborted = error instanceof DOMException && error.name === "AbortError";
            if (sequence === navigationSequence && !aborted) {
                if (mode === "pop") {
                    location.replace(destination.href);
                } else {
                    location.assign(destination.href);
                }
            }
        } finally {
            if (sequence === navigationSequence) {
                currentMain.removeAttribute("aria-busy");
                activeController = null;
            }
        }
    };

    document.addEventListener("click", (event) => {
        if (!(event instanceof MouseEvent) || !(event.target instanceof Element)) {
            return;
        }

        const link = event.target.closest<HTMLAnchorElement>(portfolioLinkSelector);
        if (!link) {
            return;
        }

        if (shouldIgnoreCurrentClick(event, link, renderedUrl)) {
            event.preventDefault();
            return;
        }

        if (!shouldEnhanceClick(event, link, renderedUrl)) {
            return;
        }

        event.preventDefault();
        void navigate(new URL(link.href, location.href), "push", null);
    });

    window.addEventListener("popstate", (event) => {
        cancelPendingScrollSave();
        const destination = new URL(location.href);
        const historyState = readHistoryState(event.state);

        if (isSameDocument(destination, renderedUrl)) {
            renderedUrl = destination;
            settleNavigation(destination, "pop", historyState, currentMain);
            return;
        }

        void navigate(destination, "pop", historyState);
    });
}

const closeMenu = initializeMenu();
initializePortfolioNavigation(closeMenu);

export {};
