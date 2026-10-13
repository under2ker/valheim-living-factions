# Changelog

## 0.3.1 - 2026-10-13
- People and settlements read in Russian when the mod speaks Russian: Steinvik is Стейнвик, the
  chieftain Astrid is «Астрид, вождь». Chronicle, map pins, dialog title and messages follow.
  Saved names are untouched, so switching back to English restores them.
- Fixed: switching the language in the mod menu left the names over villagers' heads in the old
  language until the next load; they now change at once.
- Fixed: the shipped DLL no longer carries the path of the folder it was built in.
- Package: new icon, cover picture and a card of the Wolf Fang axe in the README.
- Checked in the game, in Russian: smoke 15/15, names 3/3. Harness: every name the seven cultures
  can make reads in Russian letters.

## 0.3.0 - 2026-10-13
- Roads find their way over dry land: the cheapest walk on a 16 m grid of the world's heights,
  never through water, round steep ground where a gentler way exists, rounded into curves. A
  neighbour that cannot be reached by land is passed over; a settlement on an island has no road.
- Fixed ground work being lost on the next load. The mod asked for a tile's terrain compiler
  before the saved one had loaded and so made a second; the game then kept the one with more
  operations and threw the mod's away, with levelled pads, road paint and mended seams. Checked
  across two restarts: 0.00 m under all 15 buildings, no duplicates in the log, no torn edges.
- Nobody crowds at work: a yard has two places and a field three, a good step apart; whoever
  finds them taken fells a tree. The first shift starts at a time of each one's own, carriers
  spread along the storehouse front. Measured: at most 3 people within 3 m (was 5 and more).
- People talk: 55 new lines in English and Russian - several greetings for each standing, and
  remarks about what the speaker is doing (felling, quarrying, farming, building, carrying,
  visiting, on the road, guarding, on the tower, trading, escorting, hired).
- Workers turn to their work over a moment instead of in one frame and stay on their spot between
  swings. Guards left on the tower deck by an earlier watch climb down. Hired fighters notice
  trouble 45 m around the player.
- Checked in the game, in Russian: smoke 15/15, checks 6/6 (trading post, stamina, cargo,
  archer), features 4/4 (menu, hire button, caravan), hired fighters in a fight, a caravan
  arriving, the road with lamps before and after a restart, both custom axes in the hand.
- Not checked: cobbles of a town (needs a town in the lab world), relic effects in combat,
  multiplayer. Known: 3 points of the Vindfell - Barkthicket road lie at the water's edge.
- Dev tools: `tools/lfsuite.sh` runs several scenarios and prints one summary; commands `track`,
  `crowd`, `lang`, `mats`, `archer`, `roadtp <s> dry`; scenarios life, track, road, hired,
  caravan-in, town, terrain-persist.

- Chronicle widget tells what is going on: for the nearest known settlement its size, people and
  guards, what it is busy with, its two worst troubles (hunger, a recent raid, war, no room, no
  guards, no wood) and what comes next; the other factions take turns below, one every 7 seconds.
  Wordings change from day to day and place to place (`Forecast` in the core, checked in the harness).
- Roads: every settlement lays a road to the nearest neighbour it is not at war with (kin first,
  up to `World/RoadReach`, 1500 m). Roads sway a little, skirt buildings, are cobbled for the last
  40 m into a town, and are painted only around the player as they travel.
- Paved square at the heart of every settlement from hamlet up, wider with each tier.
- Paths are no longer a spoke to every building: places everyone uses and about half of the rest
  join the square, the others join the nearest older building. Up to five trails per settlement
  are worn by the people who actually walk them (to a tree, a neighbour, the road).
- Errands (`Features/Errands`): between shifts villagers call on another building or walk 50-85 m
  out along the road and back.
- Tower guard (`Features/TowerGuards`): one guard keeps the deck of the watchtower while another
  walks the rounds; an archer shoots from it, anyone else climbs down to fight.
- Caravans (`Features/Caravans`): a trader carrying goods with two caravan guards leaves by the
  road or arrives by it every few minutes while the player is near.
- Hired fighters (`Features/Mercenaries`): a friendly settlement hires out two fighters for
  120 + 40 per tier coins; they follow the player for 15 minutes and fight whoever is hostile to them.
- In-game checks (dev tools): `tools/scenarios/checks.txt` confirmed the chest trading post, archers
  shooting, carried cargo, footpaths and the Stag's Horn stamina saving. New commands `stamina`,
  `shots`, `tradechest`, `chestinfo`, `watch`, `roads`, `caravan`, `hire`; scenario verb `until`.
- Roads are made, not just painted: wider, their bed evened out, trees, bushes and rocks cleared
  off them, and a lamp post (a log pole with a torch that never burns out) every 30 m, on
  alternating sides (`Features/RoadLamps`).
- Settings menu: an "LF" tile on the inventory screen (TAB) opens the mod's own settings -
  language, on-screen chronicle, every feature switch, pace - on pages that are easy to extend
  (`ModMenu.BuildPages`). The language changes at once.
