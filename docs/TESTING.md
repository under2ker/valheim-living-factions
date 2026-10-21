# Testing Living Factions in the game

This is the working guide for the session that tests the mod (chat "Тест").

## Who does what

| Session | Does |
|---|---|
| **Главный чат** (main) | All code in `master`, commits, AND testing in the game |
| **Моделирование** | Models, textures and custom items, in its own worktree and branch |

There is no separate testing session any more; this guide is what the main session follows.
Rules that came out of the testing work:

- Put the expectation into the scenario (`expect` / `reject` / `until`). `pass 0 fail 0` proves nothing.
- Anything about the ground is checked across a restart: run the scenario twice in a row.
- The game runs in Russian: expectations match Russian text or language-neutral tokens such as `[Mercenary]`.
- Say what was not checked.

## Running a test

1. Check the game is not open. If the user has it open, do not close it: ask them.
2. Build and run in one go: `bash tools/lfsuite.sh --build smoke checks features`
   (names of files in `tools/scenarios/`; one summary line per scenario, every FAIL with the
   output that failed, and a count of duplicate terrain compilers in the log, which must be 0).
3. A single scenario: `bash tools/lftest.sh tools/scenarios/<name>.txt`
   It starts Valheim in a 1280x720 window, loads the lab world `LFLab` with the character
   `lftest`, runs the scenario, prints the report and quits. Tell the user before the window opens.
4. Report: `BepInEx/config/LivingFactions/report.txt` in the game folder. Pictures: `.../shots/*.png`.
   The last line is `=== DONE pass N fail M log-errors K ===`.

The script switches `Dev/DevTools` on for the run and off afterwards. Never leave it on.

Before any of that, for a change to the blueprints (`Blueprints.cs`): `dotnet run --project
tools/BlueprintCheck -c Release` lays out every building, culture, variant, war camp and wall in a
second, without the game, and fails on anything standing in something else, in a wall or a doorway,
a banner through its post or on the ground, a home with fewer beds than people, or a yard thing off
the levelled pad. It reads the sizes from the game's `dump.txt` (`probe-decor.txt` writes it).

## Scenarios that exist

