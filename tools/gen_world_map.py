#!/usr/bin/env python3
"""The world map art of the full map's "The Lands" view: an ink-on-parchment map, rendered by Chromium from SVG.

   python3 tools/gen_world_map.py

Writes Assets/Resources/UI/worldmap.png (the map, without labels: FullMap.cs places those) and
Assets/Resources/UI/worldfog.png (the cloud over an uncharted region). Landmark icons are from game-icons.net
(CC BY 3.0, by Lorc and Delapouite: see Assets/Resources/UI/CREDITS.txt), downloaded to GameData/ui-src/gi.
Region positions must match FullMap.REGIONS (a 1000 x 620 map).
"""
import os
import random
import re

from playwright.sync_api import sync_playwright

ROOT = os.path.join(os.path.dirname(__file__), "..")
GI = os.path.join(ROOT, "GameData", "ui-src", "gi")
OUT = os.path.join(ROOT, "Assets", "Resources", "UI")
INK = "#3a2814"
PAPER = "#d6c29b"
random.seed(11)


def icon(name):
    """The figure of a game-icons.net icon (512 x 512): its paths after the black backdrop."""
    svg = open(os.path.join(GI, name + ".svg"), encoding="utf-8").read()
    return "".join(f'<path d="{d}"/>' for d in re.findall(r'<path[^>]* d="([^"]+)"', svg)[1:])


def place(name, x, y, size, fill=INK, opacity=1.0):
    s = size / 512
    return f'<g transform="translate({x - size / 2:.1f} {y - size / 2:.1f}) scale({s:.4f})" fill="{fill}" opacity="{opacity}">{icon(name)}</g>'


# FullMap.REGIONS: (x, y, landmark)
REGIONS = [
    (500, 128, "castle"),        # Gladstone
    (700, 112, "pine-tree"),     # Northland Forest
    (470, 318, "village"),       # The Southland (Roland's manor, the Grey Eagle)
    (270, 450, "cave-entrance"), # The Draracle's Caves
    (205, 280, "swamp"),         # Opinwood & Gorkha Swamp
    (225, 112, "mine-wagon"),    # Urbish & the Mines
    (500, 505, "stone-tower"),   # The White Tower
    (720, 360, "huts-village"),  # Yvel
    (855, 215, "cave-entrance"), # The Catwalk Caves
    (840, 500, "castle"),        # Castle Cimmeria
]

LAND = ("M40 70 C150 30 300 52 420 40 C560 28 700 56 800 44 C870 38 912 90 902 150 C894 212 944 252 926 320 "
        "C910 380 952 432 930 482 C904 560 820 592 720 584 C600 576 520 602 420 590 C300 578 160 602 90 560 "
        "C40 520 62 440 46 360 C30 280 56 200 36 140 C26 100 30 84 40 70 Z")


def scatter(cx, cy, rx, ry, n, keep_clear=(), gap=11.4):
    pts = []
    tries = 0
    while len(pts) < n and tries < n * 40:
        tries += 1
        x, y = cx + random.uniform(-rx, rx), cy + random.uniform(-ry, ry)
        if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 > 1:
            continue
        if any((x - a) ** 2 + (y - b) ** 2 < r * r for a, b, r in keep_clear):
            continue
        if any((x - a) ** 2 + (y - b) ** 2 < gap * gap for a, b in pts):
            continue
        pts.append((x, y))
    return sorted(pts, key=lambda p: p[1])


def tree(x, y, s=1.0):
    h = 13 * s
    return (f'<path d="M{x:.1f} {y - h:.1f} L{x + 5 * s:.1f} {y - 3 * s:.1f} L{x - 5 * s:.1f} {y - 3 * s:.1f} Z" fill="{INK}"/>'
            f'<path d="M{x:.1f} {y - h * 0.7:.1f} L{x + 6.5 * s:.1f} {y:.1f} L{x - 6.5 * s:.1f} {y:.1f} Z" fill="{INK}"/>'
            f'<path d="M{x:.1f} {y:.1f} v{3 * s:.1f}" stroke="{INK}" stroke-width="1.4"/>')


def mountain(x, y, s=1.0):
    w, h = 16 * s, 20 * s
    shade = "".join(f'<path d="M{x + i * 2.4 * s:.1f} {y - h + i * 3.2 * s + 4:.1f} l{3 * s:.1f} {5 * s:.1f}" stroke="{INK}" stroke-width="0.9"/>' for i in range(1, 5))
    return (f'<path d="M{x - w:.1f} {y:.1f} L{x:.1f} {y - h:.1f} L{x + w:.1f} {y:.1f}" fill="{PAPER}" stroke="{INK}" stroke-width="1.8" stroke-linejoin="round"/>'
            + shade)


