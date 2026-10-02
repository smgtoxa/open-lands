// Converts the browser build's src/styles.css into Assets/Resources/page.uss (UI Toolkit) plus
// Assets/Scripts/Host/CssInfo.g.cs (what USS cannot express: upper-cased classes). Rules:
//   tag -> .tag-<tag> (Dom.El adds it), body/:root -> .root, [hidden] -> .is-hidden,
//   display:flex -> flex-direction: row (CSS default; UI Toolkit's is column), grid -> wrapped row,
//   gap / grid-template-columns / grid-column -> CssInfo.Layouts (Dom/CssLayout.cs sizes the children at
//   run time), var(--x) resolved, rem/em/vw/vh/calc/min/max/clamp evaluated at 1600x1000 and 14px
//   (a min()/max() mixing % with px is left to the host), unsupported properties/selectors dropped
//   (and listed with --report).
// Hand-tuned rules go in Assets/Resources/page-overrides.uss, loaded after this one.
//   node tools/gen_uss.mjs [LANDS dir] [--report]
import fs from "fs";
import path from "path";

const LANDS = process.argv.find((a, i) => i > 1 && !a.startsWith("--")) || (process.env.LANDS || (process.env.HOME || "") + "/lands");
const REPORT = process.argv.includes("--report");
let css = fs.readFileSync(path.join(LANDS, "src", "styles.css"), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
// the browser's own defaults the page relies on (Chromium's html.css), cascaded below the page's rules
const UA = `
* { flex-shrink: 0; }
body { flex-shrink: 1; }
h1, h2, h3, h4, h5, h6, b, strong, th { font-weight: bold; }
em, i { font-style: italic; }
h1 { font-size: 28px; margin: 19px 0; }
h2 { font-size: 21px; margin: 17px 0; }
h3 { font-size: 16.38px; margin: 16px 0; }
h4 { margin: 18.62px 0; }
h5 { font-size: 11.62px; margin: 19.4px 0; }
p { margin: 14px 0; }
ul, ol { margin: 14px 0; padding-left: 40px; }
button { text-align: center; padding: 1px 6px; font-size: 13.33px; font-family: Arial; }
input, select, textarea { font-size: 13.33px; font-family: Arial; }
th { text-align: center; }
small { font-size: 11.67px; }
code, kbd, pre { font-family: monospace; }
.inline-flow { flex-direction: row; align-items: center; }
.inline-flow > .text { flex-shrink: 1; min-width: 0; }
`;
// setup.html's own <style> (the desktop app's "Game data" page, shown by the host as an overlay)
const setupHtml = fs.readFileSync(path.join(LANDS, "setup.html"), "utf8");
const setupStyle = (/<style>([\s\S]*?)<\/style>/.exec(setupHtml) || [])[1] || "";
css = UA + css + "\n" + setupStyle.replace(/\/\*[\s\S]*?\*\//g, "");
const VW = 1600, VH = 1000, REM = 14;
let EM = REM;   // the current rule's own font size, when it sets one
const dropped = new Map();
const drop = (what) => dropped.set(what, (dropped.get(what) || 0) + 1);
const uppercase = new Set();

// ---- custom properties: the first definition wins (the page defines each once) ----
const vars = {};
for (const m of css.matchAll(/(--[\w-]+)\s*:\s*([^;}]+)/g)) if (!(m[1] in vars)) vars[m[1]] = m[2].trim();
const resolveVars = (v) => { for (let k = 0; k < 5 && v.includes("var("); k += 1) v = v.replace(/var\((--[\w-]+)\s*(?:,\s*([^()]*(?:\([^()]*\))?[^()]*))?\)/g, (_, n, fb) => vars[n] ?? (fb ?? "").trim()); return v; };
const layouts = [];
const fonts = [];
const fontSizes = [];
const justifySelf = [];
const shrinkSet = [];
const overflows = [];
const lineHeights = [];
const minWidthPct = [];
const scrollable = [];
const pseudos = [];
const pointers = [];
const cursors = [];
const shadows = [];
const svgStyles = [];
const fixed = [];
const backgrounds = [];
const animations = [];
const calcs = [];
// a CSS content value: "text" with \XXXX escapes; none / normal -> no box
function cssString(v) {
  v = v.replace(/!important/, "").trim();
  if (v === "none" || v === "normal") return "\u0000none";
  const m = /^(["'])(.*)\1$/.exec(v);
  if (!m) return "";
  return m[2].replace(/\\([0-9a-fA-F]{1,6})\s?/g, (_, h) => String.fromCodePoint(parseInt(h, 16))).replace(/\\(.)/g, "$1");
}

const keyframes = {};
const mediaWidths = new Set();
// url() in styles.css is relative to src/: the host resolves page paths from the web root
const pageUrl = (v) => v.replace(/url\(\s*["']?([^"')]+)["']?\s*\)/g, (_, u) => `url("${/^(data:|\/|https?:)/.test(u) ? u : "src/" + u}")`);
// ---- blocks: skip @media / @keyframes / @supports / @font-face ----
const rules = [];
let i = 0;
while (i < css.length) {
  const open = css.indexOf("{", i);
  if (open < 0) break;
  const sel = css.slice(i, open).trim();
  if (sel.startsWith("@")) {
    let depth = 1, j = open + 1;
    while (depth && j < css.length) { if (css[j] === "{") depth++; else if (css[j] === "}") depth--; j++; }
    const kf = /^@keyframes\s+([\w-]+)/.exec(sel);
    const mq = /^@media\s*\(\s*max-width\s*:\s*(\d+)px\s*\)\s*$/.exec(sel);
    if (kf) keyframes[kf[1]] = css.slice(open + 1, j - 1);
    else if (mq) {
      // a narrow-window rule: the host puts .mq-maxN on the root while the window is that narrow
      mediaWidths.add(Number(mq[1]));
      const inner = css.slice(open + 1, j - 1);
      for (const m of inner.matchAll(/([^{}]+)\{([^}]*)\}/g))
        rules.push({ sel: m[1].split(",").map((x) => `body.mq-max${mq[1]} ${x.trim()}`).join(", "), body: m[2] });
    }
    else drop(sel.split(/[\s(]/)[0]);
    i = j;
    continue;
  }
  const close = css.indexOf("}", open);
  rules.push({ sel, body: css.slice(open + 1, close) });
  i = close + 1;
}

// ---- values ----
function num(expr) {
  // evaluates px/rem/em/vw/vh/% arithmetic, calc/min/max/clamp: returns px, or null
  let e = expr.trim();
  e = e.replace(/(-?[\d.]+)rem\b/g, (_, n) => String(n * REM)).replace(/(-?[\d.]+)em\b/g, (_, n) => String(n * EM))
    .replace(/(-?[\d.]+)vw\b/g, (_, n) => String((n * VW) / 100)).replace(/(-?[\d.]+)vh\b/g, (_, n) => String((n * VH) / 100))
    .replace(/(-?[\d.]+)px\b/g, "$1").replace(/\bcalc\(/g, "(").replace(/\bmin\(/g, "Math.min(").replace(/\bmax\(/g, "Math.max(")
    .replace(/\bclamp\(([^,]+),([^,]+),([^)]+)\)/g, "Math.min(Math.max($1,$2),$3)");
  if (/%|var\(|[a-z]{3,}(?!\.min|\.max)/i.test(e.replace(/Math\.(min|max)/g, ""))) return null;
  try { const v = Function(`"use strict"; return (${e});`)(); return Number.isFinite(v) ? Math.round(v * 100) / 100 : null; } catch { return null; }
}
function length(v) {
  v = v.trim();
  if (v === "0") return "0";
  if (v === "auto") return "auto";
  if (/^-?[\d.]+%$/.test(v)) return v;
  const n = num(v);
  if (n === null) {
    const pct = /^calc\(\s*(-?[\d.]+)%\s*([-+])\s*(-?[\d.]+)px\s*\)$/.exec(v);
    if (pct) return `${pct[1]}%`;
    return null;
  }
  return `${n}px`;
}
const color = (v) => v.trim().replace(/var\(--brass\)/g, "#bda663").replace(/var\(--ember\)/g, "#bf6a3a").replace(/var\(--panel\)/g, "#101516").replace(/var\(--line\)/g, "#34392f");
const firstColor = (v) => (/(#[0-9a-f]{3,8}\b|rgba?\([^)]*\)|\b(black|white|transparent)\b)/i.exec(color(v)) || [])[0];

// a font-family list; generic families become the fonts Windows browsers use for them
const GENERIC = { "sans-serif": "Arial", serif: "Times New Roman", monospace: "Consolas", "system-ui": "Segoe UI" };
const families = (v) => v.split(",").map((f) => f.trim().replace(/^["']|["']$/g, "")).map((f) => GENERIC[f] || f);

function convertDecl(prop, value, extra) {
  value = color(resolveVars(value.replace(/!important/g, "").trim()));
  const out = [];
  const put = (p, v) => { if (v !== null && v !== undefined) out.push(`${p}: ${v};`); };
  switch (prop) {
    case "display":
      if (/^inline/.test(value)) extra.level = "inline"; else if (value !== "none") extra.level = "block";
      if (value === "none") put("display", "none");
      else if (value === "flex" || value === "inline-flex") extra.flex = true;
      else if (value === "grid" || value === "inline-grid") extra.grid = true;
      else if (value === "contents") extra.contents = true;
      else extra.block = true;
      break;
    case "flex-direction": case "flex-wrap": case "align-self": case "position": case "overflow": case "visibility": case "white-space": case "text-overflow":
      if (prop === "overflow" && value !== "visible") { extra.clips = true; extra.overflows = true; }
      if (prop === "overflow" && (value === "auto" || value === "scroll")) extra.scrolls = true;
      if (prop === "overflow" && value !== "hidden" && value !== "visible") value = "hidden";
      if (prop === "white-space" && value !== "normal" && value !== "nowrap") value = value.startsWith("pre") ? "normal" : "nowrap";
      // sticky: the page never scrolls far enough for it to move, so it stays in the flow like static
      if (prop === "position" && value === "sticky") { extra.sticky = true; break; }
      if (prop === "position" && value === "fixed") extra.fixed = true;
      if (prop === "position" && !["absolute", "relative"].includes(value)) { value = value === "fixed" ? "absolute" : "relative"; }
      if (prop === "flex-direction") extra.dir = value;
      if (prop === "flex-wrap") extra.wrap = value;
      put(prop, value);
      break;
    case "align-items": case "justify-content": case "align-content":
      put(prop, { start: "flex-start", end: "flex-end", "space-evenly": "space-around", baseline: "flex-start", normal: "stretch" }[value] || value);
      break;
    case "flex": {
      extra.shrinkSet = true;
      const p = value.split(/\s+/);
      if (value === "none") { put("flex-grow", "0"); put("flex-shrink", "0"); break; }
      if (value === "auto") { put("flex-grow", "1"); put("flex-shrink", "1"); break; }
      put("flex-grow", p[0]);
      if (p[1] !== undefined) put("flex-shrink", p[1]);
      if (p[2] !== undefined) put("flex-basis", length(p[2]) ?? "auto");
      break;
    }
    case "flex-grow": case "flex-shrink": case "opacity": if (prop === "flex-shrink") extra.shrinkSet = true; put(prop, value); break;
    case "overflow-y": case "overflow-x":
      if (value !== "visible") extra.clips = true;
      if (prop === "overflow-y" && (value === "auto" || value === "scroll")) extra.scrolls = true;
      if (value !== "visible") { put("overflow", "hidden"); extra.overflows = true; }
      break;
    case "letter-spacing": { const em = /^(-?[\d.]+)em$/.exec(value); if (em) { extra.letterEm = Number(em[1]); break; } const l = length(value); if (l !== null) put(prop, l); break; }
    case "flex-basis": case "width": case "height": case "min-width": case "min-height": case "max-width": case "max-height":
    case "left": case "top": case "right": case "bottom": case "font-size": case "border-radius":
    case "border-top-left-radius": case "border-top-right-radius": case "border-bottom-left-radius": case "border-bottom-right-radius": {
      if (prop === "font-size") extra.fontSize = value === "inherit" ? "inherit" : "set";
      if (prop === "border-radius") { const parts = value.split(/\s+/).map(length); if (parts.every((x) => x)) put(prop, parts.join(" ")); else drop(`${prop}:${value}`); break; }
      if (prop === "width") extra.width = value === "auto" ? "auto" : "set";
      if (prop === "min-width") extra.minWidth = "set";
      if ((prop === "min-width" || prop === "min-height") && /^0(px)?$/.test(value)) extra.clips = true;
      if (prop === "height") extra.height = value === "auto" ? "auto" : "set";
      const l = length(value);
      // min()/max()/calc() mixing % with fixed lengths: CssLayout evaluates it against the parent
      const sizeProp = /^(width|height|min-width|max-width|min-height|max-height)$/.test(prop);
      // and anything in vh / vw: the window's size, not the 1000px one it would be baked at
      if (sizeProp && (/v[hw]\b/.test(value) || (l === null && /%/.test(value)))) { extra.calcs = (extra.calcs || []).concat([[prop, value]]); break; }
      // plain sizes too, so the host can tell when a later rule beats its calc() value
      if (sizeProp && l !== null) extra.calcs = (extra.calcs || []).concat([[prop, ""]]);
      // Yoga ignores a percentage min-width on an absolutely placed box: CssLayout applies it
      if (prop === "min-width" && l) extra.minWidthPct = l.endsWith("%") && l !== "0%" ? parseFloat(l) : -1;
      if (l === null) drop(`${prop}:${value}`); else put(prop, l);
      break;
    }
    case "inset": { const p = value.split(/\s+/).map(length); const [t, r = t, b = t, l = r] = p; put("top", t); put("right", r); put("bottom", b); put("left", l); break; }
    case "margin": case "padding": {
      const parts = value.split(/\s+/).map((x) => (x === "auto" ? "auto" : length(x)));
      if (parts.some((x) => x === null)) { drop(`${prop}:${value}`); break; }
      put(prop, parts.join(" "));
      break;
    }
    case "margin-top": case "margin-bottom": case "margin-left": case "margin-right": case "padding-top": case "padding-bottom": case "padding-left": case "padding-right":
      put(prop, value === "auto" ? "auto" : length(value));
      break;
    case "gap": case "row-gap": case "column-gap": {
      const p = value.split(/\s+/).map(length);
      if (prop === "gap") { extra.rowGap = p[0]; extra.colGap = p[1] ?? p[0]; }
      else if (prop === "row-gap") extra.rowGap = p[0];
      else extra.colGap = p[0];
      break;
    }
    case "grid-template-columns": extra.columns = value; break;
    case "grid-column": extra.gridColumn = value; break;
    case "grid-row": extra.gridRow = value; break;
    case "grid-auto-rows": case "grid-template-rows": extra.rows = value; break;
    case "aspect-ratio": { const r = value.split("/").map((x) => parseFloat(x)); extra.aspect = value === "auto" ? 0 : r.length === 2 ? r[0] / r[1] : r[0]; break; }
    case "color": put("color", firstColor(value) ?? null); break;
    case "background-image": extra.bg = /gradient\(|url\(/.test(value) ? pageUrl(value) : ""; break;
    case "background": case "background-color": {
      if (prop === "background") extra.bg = /gradient\(|url\(/.test(value) ? pageUrl(value) : "";
      if (value === "none" || value === "transparent") { put("background-color", "rgba(0, 0, 0, 0)"); break; }
      // layered backgrounds: the plain colour at the bottom of the stack is the base; a lone gradient
      // gives its first colour
      const layers = []; let depth = 0, from = 0;
      for (let k = 0; k <= value.length; k += 1) {
        const ch = value[k];
        if (ch === "(") depth++; else if (ch === ")") depth--;
        else if ((ch === "," && depth === 0) || k === value.length) { layers.push(value.slice(from, k).trim()); from = k + 1; }
      }
      const plain = layers.filter((l) => !/gradient\(|url\(/.test(l));
      const c = firstColor(plain.length ? plain[plain.length - 1] : layers[0]);
      if (c) put("background-color", c); else drop(`background:${value.slice(0, 30)}`);
      break;
    }
    case "border": case "border-top": case "border-bottom": case "border-left": case "border-right": {
      const side = prop === "border" ? "" : prop.slice(6);
      if (value === "0" || value === "none") { put(`border${side}-width`, "0"); break; }
      const w = (value.match(/(^|\s)(-?[\d.]+px|0)(\s|$)/) || [])[2];
      const c = firstColor(value);
      if (w) put(`border${side}-width`, length(w));
      if (c) put(`border${side}-color`, c);
      break;
    }
    case "border-width": case "border-top-width": case "border-bottom-width": case "border-left-width": case "border-right-width": put(prop, value.split(/\s+/).map(length).join(" ")); break;
    case "border-color": case "border-top-color": case "border-bottom-color": case "border-left-color": case "border-right-color": put(prop, firstColor(value) ?? null); break;
    case "font-weight": if (/bold|[6-9]00/.test(value)) extra.bold = true; else if (value !== "inherit") extra.plain = true; break;
    case "font-style": if (value === "italic") extra.italic = true; else if (value === "normal") extra.plain = true; break;
    case "line-height": extra.lineHeight = value; break;
    case "fill": case "stroke": case "stroke-width": case "stroke-linecap": case "stroke-linejoin": case "fill-opacity": case "stroke-opacity": case "text-anchor": case "stroke-dasharray":
      extra.svg = (extra.svg || []).concat([[prop, value]]); break;
    case "animation": extra.animation = value; break;
    case "pointer-events": extra.pointer = value === "none" ? "none" : "auto"; break;
    case "cursor": extra.cursor = value; break;
    case "box-shadow": extra.shadow = value; break;
    case "outline": extra.outline = value; break;
    case "outline-offset": extra.outlineOffset = value; break;
    case "order": extra.order = parseInt(value, 10); break;
    case "font-family": extra.fonts = value === "inherit" ? ["inherit"] : families(value); break;
    case "font": { if (value === "inherit") { extra.fonts = ["inherit"]; extra.fontSize = "inherit"; break; } extra.fontSize = "set"; const fam = /\d(?:px|em|rem|%)(?:\/\S+)?\s+(.+)$/.exec(value); const lh = /\d(?:px|em|rem|%)\/(\S+)/.exec(value); if (lh) extra.lineHeight = lh[1]; if (fam) extra.fonts = families(fam[1]); const size = /(\d+(?:\.\d+)?px)/.exec(value); if (size) put("font-size", size[1]); if (/bold/.test(value)) extra.bold = true; break; }
    case "justify-self": extra.justifySelf = value; break;
    case "text-align": extra.textAlign = value; put("-unity-text-align", { center: "middle-center", left: "middle-left", right: "middle-right", start: "middle-left", end: "middle-right" }[value] || null); break;
    case "text-transform": if (value === "uppercase") extra.upper = true; break;
    case "transform": {
      if (value === "none") { put("translate", "0 0"); put("rotate", "0deg"); put("scale", "1 1"); break; }
      const r = /rotate\((-?[\d.]+)deg\)/.exec(value); if (r) put("rotate", `${r[1]}deg`);
      const s = /scale\((-?[\d.]+)(?:,\s*(-?[\d.]+))?\)/.exec(value); if (s) put("scale", `${s[1]} ${s[2] ?? s[1]}`);
      const t = /translate\((-?[\d.]+(?:px|%)?),?\s*(-?[\d.]+(?:px|%)?)?\)/.exec(value); if (t) put("translate", `${t[1]} ${t[2] ?? "0"}`);
      const tx = /translateX\((-?[\d.]+(?:px|%)?)\)/.exec(value); if (tx) put("translate", `${tx[1]} 0`);
      const ty = /translateY\((-?[\d.]+(?:px|%)?)\)/.exec(value); if (ty) put("translate", `0 ${ty[1]}`);
      break;
    }
    case "transform-origin": put("transform-origin", value.split(/\s+/).map((x) => (/^-?[\d.]+(px|%)$/.test(x) || ["center", "left", "right", "top", "bottom"].includes(x) ? x : length(x))).join(" ")); break;
    case "text-shadow": { const m = /(-?[\d.]+)px\s+(-?[\d.]+)px\s+(?:([\d.]+)px\s+)?(#[0-9a-f]{3,8}|rgba?\([^)]*\))/i.exec(value); if (m) put("text-shadow", `${m[1]}px ${m[2]}px ${m[3] || 0}px ${m[4]}`); break; }
    default: drop(prop);
  }
  return out;
}

// ---- selectors ----
function convertSelector(sel) {
  sel = sel.trim();
  if (/::|:nth|:first|:last|:only|:empty|:has\(|:focus-within|:placeholder|::-webkit|\[type|\[aria|\[data-|\[role|\[open\]|\[href|\[draggable|\[disabled\]|:is\(|:where\(|~|\+/.test(sel)) return null;
  sel = sel.replace(/:not\(\[hidden\]\)/g, "").replace(/\[hidden\]/g, ".is-hidden").replace(/:focus-visible/g, ":focus").replace(/:not\(:disabled\)/g, ":enabled");
  if (/:not\(/.test(sel)) return null;
  const parts = sel.split(/(\s*>\s*|\s+)/);
  const conv = parts.map((p) => {
    if (/^\s*>\s*$/.test(p)) return " > ";
    if (/^\s+$/.test(p)) return " ";
    if (p === "html") return null;
    if (p === "body" || p === ":root") return ".root";
    if (p.startsWith("body.")) return ".root" + p.slice(4);
    return p.replace(/^([a-z][a-z0-9]*)/, (t) => (t === "body" ? ".root" : `.tag-${t}`));
  });
  if (conv.includes(null)) return null;
  return conv.join("").replace(/\s+/g, " ").trim();
}

let uss = "/* GENERATED by tools/gen_uss.mjs from src/styles.css - do not edit; hand rules: page-overrides.uss */\n";
// the browser's form controls (Chromium, color-scheme: dark), on UI Toolkit's control parts; first, so the
// page's own rules win
uss += `.input-type-text, .input-type-search, .input-type-number, .tag-select {
  background-color: #3b3b3b; border-width: 1px; border-color: #858585; border-radius: 2px; color: #ffffff; font-size: 13.33px; padding: 1px 2px;
}
.input-type-checkbox { flex-direction: row; margin: 3px 3px 3px 4px; }
.input-checkbox .unity-toggle__input { flex-direction: row; }
.input-checkbox .unity-toggle__checkmark {
  width: 13px; height: 13px; margin: 0; border-width: 1px; border-color: #858585; border-radius: 2px; background-color: #3b3b3b;
}
.input-checkbox:checked .unity-toggle__checkmark {
  background-color: #99c8ff; border-color: #99c8ff; background-image: resource("ua-check"); -unity-background-image-tint-color: #3b3b3b;
}
.tag-select .unity-base-popup-field__input { flex-direction: row; align-items: center; flex-grow: 1; }
.tag-select .unity-base-popup-field__text { flex-grow: 1; }
.tag-select .unity-base-popup-field__arrow {
  width: 9px; height: 9px; margin-left: 6px; margin-right: 2px; background-image: resource("ua-arrow"); -unity-background-image-tint-color: #ffffff;
}
.unity-base-dropdown__container-outer { background-color: #3b3b3b; border-width: 1px; border-color: #858585; }
.unity-base-dropdown__item { padding: 2px 8px; color: #ffffff; font-size: 13.33px; }
.unity-base-dropdown__item:hover { background-color: #1e5bb8; }
.unity-base-dropdown__checkmark { width: 0; }
.input-range .unity-base-slider__drag-container { height: 16px; justify-content: center; }
.input-range .unity-base-slider__tracker { height: 4px; border-radius: 2px; background-color: #858585; position: absolute; left: 0; right: 0; top: 6px; }
.input-range .unity-base-slider__dragger { width: 14px; height: 14px; border-radius: 7px; background-color: #99c8ff; position: absolute; top: 1px; }
.unity-text-field__input { padding: 0; }
.input-type-text, .input-type-search { width: 158px; }
.tag-input .unity-base-field { margin: 0; min-height: 0; }
.tag-input .unity-base-text-field__input { min-height: 0; margin: 0; }
`;
// ---- cascade: CSS type selectors become USS classes (.tag-x), which would lift them to class specificity;
// one entry per selector, sorted by the CSS specificity (stable), so that among equal USS specificities the
// later - the CSS-stronger - rule wins. !important declarations go in their own entry above everything.
function specificity(sel) {
  const s = sel.replace(/::[\w-]+/g, "");
  const ids = (s.match(/#[\w-]+/g) || []).length;
  const classes = (s.match(/\.[\w-]+|\[[^\]]*\]|:(?!not\()[\w-]+/g) || []).length;
  const tags = (s.replace(/:not\(|\)/g, " ").match(/(^|[\s>+~(])[a-z][a-z0-9]*/gi) || []).length;
  return ids * 10000 + classes * 100 + tags;
}
const entries = [];
rules.forEach(({ sel, body }, order) => {
  const normal = [], important = [];
  for (const d of body.split(";")) (/!important/.test(d) ? important : normal).push(d);
  for (const one of sel.split(",")) {
    const spec = specificity(one.trim());
    entries.push({ sel: one.trim(), body: normal.join(";"), spec, order });
    if (important.length) entries.push({ sel: one.trim(), body: important.join(";"), spec: spec + 1000000, order, important: true });
  }
});
entries.sort((a, b) => a.spec - b.spec || a.order - b.order);

for (const { sel: fullSel, body, important } of entries) {
  // ::before / ::after: the host inserts a label (.ps-before / .ps-after) into the element; its content goes
  // to CssInfo.Pseudo, its other declarations style that label
  const pseudo = /::?(before|after)\s*$/.exec(fullSel);
  const sel = pseudo ? fullSel.slice(0, pseudo.index) : fullSel;
  let sels = sel.split(",").map(convertSelector).filter(Boolean);
  if (pseudo && sels.length) {
    const content = /(?:^|;)\s*content\s*:\s*([^;]+)/.exec(body);
    const color = /(?:^|;)\s*color\s*:\s*([^;]+)/.exec(body);
    const size = /(?:^|;)\s*font-size\s*:\s*([^;]+)/.exec(body);
    for (const s of sels) pseudos.push([s, pseudo[1], content ? cssString(content[1]) : null, color ? firstColor(color[1]) || null : null, size ? num(size[1]) : null]);
    sels = sels.map((s) => `${s} > .ps-${pseudo[1]}`);
  }
  if (!sels.length) { drop("selector:" + sel.slice(0, 40)); continue; }
  // !important (USS has none): the selector's last class repeated, which raises its USS specificity
  if (important) sels = sels.map((x) => x.replace(/(\.[\w-]+)((?::[\w-]+)*)$/, (_, c, ps) => c + c + c + c + ps));
  const extra = {};
  const decls = [];
  const ownSize = /(?:^|;)\s*font-size\s*:\s*([^;]+)/.exec(body);
  EM = REM;
  if (ownSize) { const v = num(resolveVars(ownSize[1].replace(/!important/, ""))); if (v) EM = v; }
  for (const d of body.split(";")) {
    const c = d.indexOf(":");
    if (c < 0) continue;
    const prop = d.slice(0, c).trim().toLowerCase(), value = d.slice(c + 1).trim();
    if (prop.startsWith("--") || prop.startsWith("-webkit") || prop.startsWith("-moz")) continue;
    decls.push(...convertDecl(prop, value, extra));
  }
  // letter-spacing in em follows the rule's own font size (else the page's 14px)
  if (extra.letterEm !== undefined) decls.push(`letter-spacing: ${Math.round(extra.letterEm * EM * 100) / 100}px;`);
  if (extra.minWidthPct) for (const s of sels) minWidthPct.push([s, extra.minWidthPct]);
  // CSS flex items do not shrink below their content (min-size: auto) unless they clip or set a 0
  // minimum; Yoga has no automatic minimum, so the default is flex-shrink: 0 (UA) and these shrink
  if (extra.clips && !extra.shrinkSet) decls.push("flex-shrink: 1;");
  if (extra.bg !== undefined) for (const s of sels) if (!/:(hover|active|focus)/.test(s)) backgrounds.push([s, extra.bg]);
  if (extra.animation !== undefined) for (const s of sels) if (!/:(hover|active|focus)/.test(s)) animations.push([s, extra.animation]);
  if (extra.fixed) for (const s of sels) if (!/:(hover|active|focus)/.test(s)) fixed.push(s);
  if (extra.svg) for (const s of sels) if (!/:(active|focus)/.test(s)) for (const [p, v] of extra.svg) svgStyles.push([s.replace(/:hover/g, ".is-hover"), p, v]);
  // svg text takes its font from CSS as well (only rules that also style svg paint, to keep the list short)
  if (extra.svg && /font-size\s*:\s*([\d.]+px)/.test(body)) for (const s of sels) svgStyles.push([s, "font-size", /font-size\s*:\s*([\d.]+px)/.exec(body)[1]]);
  if (extra.svg && /font-weight\s*:\s*(bold|[6-9]00)/.test(body)) for (const s of sels) svgStyles.push([s, "font-weight", "bold"]);
  // svg text takes font-size / font-weight from CSS too
  if (extra.cursor) for (const s of sels) cursors.push([s.replace(/:hover/g, ""), extra.cursor]);
  if (extra.shadow !== undefined || extra.outline !== undefined || extra.outlineOffset !== undefined)
    for (const s of sels) if (!/:(active|focus)/.test(s)) shadows.push([s.replace(/:hover/g, ".is-hover"), extra.shadow ?? null, extra.outline ?? null, extra.outlineOffset ?? null]);
  if (extra.pointer) for (const s of sels) if (!/:(hover|active|focus)/.test(s)) pointers.push([s, extra.pointer]);
  if (extra.calcs) for (const s of sels) if (!/:(hover|active|focus)/.test(s)) for (const [p, v] of extra.calcs) calcs.push([s, p, v]);
  if (extra.scrolls) for (const s of sels) if (!/:(hover|active|focus)/.test(s)) scrollable.push(s);
  if (extra.sticky) for (let k = decls.length - 1; k >= 0; k -= 1) if (/^(top|left|right|bottom):/.test(decls[k])) decls.splice(k, 1);
  if (extra.lineHeight && extra.lineHeight !== "normal" && extra.lineHeight !== "inherit")
    for (const s of sels) if (!/:(hover|active|focus)/.test(s)) lineHeights.push([s, parseFloat(extra.lineHeight), /px$/.test(extra.lineHeight)]);
  if (extra.fonts) for (const s of sels) if (!/:(hover|active|focus)/.test(s)) fonts.push([s, extra.fonts]);
  if (extra.shrinkSet) for (const s of sels) if (s !== "*" && !/:(hover|active|focus)/.test(s)) shrinkSet.push(s);   // "*": the UA default
  if (extra.overflows) for (const s of sels) if (!/:(hover|active|focus)/.test(s)) overflows.push(s);
  if (extra.justifySelf) for (const s of sels) if (!/:(hover|active|focus)/.test(s)) justifySelf.push([s, extra.justifySelf]);
  if (extra.fontSize) for (const s of sels) if (!/:(hover|active|focus)/.test(s)) fontSizes.push([s, extra.fontSize === "inherit"]);
  if (extra.flex && !extra.dir) decls.push("flex-direction: row;");
  if (extra.grid) { decls.push("flex-direction: row;", "flex-wrap: wrap;"); }
  if (extra.plain && !extra.bold && !extra.italic) decls.push("-unity-font-style: normal;");
  if (extra.bold || extra.italic) decls.push(`-unity-font-style: ${extra.bold && extra.italic ? "bold-and-italic" : extra.bold ? "bold" : "italic"};`);
  if (extra.upper) for (const s of sels) { const cls = /\.([a-zA-Z][\w-]*)$/.exec(s); if (cls) uppercase.add(s); }
  if (decls.length) uss += `${sels.join(", ")} {\n  ${decls.join("\n  ")}\n}\n`;
  // what CssLayout does at run time: gaps, grid tracks, grid-column
  const L = {};
  if (extra.grid) L.display = "grid"; else if (extra.flex) L.display = "flex"; else if (extra.contents) L.display = "contents"; else if (extra.block) L.display = "block";
  if (extra.dir) L.dir = extra.dir;
  if (extra.wrap) L.wrap = extra.wrap;
  if (extra.colGap !== undefined) L.colGap = parseFloat(extra.colGap) || 0;
  if (extra.rowGap !== undefined) L.rowGap = parseFloat(extra.rowGap) || 0;
  if (extra.columns) L.columns = extra.columns;
  if (extra.gridColumn) L.gridColumn = extra.gridColumn;
  if (extra.gridRow) L.gridRow = extra.gridRow;
  if (extra.rows) L.rows = extra.rows;
  if (extra.level) L.level = extra.level;
  if (extra.textAlign) L.textAlign = extra.textAlign;
  if (extra.minWidth) L.minWidth = extra.minWidth;
  if (extra.order !== undefined) L.order = extra.order;
  if (extra.aspect !== undefined) L.aspect = extra.aspect;
  if (extra.width) L.width = extra.width;
  if (extra.height) L.height = extra.height;
  if (Object.keys(L).length)
    for (const s of sels) if (!/:(hover|active|focus)/.test(s)) layouts.push([s, L]);
}

const assets = path.join(path.dirname(new URL(import.meta.url).pathname), "..", "Assets");
fs.writeFileSync(path.join(assets, "Resources", "page.uss"), uss);
const calcProps = new Set(calcs.filter(([, , v]) => v).map(([, p]) => p));
fs.writeFileSync(path.join(assets, "Scripts", "Host", "CssInfo.g.cs"), `// GENERATED by tools/gen_uss.mjs - do not edit.
using System.Collections.Generic;

namespace LolHost
{
    public static class CssInfo
    {
        /// <summary>Selectors with text-transform: uppercase (USS has none): the DOM layer upper-cases their text.</summary>
        public static readonly string[] Uppercase = { ${[...uppercase].map((s) => JSON.stringify(s)).join(", ")} };

        /// <summary>line-height per selector: (value, px?) - a bare number is a multiple of the font size.</summary>
        public static readonly (string sel, float value, bool px)[] LineHeights =
        {
${lineHeights.map(([sel, v, px]) => `            (${JSON.stringify(sel)}, ${v}f, ${px}),`).join("\n")}
        };

        /// <summary>min-width per selector, in cascade order: % of the parent, or -1 for any other value.</summary>
        public static readonly (string sel, float pct)[] MinWidthPct =
        {
${minWidthPct.map(([sel, v]) => `            (${JSON.stringify(sel)}, ${v}f),`).join("\n")}
        };

        /// <summary>overflow: auto / scroll: these scroll (mouse wheel, scrollTop).</summary>
        public static readonly string[] Scrollable = { ${scrollable.map((s) => JSON.stringify(s)).join(", ")} };

        /// <summary>::before / ::after per selector, in cascade order: (sel, "before"/"after", content or null
        /// when the rule leaves it, colour or null, font size px or -1).</summary>
        public static readonly (string sel, string which, string content, string color, float size)[] Pseudo =
        {
${pseudos.map(([sel, w, c, col, sz]) => `            (${JSON.stringify(sel)}, ${JSON.stringify(w)}, ${c === null ? "null" : JSON.stringify(c)}, ${col ? JSON.stringify(col) : "null"}, ${sz ? sz + "f" : "-1"}),`).join("\n")}
        };

        /// <summary>background / background-image per selector, in cascade order: a gradient (painted by
        /// Dom/CssGradient.cs), or "" where a rule sets a plain background instead.</summary>
        public static readonly (string sel, string value)[] Backgrounds =
        {
${backgrounds.map(([sel, v]) => `            (${JSON.stringify(sel)}, ${JSON.stringify(v)}),`).join("\n")}
        };

        /// <summary>@keyframes: name -> (offset 0..1, "prop: value; ...") steps (Dom/CssAnimation.cs runs them).</summary>
        public static readonly Dictionary<string, (float at, string decls)[]> Keyframes = new Dictionary<string, (float, string)[]>
        {
${Object.entries(keyframes).map(([name, body]) => {
  const steps = [];
  for (const m of body.matchAll(/([^{}]+)\{([^}]*)\}/g)) for (const o of m[1].split(",")) {
    const t = o.trim(); const at = t === "from" ? 0 : t === "to" ? 1 : parseFloat(t) / 100;
    steps.push(`(${at}f, ${JSON.stringify(m[2].trim().replace(/\s+/g, " "))})`);
  }
  return `            [${JSON.stringify(name)}] = new (float, string)[] { ${steps.join(", ")} },`;
}).join("\n")}
        };

        /// <summary>animation per selector, in cascade order ("none" stops it).</summary>
        public static readonly (string sel, string value)[] Animations =
        {
${animations.map(([sel, v]) => `            (${JSON.stringify(sel)}, ${JSON.stringify(v)}),`).join("\n")}
        };

        /// <summary>position: fixed - the host moves these under the root, so the viewport is their box.</summary>
        public static readonly string[] Fixed = { ${fixed.map((s) => JSON.stringify(s)).join(", ")} };

        /// <summary>SVG presentation properties set by CSS (fill, stroke...), in cascade order: they win over the
        /// elements' attributes, as in a browser (Dom/Svg.cs).</summary>
        public static readonly (string sel, string prop, string value)[] SvgStyles =
        {
${svgStyles.map(([sel, p, v]) => `            (${JSON.stringify(sel)}, ${JSON.stringify(p)}, ${JSON.stringify(v)}),`).join("\n")}
        };

        /// <summary>@media (max-width: N) widths: the root carries .mq-maxN while the window is at most N wide.</summary>
        public static readonly int[] MediaMaxWidths = { ${[...mediaWidths].sort((a, b) => a - b).join(", ")} };

        /// <summary>cursor per selector, in cascade order (inherited).</summary>
        public static readonly (string sel, string value)[] Cursors =
        {
${cursors.map(([sel, v]) => `            (${JSON.stringify(sel)}, ${JSON.stringify(v)}),`).join("\n")}
        };

        /// <summary>box-shadow / outline / outline-offset per selector, in cascade order (null: the rule leaves it);
        /// :hover is .is-hover (set by the host on hovered boxes).</summary>
        public static readonly (string sel, string shadow, string outline, string outlineOffset)[] Shadows =
        {
${shadows.map(([sel, a, b, c]) => `            (${JSON.stringify(sel)}, ${a === null ? "null" : JSON.stringify(a)}, ${b === null ? "null" : JSON.stringify(b)}, ${c === null ? "null" : JSON.stringify(c)}),`).join("\n")}
        };

        /// <summary>pointer-events per selector, in cascade order (inherited): "none" or "auto".</summary>
        public static readonly (string sel, string value)[] PointerEvents =
        {
${pointers.map(([sel, v]) => `            (${JSON.stringify(sel)}, ${JSON.stringify(v)}),`).join("\n")}
        };

        /// <summary>Sizes USS cannot compute (min()/max()/calc() with %): (sel, property, CSS value), cascade order;
        /// "" is a plain size a rule sets for the same property (the stylesheet has it).</summary>
        public static readonly (string sel, string prop, string value)[] Calc =
        {
${calcs.filter(([, p, v]) => v || calcProps.has(p)).map(([sel, p, v]) => `            (${JSON.stringify(sel)}, ${JSON.stringify(p)}, ${JSON.stringify(v)}),`).join("\n")}
        };

        /// <summary>Selectors with overflow other than visible (a new block formatting context: margins stay inside).</summary>
        public static readonly string[] Overflows =
        {
${overflows.map((sel) => `            ${JSON.stringify(sel)},`).join("\n")}
        };

        /// <summary>Selectors that set flex / flex-shrink themselves (the host leaves their shrink alone).</summary>
        public static readonly string[] ShrinkSet =
        {
${shrinkSet.map((sel) => `            ${JSON.stringify(sel)},`).join("\n")}
        };

        /// <summary>justify-self per selector (grid items; later wins): start / center / end / stretch.</summary>
        public static readonly (string sel, string value)[] JustifySelf =
        {
${justifySelf.map(([sel, v]) => `            (${JSON.stringify(sel)}, ${JSON.stringify(v)}),`).join("\n")}
        };

        /// <summary>font-size per selector, in cascade order (later wins): true = inherit (USS has no inherit),
        /// false = the stylesheet's own size.</summary>
        public static readonly (string sel, bool inherit)[] FontSizes =
        {
${fontSizes.map(([sel, i]) => `            (${JSON.stringify(sel)}, ${i}),`).join("\n")}
        };

        /// <summary>font-family per selector (OS fonts, loaded at run time; later wins; "inherit": the parent's).</summary>
        public static readonly (string sel, string[] families)[] Fonts =
        {
${fonts.map(([sel, f]) => `            (${JSON.stringify(sel)}, new[] { ${f.map((x) => JSON.stringify(x)).join(", ")} }),`).join("\n")}
        };

        /// <summary>display / flex-direction / flex-wrap / gap / grid-template-columns / grid-column / grid-row /
        /// aspect-ratio / whether width and height are set, per selector,
        /// in stylesheet order (later wins): Dom/CssLayout.cs lays the children out with them.</summary>
        public static readonly CssLayout.Rule[] Layouts =
        {
${layouts.map(([sel, L]) => `            new CssLayout.Rule(${JSON.stringify(sel)}, ${L.display ? JSON.stringify(L.display) : "null"}, ${L.dir ? JSON.stringify(L.dir) : "null"}, ${L.wrap ? JSON.stringify(L.wrap) : "null"}, ${L.colGap ?? "-1"}, ${L.rowGap ?? "-1"}, ${L.columns ? JSON.stringify(L.columns) : "null"}, ${L.gridColumn ? JSON.stringify(L.gridColumn) : "null"}, ${L.gridRow ? JSON.stringify(L.gridRow) : "null"}, ${L.aspect !== undefined ? Math.round(L.aspect * 10000) / 10000 + "f" : "-1"}, ${L.width ? JSON.stringify(L.width) : "null"}, ${L.height ? JSON.stringify(L.height) : "null"}, ${L.rows ? JSON.stringify(L.rows) : "null"}, ${L.level ? JSON.stringify(L.level) : "null"}, ${L.textAlign ? JSON.stringify(L.textAlign) : "null"}, ${L.minWidth ? "true" : "false"}, ${L.order !== undefined ? L.order : "int.MinValue"}),`).join("\n")}
        };
    }
}
`);
console.log(`${rules.length} rules -> page.uss (${uss.length} bytes), ${uppercase.size} uppercase selectors, ${layouts.length} layout rules`);
if (REPORT) console.log([...dropped].sort((a, b) => b[1] - a[1]).slice(0, 60).map(([k, v]) => `${v}\t${k}`).join("\n"));
