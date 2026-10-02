#!/bin/bash
# Builds LolCore (the engine) as a netstandard2.1 DLL and drops it with its dependencies into
# Assets/Plugins/LolCore. Run from WSL:  bash build-core.sh
set -eu
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH"
out=Assets/Plugins/LolCore
dotnet publish LolCore/LolCore.csproj -c Release -o /tmp/lolcore-publish --nologo -v quiet
mkdir -p "$out"
cp /tmp/lolcore-publish/*.dll /tmp/lolcore-publish/*.pdb "$out"/
# Unity already ships these; duplicate copies clash with its own.
rm -f "$out"/{System.Buffers,System.Memory,System.Numerics.Vectors,System.Runtime.CompilerServices.Unsafe}.dll
echo "LolCore -> $out"
