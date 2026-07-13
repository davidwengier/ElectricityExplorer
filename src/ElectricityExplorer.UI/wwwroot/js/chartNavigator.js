const controllers = new Map();

export function connect(
    mainChartId,
    overviewId,
    dotnet,
    viewStart,
    viewEnd,
    minimumSpan) {
    const main = document.getElementById(mainChartId);
    const overview = document.getElementById(overviewId);
    if (!main || !overview) {
        return;
    }

    let controller = controllers.get(overviewId);
    if (!controller) {
        controller = createController(main, overview);
        controllers.set(overviewId, controller);
    }

    controller.dotnet = dotnet;
    controller.minimumSpan = clamp(minimumSpan, 0.0001, 1);
    if (!controller.mainDrag && !controller.overviewDrag) {
        controller.setViewport(viewStart, viewEnd, false);
    }
}

export function disconnect(overviewId) {
    const controller = controllers.get(overviewId);
    if (!controller) {
        return;
    }

    controller.dispose();
    controllers.delete(overviewId);
}

function createController(main, overview) {
    const controller = {
        main,
        overview,
        dotnet: null,
        viewStart: 0,
        viewEnd: 1,
        minimumSpan: 0.0001,
        mainDrag: null,
        overviewDrag: null,
        pendingViewport: null,
        animationFrame: 0
    };

    const setViewport = (start, end, notify = true) => {
        ({ start, end } = constrain(start, end, controller.minimumSpan));
        controller.viewStart = start;
        controller.viewEnd = end;
        updateSelection(controller);

        if (notify && controller.dotnet) {
            controller.pendingViewport = { start, end };
            if (!controller.animationFrame) {
                controller.animationFrame = requestAnimationFrame(() => {
                    controller.animationFrame = 0;
                    const pending = controller.pendingViewport;
                    controller.pendingViewport = null;
                    if (pending && controller.dotnet) {
                        controller.dotnet.invokeMethodAsync(
                            "SetNormalizedViewport",
                            pending.start,
                            pending.end);
                    }
                });
            }
        }
    };
    controller.setViewport = setViewport;

    const wheel = event => {
        event.preventDefault();
        const rect = main.getBoundingClientRect();
        if (rect.width <= 0) {
            return;
        }

        const pointer = clamp((event.clientX - rect.left) / rect.width, 0, 1);
        const currentSpan = controller.viewEnd - controller.viewStart;
        const factor = Math.exp(event.deltaY * 0.0015);
        const nextSpan = clamp(
            currentSpan * factor,
            controller.minimumSpan,
            1);
        const anchor = controller.viewStart + currentSpan * pointer;
        setViewport(
            anchor - nextSpan * pointer,
            anchor + nextSpan * (1 - pointer));
    };

    const mainPointerDown = event => {
        if (event.button !== 0) {
            return;
        }

        main.setPointerCapture(event.pointerId);
        main.classList.add("is-panning");
        controller.mainDrag = {
            pointerId: event.pointerId,
            startX: event.clientX,
            viewStart: controller.viewStart,
            viewEnd: controller.viewEnd
        };
    };

    const mainPointerMove = event => {
        const drag = controller.mainDrag;
        if (!drag || drag.pointerId !== event.pointerId) {
            return;
        }

        const width = main.getBoundingClientRect().width;
        if (width <= 0) {
            return;
        }

        const span = drag.viewEnd - drag.viewStart;
        const shift = -(event.clientX - drag.startX) / width * span;
        setViewport(drag.viewStart + shift, drag.viewEnd + shift);
    };

    const mainPointerUp = event => {
        if (controller.mainDrag?.pointerId !== event.pointerId) {
            return;
        }

        controller.mainDrag = null;
        main.classList.remove("is-panning");
        if (main.hasPointerCapture(event.pointerId)) {
            main.releasePointerCapture(event.pointerId);
        }
    };

    const overviewPointerDown = event => {
        if (event.button !== 0) {
            return;
        }

        const rect = overview.getBoundingClientRect();
        const ratio = pointerRatio(event, rect);
        const edgeTolerance = 10 / Math.max(rect.width, 1);
        let mode;

        if (Math.abs(ratio - controller.viewStart) <= edgeTolerance) {
            mode = "resize-start";
        } else if (Math.abs(ratio - controller.viewEnd) <= edgeTolerance) {
            mode = "resize-end";
        } else if (controller.viewEnd - controller.viewStart >= 0.999) {
            mode = "select";
        } else if (ratio > controller.viewStart && ratio < controller.viewEnd) {
            mode = "pan";
        } else {
            mode = "select";
        }

        overview.setPointerCapture(event.pointerId);
        controller.overviewDrag = {
            pointerId: event.pointerId,
            mode,
            startRatio: ratio,
            startX: event.clientX,
            viewStart: controller.viewStart,
            viewEnd: controller.viewEnd
        };
        overview.style.cursor =
            mode === "resize-start" || mode === "resize-end"
                ? "ew-resize"
                : mode === "pan"
                    ? "grabbing"
                    : "crosshair";
        overview.classList.add("is-brushing");
    };

    const overviewPointerMove = event => {
        const rect = overview.getBoundingClientRect();
        const ratio = pointerRatio(event, rect);
        const drag = controller.overviewDrag;
        if (!drag || drag.pointerId !== event.pointerId) {
            updateOverviewCursor(controller, ratio, rect.width);
            return;
        }

        switch (drag.mode) {
            case "resize-start":
                setViewport(
                    Math.min(ratio, drag.viewEnd - controller.minimumSpan),
                    drag.viewEnd);
                break;
            case "resize-end":
                setViewport(
                    drag.viewStart,
                    Math.max(ratio, drag.viewStart + controller.minimumSpan));
                break;
            case "pan": {
                const shift = ratio - drag.startRatio;
                setViewport(drag.viewStart + shift, drag.viewEnd + shift);
                break;
            }
            case "select":
                if (Math.abs(event.clientX - drag.startX) >= 3) {
                    setViewport(
                        Math.min(drag.startRatio, ratio),
                        Math.max(drag.startRatio, ratio));
                }
                break;
        }
    };

    const overviewPointerUp = event => {
        const drag = controller.overviewDrag;
        if (!drag || drag.pointerId !== event.pointerId) {
            return;
        }

        if (event.type !== "pointercancel"
            && drag.mode === "select"
            && Math.abs(event.clientX - drag.startX) < 3) {
            const rect = overview.getBoundingClientRect();
            const ratio = pointerRatio(event, rect);
            const span = drag.viewEnd - drag.viewStart;
            setViewport(ratio - span / 2, ratio + span / 2);
        }

        controller.overviewDrag = null;
        overview.classList.remove("is-brushing");
        const rect = overview.getBoundingClientRect();
        updateOverviewCursor(controller, pointerRatio(event, rect), rect.width);
        if (overview.hasPointerCapture(event.pointerId)) {
            overview.releasePointerCapture(event.pointerId);
        }
    };

    const overviewPointerLeave = () => {
        if (!controller.overviewDrag) {
            overview.style.cursor = "";
        }
    };

    main.addEventListener("wheel", wheel, { passive: false });
    main.addEventListener("pointerdown", mainPointerDown);
    main.addEventListener("pointermove", mainPointerMove);
    main.addEventListener("pointerup", mainPointerUp);
    main.addEventListener("pointercancel", mainPointerUp);
    overview.addEventListener("pointerdown", overviewPointerDown);
    overview.addEventListener("pointermove", overviewPointerMove);
    overview.addEventListener("pointerup", overviewPointerUp);
    overview.addEventListener("pointercancel", overviewPointerUp);
    overview.addEventListener("pointerleave", overviewPointerLeave);

    controller.dispose = () => {
        if (controller.animationFrame) {
            cancelAnimationFrame(controller.animationFrame);
        }

        main.removeEventListener("wheel", wheel);
        main.removeEventListener("pointerdown", mainPointerDown);
        main.removeEventListener("pointermove", mainPointerMove);
        main.removeEventListener("pointerup", mainPointerUp);
        main.removeEventListener("pointercancel", mainPointerUp);
        overview.removeEventListener("pointerdown", overviewPointerDown);
        overview.removeEventListener("pointermove", overviewPointerMove);
        overview.removeEventListener("pointerup", overviewPointerUp);
        overview.removeEventListener("pointercancel", overviewPointerUp);
        overview.removeEventListener("pointerleave", overviewPointerLeave);
    };

    return controller;
}

