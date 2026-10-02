#!/bin/bash
# Builds the engine (Engine/, the 1:1 port of the web engine) as a netstandard2.1 DLL and drops it
# with its dependencies into Assets/Plugins/Lol. Run from WSL:  bash build-engine.sh
set -eu
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH"
out=Assets/Plugins/Lol
dotnet publish Engine/Engine.csproj -c Release -o /tmp/lol-engine-publish --nologo -v quiet
mkdir -p "$out"
cp /tmp/lol-engine-publish/*.dll /tmp/lol-engine-publish/*.pdb "$out"/
# Unity already ships these; duplicate copies clash with its own.
rm -f "$out"/{System.Buffers,System.Memory,System.Numerics.Vectors,System.Runtime.CompilerServices.Unsafe}.dll
echo "engine -> $out"
