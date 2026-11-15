// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
// Turns the owner's DC-6 Gauges designs (web/gauge-defs.js in that project) into FIPinator gauge
// files, one per instrument, drawn the same way as DC-6 Gauges' web/gauges.js draws them.
// Both projects are the owner's own work; everything is drawn in code, no images.
//
//   node tools/import-dc6-gauges.js <path to dc6-gauges/web/gauge-defs.js> <output folder>
//
// Writes <output>/engines/*.json (main centre panel) and <output>/overhead/*.json (fuel panel).
// Re-run it after changing the DC-6 Gauges designs; it overwrites only the files it writes.
"use strict";
const fs = require("fs");
const path = require("path");

const [defsPath, outDir] = process.argv.slice(2);
if (!defsPath || !outDir) {
  console.error("Usage: node tools/import-dc6-gauges.js <gauge-defs.js> <output folder>");
  process.exit(1);
}
const { DEFS, PANELS } = require(path.resolve(defsPath));

// Colours and sizes from DC-6 Gauges' gauges.js.
const INK = "#efe6cf", NEEDLE = "#efe9d6", ARM = "#2a2b2c", RED = "#d0302a", PANEL = "#8d908e";
const DX = 40; // the 240 x 240 gauge box sits in the middle of the FIP's 320 x 240 screen
const FONT_FILE = path.resolve(outDir, "..", "..", "assets", "fonts", "BarlowCondensed-Medium.ttf");
let FONT = "";

// Gauges for one engine or a pair of engines (BMEP 1, RPM 1-2...) are only building blocks:
// tools/make-dc6-gauges.ps1 combines them into 1-4 gauges, and the apps never show the parts.
const isPart = (id) => /^(bmep\d|.+(12|34))$/.test(id);

const round = (v) => Math.round(v * 100) / 100;
const key = (v) => String(Math.round(v * 100) / 100); // as FIPinator's labels look up "texts" ("0.##")
const at = (x, y) => [round(x + DX), round(y)];

// a scale's points as [value, angle, distance], angles clockwise from 12 o'clock and continuous
function points(s) {
  let prev = null;
  return s.pts.map((q) => {
    const dx = q[1] - s.pivot[0], dy = q[2] - s.pivot[1];
    let a = Math.atan2(dx, -dy) * 180 / Math.PI;
    if (prev !== null) { while (a - prev > 180) a -= 360; while (a - prev < -180) a += 360; }
    prev = a;
    return [q[0], round(a), round(Math.sqrt(dx * dx + dy * dy))];
  });
}
const first = (s) => (s.from !== undefined ? s.from : s.pts[0][0]);
const last = (s) => s.pts[s.pts.length - 1][0];
const near = (list, v) => (list || []).some((m) => Math.abs(m - v) < 1e-6);
const rim = (s, pts) => s.ticks === "out" && pts.some((p) => p[2] > 88);

// a hex colour times k plus a little grey, as gauges.js shades dial faces
function shade(hex, k, lift) {
  const n = parseInt(hex.slice(1), 16), c = [n >> 16, (n >> 8) & 255, n & 255];
  return "#" + c.map((v) => Math.round(Math.min(255, v * k + lift * 255)).toString(16).padStart(2, "0")).join("");
}

