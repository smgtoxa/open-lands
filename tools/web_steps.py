# Web page screenshots along a step list (same step names as the Unity autopilot, timed from page load):
#   python3 tools/web_steps.py "4:click:.title-button,7:click:.character-button,...,26:shot:Shots/w1.png"
# tp:L:B:D teleports (window.lolEngine.debugTeleport); shot:PATH / dump:PATH write the screenshot / tools/layout_dump.js boxes.
import asyncio, glob, json, os, sys
from playwright.async_api import async_playwright
URL = os.environ.get("LOL_URL", "http://127.0.0.1:18765/")
CHROME = os.environ.get("LOL_CHROME") or (sorted(glob.glob(os.path.expanduser("~/.cache/ms-playwright/chromium*/chrome-linux*/chrome"))) or [None])[-1]
JS = open(os.path.join(os.path.dirname(__file__), "layout_dump.js")).read()

async def main(spec, storage):
    steps = []
    for s in spec.split(","):
        t, _, action = s.partition(":")
        steps.append((float(t), action))
    async with async_playwright() as p:
        b = await p.chromium.launch(executable_path=CHROME, args=["--autoplay-policy=no-user-gesture-required"])
        pg = await (await b.new_context(viewport={"width": int(os.environ.get("LOL_VIEW", "1600x1000").split("x")[0]), "height": int(os.environ.get("LOL_VIEW", "1600x1000").split("x")[1])})).new_page()
        await pg.goto(URL + "index.html?nospeech")
        await pg.evaluate("(items) => { localStorage.clear(); for (const [k, v] of Object.entries(items)) localStorage.setItem(k, v); }", storage)
        await pg.goto(URL + "index.html?nospeech")
        start = asyncio.get_event_loop().time()
        for at, action in sorted(steps, key=lambda x: x[0]):
            wait = at - (asyncio.get_event_loop().time() - start)
            if wait > 0: await asyncio.sleep(wait)
            kind, _, arg = action.partition(":")
            try:
                if kind == "click": await pg.click(arg, timeout=500)
                elif kind == "key": await pg.keyboard.press(" " if arg == "Space" else arg)
                elif kind in ("clickat", "rclickat"):
                    x, y = map(float, arg.split(":")); await pg.mouse.click(x, y, button="right" if kind == "rclickat" else "left")
                elif kind == "tp":
                    l, b_, d = (arg.split(":") + ["", "0"])[:3]
                    await pg.evaluate(f"window.lolEngine.queueAsync(() => window.lolEngine.debugTeleport({l}, {b_ or 'undefined'}, {d}))")
                elif kind == "shot": await pg.mouse.move(1, 999); await pg.screenshot(path=arg)
                elif kind == "dump": open(arg, "w").write(await pg.evaluate(JS))
            except Exception as e:
                print(f"web: {action}: {type(e).__name__}")
        await b.close()

# optional second argument: a localStorage JSON file ({key: string}); default: intro off
store = json.load(open(sys.argv[2])) if len(sys.argv) > 2 else {"lol.settings": json.dumps({"intro": False})}
asyncio.run(main(sys.argv[1], store))
