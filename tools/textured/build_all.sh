#!/bin/bash
# The textured world packs for every level: the base export of the original art, then the material pass.
#   bash tools/textured/build_all.sh [levels...]     -> GameData/textured/level<N>
set -u
cd "$(dirname "$0")/../.."
export PATH="${LANDS:-$HOME/lands}"/tools/node24/bin:$PATH
for n in ${@:-$(seq 1 29)}; do
  [ -f GameData/textured/base/level$n/manifest.json ] || timeout 300 node tools/textured/export_hd_sources.mjs $n 4 > /dev/null 2>&1 || echo "level $n: sources export failed"
  [ -f GameData/textured/base/level$n/walls.json ] || timeout 300 node tools/textured/export_hd_walls.mjs $n 4 > /dev/null 2>&1 || echo "level $n: walls export failed"
  python3 tools/textured/texture.py $n 2>&1 | grep -v "Deprecat\|getdata"
done
echo ALLDONE