function scaleLayers(s, name, pts, ink) {
  const center = at(s.pivot[0], s.pivot[1]);
  const out = s.ticks === "out" && !rim(s, pts) ? 1 : -1;
  const fs_ = s.fs || 20;
  const big = s.ticks === "out" ? 16 : 10, mid = s.ticks === "out" ? 11 : 7, small = s.ticks === "out" ? 8 : 4;
  const layers = [];
  // a tick from distance a to distance b off the scale line, in FIPinator's terms (outer end + length)
  const span = (a, b) => ({ radius: round(Math.max(a, b)), length: round(Math.abs(a - b)) });

  const bandOff = s.bandOff !== undefined ? s.bandOff : s.ticks === "in" ? -5 : out * 2;
  for (const b of s.bands || [])
    layers.push({ type: "arc", scale: name, from: b[0], to: b[1], radius: bandOff, width: b[3] || (s.ticks === "in" ? 5 : 4), color: b[2], center });
  if (s.line) layers.push({ type: "arc", scale: name, from: first(s), to: last(s), radius: 0, width: 1.4, color: ink, center });

  const ticks = s.minor ? [] : (s.majors || []).slice();
  if (s.minor) for (let t = first(s); t <= last(s) + 1e-9; t += s.minor) ticks.push(Math.round(t * 1000) / 1000);
  for (const m of s.majors || []) if (!near(ticks, m)) ticks.push(m);
  const tris = (s.tris || []).map((x) => x[0]);
  const groups = { big: [], mid: [], small: [] };
  for (const v of ticks) {
    if (near(tris, v)) continue;
    groups[near(s.majors, v) ? "big" : near(s.mids, v) ? "mid" : "small"].push(v);
  }
  for (const [g, len, width] of [["small", small, 1.2], ["mid", mid, 1.8], ["big", big, 2.6]])
    if (groups[g].length) layers.push({ type: "ticks", scale: name, at: groups[g], ...span(0, out * len), width, color: ink, center });
  // triangle markers: tip on the scale line, base `len` away
  for (const t of s.tris || []) {
    const len = t[1];
    layers.push({ type: "ticks", scale: name, at: [t[0]], shape: "triangle", radius: out * len, length: out * len, width: round(len * 0.66), color: ink, center });
  }
  for (const v of s.reds || []) {
    const a = s.ticks === "in" ? 1 : -2;
    const b = s.bandOff !== undefined ? s.bandOff + 3 : out * (big + 2);
    layers.push({ type: "ticks", scale: name, at: [v], ...span(a, b), width: 3, color: RED, center });
  }
  // labels: [value, text] or [value, text, size factor]
  const lab = s.lab !== undefined ? s.lab : out > 0 ? -14 : s.ticks === "out" ? -(big + 12) : -(big + 9);
  const bySize = new Map();
  for (const l of s.labels || []) {
    const sm = l[2] || 1;
    if (!bySize.has(sm)) bySize.set(sm, []);
    bySize.get(sm).push(l);
  }
  for (const [sm, list] of bySize) {
    layers.push({
      type: "labels", scale: name, at: list.map((l) => l[0]), radius: round(lab * (sm < 1 ? 0.8 : 1)), size: round(fs_ * sm), color: ink, center,
      texts: Object.fromEntries(list.map((l) => [key(l[0]), l[1]])),
    });
  }
  if (s.labels2 && s.labels2.length)
    layers.push({
      type: "labels", scale: name, at: s.labels2.map((l) => l[0]), radius: s.lab2, size: round(fs_ * 0.8), color: ink, center,
      texts: Object.fromEntries(s.labels2.map((l) => [key(l[0]), l[1]])),
    });
  return layers;
}

