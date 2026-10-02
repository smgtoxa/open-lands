#!/bin/bash
# The parity suite: the C# engine against the web engine on fixed scenarios (see tools/diff.sh).
#   bash tools/parity.sh           -> one line per scenario, exit 1 if any differs
set -u
cd "$(dirname "$0")/.."
SAVE="${LANDS:-$HOME/lands}"/private/parity/real-saves.json
walk="new:0,wait:2,key:enter,wait:2"
for m in up turnr up up turnl up up up turnl up up turnr up up up turnr turnr up up up turnl up up up up turnr up up turnl up up; do walk="$walk,key:$m,wait:0.5"; done
sweep="load:$SAVE,wait:2"
for L in $(seq 1 29); do sweep="$sweep,tp:$L,wait:4,idle,mon,state"; done
forest="new:0,wait:2,key:enter,wait:2,tp:2,wait:3,mon,state"
for m in up up turnr up up up turnl up up up up turnr up up turnl up up up turnr turnr up up up up turnl up up up up up turnr up up up turnl up up; do forest="$forest,key:$m,wait:0.6,call:quickAttack"; done
declare -A S=(
  [opening]="new:0,wait:2,mon,state,key:enter,wait:3,mon,state,key:up,wait:1,key:up,wait:1,state"
  [walk]="$walk,mon,state"
  [king]="new:0,wait:2,key:enter,wait:1,goto:273,wait:3,idle,wait:3,idle,wait:3,idle,wait:5,state,mon"
  [michael]="new:1,wait:3,idle,wait:2,cflags,state,goto:273,wait:5,idle,state"
  [kieran]="new:2,wait:3,idle,wait:2,cflags,state,goto:273,wait:5,idle,state"
  [conrad]="new:3,wait:3,idle,wait:2,cflags,state,goto:273,wait:5,idle,state"
  [save]="load:$SAVE,wait:3,cflags,mon,idle,wait:5,state"
  [sweep]="$sweep"
  [combat]="load:$SAVE,wait:2,mon,state,cflags,call:quickAttack,wait:1,call:quickAttack,wait:1,call:quickAttack,wait:2,cflags,mon,call:quickCastSpell/0/0/0,wait:2,cflags,mon,wait:5,cflags,state"
  [items]="load:$SAVE,wait:2,cflags,state,call:uiUseInventorySlot/3/0,wait:2,call:uiUseInventorySlot/0/1,wait:2,call:uiUseInventorySlot/5/2,wait:2,cflags,state"
  [forest]="$forest,wait:3,cflags,mon,state"
  [camp]="load:$SAVE,wait:2,tp:3,wait:3,cflags,call:uiEnterCamp,wait:2.5,state,call:uiInCamp,call:uiCampStartHealing,wait:6,cflags,call:uiCampRested,call:uiCampSleep/8,cflags,call:uiCampRested,call:uiLeaveCamp,wait:2,state,cflags"
)
fail=0
for name in opening walk king michael kieran conrad save sweep combat items forest camp; do
  r=$(bash tools/diff.sh "${S[$name]}" 2>&1 | grep -E "^SAME|^@@" | head -1)
  echo "$name: ${r:-ERROR}"
  [[ "$r" == SAME* ]] || fail=1
done
exit $fail
