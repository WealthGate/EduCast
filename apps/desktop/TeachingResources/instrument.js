"use strict";

function instrumentCanvas(canvas, width, height, paint) {
    const context = canvas.getContext("2d");
    let offsetX = 0;
    let offsetY = 0;
    let scale = 1;
    function draw() {
        const { clientWidth, clientHeight } = canvas;
        if (!clientWidth || !clientHeight) return;
        const ratio = devicePixelRatio || 1;
        const pixelWidth = Math.round(clientWidth * ratio);
        const pixelHeight = Math.round(clientHeight * ratio);
        if (canvas.width !== pixelWidth) canvas.width = pixelWidth;
        if (canvas.height !== pixelHeight) canvas.height = pixelHeight;
        scale = Math.min(clientWidth / width, clientHeight / height);
        offsetX = (clientWidth - width * scale) / 2;
        offsetY = (clientHeight - height * scale) / 2;
        context.resetTransform();
        context.clearRect(0, 0, pixelWidth, pixelHeight);
        context.setTransform(scale * ratio, 0, 0, scale * ratio, offsetX * ratio, offsetY * ratio);
        paint(context);
    }
    new ResizeObserver(draw).observe(canvas);
    return {
        draw,
        point(event) {
            const bounds = canvas.getBoundingClientRect();
            return [(event.clientX - bounds.left - offsetX) / scale,
                (event.clientY - bounds.top - offsetY) / scale];
        }
    };
}
