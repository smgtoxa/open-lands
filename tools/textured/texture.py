#!/usr/bin/env python3
"""Textured world packs: the game's own walls, floors, ceilings, doors, decorations and floor items with the grain
of CC0 photo materials (Poly Haven, GameData/textured/materials) laid over them. The original picture stays: its
layout, colours, light and every painted detail; the material only adds surface (stone grain, wood fibre, leaves).
Creatures are left out of the pack, so they keep their original sprites.

   node tools/textured/export_hd_sources.mjs L 4 plain && node tools/textured/export_hd_walls.mjs L 4   (base)
   python3 tools/textured/texture.py L [L...]                                                      (pack)

Writes GameData/textured/level<L>/ (manifest.json, tiles.png, shapes/*.png): the HD pack format (Host/HdScene.cs).
The far and side wall views are the near view scaled and warped (as the web project's scripts/hd/hdwalls.py does),
so every distance and angle shows the same texture.
"""
import json, math, os, shutil, sys
from PIL import Image, ImageChops, ImageFilter

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
BASE = os.environ.get("TEX_BASE") or os.path.join(ROOT, "GameData", "textured", "base")
OUT = os.environ.get("TEX_OUT") or os.path.join(ROOT, "GameData", "textured")
MAT = os.path.join(ROOT, "GameData", "textured", "materials")
GREY = (128, 128, 128)
LIVING = {"monster"}   # left to the original sprites

# the look of each wall set family (the level's .VCN / .SHP name): wall, floor, ceiling, wood, green
THEMES = {
    "KEEP": ("plastered_wall_04", "marble_01", "worn_planks", "worn_planks", "forest_leaves_03"),
    "FOREST": ("bark_brown_02", "forest_ground_04", "forest_leaves_03", "bark_willow", "forest_leaves_03"),
    "SWAMP": ("bark_willow", "brown_mud_leaves_01", "forest_leaves_03", "bark_willow", "forest_leaves_03"),
    "CAVE": ("rock_wall_08", "rocky_trail", "rock_wall_08", "worn_planks", "forest_leaves_03"),
    "MINE": ("rock_boulder_dry", "rocky_trail", "rock_boulder_dry", "worn_planks", "forest_leaves_03"),
    "CATWALK": ("rock_wall_08", "rocky_trail", "rock_wall_08", "worn_planks", "forest_leaves_03"),
    "URBISH": ("worn_planks", "rocky_trail", "worn_planks", "worn_planks", "forest_leaves_03"),
    "MANOR": ("plastered_wall_04", "wood_table_worn", "worn_planks", "worn_planks", "forest_leaves_03"),
    "TOWER": ("marble_01", "marble_01", "marble_01", "worn_planks", "forest_leaves_03"),
    "YVEL": ("plastered_wall_04", "rocky_trail", "worn_planks", "worn_planks", "forest_leaves_03"),
    "CIMMERIA": ("castle_wall_slates", "marble_01", "rock_surface", "worn_planks", "forest_leaves_03"),
    "RUIN": ("rock_surface", "rocky_trail", "rock_surface", "worn_planks", "forest_leaves_03"),
}
DEFAULT_THEME = ("rock_surface", "rocky_trail", "rock_surface", "worn_planks", "forest_leaves_03")
STRENGTH = 0.85      # how far the grain lifts and darkens (0 none, 1 the soft-light blend in full)
TEXEL = 0.5          # material pixels per picture pixel at the near wall (1k material: about two wall widths)

_mat = {}