- The hire button is offered to anyone the settlement will trade with (it used to need friendship
  and was pushed off the list by the eight-button limit; the dialog now holds nine).
- Fixed torn ground: terrain tiles that disagree along their shared border are found and levelled
  to the mean on both sides about a minute after ground work ends; road beds no longer smooth
  across tile borders or inside settlements; all building pads are re-levelled once.
- Dev tools: `seams`, `perf`, `roadtp`, `roadview`, `lampview`, `inv`, `modmenu`;
  `tools/lfbuild-combined.sh` builds master plus the modelling worktree without merging;
  `docs/TESTING.md` is the guide for the testing session.
- Wolf's Fang («Клык Волка»): the Wolf Clan's relic axe, a new item built from the lowpoly-kit model
  (`src/LivingFactions/Assets`). Clan Wolf sells it at 420 coins from reputation 40 (keen).
  Checked in the game: sits in the hand (surface 0.015 m from the palm), on the back, bought with
  the affix. Existing `trade.cfg` files keep `AxeIron` until deleted.
- Stag's Horn («Рог Оленя»): the Stag Clan's merchant axe. Sold by the Stag Clan at 1500 coins from
  reputation 60. While carried, buying from the traders costs 10% less; wielding it costs 20% less
  stamina. Checked in the game: in the hand and on the back, and the stamina saving (1.60 spent
  instead of 2.00). The 10% discount is covered by the harness only.
- Equipment camera (dev tools): `look <slot> [angle] [elevation] [zoom]` frames the item itself and
  follows the player. Slots: `hand` (axe, sword, mace, tool), `shield`, `bow`, `helmet`, `chest`,
  `legs`, `cape`, `belt`, `back` (sheathed), `full`; `look auto` picks the slot of the last item.
  Angles are named (`side`, `front`, `rear`, `face`, `top`) or a bearing in degrees.
- New commands `gear <item>`, `unequip`, `sheathe` / `draw`, `face lit`, `gearinfo`, `hidehud`,
  and the scenario verb `gearshots <item>`. `tools/lfgear.sh [items]` runs a whole photo session
  (about 5 s per item) and puts pictures and contact sheets into `docs/gear/`.
- The chronicle widget now disappears together with the game's HUD at once, not up to 3 s later.
- Attach points (dev tools): `points` lists the character's nine equipment joints and skeleton,
  `points <item>` measures where an item's model sits relative to the joint it hangs on (box, long
  axis, distance from the palm to the nearest surface, the cut of the handle at the palm and how far
  to move the model), `points all` writes the same for every wearable item in the game, `marks on`
  puts coloured balls on the joints. `gearshots` now fails a hand-held item that is not in the hand.

## 0.2.2 - 2026-10-13
- Test runner (dev tools): `tools/lftest.sh <scenario>` launches the game, runs a whole scenario
  inside it and prints a PASS/FAIL report. Scenario verbs: `goto`, `waitready`, `wait`, `shot`,
  `expect`, `reject`; new commands `speed`, `equip`, `count`. A full smoke test takes about 80 s.
- While dev tools are on the game does not pause when its window loses focus.

## 0.2.1 - 2026-10-13
- Fixed buildings hanging over a gap and the ground tearing along zone borders. The game's level
  and smooth operations are relative to the heightmap as currently built, which is rebuilt a few
  frames after a change; several operations on one zone in a row stacked on top of each other.
  Ground work now runs strictly in order, waits for a zone to settle (1.5 s) after each change,
  and flattens every pad only after all shaping is done. Existing settlements repair themselves
  on load. `lf padcheck <settlement>` measures the ground under each building.

## 0.2.0 - 2026-10-13
- Houses and sheds have 3 m walls: a roof eave hangs 0.65 m below the wall top, which left 1.35 m
  of headroom at a door in a 2 m wall and stopped the player. `lf doorcheck <settlement>` sweeps
  the player's own capsule through every house door.
- Special wares per faction from `BepInEx/config/LivingFactions/trade.cfg` (assortment, prices,
  required standing): ordinary goods, relics with one property (keen, stout, light, lasting) and
  unidentified relics the faction identifies at reputation 30.
- A chest the player builds inside a settlement that trades with them becomes a trading post:
  the settlement buys useful goods out of it and leaves coins.
- Villagers haul what they gathered to the storehouse with the load on their back.
- One fighter in three is an archer (AI-tuned bow and a knife).
- Footpaths from the central green to every building, paved from town size up.
- Chronicle widget under the minimap: day, wars, the nearest known settlement's project, news.
- Every mechanic has a switch in the config section `Features`.
- Storehouse chests hold real goods, every home has a chest; theft and gifts through chests.
- Culture building styles: Fuling stake walls, Dvergr dark roofs, Draugr stone, open doorways.
- Night: villagers sleep indoors, guards patrol and may attack strangers (57%).
- Fixed: pickaxe work dug craters; inhabitants duplicated on every load; leftovers of destroyed
  settlements (now removed through the object database, not physics).

## 0.1.0 - 2026-10-12
- Simulation core, settlements from vanilla pieces, inhabitants, diplomacy window, raids.
