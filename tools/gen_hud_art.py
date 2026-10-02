# The in-game interface art (Assets/Resources/UI/hud): carved stone frames with gold trim, stone buttons and slots
# and the stone wall behind the whole screen, built from the CC0 "Dungeon Crawl 32x32 tiles" (Dungeon Crawl Stone
# Soup artists, GameData/ui-src/dcss, https://opengameart.org/content/dungeon-crawl-32x32-tiles). Every picture is
# drawn in source pixels and doubled (nearest), so it reads as pixel art next to the game's own VGA picture.
#   python3 tools/gen_hud_art.py
import os
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..")
WALL = os.path.join(ROOT, "GameData/ui-src/dcss/crawl-tiles Oct-5-2010/dc-dngn/wall")
OUT = os.path.join(ROOT, "Assets/Resources/UI/hud")
os.makedirs(OUT, exist_ok=True)
UNIT = 2

GOLD = [(70, 46, 14), (138, 96, 34), (201, 152, 62), (240, 206, 120), (255, 240, 180)]


def tile(name):
    return Image.open(os.path.join(WALL, name)).convert("RGB")


def tint(im, k, warm=(1.0, 1.0, 1.0)):
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b = px[x, y][:3]
            px[x, y] = (min(255, int(r * k * warm[0])), min(255, int(g * k * warm[1])), min(255, int(b * k * warm[2])))
    return im


def tiled(src, w, h):
    out = Image.new("RGB", (w, h))
    for y in range(0, h, src.height):
        for x in range(0, w, src.width):
            out.paste(src, (x, y))
    return out


def save(im, name):
    im = im.resize((im.width * UNIT, im.height * UNIT), Image.NEAREST)
    im.save(os.path.join(OUT, name))


