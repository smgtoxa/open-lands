// Dumps every element's box as "key x y w h" (key = nearest #id ancestor + tag.class[index] path).
// The same walk runs in the Unity host (Autopilot dump:), so tools/layout_diff.sh can compare them.
(() => {
  const out = [];
  const name = (el) => el.tagName.toLowerCase() + [...el.classList].sort().map((c) => "." + c).join("");
  const walk = (el, key) => {
    const r = el.getBoundingClientRect();
    const shown = r.width > 0 || r.height > 0;
    if (shown) out.push(`${key} ${Math.round(r.x)} ${Math.round(r.y)} ${Math.round(r.width)} ${Math.round(r.height)}`);
    if (el.tagName === "svg" || el.tagName === "SELECT") return;
    let i = 0;
    for (const c of el.children) {
      if (c.tagName === "svg" && c.querySelector("symbol")) continue;
      if (["SCRIPT", "STYLE", "TEMPLATE", "OPTION", "TITLE", "LINK", "META"].includes(c.tagName)) continue;
      walk(c, c.id ? "#" + c.id : `${key}>${name(c)}[${i}]`);
      i += 1;
    }
  };
  walk(document.body, "body");
  return out.join("\n");
})()
