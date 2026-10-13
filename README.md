# Living Factions

![Living Factions](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/store/cover.jpg)

A Valheim mod that adds seven NPC factions to your world. They set up camps, gather wood, stone
and food, put up buildings one piece at a time and grow into villages and towns. They trade with
each other, fall out, go to war and take each other's settlements. All of this goes on whether
you are around or not, and you can take part: trade, bring gifts, make friends, start a war.

Made for Valheim 1.0.17 with BepInEx 5 and Jötunn. It is an early version. Single-player and
hosting work. Dedicated servers and joining someone else's game do not work yet.

Feedback, bug reports and chat: [Discord](https://discord.gg/qVfAq3fqj6).

Русское описание ниже.

## The factions

| Faction | Who | Where | What they are like |
|---|---|---|---|
| Raven Clan | humans | Meadows, Black Forest | even-tempered, keep their word |
| Wolf Clan | humans | Black Forest, Meadows, Mountain | quick to fight |
| Stag Clan | humans | Meadows, Plains | traders, peaceful |
| Fuling Horde | Fulings | Plains | raiders |
| Dvergr Hold | Dvergr | Mistlands, Black Forest, Mountain | traders, proud |
| Greydwarf Brood | Greydwarfs | Black Forest | spread fast, do not trade |
| Draugr Court | Draugr | Swamp | hate the living |

Each one starts with a single camp. If there is no room for a faction at first, it shows up
later. Settlements you have been to get a pin on the map.

## What happens in the world

**Settlements grow.** Camp, hamlet, village, town, city. A settlement has stores of wood, stone,
food, metal and coins, and its people are split between gathering, building and guard duty. It
builds what it is short of: houses when it gets crowded, a storehouse when the stores are full,
farms and workshops, then a watchtower, a smithy and a market, and later a palisade and a great
hall. Only the capital of a faction becomes a city.

**The buildings are real.** They are made of the normal build pieces and go up in order, so a
house that is half done is a floor and a few walls with no roof. While you are away the
settlement keeps developing in the simulation and catches up when you come back.

**People have a day.** They walk to trees, rocks, fields and building sites, work there and
carry loads to the storehouse. Between shifts they visit a neighbour or take a walk down the
road. At night they sit by the fire and sleep. Guards walk the rounds, one stands on the
watchtower, some of them are archers.

**Roads and caravans.** Every settlement has a road to its nearest neighbour unless they are at
war, up to 1500 m. The road goes over dry land, has a lamp post every 30 m, and a settlement on
an island has none. Traders with two guards come and go along it.

**Politics.** Relations change with shared borders, trade, incidents and common enemies. A war
means raids: stores are looted, buildings burn, a settlement can change hands. If you are in a
settlement when it is raided, the raiders really turn up.

## What you can do

- Talk to anyone with `E`. You see how the faction feels about you, what the settlement has and
  what it needs.
- Give gifts. Things they are short of count double.
- Trade. Buy from their stores and sell your own goods. Prices depend on how scarce something is
  and how much they like you.
- Hire fighters. A friendly settlement lends you two of them for 15 minutes for 120 coins plus
  40 for each size step of the settlement.
- Declare war, pay for peace, or make an alliance once your standing is 70 or more.
- Killing their people or breaking their buildings costs standing, and enough of it means war.
  Killing monsters near their homes or raiders attacking them earns standing.

There are two relic axes so far, Wolf Fang and Stag's Horn. More weapons are being worked on.

![Wolf Fang](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/store/wolf-fang.jpg)

## On screen

- A chronicle panel shows what the nearest settlement is doing and what the other factions are
  up to.
- Settings are behind the "LF" tile on the inventory screen (Tab).
- The mod speaks English and Russian and follows the language of the game. In Russian the names
  of people and settlements are Russian too.

## Install

With r2modman or Thunderstore Mod Manager: install the package. BepInEx and Jötunn come with it.

By hand:

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
   and [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/).
2. Copy `LivingFactions.dll` into `Valheim/BepInEx/plugins/LivingFactions/`.
3. Start a world. The factions arrive on the first day. An existing world works too.

Back up your world before the first run. Settlements level the ground and place pieces.

The state of the factions is saved in `BepInEx/config/LivingFactions/<world>_<uid>.json` every
time the world saves.

## Settings

Use the menu in the game or edit `BepInEx/config/alexandr.livingfactions.cfg`. You can change the
pace of the simulation, turn wars and the non-human factions on or off, limit how many villagers
are shown, switch roads, lamps, caravans, hired fighters and the chronicle panel on or off, and
pick the language (`auto`, `en`, `ru`).

## Console

Start the game with `-console` and press F5.

| Command | What it does |
|---|---|
| `lf status` | lists factions and settlements with the distance to each |
| `lf factions` | your standing with each faction and who is at war |
| `lf chronicle [n]` | recent events |
| `lf advance <days>` | runs the simulation forward |
| `lf tp <settlement>` | takes you to a settlement |
| `lf rep <faction#> <value>` | sets your standing |

## Known limits

- Single-player and host only.
- Villagers swing their tools but the trees stay standing. What a settlement produces is decided
  by the simulation.
- The non-human factions build in the same style as the clans.
- If you remove the mod, the buildings stay in the world as normal pieces.

## Building from source

You need the .NET SDK and Valheim with BepInEx and Jötunn. Set `VALHEIM_INSTALL` if the game is
not in the default Steam folder.

```
dotnet build src/LivingFactions -c Release
dotnet run --project tools/SimHarness
```

The first command builds the plugin and copies it into the game. The second runs the simulation
without the game and checks it. `src/LivingFactions.Core` is the simulation and does not depend
on the game.

## По-русски

Мод добавляет в мир семь фракций NPC: три клана людей, фулингов, двергов, грейдворфов и
драугров. Они ставят лагеря, добывают ресурсы, строят дома по одной детали и вырастают в деревни
и города. Между собой торгуют, ссорятся и воюют, даже когда вас нет рядом.

С жителями можно говорить (`E`), торговать, дарить подарки, нанимать бойцов, заключать союз или
объявлять войну. Между поселениями есть дороги с фонарями, по ним ходят караваны. Настройки
открываются плиткой «LF» в инвентаре.

Установка: через r2modman или Thunderstore Mod Manager, либо положите `LivingFactions.dll` в
`Valheim/BepInEx/plugins/LivingFactions/`. Нужны BepInExPack Valheim и Jötunn. Перед первым
запуском сделайте копию мира.

Пока работает только одиночная игра и игра у хоста.

Отзывы, баги и общение: [Discord](https://discord.gg/qVfAq3fqj6).

## Credits

- [BepInEx](https://github.com/BepInEx/BepInEx), [HarmonyX](https://github.com/BepInEx/HarmonyX),
  [Jötunn](https://github.com/Valheim-Modding/Jotunn).
- Written with the help of AI (Claude Code). The mod contains no game files, decompiled code or
  extracted assets. Buildings are put together at runtime from the pieces in your own copy of
  the game.
