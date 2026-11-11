### Roads and Ground

- Roads choose a gentler finished bed, rise and fall evenly, and are paved in batches as the ground becomes ready.
- Settlement ground slopes into building pads along streets and paths. Existing settlements are graded as they load.
- Streets keep their full width clear of buildings, narrow where necessary instead of breaking, and avoid water and steep banks.
- Queued terrain repairs, road paint, clearing and lamps leave player-built pieces and player-worked ground alone. Blocked work is tried again later.

### Hired Fighters

- Fighters remember their employer, follow that player through portals and judge hostility by that player's standing.
- Hire limits count only that player's living fighters. A failed hire costs nothing.
- Fighters hired before this version keep a compatibility fallback and are taken over by their employer's game after a portal or zone change.

### Interface and Trade

- The chronicle can be dragged anywhere on screen and collapsed to its heading and day. Both choices are saved.
- The dialogue explains the latest changes of standing: gifts, trade, help, crimes, war and the fading of old deeds.
- Special wares are split into pages. An invalid `trade.cfg` row no longer hides later valid offers.
- A night-watch warning counts down on screen and confirms when the watch lets you go.

### Multiplayer and Saves

- Night-watch challenges and theft alarms target the involved player instead of everyone nearby.
- A denied chest-opening request no longer starts a theft countdown or attributes later changes to that player.
- Player identity, action packets, theft alarms and building reports are checked before the host applies them.
- Building updates can include plots awaiting ground work and city walls with many gates.
- Console commands reject guest-side or non-finite simulation changes.
- Faction saves and compressed history copies are replaced only after their new contents have been written completely.
- Gifts and trades reject invalid resources, non-finite values and non-positive quantities.

This release also includes every fix from 0.8.0: wars resolve in either faction-id order, interface queries no longer change the world's random history, dialogue keeps its way out, hired fighters and tamed animals follow their owner's standing, and malformed guest values are rejected.

### Verification

- Release build completed.
- Simulation harness: `ALL CHECKS PASSED`, including higher-id campaigns, deterministic relations, invalid trades and save/load.
- Blueprint check: 160858 checks, 0 failures.
- Language build: `problems: 0`.
- Packet, save, road geometry, night-watch and wares checks passed.
- The road and terrain repair scenario passed in single-player after reopening a copied world.
- Full multiplayer regression testing is still pending.

### Important

Everyone needs version 0.8.x, the server too: 0.7.x and 0.8.x do not play together. Worlds and faction state of 0.7.0 and 0.8.0 carry over. Back up your world before updating. Old roads and settlement ground are repaired as you visit them.

Download Thunderstore: https://thunderstore.io/c/valheim/p/under2ker/LivingFactions/

Download Hexium: https://valheim.hexium.gg/mods/under2ker/LivingFactions

Source: https://github.com/under2ker/valheim-living-factions
