# Web page element boxes at 1600x1000: python3 tools/layout_web.py OUT.txt [steps...]
# steps: wait:MS, key:NAME, click:SELECTOR (same meaning as the Unity autopilot).
import asyncio, glob, os, sys
from playwright.async_api import async_playwright
URL = os.environ.get("LOL_URL", "http://127.0.0.1:18765/")
CHROME = os.environ.get("LOL_CHROME") or (sorted(glob.glob(os.path.expanduser("~/.cache/ms-playwright/chromium*/chrome-linux*/chrome"))) or [None])[-1]
JS = open(os.path.join(os.path.dirname(__file__), "layout_dump.js")).read()

async def main(out, steps):
    async with async_playwright() as p:
        b = await p.chromium.launch(executable_path=CHROME)
        pg = await (await b.new_context(viewport={"width": 1600, "height": 1000})).new_page()
        await pg.goto(URL + "index.html?nospeech")
        await pg.evaluate("localStorage.clear(); localStorage.setItem('lol.settings', JSON.stringify({intro:false}))")
        await pg.goto(URL + "index.html?nospeech")
        await pg.wait_for_timeout(3000)
        for s in steps:
            kind, _, arg = s.partition(":")
            if kind == "wait": await pg.wait_for_timeout(int(arg))
            elif kind == "key": await pg.keyboard.press(arg)
            elif kind == "click": await pg.click(arg)
        await pg.mouse.move(1, 999)
        open(out, "w").write(await pg.evaluate(JS))
        await pg.screenshot(path=out.rsplit(".", 1)[0] + ".png")
        await b.close()

asyncio.run(main(sys.argv[1], sys.argv[2:]))
