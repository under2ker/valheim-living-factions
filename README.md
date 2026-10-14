# Living Factions

![Living Factions](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/store/cover.jpg?v=0.6.0)

A Valheim mod that adds seven NPC factions to your world. They set up camps, gather wood, stone
and food, put up buildings one piece at a time and grow into villages and towns. They trade with
each other, fall out, go to war and take each other's settlements. All of this goes on whether
you are around or not, and you can take part: trade, bring gifts, make friends, start a war.

Made for Valheim 1.0.17 with BepInEx 5 and Jötunn. It is an early version.

## Playing together

Works since 0.4.0, on a dedicated server or with a player hosting.

- Everyone needs the mod: the server and every player. Somebody without it, or with another
  0.x version of it (0.3 against 0.4), is not let in.
- On a dedicated server put BepInEx, Jötunn and `LivingFactions.dll` into the server folder the
  same way as into the game. The state of the factions is kept on the server, next to its config.
- The host runs the factions. The player nearest a settlement is the one who builds it.
- Tested with a dedicated server and one guest over Steam. Crossplay servers and two players in
  the same village at once have not been tested yet, so tell us how it goes.
- Back up the server world before the first run.

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

**Woodcutters and the forest.** Woodcutters fell real trees, at least 10 m from the buildings and
falling away from the village. The trunk is carried off and a sapling of the same kind goes into
the stump's place, so a village in the woods ends up in a clearing of young trees, not a bare field.

**Boss hunts.** Ask a chief about a hunt and they name the next great beast they want dead, with a
bounty in coins. Kill it, come back to any chief of that faction and collect. Every boss you kill is
news: the clans like you better for it, while the Greydwarfs, the Draugr and the Fulings take the
death of their own Elder, Bonemass and Yagluth badly.

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

There are seven relic axes, one for each faction:

| Axe | Faction | What it does | How you get it |
|---|---|---|---|
| Wolf's Fang | Wolf Clan | +25% damage | bought from the clan, standing 40 |
| Stag's Horn | Stag Clan | traders 10% cheaper, swings cost 20% less stamina | bought from the clan, standing 60 |
| Raven's Wing | Raven Clan | +25% damage | strike down two raiders attacking a Raven Clan place |
| Blackgnaw | Fuling Horde | +25% damage below a third of your health | taken from a Fuling chieftain |
| Rune Cleaver | Dvergr Hold | double durability | a thane's gift to an ally at standing 80 |
| Rootbiter | Greydwarf Brood | mends itself in the hand | carried by a Greydwarf chief |
| Barrow Moon | Draugr Court | 5% of the damage you deal heals you | carried by a Draugr chief |

To make an alliance, talk to anyone in the settlement: the window shows your standing and how far
it is from the 70 an alliance needs, and each gift shows what it would earn. A gift of what they
are short of counts double; many gifts in one day count for less.

The card below is concept art. The numbers on it are plans, not what is in the game now.

![Wolf Fang](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/store/wolf-fang.jpg)

## On screen

- A chronicle panel shows what the nearest settlement is doing and what the other factions are
  up to.
- The large map shows the land of every settlement you know of in its faction's colour, with
  a legend of the factions.
- Settings are behind the "LF" tile on the inventory screen (Tab).
- The mod speaks English, Russian, Polish, German, French, Spanish and Portuguese. It follows the
  language of the game, or you can pick one in the menu whatever the game is set to. In Russian
  the names of people and settlements are Russian too.

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

- Playing together is new and lightly tested. Back up the server world before the first run.
- Woodcutters fell trees, but the wood a settlement gets is still counted by the simulation.
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

Дровосеки по-настоящему рубят деревья и сажают на их месте саженцы. Вожди дают охоту на боссов за награду. На большой карте земли фракций отмечены их цветом. С жителями можно говорить (`E`), торговать, дарить подарки, нанимать бойцов, заключать союз или
объявлять войну. Между поселениями есть дороги с фонарями, по ним ходят караваны. Настройки
открываются плиткой «LF» в инвентаре.

Установка: через r2modman или Thunderstore Mod Manager, либо положите `LivingFactions.dll` в
`Valheim/BepInEx/plugins/LivingFactions/`. Нужны BepInExPack Valheim и Jötunn. Перед первым
запуском сделайте копию мира.

С версии 0.4.0 можно играть вместе: на выделенном сервере или у игрока-хоста. Мод нужен всем,
включая сервер. Проверено с сервером и одним гостем.

Отзывы, баги и общение: [Discord](https://discord.gg/qVfAq3fqj6).

## Credits

- [BepInEx](https://github.com/BepInEx/BepInEx), [HarmonyX](https://github.com/BepInEx/HarmonyX),
  [Jötunn](https://github.com/Valheim-Modding/Jotunn).
- Written with the help of AI (Claude Code). The mod contains no game files, decompiled code or
  extracted assets. Buildings are put together at runtime from the pieces in your own copy of
  the game.
