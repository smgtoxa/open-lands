# Interface shake test: one player run through every screen; after each click, drag or wheel turn the screen is
# watched for 1.5 s and any box that still moves after the first 3 frames is reported (a settled screen does not move).
#   python3 tools/ui_shake.py STORAGE.json
import os, re, subprocess, sys
ROOT = os.path.join(os.path.dirname(__file__), "..")
win = lambda p: subprocess.check_output(["wslpath", "-w", os.path.abspath(p)]).decode().strip()

# (label, watched root, actions); each step is given 2 s
STEPS = [
    ("main idle", ".shell", []),
    ("attack (cooldowns run)", ".shell", ["key:f"]),
    ("character after attack", "#char-overlay", ["key:f", "key:p"]),
    ("character idle", "#char-overlay", []),
    ("character close 0", ".shell", ["key:p"]),
    ("inventory after attack", "#inventory-overlay", ["key:f", "click:#inventory-open"]),
    ("inventory idle", "#inventory-overlay", []),
    ("inventory close 0", ".shell", ["key:Escape"]),
    ("hotbar click", ".shell", ["clickat:84:730"]),
    ("hotbar put back", ".shell", ["clickat:84:730"]),
    ("spell click", ".shell", ["clickat:1300:430"]),
    ("spell right-click", ".shell", ["rclickat:1360:430"]),
    ("spell picker close", ".shell", ["rclickat:1360:430"]),
    ("log wheel", ".shell", ["wheelat:1100:650:3"]),
    ("log tab", ".shell", ["clicktext:.ui-log-filter:Combat"]),
    ("map wheel", ".shell", ["wheelat:1060:250:-2"]),
    ("inventory open", "#inventory-overlay", ["click:#inventory-open"]),
    ("inventory slot", "#inventory-overlay", ["click:#inventory-grid .tag-button"]),
    ("inventory slot back", "#inventory-overlay", ["click:#inventory-grid .tag-button"]),
    ("inventory wheel", "#inventory-overlay", ["wheelat:450:300:3"]),
    ("inventory filter", "#inventory-overlay", ["openselect:#inventory-filter"]),
    ("inventory close", ".shell", ["key:Escape", "key:Escape"]),
    ("character open", "#char-overlay", ["key:p"]),
    ("character spell tick", "#char-overlay", ["click:.spell-show"]),
    ("character spell tick back", "#char-overlay", ["click:.spell-show"]),
    ("character next", "#char-overlay", ["clicktext:#char-overlay .tag-button:Next ▶"]),
    ("character close", ".shell", ["key:p"]),
    ("journal open", "#journal-overlay", ["click:#journal-open"]),
    ("journal log", "#journal-overlay", ["click:#journal-overlay .tab[data-tab=log]"]),
    ("journal log wheel", "#journal-overlay", ["wheelat:800:500:5"]),
    ("journal bestiary", "#journal-overlay", ["click:#journal-overlay .tab[data-tab=bestiary]"]),
    ("bestiary tile", "#journal-overlay", ["click:.itemdb-tile:nth-child(2)"]),
    ("journal items", "#journal-overlay", ["click:#journal-overlay .tab[data-tab=items]"]),
    ("items wheel", "#journal-overlay", ["wheelat:600:500:3"]),
    ("item tile", "#journal-overlay", ["click:.itemdb-tile:nth-child(3)"]),
    ("journal stats", "#journal-overlay", ["click:#journal-overlay .tab[data-tab=stats]"]),
    ("journal people", "#journal-overlay", ["click:#journal-overlay .tab[data-tab=npcs]"]),
    ("journal guide", "#journal-overlay", ["click:#journal-overlay .tab[data-tab=guide]"]),
    ("guide article", "#journal-overlay", ["click:.guide-card"]),
    ("guide back", "#journal-overlay", ["click:.guide-back"]),
    ("journal spells", "#journal-overlay", ["click:#journal-overlay .tab[data-tab=spellbook]"]),
    ("spellbook item", "#journal-overlay", ["click:.sb-item:nth-child(2)"]),
    ("journal close", ".shell", ["key:Escape"]),
    ("settings open", "#settings-overlay", ["click:#settings-open"]),
    ("settings tab", "#settings-overlay", ["clicktext:#settings-overlay .tab:Gameplay"]),
    ("settings wheel", "#settings-overlay", ["wheelat:800:500:3"]),
    ("settings tab 2", "#settings-overlay", ["clicktext:#settings-overlay .tab:Controls"]),
    ("settings close", ".shell", ["key:Escape"]),
    ("full map", "#map-overlay", ["key:m"]),
    ("full map wheel", "#map-overlay", ["wheelat:600:500:-2"]),
    ("full map drag", "#map-overlay", ["pressat:600:500", "releaseat:650:520"]),
    ("full map close", ".shell", ["key:m"]),
    ("menu", "#menu-overlay", ["key:Escape"]),
    ("menu close", ".shell", ["key:Escape"]),
    ("camp", ".shell", ["key:r"]),
    ("camp settle", ".shell", []),
    ("camp turn", ".shell", ["key:q"]),
    ("imp open", ".shell", ["click:.camp-station-art"]),
    ("imp spells", ".shell", ["click:.imp-tab[data-imp=spells]"]),
    ("imp sell", ".shell", ["click:.imp-tab[data-imp=sell]"]),
    ("imp list wheel", ".shell", ["wheelat:1000:500:3"]),
    ("imp sell one", ".shell", ["click:#imp-list .imp-do"]),
    ("imp close", ".shell", ["click:#camp-sheet-close"]),
    ("camp turn 2", ".shell", ["key:q"]),
    ("stash open", ".shell", ["click:.camp-station-art"]),
    ("stash wheel", ".shell", ["wheelat:600:500:3"]),
    ("stash close", ".shell", ["click:#camp-sheet-close"]),
    ("camp turn 3", ".shell", ["key:q"]),
    ("station 3", ".shell", ["click:.camp-station-art"]),
    ("station 3 close", ".shell", ["click:#camp-sheet-close"]),
]