def hill(x, y, s=1.0):
    return f'<path d="M{x - 10 * s:.1f} {y:.1f} Q{x:.1f} {y - 11 * s:.1f} {x + 10 * s:.1f} {y:.1f}" fill="none" stroke="{INK}" stroke-width="1.5"/>'


def reed(x, y):
    return (f'<path d="M{x - 6:.1f} {y:.1f} h12 M{x - 3:.1f} {y:.1f} v-7 M{x:.1f} {y:.1f} v-10 M{x + 3:.1f} {y:.1f} v-6" '
            f'stroke="{INK}" stroke-width="1.2" fill="none"/>')


def wave(x, y):
    return f'<path d="M{x:.1f} {y:.1f} q4 -4 8 0 t8 0" stroke="{INK}" stroke-width="1.1" fill="none" opacity="0.45"/>'


clear = [(x, y, 46) for x, y, _ in REGIONS]
art = []
# forests: the Northland, around the Southland, a wood by Yvel, Opinwood
for cx, cy, rx, ry, n in [(700, 112, 90, 52, 46), (420, 360, 110, 70, 40), (600, 250, 60, 40, 14), (180, 230, 70, 60, 22), (760, 430, 50, 40, 12)]:
    art += [tree(x, y, random.uniform(0.85, 1.15)) for x, y in scatter(cx, cy, rx, ry, n, clear)]
# mountains: the north-west range over the mines, the eastern ridge, hills between
for cx, cy, rx, ry, n in [(250, 140, 120, 55, 22), (840, 330, 50, 120, 16), (620, 530, 70, 30, 8)]:
    art += [mountain(x, y, random.uniform(0.8, 1.25)) for x, y in scatter(cx, cy, rx, ry, n, clear, 26)]
for cx, cy, rx, ry, n in [(360, 200, 70, 40, 10), (620, 180, 50, 30, 6), (330, 540, 70, 30, 8)]:
    art += [hill(x, y, random.uniform(0.8, 1.2)) for x, y in scatter(cx, cy, rx, ry, n, clear)]
for x, y in scatter(205, 300, 90, 55, 24, clear):
    art.append(reed(x, y))
waves = [wave(x, y) for x, y in [(950, 60), (965, 200), (975, 380), (960, 560), (880, 600), (620, 612), (250, 606), (15, 300), (12, 480), (60, 20), (300, 18), (640, 16)]]

landmarks = "".join(
    f'<ellipse cx="{x}" cy="{y + 4}" rx="34" ry="28" fill="{PAPER}" opacity="0.85" filter="url(#soft)"/>' + place(name, x, y, 56)
    for x, y, name in REGIONS if name)

map_svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1000 620" width="1600" height="992">
<defs>
  <filter id="grain" x="0" y="0" width="100%" height="100%">
    <feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" seed="4"/>
    <feColorMatrix type="matrix" values="0 0 0 0 0.23  0 0 0 0 0.16  0 0 0 0 0.08  0 0 0 0.16 0"/>
  </filter>
  <filter id="stains" x="0" y="0" width="100%" height="100%">
    <feTurbulence type="fractalNoise" baseFrequency="0.012" numOctaves="4" seed="9"/>
    <feColorMatrix type="matrix" values="0 0 0 0 0.42  0 0 0 0 0.30  0 0 0 0 0.15  0 0 0 1.6 -0.75"/>
  </filter>
  <filter id="rough"><feTurbulence type="fractalNoise" baseFrequency="0.035" numOctaves="3" seed="2"/><feDisplacementMap in="SourceGraphic" scale="9"/></filter>
  <filter id="soft"><feGaussianBlur stdDeviation="5"/></filter>
  <radialGradient id="vignette" cx="50%" cy="50%" r="72%">
    <stop offset="55%" stop-color="#2a1a08" stop-opacity="0"/>
    <stop offset="100%" stop-color="#2a1a08" stop-opacity="0.75"/>
  </radialGradient>
</defs>
<rect width="1000" height="620" fill="#c4ab80"/>
<g filter="url(#rough)">
  <path d="{LAND}" fill="none" stroke="{INK}" stroke-width="34" opacity="0.05"/>
  <path d="{LAND}" fill="none" stroke="{INK}" stroke-width="22" opacity="0.07"/>
  <path d="{LAND}" fill="none" stroke="{INK}" stroke-width="11" opacity="0.10"/>
  <path d="{LAND}" fill="{PAPER}" stroke="{INK}" stroke-width="2.4"/>
