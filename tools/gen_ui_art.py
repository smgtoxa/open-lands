# The fantasy interface art (Assets/Resources/UI) from Kenney's CC0 "Fantasy UI Borders" (GameData/ui-src/fub,
# https://kenney.nl/assets/fantasy-ui-borders): gold frames over dark bronze gradients, as 9-slice pictures.
#   python3 tools/gen_ui_art.py
import os, random
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..")
SRC = os.path.join(ROOT, "GameData/ui-src/fub/PNG/Default")
OUT = os.path.join(ROOT, "Assets/Resources/UI")
os.makedirs(OUT, exist_ok=True)


def mask(name, unit):
    """The border's shape (white on transparent, drawn in 2px units) at `unit` px per unit."""
    im = Image.open(os.path.join(SRC, "Border", name)).convert("RGBA")
    im = im.resize((im.width // 2, im.height // 2), Image.NEAREST)
    return im.resize((im.width * unit, im.height * unit), Image.NEAREST)


def gradient(size, top, bottom):
    w, h = size
    im = Image.new("RGBA", size)
    for y in range(h):
        t = y / max(1, h - 1)
        c = tuple(round(a + (b - a) * t) for a, b in zip(top, bottom))
        for x in range(w):
            im.putpixel((x, y), c)
    return im


def paint(shape, color, keep=None):
    """The shape in one colour; keep(x, y, w, h) picks the pixels drawn (corners only, say)."""
    out = Image.new("RGBA", shape.size)
    w, h = shape.size
    for y in range(h):
        for x in range(w):
            if shape.getpixel((x, y))[3] > 128 and (keep is None or keep(x, y, w, h)):
                out.putpixel((x, y), color)
    return out


def corners(n):
    return lambda x, y, w, h: (x < n or x >= w - n) and (y < n or y >= h - n)


def framed(name, unit, line, top, bottom, keep=None, inset=0):
    shape = mask(name, unit)
    bg = gradient(shape.size, top, bottom)
    if inset:   # the background inside the frame's outer line only, so the corners stand clear of the page
        clear = Image.new("RGBA", shape.size)
        clear.paste(bg.crop((inset, inset, shape.width - inset, shape.height - inset)), (inset, inset))
        bg = clear
    return Image.alpha_composite(bg, paint(shape, line, keep))


GOLD, GOLD_HI, GOLD_DIM = (176, 141, 70, 255), (226, 190, 110, 255), (120, 96, 52, 255)

# panels: the ornate-corner frame over a dark bronze fall
framed("panel-border-010.png", 2, GOLD_DIM, (24, 20, 15, 244), (11, 10, 9, 244)).save(f"{OUT}/panel.png")
# modal windows: the double frame
framed("panel-border-014.png", 2, GOLD, (26, 21, 15, 250), (12, 10, 8, 250)).save(f"{OUT}/modal.png")
# cards: corner ornaments only - the card's own border shows its state (selected)
framed("panel-border-010.png", 1, GOLD_DIM, (22, 19, 14, 240), (12, 11, 9, 240), corners(5)).save(f"{OUT}/card.png")
# buttons: small corner squares over a raised bronze face; hover and pressed faces
framed("panel-border-008.png", 1, GOLD_DIM, (52, 42, 26, 255), (24, 19, 12, 255), corners(4)).save(f"{OUT}/button.png")
framed("panel-border-008.png", 1, GOLD_HI, (78, 62, 34, 255), (36, 28, 16, 255), corners(4)).save(f"{OUT}/button-hover.png")
framed("panel-border-008.png", 1, GOLD, (20, 16, 10, 255), (40, 32, 20, 255), corners(4)).save(f"{OUT}/button-down.png")

# slots: a sunken well (dark, shadow along the top and left, light along the bottom and right)
s = 24
slot = gradient((s, s), (9, 8, 6, 255), (17, 14, 10, 255))
for i in range(s):
    for d, c in ((0, (0, 0, 0, 255)), (1, (5, 4, 3, 255))):
        slot.putpixel((i, d), c); slot.putpixel((d, i), c)
    slot.putpixel((i, s - 1), (58, 47, 30, 255)); slot.putpixel((s - 1, i), (58, 47, 30, 255))
slot.save(f"{OUT}/slot.png")

# the page behind everything: dark grained leather, tiled (seamless: the noise wraps)
random.seed(7)
n = 128
base = [[random.random() for _ in range(n)] for _ in range(n)]
tile = Image.new("RGBA", (n, n))
for y in range(n):
    for x in range(n):
        v = sum(base[(y + dy) % n][(x + dx) % n] for dy in (-1, 0, 1) for dx in (-1, 0, 1)) / 9
        g = 6 + round(v * 8)
        tile.putpixel((x, y), (g + 3, g + 2, g, 255))
tile.save(f"{OUT}/page.png")

# a heading's underline: the fading divider, gold
div = Image.open(os.path.join(SRC, "Divider Fade", "divider-fade-002.png")).convert("RGBA")
r, g, b, a = div.split()
gold = Image.new("RGBA", div.size, GOLD_DIM)
gold.putalpha(a)
gold.save(f"{OUT}/divider.png")

# (the cursors are tools/gen_hud_art.py's now: pixel art)
