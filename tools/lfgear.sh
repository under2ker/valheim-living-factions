#!/usr/bin/env bash
# Equipment photo session: wears each item in the game and photographs it from every angle its
# slot has (weapons, shields and bows also slung on the back).
# Usage:  tools/lfgear.sh LF_WolfFangAxe SwordIron ShieldBanded Bow HelmetBronze CapeDeerHide
#         tools/lfgear.sh                 (the standard set from tools/scenarios/gear.txt)
# Pictures are copied to docs/gear/, one contact sheet per item when ffmpeg is installed, together
# with report.txt (where each model sits relative to its joint), points_character.txt (the joints)
# and points_items.tsv (every wearable item of the game, the reference for new models).
# A hand-held item whose surface is further than 5 cm from the palm is reported as FAIL.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
GAME="${VALHEIM_INSTALL:-/c/Program Files (x86)/Steam/steamapps/common/Valheim}"
SHOTS="$GAME/BepInEx/config/LivingFactions/shots"
OUT="$HERE/../docs/gear"

if [ $# -eq 0 ]; then
  SCENARIO="$HERE/scenarios/gear.txt"
else
  SCENARIO="$(mktemp)"
  { printf 'god\nenv 0.4 Clear\nunequip all\n'; for item in "$@"; do printf 'gearshots %s\n' "$item"; done; printf 'look off\npoints\npoints all\nquit\n'; } > "$SCENARIO"
fi

rm -f "$SHOTS"/gear_*.png
bash "$HERE/lftest.sh" "$SCENARIO" | grep -E "^> gearshots|^(PASS|FAIL)|NOT IN THE HAND|could not|no item|wearable items|DONE|NO REPORT|already running|total"
[ $# -eq 0 ] || rm -f "$SCENARIO"

mkdir -p "$OUT"
ls "$SHOTS"/gear_*.png >/dev/null 2>&1 || { echo "no pictures were taken"; exit 1; }
cp "$SHOTS"/gear_*.png "$OUT/"
# The numbers behind the pictures: the full report, the character's joints, every wearable item.
cp "$GAME/BepInEx/config/LivingFactions/report.txt" "$OUT/report.txt" 2>/dev/null
cp "$GAME/BepInEx/config/LivingFactions"/points_* "$OUT/" 2>/dev/null

if command -v ffmpeg >/dev/null 2>&1; then
  for item in $(ls "$SHOTS"/gear_*.png | sed -E 's#.*/gear_(.*)_[a-z]+\.png#\1#' | sort -u); do
    files=( "$SHOTS"/gear_"$item"_*.png )
    n=${#files[@]}
    [ "$n" -gt 1 ] || continue
    inputs=(); scale=""; refs=""; layout=""
    for i in $(seq 0 $((n - 1))); do
      inputs+=( -i "${files[$i]}" )
      scale+="[$i:v]scale=960:540[v$i];"
      refs+="[v$i]"
      layout+="$(( (i % 2) * 960 ))_$(( (i / 2) * 540 ))|"
    done
    ffmpeg -y -loglevel error "${inputs[@]}" -filter_complex "${scale}${refs}xstack=inputs=$n:layout=${layout%|}:fill=black" "$OUT/sheet_$item.jpg"
  done
fi
echo "pictures: $(cd "$OUT" && pwd)"
