#!/usr/bin/env bash
# One-shot in-game test: launches Valheim in a window, runs a scenario inside the game and prints
# the report. Usage:  tools/lftest.sh tools/scenarios/smoke.txt [world]
# The scenario should end with `quit`. Dev tools are switched on for the run and off afterwards.
set -u
SCENARIO="${1:?scenario file}"
WORLD="${2:-LFLab}"
GAME="${VALHEIM_INSTALL:-/c/Program Files (x86)/Steam/steamapps/common/Valheim}"
CFG="$GAME/BepInEx/config"
DATA="$CFG/LivingFactions"
START=$(date +%s)

cleanup() {
  sed -i 's/^DevTools = true/DevTools = false/' "$CFG/alexandr.livingfactions.cfg"
  rm -f "$DATA/autostart.txt" "$DATA/scenario.txt" "$DATA/cmd.txt"
}
trap cleanup EXIT

if tasklist //FI "IMAGENAME eq valheim.exe" //NH 2>/dev/null | grep -qi valheim.exe; then
  echo "Valheim is already running; close it first."; exit 2
fi

mkdir -p "$DATA"
sed -i 's/^DevTools = false/DevTools = true/' "$CFG/alexandr.livingfactions.cfg"
printf '%s' "$WORLD" > "$DATA/autostart.txt"
rm -f "$DATA/report.txt" "$DATA/out.txt"
cp "$SCENARIO" "$DATA/scenario.txt"

( cd "$GAME" && ./valheim.exe -console -screen-fullscreen 0 -screen-width 1280 -screen-height 720 >/dev/null 2>&1 & )

# Wait for the report to finish; give up after 10 minutes.
for i in $(seq 1 480); do
  sleep 1
  if [ -f "$DATA/report.txt" ] && grep -q "^=== DONE" "$DATA/report.txt"; then break; fi
done

if [ -f "$DATA/report.txt" ]; then cat "$DATA/report.txt"; else echo "NO REPORT: the scenario never started"; fi

# The scenario normally quits the game itself; make sure it is gone either way.
for i in $(seq 1 20); do
  tasklist //FI "IMAGENAME eq valheim.exe" //NH 2>/dev/null | grep -qi valheim.exe || break
  [ "$i" = 3 ] && printf 'quit\n' > "$DATA/cmd.txt"
  sleep 1
done
echo "total $(( $(date +%s) - START )) s"
grep -q "^=== DONE pass [0-9]* fail 0 " "$DATA/report.txt" 2>/dev/null
