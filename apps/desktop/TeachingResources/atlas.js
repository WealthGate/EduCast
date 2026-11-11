"use strict";

const $ = id => document.getElementById(id);
const canvas = $("map");
const context = canvas.getContext("2d");
const radians = Math.PI / 180;
const colors = ["#c5ddbb", "#f3d6a1", "#d4c6e4", "#c3ddde", "#ead4ca", "#dfdfad", "#d1dcf1"];
const sphere = { type: "Sphere" };
const graticule = d3.geoGraticule().step([15, 15]);
const choices = new Map();
const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
const wrap = longitude => ((longitude + 180) % 360 + 360) % 360 - 180;
const normalize = value => value.toLocaleLowerCase("en").normalize("NFD").replace(/\p{Diacritic}/gu, "");

let projection;
let atlas;
let frame;
let drag;
let focus;
let lon = 0;
let lat = 0;
let zoom = 1;
let isGlobe = location.hash === "#globe";

function mode(globe) {
    isGlobe = globe;
    zoom = Math.min(zoom, globe ? 4 : 32);
    $("title").textContent = globe ? "World globe" : "World map";
    $("flat").setAttribute("aria-pressed", String(!globe));
    $("globe").setAttribute("aria-pressed", String(globe));
    redraw();
}

function redraw() {
    if (!frame) frame = requestAnimationFrame(draw);
}

function configure(width, height) {
    const scale = zoom * (isGlobe ? Math.min(width, height) * .47
        : Math.min((width - 16) / (2 * Math.PI), (height - 12) / Math.PI));
    projection = isGlobe
        ? d3.geoOrthographic().rotate([-lon, -lat]).translate([width / 2, height / 2])
        : d3.geoEquirectangular().rotate([-lon, 0]).translate([width / 2, height / 2 + lat * radians * scale]);
    projection.scale(scale).precision(.6).clipExtent([[0, 0], [width, height]]);
}

function draw() {
    frame = undefined;
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;
    if (width < 20 || height < 20) return;
    const ratio = devicePixelRatio || 1;
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.fillStyle = isGlobe ? "#e8f0f7" : "#c8e6f5";
    context.fillRect(0, 0, width, height);
    configure(width, height);
    const path = d3.geoPath(projection, context);
    function paint(geometry, fill, outline, weight = 1) {
        context.beginPath();
        path(geometry);
        if (fill) {
            context.fillStyle = fill;
            context.fill();
        }
        if (outline) {
            context.strokeStyle = outline;
            context.lineWidth = weight;
            context.stroke();
        }
    }

    paint(sphere, "#c8e6f5", "#638ba4");
    if (atlas) {
        for (const country of atlas.countries) {
            const color = colors[(Math.max(1, country.properties.color) - 1) % colors.length];
            paint(country, color, "#50727c", .65);
        }
        for (const lake of atlas.lakes) {
            if (lake.properties.rank <= 2 + zoom)
                paint(lake, "#c8e6f5", "#83adc3", .45);
        }
        if ($("grid").checked)
            paint(graticule(), null, "rgba(37,86,115,.28)", .65);
        context.setLineDash([4, 3]);
        for (const border of atlas.disputes)
            paint(border, null, "#7c2851", 1.1);
        context.setLineDash([]);
        if (focus?.feature && focus.feature.geometry.type !== "Point")
            paint(focus.feature, null, "#125e82", 2.1);
        if ($("labels").checked || focus)
            drawLabels(width, height);
    }

    const coordinate = (value, negative, positive) => value === 0 ? "0°"
        : `${Math.abs(value).toFixed(1)}° ${value < 0 ? negative : positive}`;
    $("location").textContent = `${coordinate(lat, "S", "N")} · ${coordinate(lon, "W", "E")}`;
    const detailZoom = clamp(Math.round(Math.log2(projection.scale() * 2 * Math.PI / 256)), 2, 17);
    $("online").href = `https://www.openstreetmap.org/#map=${detailZoom}/${lat.toFixed(4)}/${lon.toFixed(4)}`;
}

