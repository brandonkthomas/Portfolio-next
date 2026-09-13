const triggerSelector = "a[data-photo-trigger]";
const lightboxSrcsetAttribute = "data-lightbox-srcset";
const centerSlot = 1;
const settleFallbackMs = 450;
const neighborDelayMs = 400;
const swipeDistance = 85;
const swipeVelocity = 0.35;
const dismissDistance = 120;
const dismissVelocity = 0.45;
const dismissFadeDistance = 380;
const horizontalLockDistance = 14;
const horizontalLockBias = 6;
const verticalLockDistance = 30;
const verticalLockBias = 12;
const tapSlop = 6;
const staleVelocityMs = 100;

type Direction = 1 | -1;
type DragAxis = "horizontal" | "vertical";

interface LightboxPhoto {
    trigger: HTMLAnchorElement;
    picture: HTMLPictureElement;
    preview: HTMLImageElement;
    ratio: number;
}

interface LightboxElements {
    dialog: HTMLDialogElement;
    stage: HTMLElement;
    track: HTMLElement;
    status: HTMLElement;
    resizeObserver: ResizeObserver;
}

interface DragState {
    pointerId: number;
    target: EventTarget | null;
    startX: number;
    startY: number;
    lastX: number;
    lastY: number;
    lastTime: number;
    baseX: number;
    baseY: number;
    velocityX: number;
    velocityY: number;
    axis: DragAxis | null;
}

const icons = {
    close: '<path d="M18 6 6 18M6 6l12 12"></path>',
    previous: '<path d="m15 18-6-6 6-6"></path>',
    next: '<path d="m9 18 6-6-6-6"></path>'
} as const;

/** Reads lightbox sources from the server-rendered triggers once; the grid markup is static per view. */
function readPhotos(list: HTMLElement): LightboxPhoto[] {
    const photos: LightboxPhoto[] = [];

    list.querySelectorAll<HTMLAnchorElement>(triggerSelector).forEach((trigger) => {
        const picture = trigger.querySelector("picture");
        const preview = picture?.querySelector<HTMLImageElement>(`img[${lightboxSrcsetAttribute}]`);
        const ratio = Number(preview?.getAttribute("width")) / Number(preview?.getAttribute("height"));

        if (picture && preview && Number.isFinite(ratio) && ratio > 0) {
            photos.push({ trigger, picture, preview, ratio });
        }
    });

    return photos;
}

function createDiv(className: string): HTMLDivElement {
    const element = document.createElement("div");
    element.className = className;
    return element;
}

function createButton(className: string, label: string, icon: string): HTMLButtonElement {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `lightbox-button ${className}`;
    button.setAttribute("aria-label", label);
    button.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true">${icon}</svg><span>${label}</span>`;
    return button;
}

/**
 * Opens server-rendered photo links in a modal viewer. Only three slots exist regardless of catalog size,
 * full-size candidates load only once the viewer opens, and all motion is transform/opacity driven.
 * Returns a disposer that removes every listener and the dialog.
 */
