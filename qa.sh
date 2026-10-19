#!/usr/bin/env bash
# GPU-free scripted QA: runs the autoshot test on a virtual X screen (Xvfb)
# with Mesa's CPU Vulkan driver (lavapipe). The NVIDIA driver is hidden from
# the game, so it cannot use VRAM or touch Hyprland - VRAM exhaustion froze
# the desktop twice on 2026-10-16.
# Usage: [MODE=input|play|shots] [QUALITY=0|1|2] ./qa.sh <screenshot-dir> [width height]
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
OUT="${1:?usage: ./qa.sh <screenshot-dir> [width height]}"
W="${2:-1280}"; H="${3:-720}"
SW="${SWRENDER:-$HOME/.local/opt/swrender}"
[[ -x "$SW/usr/bin/Xvfb" && -f "$SW/lvp_icd.json" ]] || { echo "Missing $SW (Xvfb + lavapipe)" >&2; exit 1; }
[[ -x Build/Linux/DropZone.x86_64 ]] || { echo "Build missing - run ./build.sh" >&2; exit 1; }
mkdir -p "$OUT" Logs

disp=99; while [[ -e /tmp/.X11-unix/X$disp || -e /tmp/.X$disp-lock ]]; do disp=$((disp+1)); done
"$SW/usr/bin/Xvfb" ":$disp" -screen 0 "${W}x${H}x24" -nolisten tcp >Logs/xvfb.log 2>&1 &
xpid=$!
trap 'kill $xpid 2>/dev/null || true' EXIT
for _ in $(seq 50); do [[ -e /tmp/.X11-unix/X$disp ]] && break; sleep 0.1; done

cores=$(nproc); quota=$(( cores > 2 ? (cores - 2) * 100 : 100 ))
# MODE: (default) screenshots + full matches · play (matches, no screenshots) · input (virtual devices)
case "${MODE:-}" in
  play) mode=(--autoplay) ;;
  shots) mode=(--autoshot "$(realpath "$OUT")" --shotsonly) ;;
  input) mode=(--inputtest) ;;
  *) mode=(--autoshot "$(realpath "$OUT")") ;;
esac
[[ -n "${QUALITY:-}" ]] && mode+=(--quality "$QUALITY")
set +e
env -u WAYLAND_DISPLAY -u HYPRLAND_INSTANCE_SIGNATURE \
  PULSE_SERVER=unix:/nonexistent PIPEWIRE_REMOTE=/nonexistent SDL_AUDIODRIVER=dummy \
  DISPLAY=":$disp" SDL_VIDEODRIVER=x11 \
  VK_DRIVER_FILES="$SW/lvp_icd.json" VK_ICD_FILENAMES="$SW/lvp_icd.json" \
  __GLX_VENDOR_LIBRARY_NAME=mesa __EGL_VENDOR_LIBRARY_FILENAMES=/usr/share/glvnd/egl_vendor.d/50_mesa.json \
  LP_NUM_THREADS="$(( cores > 2 ? cores - 2 : 1 ))" \
  timeout -k 10 "${QA_TIMEOUT:-2400}" \
  systemd-run --user --scope --quiet -p MemoryMax=8G -p CPUQuota="${quota}%" \
  nice -n 10 ./Build/Linux/DropZone.x86_64 -force-glcore \
    -screen-fullscreen 0 -screen-width "$W" -screen-height "$H" \
    -logFile "$PWD/Logs/qa-player.log" "${mode[@]}"
rc=$?
set -e
grep -E "DZ_|Exception|NullReference|GfxDevice|llvmpipe|NVIDIA" Logs/qa-player.log | head -30
ls "$OUT"
exit $rc
