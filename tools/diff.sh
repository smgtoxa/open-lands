#!/bin/bash
# Runs the same step script on the web engine (tools/trace_js.mjs, the reference) and on the C#
# engine (Harness/), with script opcodes and log lines traced, and diffs the two.
#   bash tools/diff.sh "new:0,wait:2,state,key:enter,wait:3,mon,state"
# Output: Shots/diff/{js,cs}.txt and the diff. Exit 0 when they agree.
set -u
cd "$(dirname "$0")/.."
steps="$1"
out=Shots/diff; mkdir -p "$out"
export PATH="${LANDS:-$HOME/lands}/tools/node24/bin:$HOME/.dotnet:$PATH"
dotnet build Trace -v quiet --nologo > "$out/build.txt" 2>&1 || { cat "$out/build.txt"; exit 2; }
norm() {
  tr -d '\r' | sed -E 's/^( *RND#[0-9]+ [0-9.]+) .*/\1/' |
  # Same text either side: empty JS args are 0, spacing is collapsed, item-table boot noise dropped.
  sed -E 's/\(([^)]*)\)/(\1)/; :a; s/\(([^)]*),,/(\1,0,/; ta; s/,\)/,0)/; s/\(,/(0,/' |
    sed -E 's/  +/ /g; s/ +$//' | grep -v 'op setItemProperty\|op allocItemPropertiesBuffer\|^>> wait'
}
LOL_TRACE_TIMERS=1 LOL_TRACE_DICE=1 LOL_TRACE_OPS=1 LOL_TRACE_UI=1 timeout 600 node tools/trace_js.mjs "$steps" 2>&1 | norm > "$out/js.txt"
LOL_TRACE_TIMERS=1 LOL_TRACE_DICE=1 LOL_TRACE_OPS=1 LOL_TRACE_UI=1 timeout 600 dotnet Trace/bin/Debug/net8.0/Trace.dll "$steps" 2>&1 | norm > "$out/cs.txt"
if diff -u "$out/js.txt" "$out/cs.txt" > "$out/diff.txt"; then echo "SAME ($(wc -l < "$out/js.txt") lines)"; exit 0; fi
head -${DIFF_LINES:-80} "$out/diff.txt"; exit 1