function drawLabels(width, height) {
    const used = [];
    function drawLabel(name, coords, marker, emphasized = false) {
        if (isGlobe && d3.geoDistance([lon, lat], coords) >= Math.PI / 2 - .01) return;
        const [x, y] = projection(coords);
        context.font = `${emphasized ? "700" : "600"} ${emphasized ? 14 : 11}px system-ui`;
        const textWidth = context.measureText(name).width;
        const left = marker ? x + 6 : x - textWidth / 2;
        const box = [left - 3, y - 10, left + textWidth + 3, y + 7];
        if (box[0] < 0 || box[1] < 0 || box[2] > width || box[3] > height ||
            used.some(([l, t, r, b]) => l < box[2] && t < box[3] && r > box[0] && b > box[1])) return;
        used.push(box);
        if (marker) {
            context.beginPath();
            context.arc(x, y, emphasized ? 4 : 2.2, 0, 2 * Math.PI);
            context.fillStyle = emphasized ? "#ac382c" : "#275679";
            context.fill();
        }
        context.lineWidth = 3;
        context.strokeStyle = "rgba(251,253,255,.88)";
        context.strokeText(name, left, y + 3);
        context.fillStyle = "#18344a";
        context.fillText(name, left, y + 3);
    }
    if (focus) drawLabel(focus.feature.properties.name, focus.coords, true, true);
    if (!$("labels").checked) return;
    for (const country of atlas.countries) {
        const { name, label, rank } = country.properties;
        if (rank <= 2 + zoom)
            drawLabel(name, label, false);
    }
    if (zoom >= 2) {
        for (const city of atlas.places) {
            const { name, rank, capital } = city.properties;
            if (capital || rank <= 2 + zoom / 2)
                drawLabel(name, city.geometry.coordinates, true);
        }
    }
}

function changeZoom(factor, point) {
    const previous = !isGlobe && projection && point ? projection.invert(point) : null;
    zoom = clamp(zoom * factor, 1, isGlobe ? 4 : 32);
    if (previous) {
        configure(canvas.clientWidth, canvas.clientHeight);
        const next = projection.invert(point);
        lon = wrap(lon + previous[0] - next[0]);
        lat = clamp(lat + previous[1] - next[1], -85, 85);
    }
    redraw();
}

function shift(dx, dy) {
    if (!projection) return;
    lon = wrap(lon - dx / projection.scale() / radians);
    lat = clamp(lat + dy / projection.scale() / radians, -85, 85);
    redraw();
}

canvas.addEventListener("pointerdown", event => {
    if (event.button !== 0) return;
    canvas.setPointerCapture(event.pointerId);
    drag = { id: event.pointerId, x: event.clientX, y: event.clientY };
});
canvas.addEventListener("pointermove", event => {
    if (drag?.id !== event.pointerId) return;
    shift(event.clientX - drag.x, event.clientY - drag.y);
    drag = { id: event.pointerId, x: event.clientX, y: event.clientY };
});
canvas.addEventListener("lostpointercapture", () => { drag = null; });
canvas.addEventListener("wheel", event => {
    event.preventDefault();
    changeZoom(event.deltaY < 0 ? 1.24 : 1 / 1.24, [event.offsetX, event.offsetY]);
}, { passive: false });
canvas.addEventListener("keydown", event => {
    const direction = { ArrowLeft: [45, 0], ArrowRight: [-45, 0], ArrowUp: [0, 45], ArrowDown: [0, -45] }[event.key];
    if (direction) shift(...direction);
    else if (event.key === "+" || event.key === "=") changeZoom(1.5);
    else if (event.key === "-") changeZoom(1 / 1.5);
    else return;
    event.preventDefault();
});

$("flat").onclick = () => mode(false);
$("globe").onclick = () => mode(true);
$("more").onclick = () => changeZoom(1.5);
$("less").onclick = () => changeZoom(1 / 1.5);
$("labels").onchange = redraw;
$("grid").onchange = redraw;
$("reset").onclick = () => {
    lon = lat = 0;
    zoom = 1;
    focus = null;
    $("place").value = $("status").textContent = "";
    redraw();
};
$("find").onsubmit = event => {
    event.preventDefault();
    const name = normalize($("place").value.trim());
    if (!name) return;
    const match = choices.get(name) ?? [...choices].find(([key]) => key.startsWith(name))?.[1];
    if (!match) {
        $("status").textContent = "No place matched. Try another spelling.";
        return;
    }
    focus = match;
    $("place").value = match.label;
    $("status").textContent = match.label;
    [lon, lat] = match.coords;
    lat = clamp(lat, -85, 85);
    zoom = isGlobe ? 1 : match.feature.geometry.type === "Point" ? 12
        : clamp(1 / Math.sqrt(d3.geoArea(match.feature)), 2, 16);
    redraw();
};

function addPlace(label, coords, feature) {
    const option = document.createElement("option");
    option.value = label;
    $("places").append(option);
    choices.set(normalize(label), { label, coords, feature });
}

fetch("world-data.json").then(response => {
    if (!response.ok) throw new Error("Map data unavailable");
    return response.json();
}).then(data => {
    atlas = data;
    atlas.places.sort((a, b) => a.properties.rank - b.properties.rank);
    for (const country of atlas.countries) {
        addPlace(country.properties.name, country.properties.label, country);
        if (country.properties.alias !== country.properties.name)
            choices.set(normalize(country.properties.alias), choices.get(normalize(country.properties.name)));
    }
    for (const city of atlas.places)
        addPlace(`${city.properties.name} — ${city.properties.country}`, city.geometry.coordinates, city);
    $("status").textContent = "";
    redraw();
}).catch(() => {
    $("status").textContent = "Could not load the offline atlas.";
});

new ResizeObserver(redraw).observe(canvas);
mode(isGlobe);
