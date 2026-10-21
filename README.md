# Living Factions

![Living Factions](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/store/cover.jpg?v=0.7.0)

A Valheim mod that adds seven NPC factions to your world. They set up camps, gather wood, stone
and food, put up buildings one piece at a time and grow into villages and towns. They trade with
each other, fall out, go to war and take each other's settlements. All of this goes on whether
you are around or not, and you can take part: trade, bring gifts, make friends, start a war.

Made for Valheim 1.0.20 with BepInEx 5 and Jötunn. It is an early version.

## Playing together

Works since 0.4.0, on a dedicated server or with a player hosting.

- Everyone needs the mod: the server and every player. Somebody without it, or with another
  0.x version of it (0.6 against 0.7), is not let in.
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

**Every people builds its own way.** The Raven Clan roofs everything in dark shingles on dark posts,
with a raven carved on each end of a home's ridge. The Wolf Clan lays its walls log on log under
hipped thatch, a carved wolf looking out over the door. The Stag Clan keeps the thatched gable and
gives every door a porch. Fulings live in round huts under hide cones, Greydwarfs den among roots,
the Draugr build in stone and the Dvergr roof in dark shingles.

**People have a day.** They walk to trees, rocks, fields and building sites, work there and
carry their loads to the storehouse, where they put them into its chests: open one and the
carrots, wood or meat are there. More of them take up the trade the settlement is short of.
Between shifts they visit a neighbour or take a walk down the road. By day half the guards walk
a round out along the road and back, one stands on the watchtower, some of them are archers. At
night everyone sleeps in the beds of their own house, two to a bed.

**A thief in the night.** Open one of their chests after dark and a count starts: a few seconds
if people sleep in that house, half a minute in an empty storehouse. Take something of theirs
and stay too long, and the village wakes and the watch goes for you. What you put in a chest
yourself is yours to take out again.

**Bands on the move.** A settlement short of wood, stone, food or metal sends a band of two to six
out to where it is plentiful, up to 1.4 km away, weighing the distance against the danger of the
way. A raid is a band too: it marches to its target, fights at the gate and carries its loot home.
Near you the bands are people walking the land, and killing one costs the band a man.

**War campaigns.** A warlike faction at war sends scouts, sets up a war camp of tents, stakes and
banners more than halfway to an enemy place, garrisons it, and marches on the target from there.
Home sends soldiers and food along a supply line, allies of either side may join, and a place
that falls may be taken back a day later. Arms cost metal, and a long campaign wears a faction out.

**Outposts and moving house.** A faction with people to spare puts a small camp where the land
gives what home lacks (timber, stone, soil, ore), or a watch post halfway along a long road. People
leave a place that starves, or a small one an enemy war camp has come close to.

**Woodcutters and the forest.** Woodcutters fell real trees, at least 10 m from the buildings and
falling away from the village. The trunk is carried off and a sapling of the same kind goes into
the stump's place, so a village in the woods ends up in a clearing of young trees, not a bare field.

**Boss hunts.** Ask a chief about a hunt and they name the next great beast they want dead, with a
bounty in coins. Kill it, come back to any chief of that faction and collect. Every boss you kill is
news: the clans like you better for it, while the Greydwarfs, the Draugr and the Fulings take the
death of their own Elder, Bonemass and Yagluth badly.

**Roads and caravans.** Every settlement has a road to its nearest neighbour, up to 1500 m. The
road takes gentle ways over dry land, its bed cut or built up where the land is steep, has a lamp
post every 30 m outside the walls, and comes in by a street to the square through a gate in the
wall. A town has a palisade, a city a stone wall with towers. A settlement on an island has no
road. Traders with two guards come and go along it.

**Politics.** Relations change with shared borders, trade, incidents and common enemies. A war
means raids: stores are looted, buildings burn, a settlement can change hands. If you are in a
settlement when it is raided, the raiders really turn up.

## What you can do

- Talk to anyone with `E`. You see how the faction feels about you, what the settlement has and
  what it needs.
- Give gifts. Things they are short of count double.
- Trade. Buy from their stores and sell your own goods. Prices depend on how scarce something is
  and how much they like you.
- Hire fighters for 120 coins plus 40 for each size step of the settlement: two for 10 minutes
  from anyone who will deal with you, four for 15 from friends, six for 24 from allies. They go
  through portals with you.
- Declare war, pay for peace, or make an alliance once your standing is 70 or more.
- Talk to the envoy. Every settlement has an envoy's tent by its road out, and the envoy talks to
  you whatever the faction thinks of you, even at war: takes gifts (tribute at war), hears a plea
  for peace and tells you plainly what stands between you. Near the tent nobody attacks you.
