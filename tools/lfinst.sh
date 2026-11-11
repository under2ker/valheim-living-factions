#!/usr/bin/env bash
# Makes (or refreshes) test copy N of the game for running scenarios side by side:
#   tools/lfinst.sh N          -> $LF_INST_ROOT/N (default E:/lf-inst/N)
# The big read-only folders (valheim_Data, MonoBleedingEdge, D3D12) are junctions to the real game;
# the loader, BepInEx with its config and the mod are copies, and the copy keeps its own saves
# (started with -savedir), so copies never share a world, a faction state or a report.
set -eu
N="${1:?instance number}"
GAME="${VALHEIM_INSTALL:-/c/Program Files (x86)/Steam/steamapps/common/Valheim}"
ROOT="${LF_INST_ROOT:-/e/lf-inst}"
DIR="$ROOT/$N"
SAVES="$USERPROFILE/AppData/LocalLow/IronGate/Valheim"
mkdir -p "$DIR"
for d in valheim_Data MonoBleedingEdge D3D12; do
  if [ ! -e "$DIR/$d" ]; then
    cmd //c mklink //J "$(cygpath -w "$DIR/$d")" "$(cygpath -w "$GAME/$d")" >/dev/null
  fi
done
for f in valheim.exe UnityPlayer.dll UnityCrashHandler64.exe doorstop_config.ini winhttp.dll steam_appid.txt; do
  cp -f "$GAME/$f" "$DIR/"
done
cp -rf "$GAME/doorstop_libs" "$DIR/"
# BepInEx: everything but the logs and the faction data of the real game
mkdir -p "$DIR/BepInEx"
for d in core plugins patchers config; do
  [ -d "$GAME/BepInEx/$d" ] && { rm -rf "$DIR/BepInEx/$d"; cp -r "$GAME/BepInEx/$d" "$DIR/BepInEx/"; }
done
rm -rf "$DIR/BepInEx/config/LivingFactions"
mkdir -p "$DIR/BepInEx/config/LivingFactions" "$DIR/saves"
# LF_DLL: a freshly built mod instead of the one the game has (tools/lfpar.sh passes bin/Release)
[ -n "${LF_DLL:-}" ] && cp -f "$LF_DLL" "$DIR/BepInEx/plugins/LivingFactions/"
# the test character
rm -rf "$DIR/saves/characters_local"; cp -r "$SAVES/characters_local" "$DIR/saves/"
echo "$DIR"