# ---- the wall behind everything: dark cobbles, 2x2 tiles so the repeat is not obvious ----
bg = Image.new("RGB", (64, 64))
for i, n in enumerate(["brick_dark0.png", "brick_dark1.png", "brick_dark2.png", "brick_dark3.png"]):
    bg.paste(tile(n), ((i % 2) * 32, (i // 2) * 32))
save(tint(bg, 0.42, (1.0, 0.97, 0.9)), "wall.png")

def granite(base, spread, seed):
    """A seamless 32px speckled stone face (wrapping blur over random grains)."""
    import random
    rnd = random.Random(seed)
    n = [[rnd.random() for _ in range(32)] for _ in range(32)]
    im = Image.new("RGB", (32, 32))
    px = im.load()
    for y in range(32):
        for x in range(32):
            v = sum(n[(y + dy) % 32][(x + dx) % 32] for dy in (-1, 0, 1) for dx in (-1, 0, 1)) / 9
            v = (v - 0.5) * 2 + (n[y][x] - 0.5) * 0.6
            px[x, y] = tuple(max(0, min(255, int(c + v * spread))) for c in base)
    return im


FACE = granite((88, 84, 76), 22, 7)       # a button's face
STONE = tint(tiled(tile("stone2_gray0.png"), 96, 96), 0.78, (1.0, 0.98, 0.93))
INNER = tiled(granite((22, 20, 17), 10, 3), 96, 96)   # the dark face of a panel


def shade(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c)


T = 32   # the stone tiles' period: every piece below is cut so its edges and middle repeat seamlessly


def frame(band, inner=True, rivets=True):
    """A carved stone border `band` source pixels wide with a gold line inside it and a dark stone face: a 9-slice
    picture (band + one tile + band) meant to be drawn with -unity-slice-type: tiled."""
    w = h = band * 2 + T
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = im.load()
    sp, ip = STONE.load(), INNER.load()
    for y in range(h):
        for x in range(w):
            d = min(x, y, w - 1 - x, h - 1 - y)
            # light from the top left: the top and left sides of each ring are lit
            lit = (y == d and x <= w - 1 - d) or (x == d and y <= h - 1 - d)
            tx, ty = (x - band) % T, (y - band) % T
            if d == 0:
                px[x, y] = (10, 8, 6, 255)
            elif d == 1:
                px[x, y] = shade(sp[tx, ty], 1.45 if lit else 0.5) + (255,)
            elif d < band - 2:
                px[x, y] = shade(sp[tx, ty], 1.0) + (255,)
            elif d == band - 2:
                px[x, y] = shade(sp[tx, ty], 0.45 if lit else 1.3) + (255,)
            elif d == band - 1:
                px[x, y] = (GOLD[3] if lit else GOLD[1]) + (255,)
            elif inner:
                px[x, y] = ip[tx, ty] + (242,)
    if rivets:
        c = band // 2
        for cx, cy in ((c, c), (w - 1 - c, c), (c, h - 1 - c), (w - 1 - c, h - 1 - c)):
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    px[cx + dx, cy + dy] = (GOLD[1] if dx + dy > 0 else GOLD[3] if dx + dy < 0 else GOLD[2]) + (255,)
            px[cx - 1, cy - 1] = GOLD[4] + (255,)
            px[cx + 1, cy + 1] = GOLD[0] + (255,)
    return im


def slab(state, band=3):
    """A stone button (band + one tile + band, tiled 9-slice): raised, lit with a gold edge, or pressed in."""
    w = h = band * 2 + T
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = im.load()
    sp = FACE.load()
    k0 = {"normal": 1.0, "hover": 1.22, "down": 0.75}[state]
    for y in range(h):
        for x in range(w):
            d = min(x, y, w - 1 - x, h - 1 - y)
            lit = (y == d and x <= w - 1 - d) or (x == d and y <= h - 1 - d)
            c = sp[(x - band) % T, (y - band) % T]
            if d == 0:
                px[x, y] = (GOLD[2] if state == "hover" else (12, 10, 8)) + (255,)
            elif d == 1:
                up = lit != (state == "down")
                px[x, y] = shade(c, k0 * (1.7 if up else 0.5)) + (255,)
            else:
                px[x, y] = shade(c, k0) + (255,)
    return im


def slot(band=4):
    """A square hollowed into the stone (tiled 9-slice): a stone rim with a gold hairline, dark inside."""
    w = h = band * 2 + T
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    px = im.load()
    sp, ip = STONE.load(), INNER.load()
    for y in range(h):
        for x in range(w):
            d = min(x, y, w - 1 - x, h - 1 - y)
            lit = (y == d and x <= w - 1 - d) or (x == d and y <= h - 1 - d)
            tx, ty = (x - band) % T, (y - band) % T
            if d == 0:
                px[x, y] = (10, 8, 6, 255)
            elif d == 1:
                px[x, y] = shade(sp[tx, ty], 1.3 if lit else 0.55) + (255,)
            elif d == 2:
                px[x, y] = (GOLD[1] if lit else GOLD[2]) + (255,)
            elif d == 3:
                px[x, y] = (6, 5, 4, 255) if lit else shade(ip[tx, ty], 1.6) + (255,)
            else:
                px[x, y] = shade(ip[tx, ty], 1.15) + (255,)
    return im


save(frame(8), "frame.png")                           # panels and windows: 16px border
save(frame(5, rivets=False), "frame-thin.png")        # cards and boxes inside a panel: 10px border
save(frame(8, inner=False), "frame-open.png")         # around the 3D view: the picture shows through
for st in ("normal", "hover", "down"):
    save(slab(st), f"button-{st}.png")                # 6px border
save(slot(), "slot.png")                              # 8px border
print("hud art:", sorted(os.listdir(OUT)))


# ---- cursors (Assets/Resources/Cursors/game-*.png, 32x32): a pixel arrow (steel; gold over what can be clicked),
# the sword of 7Soul1's CC0 "496 pixel art icons" for an attack, the steel gauntlet of the CC0 Dungeon Crawl tiles
# for taking things, and a pixel crosshair. Hot spots: Dom/CssCursor.cs.
CUR = os.path.join(ROOT, "Assets/Resources/Cursors")
ARROW = [
    "X...............",
    "XX..............",
    "XAX.............",
    "XAAX............",
    "XAABX...........",
    "XAABBX..........",
    "XABBBBX.........",
    "XABBBBBX........",
    "XABBBBBBX.......",
    "XABBBBBXXX......",
    "XABXBBBX........",
    "XAX.XBBBX.......",
    "XX..XBBBX.......",
    "X....XBBBX......",
    "......XBBX......",
    ".......XX.......",
]


def arrow(light, mid, dark):
    im = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    px = im.load()
    for y, row in enumerate(ARROW):
        for x, ch in enumerate(row):
            if ch == "X":
                px[x, y] = (18, 12, 6, 255)
            elif ch == "A":
                px[x, y] = light + (255,)
            elif ch == "B":
                px[x, y] = (mid if y < 9 else dark) + (255,)
    return im.resize((32, 32), Image.NEAREST)


def fit32(path):
    im = Image.open(path).convert("RGBA")
    im = im.crop(im.getbbox())
    out = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    if im.width > 32 or im.height > 32:
        k = 32 / max(im.width, im.height)
        im = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.NEAREST)
    out.paste(im, (0, 0), im)
    return out


def crosshair():
    im = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    px = im.load()
    for i in range(16):
        for (x, y) in ((i, 7), (i, 8), (7, i), (8, i)):
            if 5 <= i <= 10:
                continue
            px[x, y] = GOLD[3] + (255,)
    for i in range(16):
        for (x, y) in ((i, 6), (i, 9), (6, i), (9, i)):
            if not (5 <= i <= 10) and px[x, y][3] == 0:
                px[x, y] = (18, 12, 6, 255)
    px[7, 7] = px[8, 8] = px[7, 8] = px[8, 7] = (255, 80, 60, 255)
    return im.resize((32, 32), Image.NEAREST)


ICONS = os.path.join(ROOT, "GameData/ui-src/icons496")
DCSS = os.path.join(ROOT, "GameData/ui-src/dcss/crawl-tiles Oct-5-2010")
arrow((236, 236, 240), (196, 198, 206), (140, 142, 152)).save(os.path.join(CUR, "game-default.png"))
arrow((255, 244, 190), (246, 208, 110), (196, 142, 48)).save(os.path.join(CUR, "game-pointer.png"))
fit32(os.path.join(ICONS, "W_Sword001.png")).save(os.path.join(CUR, "game-attack.png"))
# mirrored: the fingers reach up and to the left, the way the arrow points
fit32(os.path.join(DCSS, "UNUSED/armour/gauntlet1.png")).transpose(Image.FLIP_LEFT_RIGHT).save(os.path.join(CUR, "game-grab.png"))
crosshair().save(os.path.join(CUR, "game-crosshair.png"))
print("cursors made")


# ---- the compass and the lantern of the in-game interface (UI/hud/compass-D.png, lantern-STATE-LEVEL.png) ----
# The compass for each way the party faces: that way on top, the letters upright, the red needle to the north.
# The lantern: an iron cage with amber oil in its glass up to the level left (0-14) and a flame when lit.
LETTERS = {
    "N": ["X.X", "XXX", "XXX", "XXX", "X.X"],
    "E": ["XXX", "X..", "XX.", "X..", "XXX"],
    "S": [".XX", "X..", ".X.", "..X", "XX."],
    "W": ["X.X", "X.X", "XXX", "XXX", "X.X"],
}


def compass(facing):
    S = 32
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    px = im.load()
    c = (S - 1) / 2
    for y in range(S):
        for x in range(S):
            r = ((x - c) ** 2 + (y - c) ** 2) ** 0.5
            if r > 15.6:
                continue
            if r > 14.6:
                px[x, y] = (14, 10, 6, 255)
            elif r > 12.4:
                lit = (x - c) + (y - c) < 0
                px[x, y] = (GOLD[3] if lit else GOLD[1]) + (255,) if r > 13.5 else GOLD[2] + (255,)
            elif r > 11.6:
                px[x, y] = (40, 28, 12, 255)
            else:
                px[x, y] = (24, 20, 15, 255)
    # ticks between the letters
    for (tx, ty) in ((8, 8), (23, 8), (8, 23), (23, 23)):
        px[tx, ty] = GOLD[1] + (255,)
    names = ["N", "E", "S", "W"]
    spots = [(15, 4), (26, 15), (15, 26), (4, 15)]      # top, right, bottom, left (centres)
    for k, name in enumerate(names):
        pos = (k - facing) % 4
        cx, cy = spots[pos]
        col = (255, 90, 70) if name == "N" else GOLD[3]
        for yy, row in enumerate(LETTERS[name]):
            for xx, ch in enumerate(row):
                if ch == "X":
                    px[cx - 1 + xx, cy - 2 + yy] = col + (255,)
    # the needle: a diamond from the centre to the north (red) and to the south (steel)
    north = (0 - facing) % 4
    dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)]
    dx, dy = dirs[north]
    for t in range(-8, 9):
        w = (8 - abs(t)) // 3
        for s in range(-w, w + 1):
            x = int(15.5 + dx * t + (-dy) * s)
            y = int(15.5 + dy * t + dx * s)
            red = t > 0
            shade_ = (220, 50, 40) if red else (190, 196, 206)
            if s == w or s == -w:
                shade_ = (130, 26, 20) if red else (110, 114, 124)
            px[x, y] = shade_ + (255,)
    px[15, 15] = px[16, 16] = px[15, 16] = px[16, 15] = GOLD[3] + (255,)
    return im.resize((64, 64), Image.NEAREST)