- A blow on one of their people costs standing, and they fight back only after a few, more of
  them the better they like you (five for an ally, one for a wary faction). Killing their people
  or breaking their buildings costs more, and enough of it means war. A killing nobody saw may
  come out days later. Killing monsters near their homes or raiders attacking them earns standing.
- Their fields, hives and buildings are theirs: their crops cannot be picked, their pieces cannot
  be taken down for the materials.

There are seven relic axes and seven swords, one of each for every faction:

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

![The seven relic axes](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/store/axes.jpg?v=0.7.0)

| Sword | Faction | What it does | How you get it |
|---|---|---|---|
| Grey Blade | Wolf Clan | +25% damage | bought from the clan, standing 50 |
| Antlered Blade | Stag Clan | double durability | bought from the clan, standing 30 |
| Black Beak | Raven Clan | half the weight | bought from the clan, standing 40 |
| Obsidian Saber | Fuling Horde | +25% damage | bought from the horde, standing 20 |
| Rune Sword | Dvergr Hold | +25% damage | bought from the hold, standing 40 |
| Root Club | Greydwarf Brood | double durability | one Greydwarf guard in four leaves it |
| Rusted Blade | Draugr Court | double durability | bought from the court, standing 10 |

![The seven faction swords](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/store/swords.jpg?v=0.7.0)

| Shield | Faction | What it does | How you get it |
|---|---|---|---|
| Wolf-Hide Shield | Wolf Clan | +20% armour and block | bought from the clan, standing 30 |
| Stag-Trail Shield | Stag Clan | +20% armour and block | bought from the clan, standing 30 |
| Raven Kite | Raven Clan | +20% armour and block | bought from the clan, standing 40 |
| Bone Shield | Fuling Horde | +20% armour and block | bought from the horde, standing 20 |
| Copper Shield | Dvergr Hold | +20% armour and block | bought from the hold, standing 40 |
| Bark Shield | Greydwarf Brood | +20% armour and block | left by their guards, after the root club |
| Bone-Plate Shield | Draugr Court | +20% armour and block | bought from the court, standing 10 |

![The seven faction shields](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/store/shields.jpg?v=0.7.0)

From a village up, the clans' fighters carry their clan's shield, and the chief and one fighter
in three its sword.

## On screen

- A chronicle panel shows what the nearest settlement is doing and what the other factions are
  up to. Alt+H hides it or shows it again.
- The large map shows the land of every settlement you know of in its faction's colour, with
  a legend of the factions. Each settlement is named with its size; zoom in or point at it to see
  the faction, how it stands with you, its people and warriors, what it is short of and its
  trouble (under attack, food running out, at war).
- The people of the factions near you are dots on the minimap in their faction's colour, red
  when they are hostile to you.
- Settings are behind the "LF" tile on the inventory screen (Tab).
- The mod speaks English, Russian, Polish, German, French, Spanish and Portuguese. It follows the
  language of the game, or you can pick one in the menu whatever the game is set to. In Russian
  the names of people and settlements are Russian too.

## Screenshots

