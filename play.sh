#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ -x Build/Linux/DropZone.x86_64 ]] || { echo "Build missing - run ./build.sh" >&2; exit 1; }
exec ./Build/Linux/DropZone.x86_64 "$@"
