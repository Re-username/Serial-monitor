window.spSetTheme = function (theme) {
    document.documentElement.className = "theme-" + theme;
    try { localStorage.setItem("serialplotter-theme", theme); } catch (e) { }
};

window.spGetTheme = function () {
    try { return localStorage.getItem("serialplotter-theme") || "cyberpunk"; }
    catch (e) { return "cyberpunk"; }
};

window.spSvgToPngDataUrl = async function (svgText, width, height) {
    const blob = new Blob([svgText], { type: "image/svg+xml;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    try {
        const image = new Image();
        const loaded = new Promise((resolve, reject) => {
            image.onload = resolve;
            image.onerror = reject;
        });
        image.src = url;
        await loaded;
        const canvas = document.createElement("canvas");
        canvas.width = Math.max(1, Math.round(width));
        canvas.height = Math.max(1, Math.round(height));
        const ctx = canvas.getContext("2d");
        ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
        return canvas.toDataURL("image/png");
    } finally {
        URL.revokeObjectURL(url);
    }
};

window.spSvgPoint = function (container, clientX, clientY) {
    const svg = container && container.tagName && container.tagName.toLowerCase() === "svg"
        ? container
        : container.querySelector("svg");
    if (!svg) return { x: -1, y: -1 };
    const point = svg.createSVGPoint();
    point.x = clientX;
    point.y = clientY;
    const matrix = svg.getScreenCTM();
    if (!matrix) return { x: -1, y: -1 };
    const result = point.matrixTransform(matrix.inverse());
    return { x: result.x, y: result.y };
};

window.spCapturePointer = function (element, pointerId) {
    try { element.setPointerCapture(pointerId); } catch (e) { }
};

window.spScrollToEnd = function (element) {
    if (!element) return;
    element.scrollTop = element.scrollHeight;
};

window.spPlotWheel = (function () {
    let element = null;
    let reference = null;
    function onWheel(event) {
        if (!reference || !element) return;
        event.preventDefault();
        event.stopPropagation();
        const point = window.spSvgPoint(element, event.clientX, event.clientY);
        reference.invokeMethodAsync("OnPlotWheel",
            point.x, point.y, event.deltaY,
            event.ctrlKey, event.altKey, event.shiftKey);
    }
    return {
        init: function (plotElement, dotNetReference) {
            if (element) element.removeEventListener("wheel", onWheel, true);
            element = plotElement;
            reference = dotNetReference;
            element.addEventListener("wheel", onWheel, { capture: true, passive: false });
        },
        dispose: function () {
            if (element) element.removeEventListener("wheel", onWheel, true);
            element = null;
            reference = null;
        }
    };
})();

window.spHotkeys = (function () {
    let reference = null;
    const handledKeys = new Set([
        "F1", "F12", "Escape", " ",
        "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown",
        "+", "=", "-", "0"
    ]);
    function isEditing(target) {
        if (!target) return false;
        const tag = target.tagName ? target.tagName.toLowerCase() : "";
        return tag === "input" || tag === "select" || tag === "textarea" || target.isContentEditable;
    }
    function onKeyDown(event) {
        if (!reference || !handledKeys.has(event.key)) return;
        if (isEditing(event.target) && event.key !== "F1" && event.key !== "F12" && event.key !== "Escape")
            return;
        event.preventDefault();
        event.stopPropagation();
        reference.invokeMethodAsync("OnHotkey", event.key, event.ctrlKey, event.altKey, event.shiftKey);
    }
    return {
        init: function (dotNetReference) {
            if (reference) window.removeEventListener("keydown", onKeyDown, true);
            reference = dotNetReference;
            window.addEventListener("keydown", onKeyDown, true);
        },
        dispose: function () {
            window.removeEventListener("keydown", onKeyDown, true);
            reference = null;
        }
    };
})();
