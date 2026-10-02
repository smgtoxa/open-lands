#!/bin/bash
# Builds the engine DLL, then the Windows player, from WSL. The Unity editor must not have this
# project open (batch mode refuses a locked project).
#   bash build.sh            -> Build/Windows/OpenLands.exe
set -eu
cd "$(dirname "$0")"
bash build-engine.sh
# the page's own pictures (camp scene, errand / potion icons), served from StreamingAssets like the web root
mkdir -p Assets/StreamingAssets/web/src && cp -ru "${LANDS:-$HOME/lands}"/src/assets Assets/StreamingAssets/web/src/
UNITY="/mnt/c/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe"
proj="$(wslpath -w "$PWD")"
set +e
"$UNITY" -batchmode -nographics -quit -projectPath "$proj" -executeMethod Build.Windows -logFile "$proj\\Logs\\build.log"
code=$?
set -e
grep -E "error CS|BUILD RESULT|Exception|Shader error" Logs/build.log | sort -u | head -40
# a shader that fails to compile does not fail the player build: fail the script on it
if grep -q "Shader error" Logs/build.log; then echo "SHADER ERRORS (see Logs/build.log)"; exit 1; fi
exit $code