| File | Checks | Time |
|---|---|---|
| `smoke.txt` | settlement built, pads flat, doors passable, inhabitants, chests, dialog, night | ~80 s |
| `checks.txt` | chest trading post, archers shooting, carried cargo, Stag's Horn stamina | ~90 s |
| `features.txt` | settings menu and its icon, hire button, caravan, road with lamps | ~110 s |
| `walls.txt`, `walls-10.txt` | roads into settlement 4 / 10: nothing on the way in, gates on the road, no lamps inside, no fence on a road | ~5 min each, `LF_TIMEOUT=600` |
| `roadhill.txt` | the steepest stretch of Vindfell's road no steeper than 40 %, seams closed | ~4 min |
| `slope.txt`, `slope-10.txt` | paths and streets in settlement 4 / 10 ramped over banks, pads level, doors open | ~5 min each, `LF_TIMEOUT=600` |
| `gear.txt` / `tools/lfgear.sh Item ...` | every item worn and photographed, "is it in the hand" | 5 s per item |
| `banners.txt` | banners on the square's pole, hall gables, watchtower, market, city and town gates, Fuling headman's door | not timed yet |
| `decor.txt` | Steinvik: rooms and beds of every home kind, yards, the square; everyone in a bed at night (`beds`), doors and roads open | not timed yet |
| `decor-b.txt` | Fuling, Greydwarf and Dvergr homes: straw beds and heaps, yards, `beds`; `LF_TIMEOUT=600` | not timed yet |
| `cargo.txt` | a farmer, a woodcutter and a stonecutter carrying their loads (crate, log, stone) on the back | not timed yet |
| `probe-decor.txt` | measures furniture and yard pieces (`dump` with solid boxes) for `tools/BlueprintCheck` | ~60 s |
| `owngoods.txt` | own goods put into a household chest and taken back are no theft, a storehouse gift taken back is undone, night: no alarm without theft, alarm with it; carriers put their loads into chests (`chests`) | ~5.5 min |
| `probe-fixes.txt` | sleepers lie still (`track`: an asleep line walks 0 m), the war camp, a roof piece (`specimen`) | ~5 min |
| `shields-npc.txt` | the clans' shields on their guards' arms, from the left, front and back (`watchgear`) | not timed yet |
| `probe-roof.txt` | the Fuling roof piece beside the wooden one, out in the open (`specimen`, `dump`) | ~1 min |
| `shieldfit.txt` | every shield carrier's forearm clear of the board (`shieldfit`: no CLIPS) at 4, 1 and 3; the roof piece against the sky | ~2 min |
| `fuling-roofs.txt` | the Fuling storehouse, smithy and hunters' lodge from outside | ~1 min |
| `envoy.txt` | the envoy's tent at 4, the envoy at the post, a hostile player sheltered, the envoy's page and a gift | ~5 min |
| `clans.txt` | the Raven, Wolf and Stag clans' homes from outside; doors open at 1 and 2 | ~6 min |
| `probe-clans.txt` | names and sizes of the pieces for the clans' buildings (`find`, `dump`) | ~4 min |
| `pads-a.txt`, `pads-b.txt` | the same pads levelled with the old fixed wait (`settle 1.5`) and the new one (`settle 0`), `padcheck 3` and `10` | ~5 to 9 min |
| `campaign-normal.txt` | a campaign (Raven Clan on Barkthicket, set up at its war camp) stays at its camp a day later while the target is still the enemy's | ~1 min |
| `campaign-ours.txt` | the target becomes the attacker's own (`owner`): the campaign ends, "ours" | ~1 min |
| `campaign-owner.txt` | the target passes to a third faction (`owner`): the campaign ends, "other" | ~1 min |
| `campaign-scouts.txt` | the scouts fall before and after their report (`scoutloss`): the campaign ends, "scouts" | ~1 min |
| `campaign-capture.txt` | a war camp taken by another faction (`capture`) keeps no campaign, parent, road post or mourning (`campinfo`) | ~1 min |
| `campaign-capture-look.txt` | the same camp before and after it is taken, in pictures and in `campinfo` pieces: the tents and stakes go, the fire is a plain one | ~1.5 min |
| `save-recover.txt` | the factions are not started over when the state file is broken (see below); `status` after loading | ~1 min |
| `menu.txt` | the mod menu's third page shows all its 13 rows, the last one included (`rows 13 of 13 shown`) | ~1 min |
| `dialogue-buttons.txt` | an ally at standing 90 (Durhall) offers more buttons than fit: ten are shown and the tenth is "Leave"; picture of the panel | ~1 min |
| `mp-rewind.txt` | with `tools/lfmp.sh`: the host rewinds, the guest goes to the new timeline (`net`: epoch 1, no old reports) | ~2.5 min |