def main(storage, run=True):
    steps, t = ["8:click:.continue-button"], 12.0
    for label, root, actions in STEPS:
        for k, a in enumerate(actions):
            steps.append(f"{t + k * 0.3:.2f}:{a}")
        steps.append(f"{t + len(actions) * 0.3 + 0.05:.2f}:watch:{root}:1.5")
        t += len(actions) * 0.3 + 2.0
    if run: subprocess.run(["bash", os.path.join(ROOT, "play.sh"), "--storage", win(storage), "--do", ",".join(steps), "--quit-after", str(t + 1)],
                   cwd=ROOT, env={**os.environ, "PLAY_TIMEOUT": "300"}, stdout=subprocess.DEVNULL)
    log = open(os.path.join(ROOT, "Logs/player.log"), encoding="utf-8", errors="replace").read().splitlines()
    i, step, bad = 0, -1, {}
    for line in log:
        if line.startswith("autopilot watch:"):
            step += 1
        m = re.match(r"autopilot watch f(\d+) (\S+): ([-\d.]+),([-\d.]+) (\S+) -> ([-\d.]+),([-\d.]+) (\S+)", line)
        onscreen = m and all(-50 < float(v) < 2000 for v in (m.group(3), m.group(4), m.group(6), m.group(7)))   # hidden pages lie far off
        if m and step >= 0 and int(m.group(1)) >= 3 and "NaN" not in m.group(5) and onscreen:
            bad.setdefault(step, []).append((int(m.group(1)), m.group(2)[-90:], f"{m.group(3)},{m.group(4)} {m.group(5)} -> {m.group(6)},{m.group(7)} {m.group(8)}"))
    # boxes rebuilt again and again while watched (a redraw key that keeps changing): each rebuild can show a frame
    # before it is laid out
    step = -1
    for line in log:
        if line.startswith("autopilot watch:"):
            step += 1
        m = re.match(r"autopilot watch rebuilt (\d+)x NEW under (\S+)", line)
        if m and step >= 0 and int(m.group(1)) >= 3:
            bad.setdefault(step, []).append((0, m.group(2)[-90:], f"rebuilt {m.group(1)} times"))
    for s, (label, _, _) in enumerate(STEPS):
        items = bad.get(s, [])
        print(("SHAKE " if items else "ok    ") + label + (f": {len(items)} moves" if items else ""))
        for f, path, what in items[:4]:
            print(f"        f{f} {path}  {what}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Logs/ui-hud.json"), "--parse" not in sys.argv)
