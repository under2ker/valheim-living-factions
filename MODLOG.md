# MODLOG - Living Factions (Valheim)

Journal of the mod. Anything not written here is lost at the next context compaction.

## Idea / done criteria
NPC factions that develop on their own: found settlements, gather resources, build them up
(camp → hamlet → village → town → city), fight and trade with each other, and the player can
conduct diplomacy with them (reputation, gifts, trade, war, peace, alliance).
Done = works in the real game (single-player / host first) + a short clip.

User decisions (2026-10-12):
- Loader installed straight into the Steam install (no lab copy).
- Factions: human Viking clans first, then the game's races (Fulings, Dvergr, Greydwarfs, Draugr) as factions.
- Mode: single-player and host first; server-authoritative design, dedicated server later.
- Project folder: `E:\valheim-living-factions`.

## Recon (2026-10-12)
- Game: Valheim **1.0.17** (`Version.CurrentVersion = 1.0.17`, network version 40), Steam appid 892970,
  target buildid 25730771. Unity **6000.0.75f1**, Mono backend. No anti-cheat.
- Install: `C:\Program Files (x86)\Steam\steamapps\common\Valheim`
- Game code is in `valheim_Data/Managed/assembly_valheim.dll` (2.5 MB); `Assembly-CSharp.dll` is a stub.
  Also `assembly_utils.dll`, `assembly_guiutils.dll`, `Newtonsoft.Json.dll` (ships with the game).
