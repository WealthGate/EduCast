"use strict";

const $ = id => document.getElementById(id);
const metre = 61;
const inset = 22;
const fieldWidth = 8;
const fieldHeight = 6;
const canvas = $("field");
let plants = [];
let samples = [];
let quadratX = 2;
let quadratY = 2;
let side = 1;

const field = instrumentCanvas(canvas, 532, 418, context => {
    context.fillStyle = "#e5eed3";
    context.fillRect(inset, inset, fieldWidth * metre, fieldHeight * metre);
    context.strokeStyle = "#c6d8b1";
    for (let x = 0; x <= fieldWidth; x++) {
        context.beginPath();
        context.moveTo(inset + x * metre, inset);
        context.lineTo(inset + x * metre, inset + fieldHeight * metre);
        context.stroke();
    }
    for (let y = 0; y <= fieldHeight; y++) {
        context.beginPath();
        context.moveTo(inset, inset + y * metre);
        context.lineTo(inset + fieldWidth * metre, inset + y * metre);
        context.stroke();
    }
    context.fillStyle = "#28744c";
    context.beginPath();
    for (const plant of plants) {
        const x = inset + plant.x * metre;
        const y = inset + plant.y * metre;
        context.moveTo(x + 3.4, y);
        context.arc(x, y, 3.4, 0, 2 * Math.PI);
    }
    context.fill();
    const x = inset + quadratX * metre;
    const y = inset + quadratY * metre;
    context.fillStyle = "rgba(255,247,215,.36)";
    context.fillRect(x, y, side * metre, side * metre);
    context.strokeStyle = "#a63820";
    context.lineWidth = 3;
    context.strokeRect(x, y, side * metre, side * metre);
    context.strokeStyle = "#263c44";
    context.lineWidth = 2;
    context.beginPath();
    context.moveTo(inset, 402);
    context.lineTo(inset + metre, 402);
    context.stroke();
    context.fillStyle = "#263c44";
    context.font = "14px system-ui";
    context.fillText("1 m", inset + metre + 9, 407);
});

function place(x, y) {
    quadratX = Math.min(fieldWidth - side, Math.max(0, x));
    quadratY = Math.min(fieldHeight - side, Math.max(0, y));
    field.draw();
}

function reset() {
    samples = [];
    $("last").textContent = "—";
    $("samples").textContent = "0";
    $("density").textContent = "—";
}

function populate() {
    plants = Array.from({ length: 92 }, () => ({ x: Math.random() * fieldWidth, y: Math.random() * fieldHeight }));
    reset();
    field.draw();
}

$("size").oninput = () => {
    side = $("size").valueAsNumber;
    $("sizeValue").textContent = `${side} m · ${side ** 2} m²`;
    place(quadratX, quadratY);
};
$("random").onclick = () => place(Math.random() * (fieldWidth - side), Math.random() * (fieldHeight - side));
$("record").onclick = () => {
    const count = plants.filter(plant => plant.x >= quadratX && plant.x < quadratX + side &&
        plant.y >= quadratY && plant.y < quadratY + side).length;
    const area = side ** 2;
    samples.push({ count, area });
    $("last").textContent = `${count} in ${area} m²`;
    $("samples").textContent = String(samples.length);
    const total = samples.reduce((sum, sample) => sum + sample.count, 0);
    const sampledArea = samples.reduce((sum, sample) => sum + sample.area, 0);
    $("density").textContent = `${(total / sampledArea).toFixed(2)} plants/m²`;
};
$("reset").onclick = reset;
$("newField").onclick = populate;
canvas.addEventListener("pointerdown", event => {
    if (event.button !== 0) return;
    const [x, y] = field.point(event);
    place((x - inset) / metre - side / 2, (y - inset) / metre - side / 2);
});
canvas.addEventListener("keydown", event => {
    const direction = { ArrowLeft: [-.25, 0], ArrowRight: [.25, 0], ArrowUp: [0, -.25], ArrowDown: [0, .25] }[event.key];
    if (!direction) return;
    event.preventDefault();
    place(quadratX + direction[0], quadratY + direction[1]);
});
$("size").oninput();
populate();
