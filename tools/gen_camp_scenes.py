#!/usr/bin/env python3
"""The camp stations' pictures, from free art (no Lands of Lore art): each object set in a dark stone room made from
CC0 Poly Haven materials, lit by its own light. Output: Assets/StreamingAssets/camp/scene-<station>.jpg, or numbered
frames scene-<station>_NN.jpg for the animated ones (the vortex, the imp). 704 x 480 (the scene window at 4x).

   python3 tools/gen_camp_scenes.py

Sources in GameData/ui-src/camp (see Assets/StreamingAssets/camp/CREDITS.txt):
  vortex/NN.png   Animated Portal or Wormhole, Hansjoerg Malthaner (Varkalandar), CC-BY 4.0 (black keyed to alpha)
  cauldron.png    cauldrons, Kiririnochka, CC0
  chest.png       Treasure Chest, Rico Cilliers / Poly Haven, CC0
  imp/NN.png      [LPC] Imp 2, Stephen "Redshrike" Challener for William.Thompsonj, CC-BY 4.0
"""
import glob, math, os, random
from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SRC = os.path.join(ROOT, "GameData", "ui-src", "camp")
MAT = os.path.join(ROOT, "Assets", "StreamingAssets", "materials")
OUT = os.path.join(ROOT, "Assets", "StreamingAssets", "camp")
W, H = 704, 480
FLOOR = 300  # where the back wall meets the floor
os.makedirs(OUT, exist_ok=True)


def tile(name, w, h, scale, tint):
    m = Image.open(os.path.join(MAT, name + ".jpg")).convert("RGB")
    m = m.resize((int(m.width * scale), int(m.height * scale)), Image.LANCZOS)
    out = Image.new("RGB", (w, h))
    for y in range(0, h, m.height):
        for x in range(0, w, m.width):
            out.paste(m, (x, y))
    return ImageChops.multiply(out, Image.new("RGB", (w, h), tint))


