# The guide's pictures (Assets/Resources/Guide/*.png), taken from the game itself: one run of the player on a copy of
# a save (in-game interface on), a screenshot at each step, cropped to the box shown (autopilot inspect gives its place).
#   python3 tools/guide_shots.py STORAGE.json        (a localStorage copy with a game well under way)
import os, re, subprocess, sys
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..")
OUT = os.path.join(ROOT, "Assets/Resources/Guide")
SHOTS = os.path.join(ROOT, "Shots/guide")
os.makedirs(SHOTS, exist_ok=True)
win = lambda p: subprocess.check_output(["wslpath", "-w", os.path.abspath(p)]).decode().strip()

# (picture, seconds, actions before the picture, box to crop: a selector, "full" or None for the 3D view's frame)
PLAN = [
    ("overview", 12, [], "full"),
    ("minimap", 12.4, [], ".hud-map"),
    ("compass", 12.6, [], ".hud-map"),
    ("actions", 12.8, [], ".hud-actions"),
    ("spells", 13.0, [], ".hud-spells"),
    ("objectives", 13.2, [], ".hud-quests"),
    ("party", 13.4, [], ".hud-party"),
    ("hotbar", 13.6, [], ".hud-bar"),
    ("bestiary", 16, [(14, "click:#journal-open"), (14.6, "click:#journal-overlay .tab[data-tab=bestiary]")], "#journal-overlay .modal-box"),
    ("journallog", 18, [(16.6, "click:#journal-overlay .tab[data-tab=log]")], "#journal-overlay .modal-box"),
    ("inventory", 21, [(18.6, "key:Escape"), (19.4, "click:#inventory-open")], "#inventory-overlay"),
    ("character", 24, [(21.6, "key:Escape"), (22.4, "key:p")], "#char-overlay .modal-box"),
    ("fullmap", 27, [(24.6, "key:p"), (25.4, "key:m")], "#map-overlay .modal-box"),
    ("menu", 30, [(27.6, "key:Escape"), (28.4, "key:Escape")], "#menu-overlay .modal-box"),
    ("settings", 33, [(30.6, "key:Escape"), (31.4, "click:#settings-open")], "#settings-overlay .modal-box"),
    ("keys", 35, [(33.6, "clicktext:#settings-overlay .tab:Controls")], "#settings-overlay .modal-box"),
    ("circle", 41, [(35.6, "key:Escape"), (36.4, "key:r")], None),
    ("camp", 41.4, [], None),
    ("imp", 44, [(42, "key:q")], None),
    ("chest", 47, [(45, "key:q")], None),
]


def main(storage):
    steps = ["8:click:.continue-button"]
    for name, t, before, box in PLAN:
        steps += [f"{at}:{a}" for at, a in before]
        steps.append(f"{t}:shot:{win(SHOTS)}\\{name}.png")
        steps.append(f"{t + 0.1:.1f}:inspect:{box if box not in (None, 'full') else '.stage-column'}")
    end = PLAN[-1][1] + 2
    subprocess.run(["bash", os.path.join(ROOT, "play.sh"), "--storage", win(storage), "--do", ",".join(steps), "--quit-after", str(end)],
                   cwd=ROOT, env={**os.environ, "PLAY_TIMEOUT": "200"}, stdout=subprocess.DEVNULL)
    log = open(os.path.join(ROOT, "Logs/player.log"), encoding="utf-8", errors="replace").read().splitlines()
    rects = []   # the first rect printed after each inspect step, in order
    for i, line in enumerate(log):
        if line.startswith("autopilot inspect:"):
            m = next((re.search(r"world=\((-?\d+),(-?\d+),(\d+),(\d+)\)", l) for l in log[i + 1:i + 4] if l.startswith("inspect ")), None)
            rects.append(tuple(int(v) for v in m.groups()) if m else None)
    for (name, t, before, box), rect in zip(PLAN, rects):
        im = Image.open(os.path.join(SHOTS, name + ".png")).convert("RGB")
        if box != "full":
            if rect is None:
                print("no box for", name); continue
            x, y, w, h = rect
            if name == "compass":
                h = 150                                   # the map's top: the compass and the lantern
            im = im.crop((x, y, x + w, y + h))
        if im.width > 640:
            im = im.resize((640, round(im.height * 640 / im.width)), Image.LANCZOS)
        im.save(os.path.join(OUT, name + ".png"))
        print(name, im.size)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Logs/ui-hud.json"))