![A Raven Clan city in the morning](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/screenshots/press/city_morning.jpg?v=0.7.0)
![The paved square of a city](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/screenshots/press/city_square.jpg?v=0.7.0)
![A village in the woods with its paths cleared](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/screenshots/village_paths_0.3.4.jpg?v=0.7.0)
![Inside the great hall](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/screenshots/press/great_hall.jpg?v=0.7.0)
![Night at the fire](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/screenshots/press/night_fire.jpg?v=0.7.0)
![Faction lands on the map](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/screenshots/map_zones_0.4.2.jpg?v=0.7.0)
![Talking to a chieftain](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/screenshots/press/ui_dialog.jpg?v=0.7.0)
![A war camp: tents, stakes and the clan's banner](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/screenshots/press/war_camp.jpg?v=0.7.0)
![An envoy's tent by the road out of a settlement](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/screenshots/press/envoy_tent.jpg?v=0.7.0)
![Raven Clan homes under dark shingles, ravens on the gables](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/screenshots/press/raven_clan_houses.jpg?v=0.7.0)
![A Wolf Clan log house in the mountains](https://raw.githubusercontent.com/under2ker/valheim-living-factions/main/docs/screenshots/press/wolf_clan_house.jpg?v=0.7.0)

## Install

With r2modman or Thunderstore Mod Manager: install the package. BepInEx and Jötunn come with it.

By hand:

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
   and [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/).
2. Copy `LivingFactions.dll` into `Valheim/BepInEx/plugins/LivingFactions/`.
3. Start a world. The factions arrive on the first day. An existing world works too.

Back up your world before the first run. Settlements level the ground and place pieces.

The state of the factions is saved in `BepInEx/config/LivingFactions/<world>_<uid>.json` every
time the world saves, with a dated copy of the last 60 saves beside it. If you restore a backup of
the world, the factions go back to the same point too (from 0.6.1 on). Buildings that are on record
but missing from the world are put up again. `lf rewind` takes the factions back without touching
the world, for example after an `lf advance` too far. A state file that cannot be read (cut short by
a crash, say) does not start the factions over: it is kept aside as `.unreadable-<time>` and the
factions are taken from the backup beside it (`.bak`) or from the latest dated copy. A state saved by
a newer version of the mod is not loaded by an older one; it is kept aside the same way.

## Settings

Use the menu in the game or edit `BepInEx/config/alexandr.livingfactions.cfg`. You can change the
pace of the simulation, turn wars and the non-human factions on or off, limit how many villagers
are shown, switch roads, lamps, caravans, hired fighters, the minimap dots, the clearing of loose
items from settlements and the chronicle panel on or off, and pick the language (`auto` follows
the game, or one of `en`, `ru`, `pl`, `de`, `fr`, `es`, `pt`). Also: how fast standing fades
(`World/ReputationDecay`: 1 as made, 0 never), how many fighters you may hire and for how long
(`World/HiredLimit`, `World/HireMinutes`), how many blows each standing forgives and how far angry
people follow you (section `Conflict`), and the key for the chronicle (`General/ChronicleKey`).

## Console

Start the game with `-console` and press F5.

| Command | What it does |
|---|---|
| `lf status` | lists factions and settlements with the distance to each |
| `lf factions` | your standing with each faction and who is at war |
| `lf chronicle [n]` | recent events |
| `lf advance <days>` | runs the simulation forward |
| `lf rewind [day]` | lists the days you can go back to, or takes the factions back (`-30`: thirty days back) |
| `lf tp <settlement>` | takes you to a settlement |
| `lf rep <faction#> <value>` | sets your standing |

## Known limits

- Playing together is new and lightly tested. Back up the server world before the first run.
- Woodcutters fell trees and carriers fill the chests, but what a settlement gathers is still
  counted by the simulation.
- Ground the mod levelled before 0.7.0 along the borders of terrain tiles while you worked it with
  the hoe may have ditches or humps: level them over once more.
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

Дровосеки по-настоящему рубят деревья и сажают на их месте саженцы, носильщики кладут добытое в сундуки склада. Ночью жители спят в своих кроватях, а вора, открывшего их сундук, ловит стража. Поселения шлют отряды за ресурсами, воюющие фракции ставят военные лагеря и ведут осады. У каждого поселения есть шатёр посла: с ним можно говорить даже во время войны. Каждый клан строит по-своему: у Ворона тёмная черепица и резные вороны на коньках, у Волка бревенчатые стены и вальмовые крыши, у Оленя крыльцо у каждой двери. Вожди дают охоту на боссов за награду. У каждой фракции есть свой топор-реликвия, свой меч и свой щит. На большой карте земли фракций отмечены их цветом. С жителями можно говорить (`E`), торговать, дарить подарки, нанимать бойцов, заключать союз или
объявлять войну. Между поселениями есть дороги с фонарями, по ним ходят караваны. Настройки
открываются плиткой «LF» в инвентаре.

Установка: через r2modman или Thunderstore Mod Manager, либо положите `LivingFactions.dll` в
`Valheim/BepInEx/plugins/LivingFactions/`. Нужны BepInExPack Valheim и Jötunn. Перед первым
запуском сделайте копию мира.

С версии 0.4.0 можно играть вместе: на выделенном сервере или у игрока-хоста. Мод нужен всем,
включая сервер. Проверено с сервером и одним гостем.

Если восстановить мир из бэкапа, фракции тоже вернутся к тому же моменту. Команда `lf rewind`
откатывает фракции назад, не трогая мир (например, после слишком большого `lf advance`). Если файл
состояния фракций не читается (например, оборвался при вылете игры), фракции не начинаются заново:
файл откладывается как `.unreadable-<время>`, а состояние берётся из резервной копии `.bak` или из
последней сохранённой копии. Файл от более новой версии мода старая версия не загружает.

Отзывы, баги и общение: [Discord](https://discord.gg/qVfAq3fqj6).

## Credits

- [BepInEx](https://github.com/BepInEx/BepInEx), [HarmonyX](https://github.com/BepInEx/HarmonyX),
  [Jötunn](https://github.com/Valheim-Modding/Jotunn).
- Written with the help of AI (Claude Code). The mod contains no game files, decompiled code or
  extracted assets. Buildings are put together at runtime from the pieces in your own copy of
  the game.