LANTERN = [
    "........XX..........",
    ".......X..X.........",
    ".......X..X.........",
    "......XXXXXX........",
    ".....XIIIIIIX.......",
    "....XIIIIIIIIX......",
    "...XXXXXXXXXXXX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XIGGGGGGGGIX.....",
    "...XXXXXXXXXXXX.....",
    "....XIIIIIIIIX......",
    ".....XXXXXXXX.......",
]


def lantern(lit, level):
    """level 0..14: the oil left, in the reservoir under the flame (the lower half of the glass)"""
    W, H = 20, 25
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    px = im.load()
    glass_top, glass_bottom = 7, 21
    oil_rows = (level + 1) // 2                  # 0..7
    oil_top = glass_bottom - oil_rows
    for y, row in enumerate(LANTERN):
        for x, ch in enumerate(row):
            if ch == "X":
                px[x, y] = (16, 12, 8, 255)
            elif ch == "I":
                px[x, y] = ((110, 104, 96) if y < 7 else (70, 66, 60)) + (255,)
            elif ch == "G":
                bar = x in (7, 11) or y == 14            # the cage's iron bars and the band over the reservoir
                if bar:
                    px[x, y] = (38, 32, 26, 255)
                elif y >= oil_top:
                    px[x, y] = ((255, 200, 90) if y == oil_top else (210, 136, 34) if x < 9 else (176, 104, 22)) + (255,)
                else:
                    px[x, y] = ((86, 66, 36) if lit and level > 0 else (44, 40, 34)) + (210,)
    if lit and level > 0:
        hgt = 3 + level // 3                     # 3..7 rows: the flame shrinks as the oil runs out
        for k in range(hgt):
            y = 13 - k
            if y < glass_top:
                break
            wide = k < hgt - 2
            for x in ((8, 9, 10) if wide else (9,)):
                px[x, y] = ((255, 250, 200) if wide and x == 9 and k < hgt - 3 else (255, 176, 46)) + (255,)
    return im.resize((W * 2, H * 2), Image.NEAREST)


for d in range(4):
    compass(d).save(os.path.join(OUT, f"compass-{d}.png"))
for lit in (0, 1):
    for level in range(15):
        lantern(lit, level).save(os.path.join(OUT, f"lantern-{'on' if lit else 'off'}-{level}.png"))
print("compass and lantern made")
