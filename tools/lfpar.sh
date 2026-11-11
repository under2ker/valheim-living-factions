#!/usr/bin/env bash
# Runs scenarios side by side in N test copies of the game (tools/lfinst.sh), each starting from the same
# lab copy (tools/lfrestore-lab.sh --save <lab>). Reports go to <out>/<scenario>.txt.
#   tools/lfpar.sh <copies> <lab> <out> scenario [scenario...]      (names from tools/scenarios, no .txt)
# 32 GB of memory holds about four copies; more than that and the timed scenarios start to fail.
set -u
N="${1:?copies}"; LAB="${2:?lab copy}"; OUT="${3:?report dir}"; shift 3
HERE="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$OUT"
QUEUE="$OUT/.queue"; LOCK="$OUT/.lock"
printf '%s\n' "$@" > "$QUEUE"
take() {  # pops the next scenario name from the queue (mkdir is atomic: a lock)
  while ! mkdir "$LOCK" 2>/dev/null; do sleep 0.2; done
  local next; next="$(head -n1 "$QUEUE")"; tail -n +2 "$QUEUE" > "$QUEUE.t"; mv "$QUEUE.t" "$QUEUE"
  rmdir "$LOCK"; printf '%s' "$next"
}
worker() {
  local i="$1" sc
  bash "$HERE/lfinst.sh" "$i" >/dev/null
  while sc="$(take)"; [ -n "$sc" ]; do
    LF_INST="$i" bash "$HERE/lfrestore-lab.sh" --load "$LAB"
    echo "[copy $i] $sc start $(date +%T)"
    LF_INST="$i" LF_TIMEOUT="${LF_TIMEOUT:-900}" bash "$HERE/lftest.sh" "$HERE/scenarios/$sc.txt" > "$OUT/$sc.txt" 2>&1
    echo "[copy $i] $sc done $(date +%T): $(grep -a -m1 '^=== DONE' "$OUT/$sc.txt" || echo 'NO DONE LINE')"
  done
}
for i in $(seq 1 "$N"); do worker "$i" & sleep 15; done
wait
echo "ALL DONE"
