#!/usr/bin/env bash
# Multiplayer test on one machine: a dedicated server with the mod and a copy of the lab world,
# and the game joining it as a guest and running a scenario. Prints the guest's report and the
# server's log lines about the mod. Usage:  tools/lfmp.sh tools/scenarios/mp.txt
# Needs "Valheim Dedicated Server" from Steam (Library, Tools). Nothing of the player's own saves
# is touched: the server keeps its world in dist/server-data.
set -u
SCENARIO="${1:?scenario file}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
GAME="${VALHEIM_INSTALL:-/c/Program Files (x86)/Steam/steamapps/common/Valheim}"
SERVER="${VALHEIM_SERVER:-/c/Program Files (x86)/Steam/steamapps/common/Valheim dedicated server}"
SAVES="$APPDATA/../LocalLow/IronGate/Valheim"
DATA="$GAME/BepInEx/config/LivingFactions"
CFG="$GAME/BepInEx/config/alexandr.livingfactions.cfg"
SDIR="$ROOT/dist/server-data"
# A password for a server that lives on this machine for the length of the test.
PASS="lftest-local"
START=$(date +%s)

[ -f "$SERVER/valheim_server.exe" ] || { echo "no dedicated server at $SERVER"; exit 2; }
if tasklist //NH 2>/dev/null | grep -qi "valheim"; then echo "Valheim or its server is already running; close it first."; exit 2; fi

# The loader and the mod, as the game has them.
# With KEEP_WORLD=1 the server keeps its own faction state from the run before, next to its world.
KEPT="$ROOT/dist/server-state"
rm -rf "$KEPT"; mkdir -p "$KEPT"
[ "${KEEP_WORLD:-0}" = 1 ] && cp "$SERVER/BepInEx/config/LivingFactions/"*.json* "$KEPT/" 2>/dev/null
cp -r "$GAME/BepInEx" "$GAME/doorstop_libs" "$SERVER/" 2>/dev/null
[ "${KEEP_WORLD:-0}" = 1 ] && cp "$KEPT/"* "$SERVER/BepInEx/config/LivingFactions/" 2>/dev/null
cp "$GAME/winhttp.dll" "$GAME/doorstop_config.ini" "$SERVER/"
# The server listens to the test guest's `server <command>` (dev tools on, on this machine only).
sed -i 's/^DevTools = false/DevTools = true/' "$SERVER/BepInEx/config/alexandr.livingfactions.cfg"
rm -f "$SERVER/BepInEx/LogOutput.log" "$SERVER/BepInEx/config/LivingFactions/"*.txt
# A fresh copy of the lab world every run, so runs do not build on each other.
if [ "${KEEP_WORLD:-0}" != 1 ]; then
  rm -rf "$SDIR"; mkdir -p "$SDIR/worlds_local"
  cp -r "$SAVES/worlds_local/LFLab"* "$SDIR/worlds_local/"
fi

cleanup() {
  sed -i 's/^DevTools = true/DevTools = false/' "$CFG"
  rm -f "$DATA/autostart.txt" "$DATA/scenario.txt" "$DATA/cmd.txt"
  taskkill //IM valheim_server.exe //F >/dev/null 2>&1
}
trap cleanup EXIT

( cd "$SERVER" && SteamAppId=892970 ./valheim_server.exe -nographics -batchmode -name "LFTest" -port 2456 -world "LFLab" -password "$PASS" -public 0 -savedir "$(cygpath -w "$SDIR")" > "$ROOT/dist/server.out" 2>&1 & )
echo "server starting..."
for i in $(seq 1 180); do
  sleep 1
  grep -q "Faction state\|Loaded faction state\|Seeded" "$SERVER/BepInEx/LogOutput.log" 2>/dev/null && break
done
grep -q "Loaded faction state\|Seeded" "$SERVER/BepInEx/LogOutput.log" 2>/dev/null || { echo "SERVER DID NOT LOAD THE MOD STATE"; tail -5 "$SERVER/BepInEx/LogOutput.log" 2>/dev/null; tail -5 "$ROOT/dist/server.out"; exit 3; }
echo "server up after $(( $(date +%s) - START )) s"

mkdir -p "$DATA"
sed -i 's/^DevTools = false/DevTools = true/' "$CFG"
printf 'join 127.0.0.1:2456' > "$DATA/autostart.txt"
rm -f "$DATA/report.txt" "$DATA/out.txt"
cp "$SCENARIO" "$DATA/scenario.txt"
( cd "$GAME" && ./valheim.exe -console -screen-fullscreen 0 -screen-width 1280 -screen-height 720 +connect 127.0.0.1:2456 +password "$PASS" >/dev/null 2>&1 & )

for i in $(seq 1 480); do
  sleep 1
  if [ -f "$DATA/report.txt" ] && grep -q "^=== DONE" "$DATA/report.txt"; then break; fi
done
if [ -f "$DATA/report.txt" ]; then cat "$DATA/report.txt"; else echo "NO REPORT: the guest never ran the scenario"; grep -i "autostart\|connect\|password\|error" "$GAME/BepInEx/LogOutput.log" | tail -15; fi
for i in $(seq 1 20); do
  tasklist //FI "IMAGENAME eq valheim.exe" //NH 2>/dev/null | grep -qi valheim.exe || break
  [ "$i" = 3 ] && printf 'quit\n' > "$DATA/cmd.txt"
  sleep 1
done
taskkill //IM valheim.exe //F >/dev/null 2>&1
echo "--- server log (mod, errors) ---"
grep -a "Living Factions\]\|Exception" "$SERVER/BepInEx/LogOutput.log" | grep -a -v "Created NPC\|LF_\|loaded$" | cut -c1-260 | tail -25
echo "total $(( $(date +%s) - START )) s"
