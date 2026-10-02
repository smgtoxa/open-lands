#!/bin/bash
# UI parity suite: web vs Unity on the main screens (tools/compare.sh per scenario). Prints the number of
# element boxes that differ by more than 4px per screenshot (font-width noise excluded) and the sheets.
#   bash tools/ui_suite.sh [scenario...]     scenarios: start panels trade save
set -u
cd "$(dirname "$0")/.."
OPEN="4:click:.title-button,5:dump:title,7:click:.character-button,8.5:dump:select,9.5:click:.confirm-panel .game-button,12:click:.ui-choice"
declare -A S
S[start]="$OPEN,16:tp:1:272:1,18:shot:hud,18.3:dump:hud"
S[panels]="$OPEN,16:tp:1:272:1,19:click:#menu-open,21:shot:menu,21.3:dump:menu,22:key:Escape,23:click:#journal-open,25:shot:journal,25.3:dump:journal,26:key:Escape,27:click:#settings-open,29:shot:settings,29.3:dump:settings,30:key:Escape,31:key:i,33:shot:inv,33.3:dump:inv"
ch=""; for t in $(seq 22 2 34); do ch="$ch,${t}:click:.ui-choice"; done
S[trade]="$OPEN,16:tp:1:399:1,19:key:ArrowUp$ch,36:key:t,38:shot:trade,38.3:dump:trade"
S[save]="5:dump:saves,6:click:.continue-button,18:shot:late,18.3:dump:late,19:key:i,21:shot:inv,21.3:dump:inv,22:key:Escape,23:click:#game-ui .ui-face,25:shot:char,25.3:dump:char,26:key:Escape,27:key:m,29:shot:map,29.3:dump:map,30:key:Escape,31:key:j,33:shot:journal,33.3:dump:journal"
for name in ${@:-start panels trade save}; do
  store=""; [ "$name" = save ] && store=Shots/test/saves.json
  rm -f Shots/cmp/web-$name-*.txt Shots/cmp/unity-$name-*.txt
  bash tools/compare.sh "$name" "${S[$name]}" "" $store > /dev/null 2>&1
  for f in Shots/cmp/web-$name-*.txt; do
    n=$(bash tools/layout_diff.sh "$f" "${f/web-/unity-}" 4 | grep -v "#facing\|#status\|h1\[0\]\|span\[1\]\|ui-bar-text" | wc -l)
    printf '%-24s %4d boxes differ\n' "${f##*/web-}" "$n"
  done
  grep -E "Exception" Logs/player.log | sort | uniq -c | head -3
done
