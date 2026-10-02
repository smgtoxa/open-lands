# The item artwork the web port added (src/assets/*.svg the code names) as PNGs under Assets/Resources/Art:
# a canvas drawImage needs pixels, and Unity cannot rasterize an SVG into a texture. Chromium draws them, as
# the browser's `new Image()` does.
#   python3 tools/art_png.py
import asyncio, glob, os, re
from playwright.async_api import async_playwright
ROOT = os.path.join(os.path.dirname(__file__), "..")
LANDS = os.environ.get("LANDS", os.path.expanduser("~/lands"))
SIZE = 128   # an icon canvas' backing store (32 logical px x 4)
CHROME = os.environ.get("LOL_CHROME") or (sorted(glob.glob(os.path.expanduser("~/.cache/ms-playwright/chromium*/chrome-linux*/chrome"))) or [None])[-1]

async def main():
    # every src/assets/*.svg the code names: item art (ExtraItemDef.art, the errand items, the pit sigil)
    sources = glob.glob(os.path.join(ROOT, "Engine", "**", "*.cs"), recursive=True) + glob.glob(os.path.join(ROOT, "Assets", "Scripts", "**", "*.cs"), recursive=True)
    arts = sorted({m for f in sources for m in re.findall(r'"(src/assets/[a-z0-9-]+\.svg)"', open(f, encoding="utf-8").read())})
    out = os.path.join(ROOT, "Assets", "Resources", "Art")
    os.makedirs(out, exist_ok=True)
    async with async_playwright() as p:
        b = await p.chromium.launch(executable_path=CHROME)
        pg = await (await b.new_context(viewport={"width": SIZE, "height": SIZE})).new_page()
        for art in arts:
            svg = open(os.path.join(LANDS, art), encoding="utf-8").read()
            await pg.set_content(f'<body style="margin:0;background:transparent"><img style="width:{SIZE}px;height:{SIZE}px;display:block" src="data:image/svg+xml;base64,{__import__("base64").b64encode(svg.encode()).decode()}"></body>')
            await pg.wait_for_function("document.images[0].complete")
            name = os.path.splitext(os.path.basename(art))[0] + ".png"
            await pg.screenshot(path=os.path.join(out, name), omit_background=True)
            print(name)
        await b.close()

asyncio.run(main())