function convert(id, def, panelName) {
  const ink = def.ink || INK;
  const face = def.face || "#0b0b0b";
  const values = {}, scales = {};
  const layers = [];

  // case: steel screws, shadow on the panel, black rim, dial face (lit from the top), thin bright edge
  for (const [x, y] of [[18, 18], [222, 18], [18, 222], [222, 222]]) layers.push({ type: "screw", x: x + DX, y, kind: "steel" });
  layers.push({ type: "circle", r: 114, fill: "#40000000" });
  layers.push({ type: "circle", r: 112, fillTop: "#3b3c3d", fillBottom: "#050505" });
  layers.push({ type: "circle", r: 101, fillTop: shade(face, 1.6, 0.05), fillBottom: shade(face, 0.55, 0), stroke: "#000000", strokeWidth: 2 });
  layers.push({ type: "circle", r: 103, stroke: "#994a4b4c", strokeWidth: 1 });
  if (def.plate) layers.push({ type: "rect", x: def.plate[0] + DX, y: def.plate[1], width: def.plate[2], height: def.plate[3], radius: 12, fill: "#151615", stroke: "#0a0a0a" });

  const scalePts = def.scales.map(points);
  def.scales.forEach((s, i) => {
    scales["s" + i] = { points: scalePts[i] };
    layers.push(...scaleLayers(s, "s" + i, scalePts[i], ink));
  });

  // text is centred on its y in DC-6 Gauges; FIPinator's y is the baseline
  for (const t of def.text || []) layers.push({ type: "text", text: t[3], x: round(t[0] + DX), y: round(t[1] + t[2] * 0.36), size: t[2], color: ink });
  for (const b of def.brand || []) {
    const script = b[4] === "script";
    layers.push({ type: "text", text: b[3], x: round(b[0] + DX), y: round(b[1] + b[2] * 0.36), size: b[2], color: (script ? "#e6" : "#d9") + ink.slice(1), italic: script });
  }

  def.needles.forEach((n, i) => {
    const s = def.scales[n.scale || 0], pts = scalePts[n.scale || 0];
    const twin = s.ticks === "in";
    const name = "n" + (i + 1);
    const lo = pts[0][0], hi = pts[pts.length - 1][0];
    values[name] = { sim: n.v, unit: "number", default: round(lo + (hi - lo) * (0.45 + 0.1 * i)) };
    layers.push({
      type: "needle", style: "pointer", scale: "s" + (n.scale || 0), value: name, center: at(s.pivot[0], s.pivot[1]),
      length: twin ? 4 : rim(s, pts) ? -4 - i * 6 : 12 - i * 6,
      width: twin ? 2 : 3.2, tail: twin ? 26 : s.line ? 0 : 24, color: NEEDLE, tailColor: ARM, shadow: false,
      ...(n.tag !== undefined ? { tag: String(n.tag) } : {}),
    });
  });
  const pivots = [];
  for (const s of def.scales) {
    const k = s.pivot.join(",");
    if (pivots.includes(k)) continue;
    pivots.push(k);
    const twin = s.ticks === "in";
    layers.push({ type: "circle", r: twin ? 4.5 : 8, center: at(s.pivot[0], s.pivot[1]), fill: "#1a1a1a", stroke: "#555555" });
    layers.push({ type: "circle", r: twin ? 1.6 : 2.6, center: at(s.pivot[0], s.pivot[1]), fill: "#777777" });
  }
  layers.push({ type: "glare", r: 101, opacity: 0.16 });

  return {
    format: 1,
    name: "DC-6 " + def.name,
    author: "FIPinator 3000 (from the owner's DC-6 Gauges designs)",
    license: "Free and open source (AGPL-3.0): free to use, change and share.",
    description: `PMDG DC-6 ${def.name.toLowerCase()}, ${panelName}. Scale and marks as in DC-6 Gauges (read off the PMDG cockpit); needles follow PMDG's own cockpit needle values.`,
    canvas: { width: 320, height: 240, background: PANEL },
    center: [160, 120],
    font: FONT,
    values,
    scales,
    layers,
  };
}

const slug = (text) => text.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
let count = 0;
for (const [folder, panel] of [["engines", PANELS.main], ["overhead", PANELS.overhead]]) {
  for (const place of panel.gauges) {
    const def = DEFS[place.id];
    const dir = isPart(place.id) ? path.join(outDir, "parts", folder) : path.join(outDir, folder);
    fs.mkdirSync(dir, { recursive: true });
    FONT = path.relative(dir, FONT_FILE).split(path.sep).join("/");
    const gauge = convert(place.id, def, panel.name.toLowerCase());
    fs.writeFileSync(path.join(dir, slug(def.name) + ".json"), JSON.stringify(gauge, null, 2) + "\n");
    count++;
  }
}
console.log(`Wrote ${count} gauges to ${outDir} (per-engine parts in ${path.join(outDir, "parts")})`);
