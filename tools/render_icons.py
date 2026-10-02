#!/usr/bin/env python3
"""Renders the browser build's own SVG art to PNGs for the Unity page (Assets/Resources/Icons).

   python3 tools/render_icons.py [LANDS dir]

- index.html <symbol id="i-*">: the button icons, drawn white (currentColor) so USS can tint them
- the compass: its dial and its rose (the rose turns), and the lantern: its body and its flame
- src/platform/spell-widget.mjs THEMES / EXTRA_THEMES: one icon per spell, in the spell's colour
"""
import os
import re
import sys

from playwright.sync_api import sync_playwright

LANDS = sys.argv[1] if len(sys.argv) > 1 else os.environ.get("LANDS", os.path.expanduser("~/lands"))
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources", "Icons")
os.makedirs(OUT, exist_ok=True)

html = open(os.path.join(LANDS, "index.html"), encoding="utf-8").read()
items = {}  # name -> (svg markup, px width, px height)

for m in re.finditer(r'<symbol id="i-([\w-]+)" viewBox="([^"]+)">(.*?)</symbol>', html, re.S):
    name, box, body = m.groups()
    body = body.replace("currentColor", "#ffffff")
    items["i-" + name] = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{box}" width="64" height="64">{body}</svg>', 64, 64)

compass = re.search(r'<svg id="compass"[^>]*>(.*?)</svg>', html, re.S).group(1)
rose = re.search(r'(<g id="compass-rose".*?</g>\s*</g>)', compass, re.S).group(1)
dial = compass.replace(rose, "")
wrap = lambda body, box, w, h: f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{box}" width="{w}" height="{h}">{body}</svg>'
items["compass-dial"] = (wrap(dial, "0 0 100 100", 128, 128), 128, 128)
items["compass-rose"] = (wrap(rose, "0 0 100 100", 128, 128), 128, 128)

lantern = re.search(r'<svg id="lantern"[^>]*>(.*?)</svg>', html, re.S).group(1)
flame = re.search(r'(<g id="lantern-flame".*?</g>)', lantern, re.S).group(1)
items["lantern-body"] = (wrap(lantern.replace(flame, ""), "0 0 60 100", 60 * 2, 100 * 2), 120, 200)
items["lantern-flame"] = (wrap(flame, "0 0 60 100", 60 * 2, 100 * 2), 120, 200)

widget = open(os.path.join(LANDS, "src", "platform", "spell-widget.mjs"), encoding="utf-8").read()
for m in re.finditer(r'(\w+): \{ name: "([^"]+)", color: "([^"]+)".*?icon: "([^"]+)"(, stroke: true)?', widget, re.S):
    key, _, color, path, stroke = m.groups()
    attrs = (f'fill="none" stroke="{color}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"'
             if stroke else f'fill="{color}"')
    items[f"spell-{key}"] = (wrap(f'<path d="{path}" {attrs}/>', "0 0 24 24", 48, 48), 48, 48)

with sync_playwright() as p:
    # Whatever headless shell is already installed: no browser download for a few icons.
    import glob
    shells = sorted(glob.glob(os.path.expanduser("~/.cache/ms-playwright/chromium_headless_shell-*/*/chrome-headless-shell")))
    browser = p.chromium.launch(executable_path=shells[-1]) if shells else p.chromium.launch()
    page = browser.new_page()
    for name, (svg, w, h) in items.items():
        page.set_content(f'<html><body style="margin:0;background:transparent"><div id="x" style="width:{w}px;height:{h}px">{svg}</div></body></html>')
        page.locator("#x").screenshot(path=os.path.join(OUT, f"{name}.png"), omit_background=True)
    browser.close()
print(f"{len(items)} icons -> {os.path.abspath(OUT)}")
