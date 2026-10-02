// Generates Assets/Scripts/Host/PageMarkup.g.cs from the browser build's index.html: the same element
// tree (tags, ids, classes, attributes, texts, inline SVG, <symbol> icons, <select> options) built
// with the DOM layer in Assets/Scripts/Dom. Re-run whenever index.html changes.
//   node tools/gen_page.mjs [LANDS dir]
import fs from "fs";
import path from "path";

const LANDS = process.argv[2] || process.env.LANDS || (process.env.LANDS || (process.env.HOME || "") + "/lands");
const html = fs.readFileSync(path.join(LANDS, "index.html"), "utf8");
const body = html.slice(html.indexOf("<body"), html.lastIndexOf("</body>"));

// ---- a small HTML parser (the page is well formed) ----
const VOID = new Set(["input", "br", "img", "meta", "link", "use", "path", "circle", "rect", "line", "polygon", "polyline", "ellipse", "stop"]);
function parse(src) {
  const root = { tag: "#root", attrs: {}, children: [] };
  const stack = [root];
  let i = 0;
  while (i < src.length) {
    if (src.startsWith("<!--", i)) { i = src.indexOf("-->", i) + 3; continue; }
    if (src[i] === "<") {
      if (src[i + 1] === "/") {
        const end = src.indexOf(">", i);
        const tag = src.slice(i + 2, end).trim().toLowerCase();
        while (stack.length > 1) { const top = stack.pop(); if (top.tag === tag) break; }
        i = end + 1;
        continue;
      }
      const end = findTagEnd(src, i);
      const raw = src.slice(i + 1, end);
      const selfClosing = raw.endsWith("/");
      const m = /^([a-zA-Z0-9-]+)/.exec(raw);
      const tag = m[1].toLowerCase();
      const attrs = {};
      for (const a of raw.slice(m[1].length).matchAll(/([a-zA-Z_:][-a-zA-Z0-9_:.]*)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+)))?/g)) {
        attrs[a[1]] = a[2] ?? a[3] ?? a[4] ?? "";
      }
      const node = { tag, attrs, children: [] };
      stack[stack.length - 1].children.push(node);
      i = end + 1;
      if (tag === "script" || tag === "style") { const close = src.indexOf(`</${tag}>`, i); i = close + tag.length + 3; stack[stack.length - 1].children.pop(); continue; }
      if (!selfClosing && !VOID.has(tag)) stack.push(node);
      continue;
    }
    const next = src.indexOf("<", i);
    const text = src.slice(i, next < 0 ? src.length : next);
    if (text.trim()) stack[stack.length - 1].children.push({ tag: "#text", text: decode(text.replace(/\s+/g, " ")) });
    i = next < 0 ? src.length : next;
  }
  return root;
}
function findTagEnd(src, i) {
  let q = null;
  for (let j = i + 1; j < src.length; j += 1) {
    const c = src[j];
    if (q) { if (c === q) q = null; continue; }
    if (c === '"' || c === "'") q = c;
    else if (c === ">") return j;
  }
  return src.length;
}
function decode(t) {
  return t.replace(/&amp;/g, "&").replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&#39;/g, "'")
    .replace(/&nbsp;/g, " ").replace(/&#(\d+);/g, (_, n) => String.fromCodePoint(Number(n))).replace(/&#x([0-9a-f]+);/gi, (_, n) => String.fromCodePoint(parseInt(n, 16)));
}

const TEXT_TAGS = new Set(["span", "p", "b", "i", "h1", "h2", "h3", "h4", "h5", "kbd", "strong", "em", "small", "label", "li", "td", "th", "summary", "a", "button", "option"]);
const cs = (s) => JSON.stringify(s);
let n = 0;
const out = [];
const symbols = [];

function emitSvgChildren(node, parentVar, indent) {
  for (const c of node.children) {
    if (c.tag === "#text") continue;
    if (c.tag === "use") { out.push(`${indent}((SvgEl)${parentVar}).UseSymbol(${cs(c.attrs.href || c.attrs["xlink:href"] || "")});`); continue; }
    if (c.tag === "symbol") continue;
    const v = `s${n++}`;
    const attrs = Object.entries(c.attrs).map(([k, val]) => {
      if (k === "style") {
        const origin = /transform-origin:\s*([^;]+)/.exec(val);
        return origin ? `(${cs("transform-origin")}, ${cs(origin[1].replace(/px/g, "").trim())})` : null;
      }
      return `(${cs(k)}, ${cs(val)})`;
    }).filter(Boolean);
    out.push(`${indent}var ${v} = SvgEl.Node(${cs(c.tag)}${attrs.length ? ", " + attrs.join(", ") : ""});`);
    if (c.attrs.id) out.push(`${indent}Dom.Register(${cs(c.attrs.id)}, ${v});`);
    const text = c.children.filter((k) => k.tag === "#text").map((k) => k.text).join("").trim();
    if (text) out.push(`${indent}${v}.SetText(${cs(text)});`);
    out.push(`${indent}${parentVar}.Add(${v});`);
    emitSvgChildren(c, v, indent);
  }
}

function symbolBuilder(sym) {
  // a Func<SvgNode[]> that builds the symbol's nodes fresh for every <use>
  const saved = out.length;
  const v = `root`;
  out.push(`            var ${v} = new SvgEl();`);
  emitSvgChildren(sym, v, "            ");
  const lines = out.splice(saved);
  return `() => { ${lines.map((l) => l.trim()).join(" ")} return root.Children().OfType<SvgNode>().ToArray(); }`;
}

function emit(node, parentVar, indent) {
  for (const c of node.children) {
    if (c.tag === "#text") { out.push(`${indent}${parentVar}.Append(${cs(c.text.trim() ? c.text : " ")});`); continue; }
    if (c.tag === "svg") {
      const syms = c.children.filter((k) => k.tag === "symbol");
      for (const s of syms) symbols.push(`            SvgEl.Symbols[${cs(s.attrs.id)}] = (${cs(s.attrs.viewBox || "0 0 24 24")}, ${symbolBuilder(s)});`);
      if (syms.length && c.children.every((k) => k.tag === "symbol" || k.tag === "#text")) continue; // the icon defs
      const v = `e${n++}`;
      out.push(`${indent}var ${v} = Dom.El("svg", ${cs(c.attrs.class || "")});`);
      for (const [k, val] of Object.entries(c.attrs)) if (k !== "class") out.push(`${indent}${v}.SetAttr(${cs(k)}, ${cs(val)});`);
      out.push(`${indent}${parentVar}.Add(${v});`);
      emitSvgChildren(c, v, indent);
      continue;
    }
    if (c.tag === "option") {
      const text = c.children.filter((k) => k.tag === "#text").map((k) => k.text).join("").trim();
      out.push(`${indent}((DomSelect)${parentVar}).AddOption(${cs(c.attrs.value ?? text)}, ${cs(text)}, ${"selected" in c.attrs}, ${"hidden" in c.attrs});`);
      continue;
    }
    const v = `e${n++}`;
    const hasElements = c.children.some((k) => k.tag !== "#text");
    const text = hasElements ? null : c.children.map((k) => k.text).join("").trim();
    const container = TEXT_TAGS.has(c.tag) && c.tag !== "button" && hasElements;
    out.push(`${indent}var ${v} = Dom.El(${cs(c.tag)}, ${cs(c.attrs.class || "")}, ${text ? cs(text) : "null"}, ${container});`);
    for (const [k, val] of Object.entries(c.attrs)) {
      if (k === "class" || k === "style" || k.startsWith("aria-") || k === "role" || k === "for" || k === "href" || k === "target" || k === "accept" || k === "autocomplete") continue;
      if (k === "id") { out.push(`${indent}Dom.Register(${cs(idPrefix + val)}, ${v});`); continue; }
      if (k === "checked") { out.push(`${indent}((DomInput)${v}).@checked = true;`); continue; }
      out.push(`${indent}${v}.SetAttr(${cs(k)}, ${cs(val)});`);
    }
    if (c.attrs.style) {
      for (const decl of c.attrs.style.split(";")) {
        const [p, ...rest] = decl.split(":");
        if (!p || !rest.length) continue;
        const prop = p.trim().replace(/-([a-z])/g, (_, x) => x.toUpperCase());
        out.push(`${indent}${v}.SetStyle(${cs(prop)}, ${cs(rest.join(":").trim())});`);
      }
    }
    out.push(`${indent}${parentVar}.Add(${v});`);
    if (c.tag === "select") { emit(c, v, indent); out.push(`${indent}((DomSelect)${v}).Refresh();`); continue; }
    if (hasElements) emit(c, v, indent);
  }
}

let idPrefix = "";
const tree = parse(body);
const bodyNode = tree.children.find((c) => c.tag === "body") || tree;
emit(bodyNode, "root", "            ");
const code = `// GENERATED by tools/gen_page.mjs from index.html - do not edit.
using System.Linq;
using UnityEngine.UIElements;

namespace LolHost
{
    public static class PageMarkup
    {
        /// <summary>The page's icon symbols (index.html <symbol>s), registered for <use href>.</summary>
        public static void RegisterSymbols()
        {
${symbols.join("\n")}
        }

        /// <summary>index.html's &lt;body&gt;, built under root.</summary>
        public static void Build(VisualElement root)
        {
            RegisterSymbols();
${out.join("\n")}
        }
    }
}
`;
const dest = path.join(path.dirname(new URL(import.meta.url).pathname), "..", "Assets", "Scripts", "Host", "PageMarkup.g.cs");
fs.mkdirSync(path.dirname(dest), { recursive: true });
fs.writeFileSync(dest, code);
console.log(`${n} elements, ${symbols.length} symbols -> ${dest}`);

// setup.html (the desktop app's "Game data" page): its <main>, ids prefixed with setup- (it is shown
// over the game page here instead of replacing it, and both have a #status)
{
  const setupHtml = fs.readFileSync(path.join(LANDS, "setup.html"), "utf8");
  const setupBody = setupHtml.slice(setupHtml.indexOf("<body"), setupHtml.lastIndexOf("</body>")).replace(/<script[\s\S]*?<\/script>/g, "");
  out.length = 0; n = 0; idPrefix = "setup-";
  const setupTree = parse(setupBody);
  emit(setupTree.children.find((c) => c.tag === "body") || setupTree, "root", "            ");
  const setupCode = `// GENERATED by tools/gen_page.mjs from setup.html - do not edit.
using UnityEngine.UIElements;

namespace LolHost
{
    public static class SetupMarkup
    {
        /// <summary>setup.html's &lt;body&gt;, built under root (ids: setup-&lt;id&gt;).</summary>
        public static void Build(VisualElement root)
        {
${out.join("\n")}
        }
    }
}
`;
  const setupDest = path.join(path.dirname(dest), "SetupMarkup.g.cs");
  fs.writeFileSync(setupDest, setupCode);
  console.log(`${n} elements -> ${setupDest}`);
}
