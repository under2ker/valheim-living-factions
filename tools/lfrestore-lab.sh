#!/usr/bin/env bash
# Puts the lab world and its faction state back from a copy made with: tools/lfrestore-lab.sh --save <dir>
set -eu
MODE="${1:?--save or --load}"; DIR="${2:?copy dir}"
SAVES="$USERPROFILE/AppData/LocalLow/IronGate/Valheim/worlds_local"
DATA="${VALHEIM_INSTALL:-/c/Program Files (x86)/Steam/steamapps/common/Valheim}/BepInEx/config/LivingFactions"
if [ "$MODE" = --save ]; then
  rm -rf "$DIR"; mkdir -p "$DIR"
  cp -r "$SAVES/LFLab" "$DIR/"; cp "$DATA"/LFLab_*.json "$DIR/"; mkdir -p "$DIR/history"; cp -r "$DATA"/history/LFLab_* "$DIR/history/"
else
  rm -rf "$SAVES/LFLab" "$DATA"/history/LFLab_*; cp -r "$DIR/LFLab" "$SAVES/"; cp "$DIR"/LFLab_*.json "$DATA/"; mkdir -p "$DATA/history"; cp -r "$DIR"/history/LFLab_* "$DATA/history/"
fi
