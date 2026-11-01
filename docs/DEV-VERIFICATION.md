# Dev branch: security hardening and verification gate

This branch is experimental. Do not merge into main or publish a release until tests pass in a real Valheim instance.

## Changes made

- `Net.OnDev`: reject all remote developer commands. Local host commands still work.
- `Net.OnReport`: accept building progress only from the assigned builder peer; reject negative counters, infinite heights, excessive wall descriptions and excessive report counts.
- `Net.OnCall`: bound argument count (0–16) and action length (64 characters) before allocating the argument array.
- `FactionWork.Job`: avoid `Mathf.Abs(int.MinValue)` for fallback profession selection.

## Remaining security blockers

- `Net.OnCall` still trusts the client to provide actions, identity and economic values. Do not consider this endpoint secure yet.
- Validate `OnAlarm` and other incoming RPCs, including sender identity and packet bounds.
- Introduce server-authoritative validation for purchases, gifts, faction relations, combat events and building state changes.
- Add packet parsing error handling, size budgets, rate limiting and replay protection.
- Review the dedicated server / builder assignment lifecycle and reassignments.

## Tests required before release

1. Build the mod against the intended Valheim, BepInEx and Jotunn versions.
2. Run `tools/SimHarness` and `tools/BlueprintCheck` with the documented dependencies.
3. Run multiplayer scenarios: `mp.txt`, `mp-build.txt`, `mp-again.txt`, `mp-rewind.txt`, `mp-hud.txt`.
4. Verify assigned client builders report progress, and unassigned peers cannot modify it.
5. Verify dev tools remain usable on the local host, but remote dev RPCs are rejected.
6. Run terrain scenarios `road.txt`, `roadhill.txt`, `slope.txt`, `terrain-persist.txt` before any terrain changes.
7. Test save/restart, host migration or reconnection, dedicated server and recovery from backup.

## Current verification

Static source inspection and GitHub commit confirmation only. No C# compilation or Valheim runtime tests were run in this environment. Do not treat these edits as tested.
