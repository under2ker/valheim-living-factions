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

## Scenarios that exist

| File | Checks | Time |
|---|---|---|
| `smoke.txt` | settlement built, pads flat, doors passable, inhabitants, chests, dialog, night | ~80 s |
| `checks.txt` | chest trading post, archers shooting, carried cargo, Stag's Horn stamina | ~90 s |
| `features.txt` | settings menu and its icon, hire button, caravan, road with lamps | ~110 s |
| `gear.txt` / `tools/lfgear.sh Item ...` | every item worn and photographed, "is it in the hand" | 5 s per item |

Write new scenarios as files in `tools/scenarios/`; that folder is the one place under the
repository you may add files to (plus `docs/` for pictures worth keeping).

## Scenario language

One command per line, `#` starts a comment, end with `quit`.

- `goto 4` - travel to settlement 4 and wait until it is built and populated
- `wait 20` - game seconds; `waitready [timeout]` - until nothing is being built or levelled
- `expect TEXT :: command` / `reject TEXT :: command` - PASS/FAIL on the command's output
- `until TEXT :: command` - repeat the command (up to 90 s) until its output has TEXT
- `shot name` - screenshot; `gearshots Item` - wear, photograph from all angles, check the grip
- anything else is an `lf` command and its output goes into the report

## Commands worth knowing

State: `status`, `factions`, `npcs` (what each inhabitant is doing), `count`, `mat`, `roads`,
`hud` (the widget's text), `chests`, `items`, `time`, `pos`.

Measurements: `padcheck 4` (ground under each building vs the building), `seams [radius]`
(torn ground between terrain tiles), `doorcheck 4`, `perf` (frame time of the last 5 s),
`points Item`, `stamina`, `shots`, `track 20` (watches every inhabitant frame by frame for 20 s; read the
result with `track`, or wait for it with `until track done :: track`: jumps, sharp turns and
stop-start stutter per inhabitant, `JERKY` marks the odd ones).

Camera: `view 4 30 14 135` (settlement from outside), `inside 4 Hut`, `watch TEXT` (beside the
inhabitant whose `npcs` line has TEXT), `roadtp 4 300` + `roadview 4 280 10`, `lampview`,
`look hand side`, `cam` (release). `hidehud on|off`.

World: `god`, `env 0.4 Clear` (0.97 = night), `speed 3`, `tp x z`, `rep <faction#> <value>`,
`advance <days>`, `raid 5 4`, `war 2 9`, `caravan 4 [in]`, `hire 4`, `tradechest 4`,
`here <faction#>` (found a new camp ahead of the player).

Screens: `ui open chief` / `ui state` / `ui click N` (dialog), `inv` / `inv off`,
`modmenu open|page N|click N|close`.

Lab world facts: settlement 4 is Steinvik (Raven Clan, faction 8), a village; its road runs
974 m to Barkthicket (5). `status` lists the rest.

## Known and open

- Steinvik (4) is on an island: it has no road. Road checks use Vindfell (1).
- Unverified in the game: cobbles of a town, relic effects in combat, multiplayer.
