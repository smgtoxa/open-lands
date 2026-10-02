#!/bin/bash
# Runs one timed step list on the web page and the Unity player (fresh storage, no speech) and puts their
# screenshots side by side: Shots/cmp/<name>.png (web left, unity right) and a dump diff per dump step.
#   bash tools/compare.sh NAME "4:click:.title-button,...,26:shot:a,30:dump:a" [quit-after] [storage.json]
# storage.json: the localStorage both start from ({key: string}; default: intro off).
# UNITY_ARGS: extra player arguments (e.g. --hd PATH).
# shot:X / dump:X take a bare name; the steps are the ones play.sh and tools/web_steps.py share.
set -u
cd "$(dirname "$0")/.."
name=$1 steps=$2 quit=${3:-} storage=${4:-}
out=Shots/cmp; mkdir -p "$out"
W="$(wslpath -w "$PWD")"
last=$(echo "$steps" | tr ',' '\n' | cut -d: -f1 | sort -g | tail -1)
quit=${quit:-$(awk "BEGIN{print $last + 2}")}
web=$(echo "$steps" | sed -E "s#(shot|dump):([A-Za-z0-9_-]+)#\1:$out/web-$name-\2.EXT_\1#g; s#EXT_shot#png#g; s#EXT_dump#txt#g")
uni=$(echo "$steps" | sed -E "s#(shot|dump):([A-Za-z0-9_-]+)#\1:${W//\\/\\\\}\\\\Shots\\\\cmp\\\\unity-$name-\2.EXT_\1#g; s#EXT_shot#png#g; s#EXT_dump#txt#g")
mkdir -p Shots/test
if [ -n "$storage" ]; then cp "$storage" Shots/test/run.json; else printf '{"lol.settings":"{\\"intro\\":false}"}' > Shots/test/run.json; fi
python3 tools/web_steps.py "$web" Shots/test/run.json 2>&1 | sort | uniq -c
flock /tmp/lol-unity.lock bash play.sh ${UNITY_ARGS:-} --nospeech --storage "$W\\Shots\\test\\run.json" --do "$uni" --quit-after "$quit" | grep -v "^autopilot \(click\|shot\|dump\|tp\)"
grep -E "Exception|page log: .*(rror|fail)" Logs/player.log | sort | uniq -c | head
for f in "$out"/web-$name-*.txt; do [ -e "$f" ] || continue; u=${f/web-/unity-}; echo "== ${f##*/}"; bash tools/layout_diff.sh "$f" "$u" 4 | grep -v "span\[1\]\|h1\[0\]\|#status" | head -25; done
python3 - "$out" "$name" <<'PY'
import glob, os, sys
from PIL import Image
out, name = sys.argv[1], sys.argv[2]
webs = sorted(glob.glob(f"{out}/web-{name}-*.png"))
if webs:
    rows = []
    for w in webs:
        u = w.replace("/web-", "/unity-")
        pair = [Image.open(p).convert("RGB").resize((800, 500)) if os.path.exists(p) else Image.new("RGB", (800, 500)) for p in (w, u)]
        rows.append(pair)
    sheet = Image.new("RGB", (1600, 500 * len(rows)))
    for i, (a, b) in enumerate(rows): sheet.paste(a, (0, 500 * i)); sheet.paste(b, (800, 500 * i))
    sheet.save(f"{out}/{name}.png"); print("sheet", f"{out}/{name}.png", len(rows), "rows")
PY
