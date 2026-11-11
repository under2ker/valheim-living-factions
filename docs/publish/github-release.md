### 🔧 Fix Release

- **Wars between every side:** a war or a campaign could be fought only when the attacker's faction
  id was lower than the defender's. The other half of the raids and assaults ended without a fight,
  so the monster factions could take nothing by war and their campaigns always ended repelled. Both
  sides now ask for the shared relation, and a war is fought the same way whichever side began it.
- **Dialogs no longer change history:** a relation asked for by a dialogue or the forecast drew from
  the shared random stream. Opening a menu could change what the factions did next. The small
  per-pair difference now comes from the two faction ids.
- **The way out is always there:** a dialogue could show ten buttons and cut the Leave button when a
  friendly faction offered every button it had. Eleven rows fit, and Leave is kept.
- **Hired fighters and tamed animals by their owner:** a hired fighter now remembers the player whose
  coins pay it, and a tamed animal fights by the standing of the player it belongs to. Both are right
  on a dedicated server, where there is no local player to take for the owner.
- **Bad guest messages are ignored:** the host checks the count, ids, amounts and names in what a
  guest sends and ignores what cannot be true, with a warning in the log, instead of spoiling the
  factions or throwing errors.

### 🧪 Checked

The core harness checks the new war path in both id orders and the relation determinism. In-game
checks from 0.7.0 still stand; the new paths were not re-run in a live game before this release.

### ⚠️ Important

Everyone needs version 0.8.x, the server too: 0.7.x and 0.8.x do not play together. Worlds and
faction state of 0.7.0 carry over. Back up your world before updating.

🔗 **Download Ts:** https://thunderstore.io/c/valheim/p/under2ker/LivingFactions/
🔗 **Download Hex:** https://valheim.hexium.gg/mods/under2ker/LivingFactions
📂 **Github:** https://github.com/under2ker/valheim-living-factions
