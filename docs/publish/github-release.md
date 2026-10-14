### ⚔️ Faction Swords

Every faction now has a sword of its own, next to its relic axe.

- **Grey Blade** (Wolf Clan): +25% damage, standing 50, 480 coins.
- **Antlered Blade** (Stag Clan): double durability, standing 30, 300 coins.
- **Black Beak** (Raven Clan): half the weight, standing 40, 460 coins.
- **Obsidian Saber** (Fuling Horde): +25% damage, standing 20, 380 coins.
- **Rune Sword** (Dvergr Hold): +25% damage, standing 40, 520 coins.
- **Rusted Blade** (Draugr Court): double durability, standing 10, 450 coins.
- **Root Club** (Greydwarf Brood): double durability. The Greydwarfs do not trade, so one guard in four leaves it.

![The seven faction swords](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/store/swords.jpg?v=0.6.5)

### 🔄 Rewind and Self-Repair

- **`lf rewind`:** takes the factions back to an earlier day while the world stays as it is. Without a number it lists the days you can go back to; `lf rewind -30` goes thirty days back.
- **`lf advance` keeps a copy first:** a jump too far is one command to undo.
- **Settlements check themselves:** buildings on record but missing from the world (an old backup restored) are put up again and the ground under them levelled. Pieces of buildings the factions had not got to come down.

### 🛠️ Fixes

- **Chronicle for guests:** a guest with other HUD mods saw nothing under the minimap. The panel is now drawn in front of the game's interface and moves to the corner if the minimap is out of reach.
- **Item names:** the mod's axes and swords were named in English in games in other languages until a restart.
- **Traders:** a trader with more than seven offers hid the last ones.
- **Woodcutters:** an error when a tree was cleared for a building mid-chop.

### 🧪 Tested In-Game

1. Bought five swords from five traders and took the root club from a Greydwarf guard.
2. Checked every relic property in a real fight: damage, fury, healing, mending, durability, stamina.
3. Advanced a new village 30 days and rewound 25: the three newer buildings came down.
4. Loaded an old world backup with a newer faction state: the three missing buildings went up again on level ground.
5. Joined a dedicated server as a guest and saw the chronicle under the minimap.

### ⚠️ Important

Everyone needs the same mod version, the server too (any 0.6.x works with any 0.6.x). An older `trade.cfg` gets the sword offers added at its end; the rest of it stays as you made it.

🔗 **Download Ts:** https://thunderstore.io/c/valheim/p/under2ker/LivingFactions/
🔗 **Download Hex:** https://valheim.hexium.gg/mods/under2ker/LivingFactions
📂 **Github:** https://github.com/under2ker/valheim-living-factions
