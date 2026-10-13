#!/usr/bin/env bash
# Runs several in-game scenarios one after another and prints one summary: for each, its DONE line,
# every FAIL with the output that failed, and whether the game threw away a duplicate terrain
# compiler (which means ground work is being lost). Full reports are kept per scenario.
# Usage:  tools/lfsuite.sh smoke checks features          names in tools/scenarios, without .txt
#         tools/lfsuite.sh terrain-persist terrain-persist   the same one twice = a restart test
#         tools/lfsuite.sh --build smoke                     rebuild and deploy master first
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$HERE/.."
GAME="${VALHEIM_INSTALL:-/c/Program Files (x86)/Steam/steamapps/common/Valheim}"
DATA="$GAME/BepInEx/config/LivingFactions"
OUT="$ROOT/docs/gear/suite"
mkdir -p "$OUT"

if [ "${1:-}" = "--build" ]; then
  shift
  ( cd "$ROOT" && dotnet build src/LivingFactions/LivingFactions.csproj -c Release -nologo -v q 2>&1 | grep -E "rror CS|Ошибок|rror\(s\)" | sort -u )
fi

n=0; bad=0
for name in "$@"; do
  n=$((n + 1))
  file="$HERE/scenarios/$name.txt"
  [ -f "$file" ] || { echo "[$n] $name: no such scenario"; bad=$((bad + 1)); continue; }
  bash "$HERE/lftest.sh" "$file" > "$OUT/$n-$name.out" 2>&1
  cp "$DATA/report.txt" "$OUT/$n-$name.report.txt" 2>/dev/null
  done_line="$(grep "^=== DONE" "$OUT/$n-$name.out" | tail -1)"
  dupes="$(grep -c "Removed duplicate terrain compiler" "$GAME/BepInEx/LogOutput.log" 2>/dev/null)"; dupes="${dupes:-0}"
  echo "[$n] $name: ${done_line:-NO REPORT ($(tail -1 "$OUT/$n-$name.out"))}  duplicate-compilers $dupes"
  # A FAIL line and what the command actually printed, up to the next command.
  awk '/^FAIL/{f=1} /^> /{f=0} f' "$OUT/$n-$name.out" | cut -c1-220 | head -40
  case "$done_line" in *"fail 0 log-errors 0"*) [ "$dupes" = 0 ] || bad=$((bad + 1)) ;; *) bad=$((bad + 1)) ;; esac
done
echo "suite: $n run, $bad with problems; reports in docs/gear/suite/, pictures in $DATA/shots/"
[ "$bad" = 0 ]
