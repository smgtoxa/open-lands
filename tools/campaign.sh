#!/bin/bash
# The campaign, played by the Unity build's player bot (Host/PlayerBot.cs) one segment at a time:
# tools/campaign/NN-*.txt, each continuing from the quicksave the one before it made.
#   bash tools/campaign.sh [NN ...]      (default: all, in order; a segment NN > 01 starts with Continue)
set -u
cd "$(dirname "$0")/.."
W="$(wslpath -w "$PWD")"
mkdir -p Shots/bot
store=Shots/bot/campaign.json
for f in tools/campaign/*.txt; do
  n=$(basename "$f" | cut -d- -f1)
  if [ $# -gt 0 ] && [[ ! " $* " =~ " $n " ]]; then continue; fi
  play="Shots/bot/$n.play.txt"
  if [ "$n" = "01" ]; then printf '{"lol.settings":"{\\"intro\\":false}"}' > "$store"; cp "$f" "$play"; else { printf 'click:.continue-button\nwait:5\n'; cat "$f"; } > "$play"; fi
  flock /tmp/lol-unity.lock timeout 5400 ./Build/Windows/LandsOfLore.exe -screen-width 1600 -screen-height 1000 -screen-fullscreen 0 --nospeech \
    --storage "$W\\Shots\\bot\\campaign.json" --play "$W\\${play//\//\\}" --quit-when-done -logFile "$W\\Logs\\bot-$n.log" > /dev/null
  fails=$(grep -a "^BOT FAIL" Logs/bot-$n.log | wc -l); exc=$(grep -a "Exception" Logs/bot-$n.log | sort -u | head -3)
  printf '%s %s %s\n' "$n" "$(grep -a '^BOT DONE' Logs/bot-$n.log || echo 'did not finish')" "$([ -n "$exc" ] && echo "EXCEPTIONS")"
  grep -a "^BOT FAIL" Logs/bot-$n.log | head -10
  [ -n "$exc" ] && echo "$exc"
done