- Saves: `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\` - did NOT exist at recon time (game never
  launched on this machine), so there was nothing to back up. 1.0 stores each world as a folder in
  `worlds_local/`.
- Loader: **BepInExPack_Valheim 5.4.2351** (Thunderstore, denikson) + **Jotunn 2.30.2** (ValheimModding).
  Installed 2026-10-12. Uninstall = delete `BepInEx/`, `doorstop_libs/`, `winhttp.dll`,
  `doorstop_config.ini`, `changelog.txt`, `start_*_bepinex.sh` from the game folder.
- Decompile (outside the repo, never commit): `~/valheim-decomp/{valheim,utils,guiutils}` via
  `ilspycmd 11.1` (`DOTNET_ROLL_FORWARD=LatestMajor`).
- Prior art: VikingSettlements (MIT, github.com/abjumb/VikingSettlements - static worldgen villages,
  clones Dvergr for settlers), VikingsForHire, MidgardPlus. None has a self-developing strategic sim
  with real diplomacy. Knowledge-base note: `games/valheim/mjolnir-frostner-rework.md`.

## Route
BepInEx 5 plugin + Jotunn + HarmonyX (managed patching). Reason: the community-standard loader for
Valheim, the game is Mono, and the idea needs runtime logic (sim, spawning, AI hostility, UI) - not data.

## Engine facts learned (exact names)
- `Character.Faction` enum: Players, AnimalsVeg, ForestMonsters, Undead, Demon, MountainMonsters,
  SeaMonsters, PlainsMonsters, Boss, MistlandsMonsters, Dverger, PlayerSpawned, TrainingDummy, DeepNorth.
- Hostility is one static: `BaseAI.IsEnemy(Character a, Character b)` - same `GetGroup()` ⇒ never
  enemies; aggravated flag makes Dverger-style neutrals hostile. Patch point for custom factions.
- Zones are 64 m; zone id type is `Vector2s` in 1.0 (`ZoneSystem.GetZone(Vector3)`),
  `ZoneSystem.instance.IsZoneLoaded(pos)`, `ZNetScene.instance.IsAreaReady(pos)`,
  `ZoneSystem.instance.m_locationInstances` (Dictionary<Vector2s, LocationInstance>), water level 30.
- Works for unloaded zones: `WorldGenerator.instance.GetBiome(x,z)`, `.GetHeight(x,z)`.
- World time: `ZNet.instance.GetTimeSeconds()`; server check `ZNet.instance.IsServer()`.
- Save hook: `public static Action ZNet.WorldSaveStarted / WorldSaveFinished`.
- Structural integrity + rain wear: `WearNTear.UpdateWear(float time)` (only runs for the ZDO owner).
- Terrain: `TerrainOp` prefab applies itself in `Awake` through `TerrainComp.ApplyOperation` (RPC that
  serializes only the op's *prefab hash*, resolved by `ObjectDB.TryGetTerrainOp`). Owner-side entry
  point: private `TerrainComp.DoOperation(Vector3 pos, Vector3 rot, TerrainOp.Settings)`.
- NPC building blocks: `Player : Humanoid : Character`; `MonsterAI : BaseAI`; `VisEquipment.m_isPlayer`,
  `SetModel/SetHairItem/SetBeardItem/SetSkinColor/SetHairColor`; `Humanoid.m_defaultItems/m_randomWeapon…`.
- Interaction interfaces: `Interactable.Interact(Humanoid user, bool hold, bool alt)`, `Hoverable`.

## Architecture
- `src/LivingFactions.Core` - game-agnostic simulation (plain C#, no Unity). Compiled into the plugin
  and into `tools/SimHarness` (headless runner + assertions, `dotnet run`).
- `src/LivingFactions` - BepInEx plugin: world adapter, materializer (sim state → pieces/NPCs when a
  player is near), faction hostility patch, diplomacy UI, console commands, persistence.
- Sim state is saved as JSON next to the BepInEx config, keyed by world name + uid, on
  `ZNet.WorldSaveStarted`.

## Log
- 2026-10-12: recon, loader install, decompile, project scaffold.
- 2026-10-12/13: vertical slice verified in the real game over four sessions (lab world `LFLab`,
  lab character `lftest`, both created by the mod's dev autostart - the user had no saves).

## Verified in game (oracle: mod log + in-engine screenshots, see docs/screenshots)
- Loader chain on 1.0.17: BepInEx 5.4.23.5 → Jotunn 2.30.2 → plugin. No errors in LogOutput.log.
- Seeding in a real world, late arrivals (3 factions day 1, 6 by day 3 after the site-rule fix).
- A settlement the sim developed off-screen (Steinvik, Raven Clan, 16 days) materialised on arrival:
  11 buildings, 302 pieces, cleared green, banner, 10 inhabitants. 0 exceptions.
- Construction in progress is visible (lean-to with walls and no roof yet).
- Diplomacy window (Jotunn wood panel): main / gift / trade / news / politics pages, Cyrillic OK.
- Hostility: `rep -80` ⇒ faction declares war, NPC health bars turn red, they attack and bark.
- Staged raid: greydwarf raiders spawned at a visited settlement and killed villagers.
- Save → quit → reload: faction state reloaded, **0 pieces placed again**, NPCs not duplicated.
- NOT yet exercised in game: `FactionMember.SetTalking` (NPC follows the player while the window
  is open) - added after the last session, compiles only. Buying/selling with real coins.

## Geometry facts (dumped from the running game with `lf dump`; full list in the dev dump.txt)
- 2 m module. `wood_floor` snaps (±1,0,±1). `woodwall` origin at CENTRE (±1,±1,0), thin in Z.
  `wood_wall_half` (±1,±0.5). `wood_door` like a wall. `wood_gate` y from -1 to +2.
- `wood_roof` (26°): high edge at z=-1 (y=+1), low edge at z=+1 (y=0) - origin at the LOW edge.
  `wood_roof_45`: high z=-1 (y=+1), low z=+1 (y=-1) - origin at MID height.
  `wood_roof_top(_45)`: ridge along X, base snaps (±1,0,±1), peak +0.5 (+1).
- Gables `wood_wall_roof` (rise 1) / `_45` (rise 2): base at y=0, high side at local +X.
  Unity yaw θ sends local +X to (cos θ, 0, -sin θ): yaw 90 ⇒ rises toward -Z.
- `wood_pole_log` 2 m / `wood_pole_log_4` 4 m, origin centre. `stake_wall`, `wood_fence`: origin at
  the bottom. `piece_banner01..11` hang 3 m down from their origin.
- `fire_pit_haldor` = campfire that never needs fuel.

## Gotchas
1. `um scan` reported "Unknown native engine" while Steam was still downloading (empty folder).
   Check `appmanifest_*.acf` StateFlags (4 = installed) before trusting a scan.
2. Vanilla locations are dense: with a 70 m "keep clear" radius ~0 of 600 probes passed. Only the
   settlement's heart avoids locations now (28 m + the location's exterior radius); plots check
   their own spot. `lf probe` tallies rejection reasons.
3. A third of Meadows probes are under water (biome is Meadows, height < 31.5). Always check height.
4. New characters fly in with the Valkyrie: `Player.m_localPlayer` exists high in the air for the
   first seconds. A dev `here` during the intro founded a camp on the sea floor.
5. Setting `transform.position` on the player is undone by physics; use `Player.TeleportTo`.
6. `TerrainComp.DoOperation` only works once the compiler is initialised (a frame or two after
   `GetAndCreateTerrainCompiler`) and only for its owner - the materializer queues and retries.
7. Killing the game process skips the world save AND the faction save; `lf quit` saves both.
8. Vanilla has a human NPC base in 1.0: `FallenWarrior` (Humanoid+MonsterAI, `VisEquipment.m_isPlayer`,
   `Player_animator`, hair prefix `Hair` x31, `Beard` x21) with AI-tuned `FW_*` weapons. The mod
   converts the Player prefab instead (works), but `FW_*` gear is worth trying for archers.
9. Thunderstore zips use backslashes; bash heredocs in this harness mangle backslashes and quotes -
   write helper scripts with the editor tool, not a heredoc.
10. The game hides the local player whenever the camera is within 2 m of their feet
   (`Player.FixedUpdate` -> `Character.SetVisible(false)` moves the LOD reference point away), and
   brings them back only on the next physics step. Close-ups came out as empty grass until
   `SetVisible` was patched while the gear camera is on.
11. Equipment models live in private `VisEquipment` fields (`m_rightItemInstance`,
   `m_leftItemInstance`, `m_helmetItemInstance`, `m_rightBackItemInstance`, ...). Chest, legs, cape
   and belt are skinned onto the body, so the gear camera frames a body region for those.
   Sun: light travels along `EnvMan.GetSunDirection()`; lit head-on polished metal blooms out, 65
   degrees off the camera axis reads well.
12. How an item gets onto the character (`VisEquipment.AttachItem`): the first prefab child named
   `attach` (or `attach_skin`; `attach_back` first when put away) is instantiated and parented to
   the joint with local position and rotation reset, plus the optional `equipoffset` child's
   position and rotation. So the model's zero point IS the joint - for a weapon, the middle of
   the palm - and the handle must pass through it. `SetParent` keeps world scale, so the joint's
   own scale (0.95 on the hands, 95 on the back joints) does not reach the model.
   Reference, `AxeIron` in hand space (metres): handle along Z through (0,0), butt at z -0.16,
   top at z +0.72, head towards +X (box x -0.03..+0.25), thickness y +-0.02. Sheathed it uses the
   same `attach` on `BackOneHanded_attach`. Every player weapon in the game has its surface
   within 0.02 m of the palm (`points all`); only monster attacks and snowballs exceed 0.05.
13. Game meshes are not readable from scripts, but `mesh.GetVertexBuffer(i).GetData(bytes)` and
   `GetIndexBuffer().GetData` copy them back from the graphics card into a readable mesh
   (`AttachPoints.ReadableCopy`). That is what makes exact measurements of vanilla models possible.

## Next
- Inhabitants that visibly work (chop, mine, hammer), sleep at night, guards on the tower.
- Culture-specific building styles (goblin_* pieces for Fulings, dvergr pieces, crypt stone for Draugr).
- Player bases as raid targets for factions at war with the player; hired guards; quests.
- Multiplayer: sync state to clients (map pins, UI) and drive spawning from the zone owner.
- Storehouse chests that hold part of the real stock; theft detection for crops.
- Showcase clip (`um win record` needs `um win setup`, which downloads an ffmpeg - ask first).
- 2026-10-13: visible work (FactionWork.cs). Villagers are steered by writing the AI's own patrol
  point (`ZDOVars.s_patrolPoint` + `s_patrol`; BaseAI re-reads it every second) with a small
  `m_randomMoveRange`, so target detection and combat stay vanilla. At the spot they equip a tool and
  swing: weapons via `Humanoid.StartAttack(null,false)`, tools (ItemType.Tool is NOT IsWeapon) by
  triggering `m_shared.m_attack.m_attackAnimation` on ZSyncAnimation. Damage from members to
  trees/rocks/destructibles and to their own settlement's pieces is cancelled by prefixes.
  Verified in game: farmers with cultivators in the field, pickaxe at the stone yard, axe at a
  birch, everyone at the fire at night with guards outside, talk window stays open (SetTalking),
  0 exceptions. Not checked: builders at an active site (none was under construction nearby).
- 2026-10-13 (later): fixes after the user's play test.
  - CRATER: stonecutters used `Humanoid.StartAttack` with a pickaxe, and a pickaxe attack digs
    terrain. Work is now animation-only (`ZSyncAnimation.SetTrigger(attackAnimation [+ "0" when
    m_attackChainLevels > 1])`), no Attack object, nothing hit.
  - NPC DUPLICATION (found on re-check, earlier "not duplicated" claim was wrong): ZDOIDs do NOT
    survive a reload, so the saved id list looked dead every session and 10 more inhabitants were
    spawned each time (38 after four sessions). Inhabitants are now counted from the world itself:
    `ZDOMan.FindSectorObjects(zone, SimulationDistance.OriginalDistance, list)` filtered by prefab
    hash + `lf_sid`. Surplus is culled. Verified: 38 -> 10 and stays 10 across reloads.
  - Claims: at most `WorkersPerTree` (1-2) per tree, 2 builders per site, 1 per work stand.
  - Ground: pad height = average of the footprint, level radius +1.5 m, neighbours re-levelled after
    each new pad, and `Building.TerrainRev` re-levels old/dug-up pads once (crater filled, verified).
  - Night: villagers and the chief walk to their house door and are moved inside (`lf_asleep`), wake
    at dawn or on alarm; guards walk an 8-leg perimeter. NightWatch: a guard who notices a
    neutral/wary player inside the perimeter walks up and warns; if the player is still there 8 s
    later, `NightAttackChance` (0.57) decides between attack (guards + chief aggravated 90 s) and
    letting them stay. Verified in log: night_halt -> night_attack with the chance forced to 1.
  - The lab player died during that test although `god` was on - not investigated.
  - `lf npcs` prints every inhabitant's role, job, state and tool: use it before trusting behaviour.
  - ffmpeg was already installed (winget); `um win setup` downloaded nothing. Clip: docs/video (git-ignored).
- 2026-10-13 (evening): building audit, chests, culture styles.
  - Chests.cs: storehouse chests mirror 25% of stock as real items (wood+stone / food / metal+coins),
    per-chest ledger in ZDO (`lf_n0/lf_n1`); a drop since the last sync = theft (stock down,
    `PlayerStole` reputation hit), a rise = gift. Every home has a chest of seeded personal loot
    (Hut got one; appended at the END of the blueprint so existing huts receive it).
    Verified: `lf chests` shows Wood x45 Stone x21 / CookedMeat x6 / Coins x13 and house loot.
    NOT exercised: actually taking items as a player (theft path) and depositing (gift path).
  - Audit fixes: storehouse goods moved outside so the chests are reachable (old storehouses are
    rebuilt once via `Building.LayoutRev`, verified by screenshot); inhabitants spawn 4-8 m from the
    centre so nobody appears inside a closed hut; outer ring moved to 69 m and palisade to 60.5 m so
    ring-5 buildings do not intersect the wall. Houses keep real doors: the player opens them, NPCs
    cannot, which is why sleepers are placed inside and brought out to the door on waking.
  - Styles from dumped geometry: `stone_wall_2x1` (2x1x1, origin centre), `stone_floor_2x2`
    (1 m thick, origin centre), `stone_pillar` (1x2x1), `stone_arch`; `goblin_woodwall_2m`
    (2x2, origin at the BOTTOM), `goblin_pole` (4.5 m, origin ~0.5 above its foot);
    `darkwood_roof`/`_top` have the same snaps as the wood ones. Fuling: stake walls, open
    doorways (verified by screenshot). Dvergr: dark roofs, Draugr: stone - NOT seen in game
    (no Draugr faction in the lab world).
  - Lab world: test settlements 7 (sea floor) and 9 (Fuling camp founded 27 m from Steinvik by a
    mis-timed dev teleport) were destroyed with `lf destroy`; their pieces are removed the next
    time the area is loaded - not yet observed. `lf here` now refuses water and spots within 160 m
    of another settlement.
  - Screenshots taken in the same command batch as a camera move are motion-blurred; wait a frame.
  - The lab player keeps dying near hostile NPCs although `god` was sent; uninvestigated.
- 2026-10-13 (night), v0.2.0. See CHANGELOG.md.
  - DOOR: player capsule is r 0.49, h 1.85. `wood_roof_45` hangs to wallTop-0.64 and 0.68 m out, so a
    2 m wall leaves 1.36 m under the eave. All walk-in buildings now use 3 m walls
    (`LayoutRevision` rebuilds old ones). Verified with `lf doorcheck 4`: 4/4 clear. The check
    ignores the door piece itself (vanilla door, which players pass); nobody walked through by hand.
  - RemovePieces by physics missed pieces (167 leftovers of the test Fuling camp); now ZDO-tag based.
  - Verified through the dialog: wares page from trade.cfg, relic bought (`Bow[light]`), mystery
    bought and identified. Verified by state/screens: hauling state, paths, HUD widget.
  - NOT verified in game: relic stat effects in combat, archers actually shooting, chest trading
    post, paved paths (no town yet), cargo mesh placement on the back, theft path.
  - Not done: inter-settlement roads (only the `Materializer.QueueRoad` helper exists),
    upcoming-event forecast in the widget (the sim is stochastic; it shows current state and news).
- 2026-10-13, v0.2.1: floating buildings + torn ground. `TerrainComp.LevelTerrain/SmoothTerrain` add
  a delta computed from `m_hmap.GetHeight`, i.e. the heightmap as last BUILT; `Poke` rebuilds it
  later. Queuing several ops per frame on one zone stacked the deltas (pads off by up to 0.88 m,
  seams at zone borders). Fix: FIFO queue, per-TerrainComp settle time 1.5 s, all level-only jobs
  after all level+smooth jobs (a pad's blend reaches its neighbours' pads). Measured with
  `lf padcheck 4`: worst deviation 0.88 m before, 0.56 m with sequencing only, 0.00 m with
  flatten-last. Smooth lerps toward the op's own height, so it never unflattens its own pad.
- 2026-10-13, v0.2.2: fast testing. `tools/lftest.sh tools/scenarios/smoke.txt` = launch + scenario +
  report + quit in ~80 s (15 checks; before: 8-10 min of sleeps per verification). Lessons:
  the game PAUSES in single-player when its window loses focus (menu opens) - scaled waits never
  end, so the runner uses real time and `Game.Pause` is skipped in dev mode; `Time.timeScale` is
  overwritten every frame from private static `Game.m_timeScale` (set that for `speed`);
  `Player.TeleportTo` is silently refused right after login - wait for the area to be ready and
  retry; `quit` must come after the report is closed.
- 2026-10-13 10:10, check of the custom axes from the "model-simulation" branch build (498688 bytes):
  both register (`LF_WolfFangAxe: 11 parts`, `LF_StagHornAxe: 8 parts, scale 0,372, size 0,88`),
  icons and names show in the hotbar ("New item: Stag's Horn"), `lf equip` works. FAIL: the mesh is
  about 8.5x too large - `lf dump` gives bounds z -3.99..3.54 (7.5 m) against 0.88 m for AxeIron,
  and the in-hand screenshots show a giant blade across the foreground and nothing sensible in the
  hand. Not tested: relic purchase of either axe (the scenario clicked the wrong rows), the 10 %
  price bonus, the stamina bonus. No exceptions in the log.
- 2026-10-13 (INBOX, axe 1 of 6): Рог Оленя (`stag-horn-axe`), по задаче из docs/handoff/INBOX.md.
  - Assets: `src/LivingFactions/Assets/stag-horn-axe.json`, 8 деталей, PNG по деталям и иконка
    (из docs/art/lowpoly-kit/output). Клонируется из AxeIron, как Клык Волка. Таблица `AxeSpec` в
    CustomItems.cs: новые топоры добавляются строкой, сборка меша общая.
  - Торговля: `clan_stag relic LF_StagHornAxe 1 1500 60 merchant` в trade.cfg (и в примере). Цена и
    репутация из брифа.
  - Способности (affix `merchant`):
    - покупка у торгующих фракций −10%, пока топор в инвентаре игрока: `Simulation.PlayerBuyMult`
      (ядро, по умолчанию null) и `Wares.BuyMultiplier` в SimHost;
    - выносливость −20%, пока топор в руке: Harmony-префикс на `Player.UseStamina`.
  - Упрощено (по брифу, записываю явно):
    - −10% действует только на `BuyPrice` симуляции (товары по цене из простых списков); фиксированные
      цены trade.cfg не меняются;
    - −20% выносливости срезает все траты стамины владельца, пока он держит топор (блок, бег, прыжок, удар),
      а не только удар: удар-специфичного метода в доступных сборках не нашёл;
    - путь получения «рынок торгового города» сведён к торговле у любого поселения клана Оленя с
      репутацией 60+, потому что торговых городов в моде отдельно нет.
  - Сборка Release: 0 ошибок. SimHarness: ALL CHECKS PASSED.
  - НЕ проверено в игре: вид модели в руке и на земле, ориентация, покупка и свойство, −10% и −20%.
    Проверка: `lf spawn LF_StagHornAxe`, покупка в поселении клана Оленя, строка лога "Delivered relic".
    Возможный риск: Harmony-префикс UseStamina ищет параметр по имени `v`; если имя иное, в логе будет
    ошибка патча, тогда поправить имя параметра.
- 2026-10-13, axes: scale fix after the in-game check (Клык Волка и Рог Оленя были в 8,5 раза больше).
  Cause: the vanilla mesh sits under its own node with scale ~8.5, and the vertices were fitted in the
  prefab's space but written as the mesh's local space. Fix: vertices go through the inverse of
  (go.worldToLocal * mesh.localToWorld) before the mesh is set, and normals are recomputed from the final
  triangles. The log now prints the node's lossy scale. NOT yet re-checked in game.
- 2026-10-13, axes: in-game check of the two custom axes (main chat, build 499200 bytes, 10:12).
  - Size: `lf dump` Клык Волка z -0.41..0.47, Рог Оленя z -0.41..0.47, AxeIron z -0.41..0.47. Fixed.
  - Purchase of Клык Волка (Vindfell, reputation 50): 420 coins, "Delivered relic LF_WolfFangAxe [keen]",
    damage 138 with keen vs 110 without. Works.
  - Purchase of Рог Оленя (Hvalborg, reputation 70): 1500 coins, "Delivered relic LF_StagHornAxe [merchant]",
    the offer line shows the affix text. Works.
  - Hand: the wolf axe shows at the right size in the third-person shot; the side view is not confirmed
    (the camera sits behind the player, the character is in shade). Needs a side-on shot.
  - NOT confirmed in game: the 10% buying discount (checked by the simulation harness instead:
    `a merchant's hook cuts the buying price by 10% only for its owner`, 1.93 -> 1.74; the in-game
    shop prices before/after the purchase were not compared cleanly) and the -20% stamina (no
    measurement). Sky: thin light lines in day shots; not related to these changes (no code here
    touches the sky or lighting), cause unknown.