def epx(img):
    """Scale2x (EPX): pixel-art edges doubled without blur."""
    w, h = img.size
    src = img.load()
    out = Image.new(img.mode, (w * 2, h * 2))
    dst = out.load()
    for y in range(h):
        for x in range(w):
            p = src[x, y]
            a = src[x, y - 1] if y > 0 else p
            b = src[x + 1, y] if x < w - 1 else p
            c = src[x - 1, y] if x > 0 else p
            d = src[x, y + 1] if y < h - 1 else p
            e0 = a if c == a and c != d and a != b else p
            e1 = b if a == b and a != c and b != d else p
            e2 = c if d == c and d != b and c != a else p
            e3 = d if b == d and b != a and d != c else p
            dst[2 * x, 2 * y] = e0; dst[2 * x + 1, 2 * y] = e1; dst[2 * x, 2 * y + 1] = e2; dst[2 * x + 1, 2 * y + 1] = e3
    return out


def smooth4(img):
    """A 4x nearest-neighbour picture made smooth: back to its own pixels, then EPX twice."""
    w, h = img.size
    small = img.resize((w // 4, h // 4), Image.NEAREST)
    return epx(epx(small))


def material(name):
    """The material's grain: its luminance with the broad light and colour taken out, centred on 128."""
    if name not in _mat:
        m = Image.open(os.path.join(MAT, name + ".jpg")).convert("L")
        hp = ImageChops.add(ImageChops.subtract(m, m.filter(ImageFilter.GaussianBlur(10)), 1, 128), Image.new("L", m.size, 0))
        hp = hp.point(lambda v: max(0, min(255, round(128 + (v - 128) * 1.6))))
        _mat[name] = hp
    return _mat[name]


def tiled(name, w, h, texel=TEXEL, ox=0, oy=0):
    m = material(name)
    m = m.resize((max(8, round(m.width * texel)), max(8, round(m.height * texel))), Image.LANCZOS)
    out = Image.new("L", (w, h))
    for y in range(-(oy % m.height), h, m.height):
        for x in range(-(ox % m.width), w, m.width):
            out.paste(m, (x, y))
    return out


def classes(img):
    """Masks (L) of the picture's pixels by what they look like: green (leaves, moss), wood (warm, saturated),
    the rest (stone, plaster); flat grey filler and black are left alone."""
    hsv = img.convert("RGB").convert("HSV")
    h, s, v = hsv.split()
    rgb = img.convert("RGB")
    grey = ImageChops.difference(rgb, Image.new("RGB", img.size, GREY)).convert("L").point(lambda x: 0 if x < 10 else 255)
    lit = v.point(lambda x: 255 if x > 22 else 0)
    green = ImageChops.multiply(h.point(lambda x: 255 if 38 <= x <= 125 else 0), s.point(lambda x: 255 if x > 55 else 0))
    warm = ImageChops.multiply(h.point(lambda x: 255 if 8 <= x <= 30 else 0), s.point(lambda x: 255 if x > 120 else 0))
    warm = ImageChops.subtract(warm, green)
    base = ImageChops.multiply(grey, lit)
    if img.mode == "RGBA":
        base = ImageChops.multiply(base, img.split()[3].point(lambda a: 255 if a > 0 else 0))
    soft = lambda m: ImageChops.multiply(m, base).filter(ImageFilter.GaussianBlur(1.2))
    rest = ImageChops.subtract(ImageChops.subtract(base, green), warm)
    return soft(green), soft(warm), soft(rest)


def grain(img, detail, mask):
    """img with the grain soft-lit into it where the mask is."""
    rgb = img.convert("RGB")
    lit = ImageChops.soft_light(rgb, Image.merge("RGB", (detail, detail, detail)))
    lit = Image.blend(rgb, lit, STRENGTH)
    out = Image.composite(lit, rgb, mask)
    if img.mode == "RGBA":
        out = out.convert("RGBA"); out.putalpha(img.split()[3])
    return out


def texture_flat(img, theme, wall=0, texel=TEXEL, seed=0):
    """A picture seen face on (a near wall, a decoration): the theme's wall material, wood on the warm parts."""
    w, h = img.size
    green, warm, rest = classes(img)
    img = grain(img, tiled(theme[wall], w, h, texel, seed * 37, seed * 53), rest)
    img = grain(img, tiled(theme[3], w, h, texel, seed * 11, 0), warm)
    img = grain(img, tiled(theme[4], w, h, texel * 1.3, seed * 7, seed * 3), green)
    return img


def texture_backdrop(img, theme):
    """The floor and ceiling picture: each material laid on its plane in perspective (rows nearer the horizon
    shrink), the horizon at the picture's darkest band."""
    w, h = img.size
    gray = img.convert("L")
    rows = [sum(gray.crop((0, y, w, y + 1)).getdata()) / w for y in range(h)]
    mid = h // 2
    horizon = min(range(h // 4, 3 * h // 4), key=lambda y: rows[y] + abs(y - mid) * 0.5)
    green, warm, rest = classes(img)
    out = img
    for plane, name in (("floor", theme[1]), ("ceiling", theme[2])):
        src = material(name)
        sw, sh = src.size
        det = Image.new("L", (w, h), 128)
        px = det.load()
        sp = src.load()
        for y in range(h):
            dy = (y - horizon) if plane == "floor" else (horizon - y)
            if dy <= 2:
                continue
            z = (h * 0.9) / dy                       # depth of this row on the plane
            fade = min(1.0, dy / (h * 0.12))         # the grain fades out near the horizon (too fine to see)
            for x in range(w):
                u = int((x - w / 2) * z * 0.9 / TEXEL) % sw
                v = int(z * 260 / TEXEL) % sh
                px[x, y] = round(128 + (sp[u, v] - 128) * fade)
        mask = rest.point(lambda a: a)
        rowmask = Image.new("L", (w, h), 0)
        if plane == "floor":
            rowmask.paste(255, (0, horizon, w, h))
        else:
            rowmask.paste(255, (0, 0, w, horizon))
        out = grain(out, det, ImageChops.multiply(mask, rowmask))
    return out


# ---- the walls: hdwalls.py's cutting and warping ----
def solve(A, B):
    n = len(B); M = [row[:] + [B[i]] for i, row in enumerate(A)]
    for c in range(n):
        piv = max(range(c, n), key=lambda r: abs(M[r][c])); M[c], M[piv] = M[piv], M[c]
        for r in range(n):
            if r != c and M[c][c]:
                f = M[r][c] / M[c][c]
                for k in range(c, n + 1): M[r][k] -= f * M[c][k]
    return [M[i][n] / M[i][i] for i in range(n)]


def find_coeffs(pa, pb):
    A = []; B = []
    for p1, p2 in zip(pa, pb):
        A.append([p1[0], p1[1], 1, 0, 0, 0, -p2[0] * p1[0], -p2[0] * p1[1]]); B.append(p2[0])
        A.append([0, 0, 0, p1[0], p1[1], 1, -p2[1] * p1[0], -p2[1] * p1[1]]); B.append(p2[1])
    return solve(A, B)


def warp_into(near, export_img, W, H):
    ex = export_img.convert("RGB").resize((W, H), Image.NEAREST)
    diff = ImageChops.difference(ex, Image.new("RGB", (W, H), GREY)).convert("L").point(lambda v: 255 if v > 12 else 0)
    box = diff.getbbox()
    if not box: return None
    x0, x1 = box[0], box[2] - 1
    def span(x):
        col = diff.crop((x, 0, x + 1, H)).getbbox(); return (col[1], col[3]) if col else (0, H)
    t0, b0 = span(x0); t1, b1 = span(x1)
    quad = [(x0, t0), (x1 + 1, t1), (x1 + 1, b1), (x0, b0)]
    nw, nh = near.size
    coeffs = find_coeffs(quad, [(0, 0), (nw, 0), (nw, nh), (0, nh)])
    return near.transform((W, H), Image.PERSPECTIVE, coeffs, Image.BICUBIC)


FRONT = {7, 5, 2, 1}   # the views seen face on (the rest are side walls)


def theme_of(manifest):
    names = [k.split(".")[0].upper() for k in manifest["shapes"] if manifest["shapes"][k].get("kind") == "decoration"]
    for key in THEMES:
        if any(n.startswith(key) for n in names):
            return key, THEMES[key]
    return "?", DEFAULT_THEME


def run_level(lvl):
    src = os.path.join(BASE, f"level{lvl}")
    dst = os.path.join(OUT, f"level{lvl}")
    if not os.path.exists(os.path.join(src, "walls.json")):
        print(f"level {lvl}: no base export"); return
    os.makedirs(os.path.join(dst, "shapes"), exist_ok=True)
    manifest = json.load(open(os.path.join(src, "manifest.json")))
    key, theme = theme_of(manifest)
    j = json.load(open(os.path.join(src, "walls.json")))
    tiles = Image.open(os.path.join(src, "tiles.png")).convert("RGBA")
    base_alpha = tiles.split()[3].point(lambda a: 255 if a > 127 else 0)
    ts = 8 * j["scale"]; cols = j["tileColumns"]
    pics = {os.path.basename(p["file"]): p for p in j["pictures"]}
    wall_dir = os.path.join(src, "walls")

    def paste(p, img):
        gen = img.convert("RGBA").resize((p["width"] * ts, p["height"] * ts), Image.LANCZOS)
        for c in p["cells"]:
            piece = gen.crop((c["x"] * ts, c["y"] * ts, (c["x"] + 1) * ts, (c["y"] + 1) * ts))
            if c["flipped"]: piece = piece.transpose(Image.FLIP_LEFT_RIGHT)
            t = c["tile"]; at = ((t % cols) * ts, (t // cols) * ts)
            # the tile's see-through pixels (the pictures show them black): the base sheet's alpha
            piece.putalpha(base_alpha.crop((at[0], at[1], at[0] + ts, at[1] + ts)))
            tiles.paste(piece, at)

    if "backdrop.png" in pics:
        paste(pics["backdrop.png"], texture_backdrop(smooth4(Image.open(os.path.join(wall_dir, "backdrop.png")).convert("RGBA")), theme))
    sets = sorted({int(n.split("_")[0][3:]) for n in pics if n.startswith("set")})
    for m in sets:
        near_p = pics.get(f"set{m}_view7.png")
        near_w = near_p["width"] if near_p else 16
        # far views first, the near view last (a tile shared by two views keeps the nearest one's look)
        for v in [1, 2, 5, 0, 3, 4, 6, 8, 7]:
            p = pics.get(f"set{m}_view{v}.png")
            if not p: continue
            pic = smooth4(Image.open(os.path.join(wall_dir, f"set{m}_view{v}.png")).convert("RGBA"))
            texel = TEXEL * (p["width"] / near_w if v in FRONT else 1.0)
            paste(p, texture_flat(pic, theme, texel=texel, seed=m))
    tiles.save(os.path.join(dst, "tiles.png"), optimize=True)

    # the things that are not alive: decorations, doors, items, thrown things
    shapes = {}
    for k, s in manifest["shapes"].items():
        if s.get("kind") in LIVING: continue
        img = Image.open(os.path.join(src, s["file"])).convert("RGBA")
        texel = TEXEL * (0.6 if s.get("kind") in ("item", "thrown") else 1.0)
        out = texture_flat(img, theme, texel=texel, seed=hash(k) % 97)
        out.save(os.path.join(dst, s["file"]), optimize=True)
        shapes[k] = s
    manifest["shapes"] = shapes
    json.dump(manifest, open(os.path.join(dst, "manifest.json"), "w"), indent=1)
    print(f"level {lvl} ({key}): {len(sets)} wall sets, {len(shapes)} shapes", flush=True)


if __name__ == "__main__":
    for lvl in [int(a) for a in sys.argv[1:]] or [1]:
        run_level(lvl)