</g>
<g fill="none" stroke-linecap="round">
  <path d="M575 62 Q 520 90 540 140 T 500 250 Q 460 300 480 360 T 440 470 Q 420 540 440 620" stroke="#4f6b72" stroke-width="5" opacity="0.85"/>
  <path d="M492 270 Q 600 300 690 330" stroke="#4f6b72" stroke-width="3.2" opacity="0.8"/>
  <path d="M575 62 Q 520 90 540 140 T 500 250 Q 460 300 480 360 T 440 470 Q 420 540 440 620" stroke="#9fb7b3" stroke-width="1.4" opacity="0.7"/>
</g>
<g fill="none" stroke="#6b4a26" stroke-width="2" stroke-dasharray="6 5" stroke-linecap="round" opacity="0.85">
  <path d="M500 128 Q 600 100 700 112"/>
  <path d="M500 128 Q 520 230 470 318"/>
  <path d="M470 318 Q 360 380 270 450"/>
  <path d="M470 318 Q 320 300 205 280"/>
  <path d="M205 280 Q 190 190 225 112"/>
  <path d="M470 318 Q 520 420 500 505"/>
  <path d="M470 318 Q 600 340 720 360"/>
  <path d="M720 360 Q 800 280 855 215"/>
  <path d="M720 360 Q 800 430 840 500"/>
</g>
{"".join(art)}
{landmarks}
{"".join(waves)}
{place("wyvern", 955, 120, 70, INK, 0.55)}
{place("sailboat", 960, 520, 46, INK, 0.6)}
<g transform="translate(110 520)">
  <circle r="44" fill="none" stroke="{INK}" stroke-width="1.2" opacity="0.7"/>
  <circle r="38" fill="none" stroke="{INK}" stroke-width="0.6" opacity="0.7"/>
  <path d="M0 -50 L7 -7 L0 0 Z M0 50 L-7 7 L0 0 Z M50 0 L7 7 L0 0 Z M-50 0 L-7 -7 L0 0 Z" fill="{INK}"/>
  <path d="M0 -50 L-7 -7 L0 0 Z M0 50 L7 7 L0 0 Z M50 0 L7 -7 L0 0 Z M-50 0 L-7 7 L0 0 Z" fill="{PAPER}" stroke="{INK}" stroke-width="0.8"/>
  <path d="M0 -28 L4 -4 L0 0 Z M28 0 L4 4 Z M0 28 L-4 4 Z M-28 0 L-4 -4 Z" fill="{INK}" opacity="0.6" transform="rotate(45)"/>
  <text y="-56" text-anchor="middle" font-family="Georgia, serif" font-size="14" font-weight="bold" fill="{INK}">N</text>
</g>
<rect width="1000" height="620" filter="url(#stains)" opacity="0.55"/>
<rect width="1000" height="620" filter="url(#grain)"/>
<rect width="1000" height="620" fill="url(#vignette)"/>
</svg>'''

fog_svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 160" width="400" height="320">
<defs>
  <filter id="cloud" x="-20%" y="-20%" width="140%" height="140%">
    <feTurbulence type="fractalNoise" baseFrequency="0.04" numOctaves="4" seed="5" result="n"/>
    <feDisplacementMap in="SourceGraphic" in2="n" scale="26"/>
    <feGaussianBlur stdDeviation="5"/>
  </filter>
</defs>
<g filter="url(#cloud)" fill="#cdb791">
  <ellipse cx="100" cy="80" rx="74" ry="56"/>
  <ellipse cx="70" cy="70" rx="40" ry="34"/>
  <ellipse cx="135" cy="92" rx="42" ry="32"/>
</g>
<g filter="url(#cloud)" fill="#bfa67c" opacity="0.5"><ellipse cx="100" cy="86" rx="60" ry="40"/></g>
</svg>'''

with sync_playwright() as p:
    # whatever headless shell is already installed (as tools/render_icons.py)
    import glob
    shells = sorted(glob.glob(os.path.expanduser("~/.cache/ms-playwright/chromium_headless_shell-*/*/chrome-headless-shell")))
    browser = p.chromium.launch(executable_path=shells[-1]) if shells else p.chromium.launch()
    page = browser.new_page()
    for name, svg, w, h, transparent in [("worldmap", map_svg, 1600, 992, False), ("worldfog", fog_svg, 400, 320, True)]:
        page.set_viewport_size({"width": w, "height": h})
        page.set_content(f'<html><body style="margin:0;background:transparent">{svg}</body></html>')
        page.locator("svg").screenshot(path=os.path.join(OUT, name + ".png"), omit_background=transparent)
        print(name)
    browser.close()