- 2026-10-13, axes: Рог Оленя blade overexposed in the gear camera (side view). Cause: the blade texture
  from the kit is bright and warm (mean 183,129,65, peaks 255) against the wolf's steel (111,115,119),
  so the game's light blows it out. Fix: the mod's copy `Assets/stag-horn-axe_blade.png` is darkened to
  60% (mean 110,78,39, peaks 153). The kit original is untouched. NOT yet re-checked in game.
  Merged master 4bc1176 (equipment camera) on the way; the build includes it.
  - Checked in game (main chat, DLL 507392 bytes, 10:36): the Рог Оленя blade is readable on the side view,
    no glow; hand size 0.86 m (Клык Волка 0.89 m); no errors in the log. The white haft still reads light
    on the side view, accepted for now.
- 2026-10-13, axes: grip fix. The axes hung beside the palm: the model was centred on the vanilla bounding
  box, but the game puts the item's `attach` node in the joint with position and rotation reset, so the
  joint's zero is the middle of the palm and the handle must pass through it. Fix in CustomItems.cs:
  kit (x,y,z) -> joint (-x, z, y), a turn, not a mirror; the grip's centre goes to (0,0); the length is
  fitted to 0.88 m, Z from -0.16 to +0.72 (vanilla AxeIron, docs/reference/attach-points-items.tsv);
  the vertices go into the `attach` node's space; normals are recomputed. `tools/lfgear.sh LF_WolfFangAxe
  LF_StagHornAxe AxeIron` (DLL 10:48 build): all three PASS, surface 0.015 m from the palm (AxeIron 0.014).
  Pictures: docs/gear/sheet_LF_WolfFangAxe.jpg, sheet_LF_StagHornAxe.jpg.
  Not checked in game: the stamina -20%, the 10% buying discount in the game's own prices.
- 2026-10-13, axes: stamina -20% confirmed in game by the main chat on the shared build (1,60 instead of
  2,00 with merchant's axe in hand). The 10% buying discount is still checked only by the harness.
