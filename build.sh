#!/usr/bin/env bash
# Regenerates every asset and the scene, then builds the Linux player to Build/Linux.
# -nographics: no GPU is opened, so building can't use VRAM.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
mkdir -p Logs
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.6.3f1/Editor/Unity}"
exec "$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod DropZone.EditorTools.DropZoneBuilder.GenerateAndBuildLinux -quit -logFile "$PWD/Logs/build.log"
