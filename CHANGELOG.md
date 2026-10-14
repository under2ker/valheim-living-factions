# Changelog

## 0.6.0 - 2026-10-14
- Woodcutters fell real trees. Only trees at least 10 m from every building, falling away from the
  village, and only by the player who builds the settlement (one felling per tree when playing
  together). The trunk is carried off and a sapling of the same kind is planted in the stump's
  place, so the wood round a village turns into a clearing of young trees instead of vanishing.
  Woodcutters prefer trees that may come down. Switch in the menu, page Villagers.
- Boss hunts. A chief names the next great beast they want dead and the bounty: 150 coins for
  Eikthyr, more for each one after. Kill it, come back to any chief of that faction, collect.
  Killing a boss is news everywhere: the clans think better of you (+10), the Dvergr too (+5), and
  the Greydwarfs, the Draugr and the Fulings take the death of their Elder, Bonemass and Yagluth
  badly (-25). Those three never put a bounty on their own.
- Fixed: villagers on a visit and guards at the end of a night round kept stepping about on the
  spot, which read as jerking. They now stand still once they get there. Measured: no jerky
  movement among 26 inhabitants (was up to 10 in the evening).
- Fixed: a sapling inside a doorway could keep people out of a house; the whole way in is cleared.
- Checked in the game: a tree felled and a sapling planted in 26 s; a hunt taken, Eikthyr killed,
  the bounty paid; features, life, track, hired fighters, caravan, road, ground across two
  restarts, names, map, town, smoke, checks, languages all passed.
- Not checked: how long a sapling takes to grow into a tree in a normal game, and the bounty
  with two players at once.

## 0.5.0 - 2026-10-14
- Five more relic axes, none of them sold, each come by its own way and given once to a character:
  - Raven's Wing (Raven Clan, +25% damage): strike down two raiders attacking a Raven Clan place
    and the jarl gives it to you;
  - Blackgnaw (Fuling Horde, +25% damage below a third of your health): taken from a Fuling chieftain;
  - Rune Cleaver (Dvergr Hold, double durability): a thane's gift to an ally at standing 80, asked
    for in talk;
  - Rootbiter (Greydwarf Brood, mends itself in the hand): carried by a Greydwarf chief;
  - Barrow Moon (Draugr Court, 5% of the damage you deal heals you): carried by a Draugr chief.
- The way to an alliance is shown: the dialog says how far your standing is from the 70 an
  alliance needs, the button shows it too, and every gift says what it would earn right now.
- Names follow the mod's language in all seven: the Greydwarf places and the Draugr's by-names,
  which are made of English words, are translated; Norse, Fuling and Dvergr names stay names.
- Checked in the game: each of the five axes comes the way it should (a Fuling and a Greydwarf
  chief struck down, the thane's gift asked for, two raiders struck down at a Raven Clan city) and
  sits in the hand (6 to 13 mm from the palm); smoke 15/15, checks 8/8, languages 15/15.
- Not checked in a real fight: the three new properties (fury, thirst, a self-mending blade).

## 0.4.2 - 2026-10-14
- Faction lands on the map. Every settlement you know of has a circle around it in its faction's
  colour, wider the bigger the place, and its mark is in that colour too. The large map has a
  legend of the factions you have met. Far zoomed out the lands stay visible as patches of colour.
  The small map shows only the marks, so the corner of the screen stays clear.
- Two new switches in the mod menu, page World: faction lands on the map (on), and every
  settlement on the map from the start (off, so you still find them yourself).
- Checked in the game: the large map close and far, with known settlements and with all of them,
  and the small map (docs/screenshots/map_zones_0.4.2.jpg).

## 0.4.1 - 2026-10-14
- Fixed: on a large or full screen the chronicle panel slid behind the minimap. It now hangs from
  the bottom edge of the minimap, right edges in line, at any screen size. Checked at 1280x720
  and 1920x1080.
- New icon and cover picture.

## 0.4.0 - 2026-10-14
- Playing together. The simulation runs on whoever hosts the world, a player or a dedicated
  server, and everyone else reads a copy the host sends out. What a guest does (gifts, trade,
  hiring, kills, war, peace, alliance, theft, finding a settlement) reaches the host and stays.
  The player nearest a settlement puts it up in the world, levels its ground and brings its people
  out; a dedicated server builds nothing itself. Roads, lamps and raids have one keeper in each
  neighbourhood, so nothing is done twice. Everyone on the server needs the mod, the server too.
- Five more languages: Polish, German, French, Spanish and Portuguese, about 290 lines each. The
  language follows the game by default; the menu now lets you pick any of the seven whatever the
  game is set to. Names of people and places are translated for Russian only.
- Fixed: a sapling or a tree on a doorstep kept people out of their house. Doorsteps and paths
  are cleared again every 20 seconds, because growth turns up later than the ground it stands on.
- Checked on a dedicated server with one guest on the same machine: the guest received the state
  and saw the village with level ground and its people; a purchase by the guest stayed in the
  settlement's stores after the host's next copy; the guest put up a settlement nobody had visited
  (623 pieces, twice, the same number both times), the server recorded every building, and after
  the server was restarted nothing was placed again.
- Not checked: a player hosting from the game with a friend joining, and two players in the same
  settlement at once. Both need a second Steam account.
- Dev tools: `tools/lfmp.sh` starts a dedicated server with a copy of the lab world and joins the
  game to it as a guest; scenarios mp, mp-build, mp-again, langs; commands `net`, `server <cmd>`,
  `lang mod <code>`; translations live in `tools/lang/*.tsv` and `tools/lang/build.py` checks them.

## 0.3.4 - 2026-10-13
- Paths inside a settlement are kept clear of trees, rocks and bushes. A village that grew in a
  wood used to keep the whole wood between its houses, so its paths lay under the canopy and
  could not be seen. Done once a session for each settlement near the player, and again when it
  builds something new. Worn trails to a tree or a neighbour are left as they are.
- Checked in the game: the village of Fjallfell from above before and after
  (docs/screenshots/village_paths_0.3.4.jpg), smoke 15/15.

## 0.3.3 - 2026-10-13
- Fixed: the doorway of the great hall was blocked by a table and a bench, so nobody could walk
  in. Both tables now stand along the back wall. Halls that are already built are taken down and
  put up again once, on the next visit.
- Fixed a typo in the Russian description of the Wolf's Fang.
- README says what the two axes really do in the game. The Wolf's Fang card is marked as concept
  art, its numbers are plans.
- Checked in the game: a settlement grown into a city has its square and paths cobbled, the
  ground under 29 buildings is level and has no torn edges; the Wolf's Fang deals 1.25 times
  the damage of the same axe without its mark; checks 8/8; smoke 14/15, the one miss being
  raiders left in the test world by the scenario before it.
- Dev tools: commands `damage` and `emptybag`.

## 0.3.2 - 2026-10-13
- Discord server for feedback, bug reports and chat: https://discord.gg/qVfAq3fqj6. The link is in the
  README and in the package description. Nothing changed in the game itself.

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
