"use strict";

const $ = id => document.getElementById(id);
const radians = Math.PI / 180;
const fixedStep = 1 / 240;
let length;
let gravity;
let angle;
let velocity;
let elapsed;
let lastCross;
let lastFrame;
let remaining;
let running;
let frame;

const diagram = instrumentCanvas($("diagram"), 600, 395, context => {
    const pivot = { x: 300, y: 65 };
    const rod = 100 + 110 * Math.sqrt(length / 2);
    const x = pivot.x + Math.sin(angle) * rod;
    const y = pivot.y + Math.cos(angle) * rod;

    context.strokeStyle = "#a4bbc9";
    context.setLineDash([5, 5]);
    context.beginPath();
    context.moveTo(pivot.x, pivot.y);
    context.lineTo(pivot.x, 345);
    context.stroke();
    context.setLineDash([]);
    context.strokeStyle = "#87a3b5";
    context.beginPath();
    context.arc(pivot.x, pivot.y, 79, Math.PI / 2 - 70 * radians, Math.PI / 2 + 70 * radians);
    context.stroke();
    context.textAlign = "center";
    context.font = "13px system-ui";
    context.fillStyle = "#426074";
    for (const tick of [-60, -45, -30, -15, 0, 15, 30, 45, 60]) {
        const sine = Math.sin(tick * radians);
        const cosine = Math.cos(tick * radians);
        context.beginPath();
        context.moveTo(pivot.x + sine * 76, pivot.y + cosine * 76);
        context.lineTo(pivot.x + sine * 83, pivot.y + cosine * 83);
        context.stroke();
        if (tick % 30 === 0)
            context.fillText(`${tick}°`, pivot.x + sine * 98, pivot.y + cosine * 98 + 4);
    }
    context.fillStyle = "#18344a";
    context.fillRect(246, 41, 108, 12);
    context.strokeStyle = "#304f65";
    context.lineWidth = 4;
    context.beginPath();
    context.moveTo(pivot.x, 50);
    context.lineTo(pivot.x, pivot.y);
    context.lineTo(x, y);
    context.stroke();
    context.fillStyle = "#176387";
    context.beginPath();
    context.arc(x, y, 17, 0, Math.PI * 2);
    context.fill();
    context.fillStyle = "#a13f24";
    context.beginPath();
    context.arc(pivot.x, pivot.y, 5, 0, Math.PI * 2);
    context.fill();
    context.font = "600 18px system-ui";
    context.fillText(`${(angle / radians).toFixed(1)}°`, 300, 366);
});

function reset() {
    cancelAnimationFrame(frame);
    running = false;
    length = $("length").valueAsNumber;
    gravity = $("gravity").valueAsNumber;
    angle = $("angle").valueAsNumber * radians;
    velocity = elapsed = remaining = 0;
    lastCross = lastFrame = undefined;
    $("lengthValue").textContent = `${length.toFixed(2)} m`;
    $("gravityValue").textContent = `${gravity.toFixed(2)} m/s²`;
    $("angleValue").textContent = `${$("angle").value}°`;
    $("prediction").textContent = `${(2 * Math.PI * Math.sqrt(length / gravity)).toFixed(3)} s`;
    $("start").textContent = "Start";
    $("elapsed").textContent = "0.00 s";
    $("cycles").textContent = "0";
    $("period").textContent = "—";
    diagram.draw();
}

function tick(timestamp) {
    if (!running) return;
    if (lastFrame !== undefined) remaining += Math.min(.25, (timestamp - lastFrame) / 1000);
    lastFrame = timestamp;
    while (remaining >= fixedStep) {
        const before = angle;
        const acceleration = -gravity / length * Math.sin(angle);
        angle += velocity * fixedStep + acceleration * fixedStep ** 2 / 2;
        velocity += (acceleration - gravity / length * Math.sin(angle)) * fixedStep / 2;
        if (before > 0 && angle <= 0) {
            const crossing = elapsed + fixedStep * before / (before - angle);
            if (lastCross !== undefined) {
                $("period").textContent = `${(crossing - lastCross).toFixed(3)} s`;
                $("cycles").textContent = String(Number($("cycles").textContent) + 1);
            }
            lastCross = crossing;
        }
        elapsed += fixedStep;
        remaining -= fixedStep;
    }
    $("elapsed").textContent = `${elapsed.toFixed(2)} s`;
    diagram.draw();
    frame = requestAnimationFrame(tick);
}

$("start").onclick = () => {
    running = !running;
    lastFrame = undefined;
    $("start").textContent = running ? "Pause" : "Resume";
    if (running) frame = requestAnimationFrame(tick);
    else cancelAnimationFrame(frame);
};
$("reset").onclick = reset;
for (const control of ["length", "gravity", "angle"])
    $(control).oninput = reset;
reset();
