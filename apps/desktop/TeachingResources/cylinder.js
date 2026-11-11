"use strict";

const $ = id => document.getElementById(id);
let shown = false;
const readingY = volume => 345 - 2.83 * volume;

const cylinder = instrumentCanvas($("cylinder"), 560, 398, context => {
    const volume = $("water").valueAsNumber;
    const y = readingY(volume);
    context.fillStyle = "#e4f1f8";
    context.fillRect(209, 49, 118, 308);
    context.beginPath();
    context.moveTo(209, y - 9);
    context.quadraticCurveTo(268, y + 9, 327, y - 9);
    context.lineTo(327, 357);
    context.lineTo(209, 357);
    context.closePath();
    context.fillStyle = "#8ed0ec";
    context.fill();
    context.beginPath();
    context.moveTo(209, y - 9);
    context.quadraticCurveTo(268, y + 9, 327, y - 9);
    context.strokeStyle = "#1d658e";
    context.lineWidth = 2.5;
    context.stroke();
    context.strokeStyle = "#395e74";
    context.beginPath();
    context.moveTo(201, 45);
    context.lineTo(209, 55);
    context.lineTo(209, 357);
    context.lineTo(327, 357);
    context.lineTo(327, 45);
    context.stroke();
    context.fillStyle = "#395e74";
    context.fillRect(191, 357, 154, 13);
    context.font = "16px system-ui";
    context.textAlign = "left";
    context.fillText("mL", 342, 43);
    for (let tick = 0; tick <= 100; tick += 2) {
        const major = tick % 10 === 0;
        const level = readingY(tick);
        context.beginPath();
        context.lineWidth = major ? 1.6 : .8;
        context.moveTo(major ? 302 : 314, level);
        context.lineTo(327, level);
        context.stroke();
        if (major) context.fillText(tick, 340, level + 5);
    }
    if (shown) {
        context.setLineDash([6, 4]);
        context.strokeStyle = "#a63d27";
        context.lineWidth = 2;
        context.beginPath();
        context.moveTo(140, y);
        context.lineTo(334, y);
        context.stroke();
        context.setLineDash([]);
        context.fillStyle = "#a63d27";
        context.font = "600 15px system-ui";
        context.fillText("Eye level", 67, y + 5);
    }
});

function refresh() {
    $("volume").textContent = shown ? `${$("water").value} mL` : "—";
    $("reveal").textContent = shown ? "Hide reading" : "Show reading";
    $("reveal").setAttribute("aria-pressed", String(shown));
    cylinder.draw();
}
$("reveal").onclick = () => { shown = !shown; refresh(); };
$("random").onclick = () => {
    $("water").value = String(8 + Math.floor(Math.random() * 88));
    shown = false;
    refresh();
};
$("water").oninput = refresh;
refresh();
