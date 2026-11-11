# Hexium (valheim.hexium.gg)

Страница: https://valheim.hexium.gg/mods/under2ker/LivingFactions. Адрес совпадает с Thunderstore
(`under2ker/LivingFactions`), так что сайт, скорее всего, берёт архив, README и иконку оттуда сам.
Если там есть поля для ручного заполнения, ниже текст для каждого. Тексты на английском.

## Short description

```
Seven NPC factions build villages piece by piece, send bands out for wood and ore, march to war and lay siege on their own. Talk to their envoys, trade, hire fighters, take boss hunts, earn a relic axe, a sword and a shield from every faction. Single-player and servers. EN, RU, PL, DE, FR, ES, PT.
```

## Description

```
Living Factions brings seven NPC factions into your Valheim world. They set up camps, gather wood, stone and food, put up buildings one piece at a time and grow into villages, towns and cities. They trade, fall out, march to war and take each other's settlements, whether you are around or not. And you can take part.

THE FACTIONS
- Raven Clan: humans of the Meadows and Black Forest. Even-tempered, keep their word.
- Wolf Clan: humans of the Black Forest and Mountains. Quick to fight.
- Stag Clan: humans of the Meadows and Plains. Traders, peaceful.
- Fuling Horde of the Plains. Raiders.
- Dvergr Hold of the Mistlands. Traders, proud.
- Greydwarf Brood of the Black Forest. Spread fast, do not trade.
- Draugr Court of the Swamp. Hate the living.

A WORLD THAT KEEPS GOING
- Real buildings made of the game's own pieces, put up in order, and every people builds its own way (Raven shingles and carvings, Wolf logs and hipped thatch, Stag porches, Fuling huts under hides).
- People have a day: woodcutters, stonecutters, farmers, carriers who fill the storehouse chests, guards on the round. At night everyone sleeps in their own beds, and a thief at their chests may wake the village.
- Woodcutters fell real trees away from the houses and plant saplings in their place.
- Bands go out for wood, stone, food and ore and come home with their loads; near you they are real people.
- War on foot: raids march, war camps go up halfway to the target, sieges with supply lines, allies and counterattacks.
- Outposts and watch posts, roads with lamp posts, streets and gates, caravans with guards, city stone walls.
- Faction lands on the map, settlement names that open up into their state, villagers on the minimap, a chronicle under the minimap.

WHAT YOU CAN DO
- Talk to anyone, give gifts, trade, buy relics, hire up to six fighters by your standing (they follow you through portals).
- Talk to the envoy outside every settlement, even at war: gifts, tribute, peace, and what stands between you.
- Make an alliance at standing 70, declare war or pay for peace.
- Take boss hunts for bounties.
- Earn seven relic axes, seven swords and seven shields, one of each per faction.

PLAYING TOGETHER
Single-player, a player hosting, and dedicated servers. Everyone needs the mod, the server too (any 0.7.x works with any 0.7.x; 0.6.x does not play with 0.7.x).

BACKUPS
Restoring a world backup takes the factions back too. "lf rewind" in the console takes the factions back without touching the world.

Needs BepInExPack Valheim and Jotunn. Back up your world before the first run: settlements level the ground and place pieces.

Feedback and bug reports: https://discord.gg/qVfAq3fqj6
Source and releases: https://github.com/under2ker/valheim-living-factions

Made with the help of AI (Claude Code). No game files, decompiled code or extracted assets are included.
```

## Changelog 0.8.0

```
- Fixed: wars and campaigns are fought whatever the order of the factions' ids. Before, half of the raids and assaults ended without a fight: the pair's relation is kept on the faction with the lower id, and the band looked for it on the attacker. The monster factions could take nothing by war, and their campaigns always ended repelled.
- Fixed: a relation asked for by a dialogue or the forecast no longer draws from the shared random stream, so opening a menu cannot change the history of the world.
- Fixed: the dialogue's Leave button is always on screen; eleven buttons fit.
- Fixed: a hired fighter fights by the standing of the player whose coins pay it, and a tamed animal by the standing of its owner, including on a dedicated server.
- Fixed: the host ignores malformed guest messages (impossible counts, ids, amounts and names) with a warning, instead of spoiling the factions.
- Worlds and saves of 0.7.0 carry over; everyone in a game needs the same 0.8.x version.
```

## Tags

NPCs, Gameplay, Overhaul, Multiplayer, Weapons, Immersion

## Pictures

Иконка `docs/store/icon.png`, обложка `docs/store/cover.jpg`, оружие `docs/store/axes.jpg`,
`docs/store/swords.jpg` и `docs/store/shields.jpg`, снимки из `docs/screenshots/press/`.