def radial(w, h, cx, cy, rx, ry, color, power=1.6):
    """A soft light: color at (cx, cy) fading to black at the ellipse's edge."""
    g = Image.new("L", (w, h))
    px = g.load()
    for y in range(h):
        for x in range(w):
            d = math.sqrt(((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2)
            px[x, y] = int(255 * max(0.0, 1 - d) ** power)
    return Image.composite(Image.new("RGB", (w, h), color), Image.new("RGB", (w, h), (0, 0, 0)), g)


def room(light_x, light_y, light):
    """Back wall of dark stone blocks, a flagstone floor, the station's light on both, the corners in shadow."""
    wall = tile("castle_wall_slates", W, FLOOR, 0.55, (120, 108, 92))
    floor = tile("rocky_trail", W, H - FLOOR, 0.45, (96, 86, 72))
    # the floor in perspective: rows nearer the wall squeezed
    persp = Image.new("RGB", (W, H - FLOOR))
    fh = H - FLOOR
    for y in range(fh):
        t = (y + 1) / fh
        src_y = int((t ** 1.6) * (fh - 1))
        persp.paste(floor.crop((0, src_y, W, src_y + 1)), (0, y))
    img = Image.new("RGB", (W, H))
    img.paste(wall, (0, 0))
    img.paste(persp, (0, FLOOR))
    # the wall's foot in shadow, a thin line where it meets the floor
    d = ImageDraw.Draw(img, "RGBA")
    for i in range(22):
        d.line([(0, FLOOR - i), (W, FLOOR - i)], fill=(0, 0, 0, int(110 * (1 - i / 22))))
    d.line([(0, FLOOR), (W, FLOOR)], fill=(10, 8, 6, 200), width=2)
    img = ImageEnhance.Brightness(img).enhance(1.0)
    lit = radial(W, H, light_x, light_y, W * 0.62, H * 0.9, light, 1.4)
    img = ImageChops.add(ImageChops.multiply(img, ImageChops.add(lit, Image.new("RGB", (W, H), (105, 96, 86)))), Image.new("RGB", (W, H), (0, 0, 0)))
    vignette = radial(W, H, W / 2, H * 0.55, W * 0.75, H * 0.85, (255, 255, 255), 0.7)
    return ImageChops.multiply(img, vignette)


def shadow(img, box, strength=150):
    """A soft oval shadow on the floor under an object."""
    x0, y0, x1, y1 = box
    s = Image.new("L", (W, H))
    ImageDraw.Draw(s).ellipse(box, fill=strength)
    s = s.filter(ImageFilter.GaussianBlur(14))
    return Image.composite(Image.new("RGB", (W, H), (0, 0, 0)), img, s)


def place(base, obj, height, cx, bottom):
    obj = obj.resize((int(obj.width * height / obj.height), height), Image.LANCZOS)
    base = base.convert("RGBA")
    base.alpha_composite(obj, (int(cx - obj.width / 2), int(bottom - obj.height)))
    return base.convert("RGB"), obj.size


def light_obj(obj, color, k):
    """Tint an object towards its light (k 0..1), keeping its alpha."""
    rgb = obj.convert("RGB")
    lit = ImageChops.multiply(rgb, Image.new("RGB", rgb.size, color))
    out = Image.blend(rgb, lit, k)
    out.putalpha(obj.split()[3])
    return out


def save(img, name):
    # drawn at the game's own pixel size: the scene window's 176 x 120, 128 colours, then 4x with hard edges (as
    # sharp as the game's picture next to it; the photo materials and painted objects otherwise looked HD)
    small = img.convert("RGB").resize((img.width // 4, img.height // 4), Image.BOX)
    small = small.quantize(colors=128, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE).convert("RGB")
    small.resize((small.width * 4, small.height * 4), Image.NEAREST).save(os.path.join(OUT, name), quality=95, subsampling=0)


for old in glob.glob(os.path.join(OUT, "scene-*.jpg")):
    os.remove(old)

# ---- the travelling circle: the vortex hovering over a ring of floor light (blended additively) ----
frames = sorted(glob.glob(os.path.join(SRC, "vortex", "*.png")))[::2]   # 32 of the 64 frames
base = room(W / 2, 220, (150, 120, 255))
base = shadow(base, (190, 380, 514, 440), 60)
glow = radial(W, H, W / 2, 410, 220, 40, (110, 80, 220), 1.2)
base = ImageChops.add(base, glow)
for i, f in enumerate(frames):
    v = Image.open(f).convert("RGBA")
    v = v.resize((int(v.width * 1.75), int(v.height * 1.75)), Image.LANCZOS)
    layer = Image.new("RGB", (W, H))
    rgb = Image.composite(v.convert("RGB"), Image.new("RGB", v.size), v.split()[3])
    layer.paste(rgb, (int(W / 2 - v.width / 2), int(215 - v.height / 2)))
    save(ImageChops.add(base, layer), f"scene-travel_{i:02d}.jpg")

# ---- the cauldron: on a bed of embers, its brew throwing green light up the wall ----
base = room(W / 2, 330, (255, 160, 90))
base = ImageChops.add(base, radial(W, H, W / 2, 200, 260, 200, (40, 90, 30), 1.5))
base = ImageChops.add(base, radial(W, H, W / 2, 425, 200, 45, (200, 80, 20), 1.1))     # the embers' glow
d = ImageDraw.Draw(base)
random.seed(3)
for _ in range(260):                                                                  # embers and coals
    x = random.gauss(W / 2, 70); y = random.gauss(420, 9)
    r = random.uniform(2, 6)
    c = random.choice([(255, 170, 60), (240, 110, 30), (120, 40, 15), (60, 25, 15)])
    d.ellipse((x - r, y - r * 0.6, x + r, y + r * 0.6), fill=c)
pot = light_obj(Image.open(os.path.join(SRC, "cauldron.png")).convert("RGBA"), (255, 200, 150), 0.25)
base, _ = place(base, pot, 290, W / 2, 432)
save(base, "scene-craft.jpg")

# ---- the stash chest: in a pool of warm lantern light ----
base = room(W * 0.45, 260, (255, 200, 130))
base = shadow(base, (150, 395, 560, 455), 170)
chest = light_obj(Image.open(os.path.join(SRC, "chest.png")).convert("RGBA"), (255, 214, 160), 0.3)
chest = ImageEnhance.Brightness(chest).enhance(0.9)
base, _ = place(base, chest, 270, W / 2, 448)
save(base, "scene-stash.jpg")

# ---- the imp trader: the imp behind a plank counter with a red candle glow ----
frames = sorted(glob.glob(os.path.join(SRC, "imp", "*.png")))
base = room(W / 2, 250, (255, 120, 80))
counter = tile("worn_planks", 520, 150, 0.35, (150, 110, 80))
cd = ImageDraw.Draw(counter, "RGBA")
cd.rectangle((0, 0, 519, 14), fill=(60, 40, 26, 255))                               # the counter's top edge
cd.line([(0, 15), (519, 15)], fill=(20, 12, 8, 255), width=3)
for i in range(30):
    cd.line([(0, 149 - i), (519, 149 - i)], fill=(0, 0, 0, int(160 * (1 - i / 30))))
for i, f in enumerate(frames):
    img = base.copy().convert("RGBA")
    imp = Image.open(f).convert("RGBA")
    imp = imp.resize((imp.width * 6, imp.height * 6), Image.NEAREST)
    img.alpha_composite(imp, (int(W / 2 - imp.width / 2), 300 - imp.height + 60))
    img.alpha_composite(counter.convert("RGBA"), (int(W / 2 - 260), 300))
    # potions standing on the counter
    dd = ImageDraw.Draw(img)
    for x, col in [(150, (120, 30, 160)), (200, (30, 130, 60)), (500, (190, 40, 30)), (548, (40, 80, 180))]:
        dd.ellipse((x - 16, 262, x + 16, 300), fill=col, outline=(16, 10, 6), width=2)
        dd.rectangle((x - 6, 246, x + 6, 266), fill=(150, 150, 140), outline=(16, 10, 6), width=2)
        dd.ellipse((x - 10, 270, x - 3, 280), fill=(230, 230, 220))
    img = ImageChops.add(img.convert("RGB"), radial(W, H, W / 2, 250, 260, 160, (40, 10, 0), 1.6))
    save(img, f"scene-imp_{i:02d}.jpg")

print("done:", len(glob.glob(os.path.join(OUT, "scene-*.jpg"))), "pictures")