`save-recover.txt` needs the state file broken by hand first, after `tools/lfrestore-lab.sh --load`:
in `BepInEx/config/LivingFactions/` copy `LFLab_<uid>.json` to `.json.bak` (or delete the `.bak` to
test the kept copies in `history/`), then overwrite the `.json` with broken text, or set its
`"Version"` to 2 for a newer format. The log must say where the state came from ("restored from its
backup file", "restored from a kept copy of day ..."), the `.json` must be valid again after the run, a
`.unreadable-<time>` copy must be kept and the `.bak` must not be overwritten. With the `.json`
deleted instead, the factions start over (`status` lists new camps). Restore the lab afterwards.

Multiplayer on one machine: `bash tools/lfmp.sh tools/scenarios/mp-rewind.txt` (a dedicated server
from Steam's tools with a copy of the lab world, the game joining it as a guest; `server <command>`
runs a command on the host). The server's mod lines are printed after the guest's report.

Write new scenarios as files in `tools/scenarios/`; that folder is the one place under the
repository you may add files to (plus `docs/` for pictures worth keeping).

## Scenario language

One command per line, `#` starts a comment, end with `quit`.

- `goto 4` - travel to settlement 4 and wait until it is built and populated
- `wait 20` - game seconds; `waitready [timeout]` - until nothing is being built or levelled (ground
  work waiting for land that is not loaded at all is not waited for). Measured on the
  restored lab, `goto 4` is ready after about 11 s; before the ground work waited for the heightmap's
  rebuild instead of a fixed 1.5 s per change it ran out at 240 s with 39 jobs left.
- `expect TEXT :: command` / `reject TEXT :: command` - PASS/FAIL on the command's output
- `until TEXT :: command` - repeat the command (up to 90 s) until its output has TEXT
- `shot name` - screenshot; `gearshots Item` - wear, photograph from all angles, check the grip
- anything else is an `lf` command and its output goes into the report

## Commands worth knowing

State: `status`, `factions`, `npcs` (what each inhabitant is doing), `count`, `mat`, `roads`,
`hud` (the widget's text), `chests`, `items`, `time`, `pos`.

Measurements: `padcheck 4` (ground under each building vs the building), `seams [radius]`
(torn ground between terrain tiles), `doorcheck 4`, `roadcheck 1` (its roads walked from 100 m out:
BLOCKED, MISALIGNED gates, LAMP INSIDE, FENCE ON ROAD), `roadslope 1 steep` (the road's ground now
against its bed: TOO STEEP), `pathcheck 4` (steps along the paths in a settlement: BANK), `perf` (frame time of the last 5 s),
`points Item`, `stamina`, `shots`, `track 20` (watches every inhabitant frame by frame for 20 s; read the
result with `track`, or wait for it with `until track done :: track`: jumps, sharp turns and
stop-start stutter per inhabitant, `JERKY` marks the odd ones).

Chests: `openchest 4 4` / `openchest 4 Storehouse 0` (a building's chest by id, or by kind and its nth chest),
`chestmove put|take Stone 5` (into or out of the chest just opened), `closechest`, `chests` (contents and
the carriers' loads put in), `thief 4` (whether a night thief's count is on, whether the settlement was raised), `envoy 4 [go|view]` (the
envoy's tent and envoy; go: stand at its mouth; view: the camera on it), `shieldfit 8` (the forearm against the
shield board, for every carrier), `settle 1.5` (ground work waits a fixed time again, for comparing).

Camera: `view 4 30 14 135` (settlement from outside), `watchgear Guard left 1.8` (close on one inhabitant from a side), `inside 4 Hut`, `watch TEXT` (beside the
inhabitant whose `npcs` line has TEXT), `roadtp 4 300` + `roadview 4 280 10`, `roadtp 1 steep`, `roadview 1 gate [n]` (in along a street at its gate), `lampview`,
`look hand side`, `cam` (release). `hidehud on|off`.

World: `god`, `env 0.4 Clear` (0.97 = night), `speed 3`, `tp x z`, `rep <faction#> <value>`,
`advance <days>`, `raid 5 4`, `war 2 9`, `caravan 4 [in]`, `hire 4`, `tradechest 4`,
`here <faction#>` (found a new camp ahead of the player).

Campaigns: `campaign <faction#> <settlement id> [camp]` (start one, at its war camp at once with
`camp`), `squads`, `goto camp`, `owner <settlement id> <faction#>` (only the owner changes),
`capture <settlement id> <faction#>` (taken as after a won assault), `scoutloss <faction#> [reported]`,
`campinfo <settlement>` (its ties, and each building's kind, layout revision and pieces).

Saves: `rewind` (the kept copies by day), `rewind <day>` / `rewind -<days>`. Multiplayer: `net` (host or
guest, the timeline's epoch, the settlements this player builds as `[4][9][26]` (each in its own
brackets, sorted; `none` when it builds none; it was `[4 9 26 ]` before 0.7.0, so test for `[26]`),
unconfirmed building reports), `server <command>` (on the host).

Screens: `ui open chief` / `ui state` / `ui click N` (dialog), `inv` / `inv off`,
`modmenu open|page N|click N|close`.

Lab world facts: settlement 4 is Steinvik (Raven Clan, faction 8), a village; its road runs
974 m to Barkthicket (5). `status` lists the rest.

## Known and open

- Steinvik (4) is on an island: it has no road. Road checks use Vindfell (1).
- Pads are not always exactly level: `smoke.txt` expects `worst deviation 0.0` from `padcheck 4`
  and on 2026-10-21 got 0.13 m (Longhouse #22, ground +0.00 .. +0.13) in one run and 0.04 m in
  another (a separate run of `goto 4` and `padcheck 4`). The same kind of value was there on 2026-10-20 before the fixes of
  2026-10-21 were made: `pads-a.txt` gave 0.12 m (Farm #7 +0.12, GreatHall #22 -0.06). Open work on
  fitting the pads, not a new fault; until it is done a smoke run of 14 PASS 1 FAIL on padcheck only
  is that. Three smoke runs after those on the same build had 0.0 and 15 PASS.
- `smoke.txt` once hung at `goto 4` (with `speed 3`) until the 900 s timeout, on 2026-10-21, in one
  of five runs on the same build; the other four were ready after about 15 s. The cause is not known
  (that run's game log was overwritten): keep each run's `LogOutput.log` when it happens again.
- Unverified in the game: cobbles of a town, relic effects in combat, two players in the world at
  once (one guest on a dedicated server is tested with `tools/lfmp.sh`), a guest refusing a host on
  a newer save format.
