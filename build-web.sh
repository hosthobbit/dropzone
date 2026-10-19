#!/usr/bin/env bash
# Regenerates every asset and the scene, then builds the browser version to Build/WebGL.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
mkdir -p Logs
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.6.3f1/Editor/Unity}"
exec "$UNITY" -batchmode -nographics -buildTarget WebGL -projectPath "$PWD" -executeMethod DropZone.EditorTools.DropZoneBuilder.GenerateAndBuildWebGL -quit -logFile "$PWD/Logs/build-web.log"