export function initializePhotoLightbox(list: HTMLElement): () => void {
    const photos = readPhotos(list);
    if (photos.length === 0 || typeof HTMLDialogElement !== "function" || !("ResizeObserver" in window)) {
        return () => undefined;
    }

    const root = document.documentElement;
    const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
    const canNavigate = photos.length > 1;

    let lightbox: LightboxElements | null = null;
    // Positions 0..n-1 are photos; position n is a blank gap that briefly separates the last and first photos.
    const gapPosition = canNavigate ? photos.length : -1;
    const positionCount = canNavigate ? photos.length + 1 : 1;
    let position = 0;
    let shownIndex = 0;
    let lastTravel: Direction = 1;
    let pendingDirection: Direction | 0 | null = null;
    let closing = false;
    let drag: DragState | null = null;
    let dragX = 0;
    let dragY = 0;
    let dragFrame: number | null = null;
    let resizeFrame: number | null = null;
    let settleTimer = 0;
    let closeTimer = 0;
    let neighborTimer = 0;
    let slotWidth = 0;
    let slotHeight = 0;

    const wrap = (value: number): number => ((value % positionCount) + positionCount) % positionCount;

    const getSlots = (elements: LightboxElements): HTMLElement[] =>
        Array.from(elements.track.children) as HTMLElement[];

    /** Requests the smallest candidate covering the contained display box rather than the full viewport width. */
    const getSizes = (photo: LightboxPhoto): string =>
        `${Math.max(1, Math.ceil(Math.min(slotWidth, slotHeight * photo.ratio)))}px`;

    const measure = (elements: LightboxElements): void => {
        const slot = getSlots(elements)[centerSlot];
        const styles = getComputedStyle(slot);
        slotWidth = slot.clientWidth - parseFloat(styles.paddingLeft) - parseFloat(styles.paddingRight);
        slotHeight = slot.clientHeight - parseFloat(styles.paddingTop) - parseFloat(styles.paddingBottom);
    };

    const fillSlot = (slot: HTMLElement, slotPosition: number, priority: "high" | "low"): void => {
        const photo = photos[slotPosition];
        if (slotPosition === gapPosition || !photo) {
            clearSlot(slot);
            return;
        }

        const frame = createDiv("lightbox-frame");
        frame.style.setProperty("--photo-ratio", String(photo.ratio));

        // An already-decoded grid preview paints immediately while the full-size candidate loads.
        if (photo.preview.complete && photo.preview.naturalWidth > 0) {
            const placeholder = document.createElement("img");
            placeholder.className = "lightbox-placeholder";
            placeholder.src = photo.preview.currentSrc || photo.preview.src;
            placeholder.alt = "";
            placeholder.draggable = false;
            frame.append(placeholder);
            frame.dataset.preview = "true";
        }

        // Built from the data attributes rather than cloned, so the grid's preview src never starts a request.
        // Elements join the picture before sizes/srcset are assigned, and sizes precedes srcset, so selection
        // runs once against the final candidates instead of the default 100vw.
        const picture = document.createElement("picture");
        const sizes = getSizes(photo);
        photo.picture.querySelectorAll<HTMLSourceElement>(`source[${lightboxSrcsetAttribute}]`).forEach((gridSource) => {
            const source = document.createElement("source");
            picture.append(source);
            source.type = gridSource.type;
            source.sizes = sizes;
            source.srcset = gridSource.getAttribute(lightboxSrcsetAttribute) ?? "";
        });

        const image = document.createElement("img");
        picture.append(image);
        image.className = "lightbox-image";
        image.alt = photo.preview.alt;
        image.draggable = false;
        image.setAttribute("fetchpriority", priority);
        image.sizes = sizes;
        image.srcset = photo.preview.getAttribute(lightboxSrcsetAttribute) ?? "";
        image.addEventListener("load", () => {
            // Decoding before the reveal keeps the fade from waiting on a synchronous main-thread decode.
            const reveal = (): void => {
                if (image.isConnected) {
                    frame.dataset.loaded = "true";
                }
            };
            image.decode().then(reveal, reveal);
        });

        frame.append(picture);
        slot.dataset.photoIndex = String(slotPosition);
        slot.replaceChildren(frame);
    };

    const clearSlot = (slot: HTMLElement): void => {
        slot.replaceChildren();
        delete slot.dataset.photoIndex;
    };

    const fillNeighbors = (): void => {
        window.clearTimeout(neighborTimer);
        if (!lightbox || !canNavigate) {
            return;
        }

        const slots = getSlots(lightbox);
        if (!slots[0].hasChildNodes()) {
            fillSlot(slots[0], wrap(position - 1), "low");
        }
        if (!slots[2].hasChildNodes()) {
            fillSlot(slots[2], wrap(position + 1), "low");
        }
    };

    const updateSlots = (elements: LightboxElements): void => {
        getSlots(elements).forEach((slot, position) => {
            if (position === centerSlot) {
                slot.removeAttribute("aria-hidden");
            } else {
                slot.setAttribute("aria-hidden", "true");
            }
        });
    };

    const announce = (elements: LightboxElements): void => {
        if (position !== gapPosition) {
            shownIndex = position;
            elements.status.textContent = `Photo ${position + 1} of ${photos.length}`;
        }
    };

    const render = (elements: LightboxElements): void => {
        const slots = getSlots(elements);
        fillSlot(slots[centerSlot], position, "high");
        clearSlot(slots[0]);
        clearSlot(slots[2]);
        updateSlots(elements);
        announce(elements);

        window.clearTimeout(neighborTimer);
        if (canNavigate) {
            // Neighbors wait briefly so the visible photo gets the connection first.
            neighborTimer = window.setTimeout(fillNeighbors, neighborDelayMs);
        }
    };

    const writeDrag = (elements: LightboxElements): void => {
        const { style } = elements.dialog;
        style.setProperty("--lightbox-drag-x", `${dragX}px`);
        style.setProperty("--lightbox-dismiss-y", `${dragY}px`);
        style.setProperty("--lightbox-dismiss", String(Math.min(Math.abs(dragY) / dismissFadeDistance, 1)));
    };

    const cancelDragFrame = (): void => {
        if (dragFrame !== null) {
            cancelAnimationFrame(dragFrame);
            dragFrame = null;
        }
    };

    /** Writes the latest offsets and flushes style so a following transition starts exactly from them. */
    const flushDrag = (elements: LightboxElements): void => {
        cancelDragFrame();
        writeDrag(elements);
        void getComputedStyle(elements.track).transform;
        void getComputedStyle(elements.stage).transform;
    };

    const resetDrag = (elements: LightboxElements): void => {
        cancelDragFrame();
        dragX = 0;
        dragY = 0;
        writeDrag(elements);
    };

    /** Commits the pending slot rotation, leaving drag offsets untouched. */
    const commit = (elements: LightboxElements): Direction | 0 | null => {
        const direction = pendingDirection;
        if (direction === null) {
            return null;
        }

        pendingDirection = null;
        const { dialog, track } = elements;
        delete dialog.dataset.animating;
        dialog.style.setProperty("--lightbox-shift", "0");

        if (direction === 1) {
            const recycled = track.firstElementChild as HTMLElement;
            track.append(recycled);
            fillSlot(recycled, wrap(position + 1), "low");
        } else if (direction === -1) {
            const recycled = track.lastElementChild as HTMLElement;
            track.prepend(recycled);
            fillSlot(recycled, wrap(position - 1), "low");
        }

        updateSlots(elements);
        return direction;
    };

    /** Completes an in-flight slide or snap-back at its resting position. */
    const settle = (): void => {
        window.clearTimeout(settleTimer);
        if (lightbox && commit(lightbox) !== null) {
            resetDrag(lightbox);
            // Arriving in the gap continues straight through to the photo on the other side.
            if (position === gapPosition && !closing) {
                step(lastTravel);
            }
        }
    };

    /**
     * Stops an in-flight slide or snap-back where it currently appears. The slot rotation is committed and the drag
     * offsets absorb the remaining distance, so the next motion continues from here instead of jumping to rest.
     */
    const freeze = (elements: LightboxElements): void => {
        window.clearTimeout(settleTimer);
        if (pendingDirection === null) {
            return;
        }

        const trackX = new DOMMatrixReadOnly(getComputedStyle(elements.track).transform).m41;
        const stageY = new DOMMatrixReadOnly(getComputedStyle(elements.stage).transform).m42;
        const slotWidthPx = elements.stage.getBoundingClientRect().width;
        const direction = commit(elements) ?? 0;
        // At rest the track sits one slot left; a committed rotation moves the destination slot back one position.
        // Carry at most one slot of remaining distance: only three slots exist, so presses that outpace the motion
        // would otherwise slide through empty track.
        const remainingX = trackX + slotWidthPx + direction * slotWidthPx;
        dragX = Math.max(-slotWidthPx, Math.min(slotWidthPx, remainingX));
        dragY = stageY;
        flushDrag(elements);
    };

    /** Animates from the current offsets to a neighboring slot, or back to rest when direction is 0. */
    const startAnimation = (elements: LightboxElements, direction: Direction | 0, gapLeg = false): void => {
        pendingDirection = direction;
        if (reducedMotion.matches) {
            settle();
            return;
        }

        flushDrag(elements);
        elements.dialog.dataset.animating = gapLeg ? "gap" : "true";
        dragX = 0;
        dragY = 0;
        writeDrag(elements);
        elements.dialog.style.setProperty("--lightbox-shift", String(-direction));
        settleTimer = window.setTimeout(settle, settleFallbackMs);
    };

    const step = (direction: Direction): void => {
        if (!lightbox || !canNavigate || closing) {
            return;
        }

        // Retargets an in-flight slide from its on-screen position so rapid presses read as continuous motion.
        freeze(lightbox);
        fillNeighbors();
        const leavingGap = position === gapPosition;
        position = wrap(position + direction);
        lastTravel = direction;
        announce(lightbox);
        startAnimation(lightbox, direction, leavingGap || position === gapPosition);
    };

    const jumpTo = (photoIndex: number): void => {
        if (!lightbox || closing || photoIndex === position) {
            return;
        }

        // Freeze commits any in-flight rotation without continuing out of the gap; render then replaces every slot.
        freeze(lightbox);
        resetDrag(lightbox);
        position = photoIndex;
        render(lightbox);
    };

    const finishClose = (): void => {
        window.clearTimeout(closeTimer);
        window.clearTimeout(neighborTimer);
        if (!lightbox) {
            return;
        }

        const elements = lightbox;
        const { dialog } = elements;
        elements.resizeObserver.disconnect();
        delete root.dataset.lightboxOpen;
        if (dialog.open) {
            dialog.close();
        }

        delete dialog.dataset.state;
        delete dialog.dataset.animating;
        delete dialog.dataset.dragging;
        dialog.style.setProperty("--lightbox-shift", "0");
        resetDrag(elements);
        // Releases decoded full-size bitmaps and cancels any pending downloads.
        getSlots(elements).forEach(clearSlot);
        pendingDirection = null;
        drag = null;
        closing = false;

        const { trigger } = photos[shownIndex];
        if (trigger.isConnected) {
            trigger.focus({ preventScroll: true });
            trigger.scrollIntoView({ block: "nearest" });
        }
    };

    const close = (flingDirection: number): void => {
        if (!lightbox || closing || !lightbox.dialog.open) {
            return;
        }

        freeze(lightbox);
        cancelDragFrame();
        closing = true;
        drag = null;
        if (reducedMotion.matches) {
            finishClose();
            return;
        }

        const { dialog } = lightbox;
        delete dialog.dataset.dragging;
        if (flingDirection !== 0) {
            dialog.style.setProperty("--lightbox-dismiss-y", `${flingDirection * window.innerHeight * 0.6}px`);
        }
        dialog.dataset.state = "closing";
        closeTimer = window.setTimeout(finishClose, settleFallbackMs);
    };

    const onResize = (): void => {
        if (resizeFrame !== null) {
            return;
        }

        resizeFrame = requestAnimationFrame(() => {
            resizeFrame = null;
            if (!lightbox?.dialog.open) {
                return;
            }

            measure(lightbox);
            for (const slot of getSlots(lightbox)) {
                const photo = photos[Number(slot.dataset.photoIndex)];
                if (!photo) {
                    continue;
                }

                const sizes = getSizes(photo);
                slot.querySelectorAll<HTMLSourceElement | HTMLImageElement>("picture > source, picture > img")
                    .forEach((candidate) => {
                        candidate.sizes = sizes;
                    });
            }
        });
    };

    const onPointerDown = (event: PointerEvent): void => {
        if (!lightbox || closing || drag || !event.isPrimary || event.button !== 0) {
            return;
        }

        // Grabbing a moving slide holds it in place rather than snapping it to rest.
        freeze(lightbox);
        fillNeighbors();
        lightbox.stage.setPointerCapture(event.pointerId);
        drag = {
            pointerId: event.pointerId,
            target: event.target,
            startX: event.clientX,
            startY: event.clientY,
            lastX: event.clientX,
            lastY: event.clientY,
            lastTime: event.timeStamp,
            baseX: dragX,
            baseY: dragY,
            velocityX: 0,
            velocityY: 0,
            axis: null
        };
    };

    const onPointerMove = (event: PointerEvent): void => {
        if (!lightbox || !drag || event.pointerId !== drag.pointerId) {
            return;
        }

        const deltaX = event.clientX - drag.startX;
        const deltaY = event.clientY - drag.startY;
        const elapsed = event.timeStamp - drag.lastTime;
        if (elapsed > 0) {
            drag.velocityX = (event.clientX - drag.lastX) / elapsed;
            drag.velocityY = (event.clientY - drag.lastY) / elapsed;
            drag.lastX = event.clientX;
            drag.lastY = event.clientY;
            drag.lastTime = event.timeStamp;
        }

        if (drag.axis === null) {
            const distanceX = Math.abs(deltaX);
            const distanceY = Math.abs(deltaY);
            if (distanceY > distanceX + verticalLockBias && distanceY > verticalLockDistance) {
                drag.axis = "vertical";
            } else if (canNavigate && distanceX > distanceY + horizontalLockBias && distanceX > horizontalLockDistance) {
                drag.axis = "horizontal";
            } else {
                return;
            }

            lightbox.dialog.dataset.dragging = drag.axis;
        }

        dragX = drag.baseX + (drag.axis === "horizontal" ? deltaX : 0);
        dragY = drag.baseY + (drag.axis === "vertical" ? deltaY : 0);
        if (dragFrame === null) {
            const elements = lightbox;
            dragFrame = requestAnimationFrame(() => {
                dragFrame = null;
                writeDrag(elements);
            });
        }
    };

    const onPointerEnd = (event: PointerEvent): void => {
        if (!lightbox || !drag || event.pointerId !== drag.pointerId) {
            return;
        }

        const state = drag;
        drag = null;
        delete lightbox.dialog.dataset.dragging;
        flushDrag(lightbox);

        const deltaX = event.clientX - state.startX;
        const deltaY = event.clientY - state.startY;
        const fresh = event.timeStamp - state.lastTime < staleVelocityMs;
        const velocityX = fresh ? state.velocityX : 0;
        const velocityY = fresh ? state.velocityY : 0;

        const displaced = dragX !== 0 || dragY !== 0;
        if (event.type === "pointercancel") {
            if (displaced) {
                startAnimation(lightbox, 0);
            }
            return;
        }

        if (state.axis === "horizontal") {
            if (Math.abs(deltaX) > swipeDistance || Math.abs(velocityX) > swipeVelocity) {
                const travel = Math.abs(deltaX) > swipeDistance ? deltaX : velocityX;
                step(travel < 0 ? 1 : -1);
            } else {
                startAnimation(lightbox, 0);
            }
            return;
        }

        if (state.axis === "vertical") {
            if (Math.abs(deltaY) > dismissDistance || Math.abs(velocityY) > dismissVelocity) {
                const travel = Math.abs(deltaY) > dismissDistance ? deltaY : velocityY;
                close(travel < 0 ? -1 : 1);
            } else {
                startAnimation(lightbox, 0);
            }
            return;
        }

        // Releasing a held slide resumes it; otherwise a tap outside the photo closes, and a lone photo closes on any tap.
        if (displaced) {
            startAnimation(lightbox, 0);
            return;
        }

        const tappedPhoto = state.target instanceof Element && state.target.closest(".lightbox-frame") !== null;
        if (Math.hypot(deltaX, deltaY) <= tapSlop && (!canNavigate || !tappedPhoto)) {
            close(0);
        }
    };

    const onKeyDown = (event: KeyboardEvent): void => {
        if (closing || event.altKey || event.ctrlKey || event.metaKey) {
            return;
        }

        let action: (() => void) | null = null;
        switch (event.key) {
            case "ArrowRight":
                action = () => step(1);
                break;
            case "ArrowLeft":
                action = () => step(-1);
                break;
            case "Home":
                action = () => jumpTo(0);
                break;
            case "End":
                action = () => jumpTo(photos.length - 1);
                break;
            case "Escape":
                // Handled directly: close watchers may skip the cancelable dialog event for some key presses.
                action = () => close(0);
                break;
        }

        if (!action) {
            return;
        }

        event.preventDefault();
        // Held keys advance once per completed slide instead of snapping through in-flight transitions.
        if (!(event.repeat && pendingDirection !== null)) {
            action();
        }
    };

    const createLightbox = (): LightboxElements => {
        const dialog = document.createElement("dialog");
        dialog.className = "photo-lightbox";
        dialog.setAttribute("aria-label", "Photo viewer");

        const stage = createDiv("lightbox-stage");
        const track = createDiv("lightbox-track");
        track.append(createDiv("lightbox-slot"), createDiv("lightbox-slot"), createDiv("lightbox-slot"));
        stage.append(track);

        const closeButton = createButton("lightbox-close", "Close", icons.close);
        const previousButton = createButton("lightbox-previous", "Previous", icons.previous);
        const nextButton = createButton("lightbox-next", "Next", icons.next);
        closeButton.autofocus = true;
        previousButton.hidden = !canNavigate;
        nextButton.hidden = !canNavigate;

        const status = document.createElement("p");
        status.className = "lightbox-status";
        status.setAttribute("aria-live", "polite");

        dialog.append(createDiv("lightbox-backdrop"), stage, closeButton, previousButton, nextButton, status);

        closeButton.addEventListener("click", () => close(0));
        previousButton.addEventListener("click", () => step(-1));
        nextButton.addEventListener("click", () => step(1));
        dialog.addEventListener("keydown", onKeyDown);
        dialog.addEventListener("cancel", (event) => {
            event.preventDefault();
            close(0);
        });
        // Browsers may close a modal without a cancelable event; clean up whenever that happens.
        dialog.addEventListener("close", () => {
            if (root.dataset.lightboxOpen) {
                finishClose();
            }
        });
        dialog.addEventListener("transitionend", (event) => {
            if (event.target === dialog && event.propertyName === "opacity" && closing) {
                finishClose();
            } else if ((event.target === track || event.target === stage)
                && event.propertyName === "transform"
                && pendingDirection !== null) {
                settle();
            }
        });
        stage.addEventListener("contextmenu", (event) => event.preventDefault());
        stage.addEventListener("pointerdown", onPointerDown);
        stage.addEventListener("pointermove", onPointerMove);
        stage.addEventListener("pointerup", onPointerEnd);
        stage.addEventListener("pointercancel", onPointerEnd);

        document.body.append(dialog);
        return { dialog, stage, track, status, resizeObserver: new ResizeObserver(onResize) };
    };

    const open = (photoIndex: number): void => {
        lightbox ??= createLightbox();
        const elements = lightbox;
        if (elements.dialog.open) {
            return;
        }

        position = photoIndex;
        shownIndex = photoIndex;
        lastTravel = 1;
        closing = false;
        root.dataset.lightboxOpen = "true";
        elements.dialog.showModal();
        measure(elements);
        render(elements);
        elements.resizeObserver.observe(elements.stage);
    };

    const findPhotoIndex = (target: EventTarget | null): number => {
        const trigger = target instanceof Element ? target.closest<HTMLAnchorElement>(triggerSelector) : null;
        return trigger ? photos.findIndex((photo) => photo.trigger === trigger) : -1;
    };

    const onListClick = (event: MouseEvent): void => {
        const photoIndex = findPhotoIndex(event.target);
        if (photoIndex >= 0) {
            event.preventDefault();
            open(photoIndex);
        }
    };

    // Without an href the trigger has no native activation, so Enter and Space open it like a button.
    const onListKeyDown = (event: KeyboardEvent): void => {
        if (event.key !== "Enter" && event.key !== " ") {
            return;
        }

        const photoIndex = findPhotoIndex(event.target);
        if (photoIndex >= 0) {
            event.preventDefault();
            open(photoIndex);
        }
    };

    // The href is the no-JavaScript full-size fallback. Removing it while the lightbox is active prevents the
    // browser's link-preview status bubble and makes the trigger an explicit button; disposal restores it.
    for (const { trigger } of photos) {
        const href = trigger.getAttribute("href");
        if (href !== null) {
            trigger.dataset.photoHref = href;
            trigger.removeAttribute("href");
        }
        trigger.setAttribute("role", "button");
        trigger.tabIndex = 0;
    }

    list.addEventListener("click", onListClick);
    list.addEventListener("keydown", onListKeyDown);

    return () => {
        list.removeEventListener("click", onListClick);
        list.removeEventListener("keydown", onListKeyDown);
        for (const { trigger } of photos) {
            if (trigger.dataset.photoHref !== undefined) {
                trigger.setAttribute("href", trigger.dataset.photoHref);
                delete trigger.dataset.photoHref;
            }
            trigger.removeAttribute("role");
            trigger.removeAttribute("tabindex");
        }
        window.clearTimeout(settleTimer);
        window.clearTimeout(closeTimer);
        window.clearTimeout(neighborTimer);
        cancelDragFrame();
        if (resizeFrame !== null) {
            cancelAnimationFrame(resizeFrame);
        }

        delete root.dataset.lightboxOpen;
        if (lightbox) {
            lightbox.resizeObserver.disconnect();
            if (lightbox.dialog.open) {
                lightbox.dialog.close();
            }
            lightbox.dialog.remove();
            lightbox = null;
        }
    };
}
