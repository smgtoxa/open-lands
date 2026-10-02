#!/bin/bash
# The release: Windows and Linux players without anything that may not be given away, each with the licences and
# credits, zipped into a folder of the user's Downloads.
#   bash release.sh [OUT_DIR]
set -eu
cd "$(dirname "$0")"
LANDS="${LANDS:-$HOME/lands}"   # the web project
OUT="${1:-${OUT:-$HOME/OpenLands-release}}"
bash build-engine.sh
mkdir -p Assets/StreamingAssets/web/src && cp -ru "$LANDS"/src/assets Assets/StreamingAssets/web/src/
UNITY="/mnt/c/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe"
proj="$(wslpath -w "$PWD")"
rm -rf Build/Release
set +e
"$UNITY" -batchmode -nographics -quit -projectPath "$proj" -executeMethod Build.Release -logFile "$proj\\Logs\\release.log"
code=$?
set -e
grep -E "error CS|BUILD RESULT|Exception" Logs/release.log | sort -u | head -20
[ $code -eq 0 ] || exit $code
[ -d Assets/Resources/Guide ] || { echo "the guide's pictures were not put back"; exit 1; }
for plat in Windows Linux; do
  d=Build/Release/$plat
  data=$(ls -d $d/OpenLands_Data)
  # the web project's old camp pictures: their origin is not recorded, so they are not given away
  rm -rf "$data/StreamingAssets/web/src/assets/camp" "$data/StreamingAssets/web/src/assets/camp.png" "$data/StreamingAssets/web/src/assets/camp-plain.png"
  find "$data/StreamingAssets" -name "*.meta" -delete
  rm -rf "$d"/*_BurstDebugInformation_DoNotShip "$d"/*_BackUpThisFolder_ButDontShipItInYourBuild
  cp release/README.txt release/CREDITS.txt "$d"/
  cp "$LANDS"/COPYING "$d"/LICENSE.txt
  cp "$LANDS"/NOTICE "$d"/NOTICE.txt
  mkdir -p "$d"/licenses && cp Assets/Resources/Fonts/OFL*.txt "$d"/licenses/
done
mkdir -p "$OUT"
( cd Build/Release && rm -f "$OUT"/OpenLands-*.zip "$OUT"/OpenLands-*.tar.gz "$OUT"/OpenLands-Source.zip
  python3 -c "import shutil; shutil.make_archive('$OUT/OpenLands-Windows', 'zip', 'Windows')"
  tar -czf "$OUT/OpenLands-Linux.tar.gz" --transform 's,^\./,OpenLands/,' -C Linux . )
cp release/README.txt "$OUT"/README.txt
ls -la "$OUT"