function updateSelection(controller) {
    const start = controller.viewStart * 1000;
    const end = controller.viewEnd * 1000;
    const width = Math.max(1, end - start);
    const overview = controller.overview;

    setAttribute(overview, ".chart-overview-shade-left", "width", start);
    setAttribute(overview, ".chart-overview-shade-right", "x", end);
    setAttribute(overview, ".chart-overview-shade-right", "width", 1000 - end);
    setAttribute(overview, ".chart-overview-selection", "x", start);
    setAttribute(overview, ".chart-overview-selection", "width", width);
    setAttribute(overview, ".chart-overview-handle-start", "x1", start);
    setAttribute(overview, ".chart-overview-handle-start", "x2", start);
    setAttribute(overview, ".chart-overview-handle-end", "x1", end);
    setAttribute(overview, ".chart-overview-handle-end", "x2", end);
}

function updateOverviewCursor(controller, ratio, width) {
    const tolerance = 10 / Math.max(width, 1);
    if (Math.abs(ratio - controller.viewStart) <= tolerance
        || Math.abs(ratio - controller.viewEnd) <= tolerance) {
        controller.overview.style.cursor = "ew-resize";
    } else if (ratio > controller.viewStart && ratio < controller.viewEnd) {
        controller.overview.style.cursor = "grab";
    } else {
        controller.overview.style.cursor = "crosshair";
    }
}

function setAttribute(root, selector, name, value) {
    root.querySelector(selector)?.setAttribute(name, value.toString());
}

function pointerRatio(event, rect) {
    return clamp((event.clientX - rect.left) / Math.max(rect.width, 1), 0, 1);
}

function constrain(start, end, minimumSpan) {
    if (end < start) {
        [start, end] = [end, start];
    }

    let span = end - start;
    if (span < minimumSpan) {
        const centre = (start + end) / 2;
        start = centre - minimumSpan / 2;
        end = centre + minimumSpan / 2;
        span = minimumSpan;
    }

    if (start < 0) {
        end -= start;
        start = 0;
    }

    if (end > 1) {
        start -= end - 1;
        end = 1;
    }

    if (span >= 1) {
        return { start: 0, end: 1 };
    }

    return {
        start: clamp(start, 0, 1 - span),
        end: clamp(end, span, 1)
    };
}

function clamp(value, minimum, maximum) {
    return Math.min(maximum, Math.max(minimum, value));
}
