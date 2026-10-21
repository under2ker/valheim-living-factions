#!/usr/bin/env bash
# Films a scenario: starts the game through lftest.sh and records its window (picture and the game's
# own sound) from the scenario's `mark rec` line for the given number of seconds.
# Usage:  LF_WIDTH=1920 LF_HEIGHT=1080 tools/lfrecord.sh tools/scenarios/film-a.txt <seconds> <out base>
set -u
SCENARIO="${1:?scenario}"; SECONDS_REC="${2:?seconds}"; OUT="${3:?output base path}"
HERE="$(cd "$(dirname "$0")" && pwd)"
GAME="${VALHEIM_INSTALL:-/c/Program Files (x86)/Steam/steamapps/common/Valheim}"
REPORT="$GAME/BepInEx/config/LivingFactions/report.txt"
rm -f "$REPORT"
bash "$HERE/lftest.sh" "$SCENARIO" > "$OUT.report.txt" 2>&1 &
TEST=$!
for i in $(seq 1 400); do
  sleep 0.5
  grep -q "^mark rec" "$REPORT" 2>/dev/null && break
done
grep -q "^mark rec" "$REPORT" 2>/dev/null || { echo "the scenario never reached 'mark rec'"; wait $TEST; exit 1; }
um win record --exe valheim.exe --out "$(cygpath -w "$OUT")" --seconds "$SECONDS_REC" --fps 60 > "$OUT.rec.log" 2>&1
wait $TEST
grep -E "^mark|FAIL|DONE" "$OUT.report.txt"
tail -3 "$OUT.rec.log"
