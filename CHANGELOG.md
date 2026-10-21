# Changelog

## 0.7.0 - 2026-10-21
- Bands on the move. A settlement short of wood, stone, food or metal sends a band out (two to six
  people) to the place within 1.4 km where that is plentiful, weighed against the distance and the
  danger of the way (the wilds of the biome, enemies near). The band carries food for the road,
  walks at a person's pace, gathers for a day, may lose men to what lives out there, and comes home
  with its load. Its people are away from home meanwhile.
- War bands march: a raid is no longer settled the moment it is decided. The band leaves, walks to
  its target, fights when it gets there (a player there sees it arrive) and carries its loot home;
  a band beaten on the way or at the gate comes back short.
- Near a player the bands are people in the world, foragers, warriors or scouts, walking the way
  the band goes; killing one costs the band a man.
- War campaigns, first stage. A warlike faction at war, with men and food to spare, picks an enemy
  place within 2.2 km and sends scouts. If what they report can be taken, it sets up a war camp
  more than halfway there, garrisoned from home and its other places nearby, then marches on the
  target from the camp. Taken: the camp's people move in. Thrown back: one more try while the camp
  holds, then the camp is given up. The camp is a real place others can attack; losing it ends the
  campaign, so does peace. Every stage is in the chronicle, with the reason when a campaign ends.
- Night in a village. People sleep in the beds of their houses, shared out over the homes as many
  to each as it houses (they all crowded into one hut, standing), lying down on the floor beside
  the beds where there are more of them than beds. A chest of theirs opened after dark starts a
  count, shown on screen: ten seconds with people asleep in that house, four with someone awake
  close by, half a minute elsewhere. Out of the settlement before it runs out, the thief got away;
  if not, the settlement is raised ("Thief! Wake up, everyone!"): everyone wakes, the watch and the
  people go for the thief and search round the place for three minutes instead of going back to
  bed, and standing drops. Only for a thief, though: the count ends quietly when the chest is closed
  with nothing of theirs taken, and the alarm comes when something of theirs is gone or the chest is
  still open as the count runs out. On every player's game, so it works with several. A chest a
  player built in the village starts nothing.
- Fixed: putting something of your own into a home's chest and taking it out again was theft. A chest
  now remembers what players put in, and only what is missing beyond that was the household's. A
  gift put into the storehouse and taken back within ten minutes undoes the gift (its standing and
  the stock it added) instead of counting as theft.
- An envoy's tent outside every settlement (not war camps nor outposts), beside its first road out,
  beyond its homes or its wall. The envoy talks to you whatever the faction thinks of you: gifts are
  taken (tribute at war), peace can be asked for, and the envoy says plainly what stands between you
  (war, a theft, blood, a suspicion, blows on their buildings or people, siding with their enemies,
  many gifts lately) and where their goodwill goes from here. The envoy carries no weapon, cannot be
  hurt and is nobody's enemy; within 8 m of the envoy the faction's people leave you alone, even at
  war, unless you strike one of them. Dev: `envoy <settlement> [go]`.
- Carriers really put their loads away. A woodcutter, stonecutter, farmer or hunter walks up to the
  storehouse chest for the goods (or the chest of the workshop where there is no storehouse), lifts
  the lid and puts in what they carried: wood, stone, carrots, turnips or barley from the field, meat
  from the hunters' lodge. Nothing is made out of thin air: the loads only show more of the stock the
  settlement has (a chest shows up to half of it), and taking them is theft like any of its goods.
- People asleep lie still in their beds. Before, the body stood in the frame of the bed and in the
  one sharing it, was pushed out and put back, and twitched and slid about with its legs walking.
- War campaigns, second stage:
  - a supply line: while the camp stands, and while a place taken is held, home sends a quarter of
    its soldiers and food every other day or so;
  - allies come: when a war camp goes up, friends of either side within reach may send a share of
    their soldiers, to the camp (they march in the assault with the band and go home with whoever
    came through) or to the place attacked (they fight for it while they stay, four days);
  - holding what was taken: a day or so after a place falls, the side that lost it gathers the
    soldiers it has nearest and marches to take it back; losing it again ends the campaign;
  - arms cost metal: a war band takes half a unit of metal a soldier from its settlement before it
    goes, and fights worse armed for what is short;
  - a campaign wears its people out, so peace comes sooner the longer it lasts;
  - a war camp now finds ground even with water or broken land in the way (round the target on
    home's side, at the last by home itself); before, a camp could fail to go up at all.
- Fixed: the sharpened stakes of war camps and before Fuling gates pointed in, at the camp or the
  wall; they point out now. Both are put up again once.
- War camps look like camps: three or four pitched tents round the fire, each turned its own way,
  hides inside, banners on poles, sharpened stakes facing out, firewood, a spit and torches, on a
  wider levelled ground. Its people come out by the fire, not on a tent roof.
- Outposts. A faction with a village to spare a few people puts them out where the land gives much
  more of what home lacks (timber, stone, soil, ore) within a kilometre, or, between two of its
  places far apart, a watch post halfway along the road. An outpost stays a camp of up to six,
  works mostly at its one trade (ore without a smithy), and sends what it gathers home in loads
  carried on foot. A road post keeps half its people on guard and makes the country round it safer
  for the faction's bands, as every place of theirs does nearby. At most two a faction.
- People move house: from a place that starves, or a small one an enemy war camp has come up close
  to, some of those who are not soldiers walk to the nearest place of their own that is fed and safe.
- A building that loses more than half of its pieces in the world (to a player's axe, to monsters,
  in a fight) is a ruin in the record too, for every player in the game: the settlement is weaker
  for it and builds it again in time, at its own cost. Before, it stood whole on record while it had
  a single piece left, and one taken down to the last piece was put up again for nothing. A player
  who was striking the faction's buildings just before is named in the chronicle and loses standing.
- Trades follow the settlement's needs: more of its people cut wood, quarry stone or farm where it
  is short of that and where it has the workshop for it, and they change trade as its needs change
  (it was one in three each, whatever the place lacked).
- Day patrols: by day half the guards walk a round, about the edge of the settlement and a stretch
  out along its road and back, stopping at each turn to look about; the rest keep near the fire.
- Food given counts by how filling it is and the work in it: berries and mushrooms as picked are
  worth little, meat off the fire more, a dish from the cauldron the most. The same holds when
  selling food to them (what they sell is priced the same way, so buying and selling back gains nothing).
- Villages on rough ground: a new building goes on a plot that can be walked to from the fire (no
  climb much steeper than the distance allows, no bank over about 30 degrees on the way), and only
  where none is left on a steeper one. Where a path between buildings runs over a bank too steep to
  walk, the ground along it is smoothed into a slope, once for each building, never on a pad.
- Houses no longer stand alike: each is turned a little its own way, and has a yard of its own: a
  woodpile along one side and things of its people by the other side and at the back (a chopping
  block, a bench, a tanning rack, a heap of stones or of bones, roots by a Greydwarf den), never in
  front of the door, and left out where a road or a path runs. Settlements founded from this version
  on also have their plots shifted a little off the rings. Every house is put up again once.
- Homes house fewer, and everyone sleeps in a bed: two people share each bed (the beds are wide),
  and a home houses two for each bed it has. A hut has one bed and houses 2 (was 3), a house two
  beds for 4 (was 5), a longhouse four for 8 (was 10), the great hall three for 6, the chief's
  household among them (6, as before). Fulings sleep in their straw beds and the headman's heap of
  straw, Greydwarfs on their straw heaps (they lay on the floor beside them). The rooms are laid out again so that no bed, chest, table
  or torch stands in another, in a wall or in a doorway. Settlements build a few more homes and grow
  as fast as before.
- Faction banners hang as they should: flat against the side of their pole facing the square or the
  road, on both gable ends of a longhouse and of the great hall, on the front corner post of a
  watchtower, never through a post, a wall, the eaves or into the ground. A town's gate posts are
  taller, a city's gate towers a pillar higher, so the banner hangs above the wall beside them;
  walls are put up again once. The Fuling headman's totem and banner stand clear of the way in.
- The square has more about it: a chopping block by the common woodpile, two torches, and things of
  the faction's people round its edge. Inside workshops the chests, woodpiles and the stonecutter no
  longer stand in the walls, the field's beehive no longer in its fence, and the market's cart waits
  beside the stalls instead of in front of the banner. The square and war camps are put up again once.
- Hired fighters follow your standing: an allied faction lets you have up to six at once and for
  24 minutes, a friendly one four for 15, anyone else two for 10 (settings World/HiredLimit and
  World/HireMinutes). The settlement says how long they stay when you hire them.
- Fixed: hired fighters stayed behind when the player went through a portal (they only caught up
  over a short distance). Those within 40 m now go through with the player and come out beside them.
- Fixed: walking into a village as a guest in a game for several (or when one of its chests changed
  hands between the players' games) could tell you "Your theft has been noticed" with nothing taken,
  cost standing for it, and soon set the faction against you. A change in a chest now counts as theft
  or a gift only after you yourself had that chest open; otherwise it is just counted again.
- A setting for how fast standing fades (World/ReputationDecay, also in the mod menu): 1 as before,
  0.5 half as fast, 0 never, so friendships and alliances last. In a game for several the host's counts.
- Fixed: a world backup restored after `lf advance` kept the factions on the later day (people
  counted as then, the chronicle on that day, buildings as in the backup). Every world save keeps a
  copy of the faction state stamped with the world's own clock, and a world now loads with exactly
  the copy saved with it, which comparing clocks alone could not tell when `lf advance` had moved
  the factions on without moving the clock.
- Fixed: people walked two or three steps, stopped, and walked on. The game walks an idle creature
  to a far point in short legs, each turned a random way, and stops it to turn at the start of
  every leg; the factions' people now walk where they are going in one go.
- Fixed: caravans, bands and hired fighters vanished in front of a player walking with them, at the
  end of their way or when they fell behind where the simulation had them. They now go only where
  nobody near sees it (a while later, whoever looks); a caravan followed out keeps on along the road.
- Fixed: caravan guards were drawn from one creature to the next far off the road (into the water
  after a Neck). Nobody wades out after a creature in the water any more, and people on the road give
  up a chase that takes them 30 m from where it began.
- Fixed: wall torches stood off the wall, turned sideways, in houses and on road lamps (their plate
  was taken for their back); they now sit flat on the wall or post with the torch out into the room
  or over the road. Road lamps are put up again.
- Fixed: what workers carry on their back was turned as its model happened to be made (a log
  standing up through the head), and a farmer carried a whole turnip plant with its leaves, like a bush
  hanging in the air behind the head. A farmer now carries a small crate of the harvest, a woodcutter a
  log across the shoulder blades, a stonecutter a stone, each lying flat against the back where a shield
  is slung, and moving with it.
- Fixed: a chief would greet you with "the chief is by the fire".
- Fixed: ground dug out under a building (a pit, by an old fault) or piled up was taken as the level
  to build on. A pad far off the land as the world made it is taken down, the ground levelled back to
  the land and the building put up again there; levelling far off the land is refused outright.
- City walls: a city now raises a stone wall two courses high at 78 m, outside its outer ring of
  houses (the old palisade at 60.5 m stood among them), with stone towers flying its banner by each
  gate. Towns keep their palisade. Both have gates where their roads come through, not just north
  and south, and are rebuilt when a new road arrives; trees on the line of the wall are cleared.
  A field's fence leaves a gap where a road runs through it.
- Fixed: a settlement's goods were free for the taking. Its fields could be harvested and the food
  sold or given back to the same people (standing for nothing), its hives emptied, its woodpiles,
  stone heaps and every other piece of it taken down with the hammer or broken for the materials,
  and the roots round a Greydwarf den broken for their wood. Now its fields and hives are its own
  ("This belongs to the Raven Clan."), its pieces cannot be taken down and leave nothing when
  broken. Its chests can still be robbed, but for a day after a theft the faction neither buys from
  the thief nor takes gifts, so goods taken from its chests cannot be sold or given back to it; put
  back into its chests they only fill the stores again.
- Fixed: gates sat up to a few metres off the road (their bearing was rounded to 6 degrees) and a
  gap was left between a gate's posts and the wall; the road now runs through the middle of the gate
  and each stretch of wall is laid from one gate post to the next.
- Fixed: villagers and guards turned on the player "for nothing". The game made everyone within
  20 m furious with a player who hit any of them, so fighting off raiders in a village set its own
  people on you. That is gone (see the next point). The night watch's challenge is now shown on
  screen and gives 15 s to leave instead of 8.
- People of the factions answer blows by their standing with you, not the game's "hit one, anger all":
  - each blow costs standing (1.5 by default), and they fight back only after a number of blows that
    depends on standing: 5 for an ally, 3 friendly, 2 neutral, 1 wary; hostile ones and those at war
    fight anyway. Until then the one struck protests ("Careful, friend!");
  - once angry, the faction's people near the fight go for you ("To arms!"), allied factions' people
    nearer by join in; they follow you only so far from the place (60 m), go back to work if you get
    away, and take it up again if you come back before it cools (5 minutes, twice that after a killing,
    four times after their chief's);
  - something attacking you near friendly or allied fighters has them stand with you;
  - the faction warns you before it breaks: when it stops trusting you, when the alliance is about to
    end, when it is about to come to war;
  - a killing seen by one of theirs is known at once; unseen, it may come out days later ("Nobody saw
    it. For now."), and the trail can also go cold;
  - killings leave a grievance that makes standing come back slower for a while;
  - a chief's death weighs two and a half times as much, and the settlement goes without a head for
    three days (no new building, three quarters of the work), then chooses a new chief, who may lead the
    faction a little more or less warlike;
  - caravan folk and hired fighters killed no longer count against a settlement's people.
  All thresholds and ranges are settings, section Conflict.
- Fixed: the faction shields were turned a quarter round on the arm, and the arm came through them.
  The Raven Kite stuck out in front of the hand and lay across the back; the round shields had their
  tops facing forward (the wolf's tail, the antler tines, the crossed tusks); and the board lay flat
  against the hand, so the elbow end of the forearm, which runs across the back of the board, came
  through it on most of the people carrying one. They now sit as the game's own shields do: upright
  on the arm and on the back, the kite hanging below the hand, the board standing off the arm and
  turned with it.
- Seven faction shields, all stout relics (+20% armour and block). Six are sold by the traders of their
  faction; the Greydwarf bark shield is left by their guards, after the root club:
  Wolf-Hide Shield (Wolf Clan, standing 30, 260 coins), Stag-Trail Shield (Stag Clan, 30, 300),
  Raven Kite (Raven Clan, 40, 340), Bone Shield (Fuling Horde, 20, 280), Copper Shield (Dvergr Hold,
  40, 420), Bark Shield (Greydwarf Brood), Bone-Plate Shield (Draugr Court, 10, 300).
- The three human clans build each in their own way. The Raven Clan roofs everything in dark
  shingles on dark posts, a raven carved on each end of a home's ridge. The Wolf Clan lays its walls
  log on log and hips the thatch of its huts and houses on all four sides, a carved wolf looking out
  from the top over the door (wolves on the ends of a longhouse's ridge). The Stag Clan keeps the
  thatched gable and gives every door a porch on two posts. The clans' buildings are put up again
  once.
- After the ground work of a settlement is done, a pad still not level (by 0.1 m) is flattened again:
  shaping one pad blends its edge into the land and could leave a neighbour's tilted.
- The Fulings' storehouses, smithies and hunters' lodges are roofed with hides on pole frames, as in
  their camps, instead of straw (put up again once).
- Fulings and Greydwarfs live in homes of their own. Fuling huts are round, a ring of their stake
  walls under the great cone roof of their camps, with straw beds, a fire, a totem and a banner at
  the headman's door. Greydwarfs den among twisted roots under the same cone, with straw and root
  tangles at the way in. Existing homes of both are taken down and put up again once.
- The clans show their colours: from a village up, their fighters carry the clan's shield, and the
  chief and one fighter in three its sword.
- A trader shows up to nine offers (was seven, then eight); the merchant's property is described
  in fewer words so it fits on its button.
- Settlements keep their ground clean: every 10 minutes the items lying loose inside a village,
  camp, war camp or outpost (left from fights and raids) are cleared away. Items dropped in the
  last minute, lying within 8 m of a player, placed as pieces or in a player's own base stay, and
  chests and item stands are never touched. Features/LootSweep and LootSweepMinutes in the config,
  a switch in the mod menu; dev: `lf sweep [settlement] [now]`.
- Fixed: a settlement whose plots were all on a slope or a shore never built anything (seen on a
  village founded by hand: a camp for 60 days, then taken by the Greydwarfs). It now builds on the
  next ring out. `lf here` refuses a spot with no room to grow unless told `force`, and takes a map
  position: `lf here <faction> at <x> <z>`.
- Fixed: one gift of several goods showed the same chronicle message once per good.
- The large map tells more about the settlements you know. Each one is named with its size, or as
  a war camp, outpost or road post. Zoom in close, or point at it, and the name opens up: the
  faction and how it stands with you, people and warriors, what they are short of, and trouble
  (under attack, food running out, at war with whom).
- The people of the factions near you (within 120 m, up to 40) are small dots on the minimap, in
  their faction's colour, red when they are hostile to you, a larger dot for a chief. Only the mod's
  own people are marked, not animals or other mods' characters, and not on the large map.
  Map/VillagersOnMinimap in the config and a switch in the mod menu (on by default).
- Roads come into a settlement by a street: from the square it winds between the buildings
  (never through one), then runs straight out through where the wall is, so it goes through its
  gate square-on. Roads that would leave close together share one street and one gate. New
  buildings are no longer put up on a street or on any road passing by, and nothing of a yard
  (woodpiles, hives, poles) stands on one: what stood there is taken away when you come by.
  Trees and rocks are cleared off the roads for 100 m round you (was 45 m), so a settlement's
  streets are clear while you are in it.
- Fixed: a road could run into a wall and go on beyond it only as a line of lamps, or end at the
  wall with no way through. The roads no longer change with wars, captures and alliances (only
  with settlements founded or lost, and with what stands on their plots), a gate once opened stays
  open, gates closer than 6.5 m share one opening (was 12 degrees, about 13 m at a town's wall, so
  the second road met the wall), and a road from elsewhere passing through a settlement gets its
  gates too. With several players, a wall put up by a guest no longer comes down and goes up again
  every time the host's copy of the factions arrives.
- Field fences leave a gap wherever any road or one of the settlement's own paths runs through
  them, also the fences of fields put up before the road came (they are opened when you come by).
- Road lamps stand outside the walls only, off the line of every road and off the pads, and not in
  rocks or ruins. Lamps of older versions, lamps inside a settlement and lamps by no road any more
  are taken down when you come by; the right ones go up as you pass.
- Fixed: levelling a large area with the hoe left ditches and humps along the borders of the terrain
  tiles (every 64 m). The mod mends the tears its own ground work can leave between two tiles, but it
  did so everywhere round the player, and it took the game's own small seams under a player's
  levelling for such tears, levelling them to the mean of the two tiles. It now mends only where it
  shaped the ground itself, never where a player has worked the ground and never by a player's
  building. Ditches already dug that way stay in the world: level them over once more.
- Roads no longer go up and down in steps where their bed is cut into a slope: each stretch levelled
  is then drawn even toward the bed a spot every metre, so it climbs smoothly instead of in flat
  terraces 0.4 m apart.
- Settlements shape their ground much faster (pads, paths, road beds): the ground work waited a
  second and a half after every change before the next one on the same patch of land, where the game
  is done with it in a frame or two.
- Fixed: a caravan held up on its road (a rock, a bank too steep, a wall) stood there until you went
  away and then vanished. It now tries the next stretch of the road, and one held up for good is set
  down further along where nobody sees it; its guards left behind catch up the same way.
- A key that hides the chronicle under the minimap or shows it again: Alt+H by default
  (General/ChronicleKey). It is the same as the switch "Chronicle on screen" in the mod menu, and the
  on-screen messages have their own switch there.
- Fixed: with several players, a blow on a settlement's building could be charged to the striker by
  another player's game as well. Only the striker's own game counts it now.
- Roads take gentler ways over the land: the route is found on a grid twice as fine, a climb costs
  more the steeper it is, every 4 m of it is looked at (a drop between two points of the route is
  seen), and no 4 m steeper than 27 degrees is taken where another way exists. Where the land is
  still steep, the road bed is cut down or built up (at most 3 m) to an easy grade, never next to a
  building's pad. The way between two settlements no longer comes back past a settlement's wall,
  where it met the wall away from any gate.
- Pads are fitted to their neighbours: a building's level ground is no further above or below the
  pads round it than a walk up the open ground between them allows (35 in 100), as near its own land
  as that leaves it. On a slope two buildings side by side stood up to 3 m apart in height, a bank no
  ramp could take. Settlements made before are fitted once: the buildings whose pads move are taken
  down and put up again at the new height. Ramps along the paths may now start right at the edge of
  a pad (they kept 1.3 m off every pad before, and left a step there), never on a building's own
  ground.
- Fixed: ground work on a patch of land at the edge of where the world is loaded round you (the paint
  and bed of a road some 70 m off, a pad there) could wait for ever, until you came closer.
- Paths and streets in a settlement that go over a bank (a pad cut into a slope, two pads at
  different heights) get a ramp cut along them, from one pad's floor to the next, the whole ramp or
  none of it (where it would cut more than 6 m into the land it is left out). Looked at again
  every time you come, so ground work cut short by quitting is finished later. A steep bank between
  two pads close together can stay steeper than a walk.
- New settlements are founded on somewhat gentler ground.
- Dev: `roadcheck <settlement>` (its roads walked with the player's capsule from 100 m out to the
  square: what blocks them, gates off the road, lamps inside, fences on a road), `roadslope
  <settlement> [metres|steep]`, `roadtp <settlement> steep`, `roadview <settlement> gate [n]`,
  `pathcheck <settlement>`; `roads` tells each road's streets and its steepest 10 m of land and bed.
- Dev: `mapinfo` (what the settlement marks say, how many villager dots there are).
- Dev: `beds <settlement>` (where each one sleeps tonight), `outside <settlement> <kind> [nth] [dist] [height] [bearing]`
  (the camera on a building from outside); `dump` also gives the solid boxes of a piece;
  `tools/BlueprintCheck` checks every blueprint without the game (things standing in each other, in walls
  or doorways, banners, beds for everyone).
- Dev: `squads`, `expedition <settlement> [resource]`, `campaign <faction#> <settlement id> [camp]`, `campsite <home> <target>`,
  `specimen`/`specam` (how a piece sits), `dig`, `wreck`, `openchest`, `goto camp`, `grudge`, `hit`, `resetgifts`; camera moves for filming (`orbit`, `fly`, `dolly`, `follow`, `timelapse`, `daycard`,
  `still`), `hideme`, `mark`, `npcgear`; `tools/lfrecord.sh` films a scenario.
- Fixed: a war campaign went on when the place it was aimed at changed hands. It now ends when the
  place passes to a third faction or is already its own (taken by another of its bands), at the camp,
  when the scouts come back and when the band is about to march; its bands still on the road turn
  home. The chronicle says why ("… call off their campaign: … is theirs already", "… give up their
  campaign: … has passed into other hands").
- Fixed: a campaign whose scouts fell on the way home, after they had seen the place, waited for
  peace to end. Scouts that never come back now end it either way.
- Fixed: a war camp or an outpost taken by another faction kept its old ties (its campaign, its home
  settlement, its outpost goal, its road post, its old chief's mourning) and never grew or built. It is
  a plain place of the new owner now, and the tents and stakes of a taken war camp come down: its fire
  is built again as a plain one.
- A faction state file that cannot be read no longer makes the factions start over. It is kept
  aside (`.unreadable-<time>`), the state is taken from its backup (`.bak`) or else from the latest
  readable kept copy, and written back at once. An unreadable file never replaces the good backup.
  With no state file at all the world is new to the factions, as before.
- A faction state from a newer version of the mod is not loaded (it is kept aside the same way, and
  the backup is used): its fields could mean something else to this one. A guest whose host runs a
  newer save format keeps its own copy instead of taking the host's.
- With several players, taking the factions back (`lf rewind`) starts a new timeline: each guest
  forgets what it built and reported in the one taken back, takes down what stands of places that
  are not there any more, and does not send old building progress into the new one.
- The mod menu shows thirteen rows a page (the world page has that many; the last was cut off).
- The dialogue shows at most ten buttons, as many as fit in its frame, and the way out ("Leave", or
  "Back" on an inner page) is always among them: a faction offering more than that (an ally at high
  standing) gives up its last offer instead of the way out.
- Dev: `owner <settlement id> <faction#>`, `capture <settlement id> <faction#>`, `scoutloss <faction#>
  [reported]`, `campinfo <settlement>` (its ties, and every building's layout and pieces); `net` tells
  the guest's timeline (epoch), and lists the settlements this player builds as `[4][9][26]`, sorted
  (it was `[4 9 26 ]`).
- Checked in the game: a campaign ending when its target changes hands, is already its own or loses
  its scouts (campaign-normal, campaign-ours, campaign-owner, campaign-scouts), a war camp taken and
  cleaned up (campaign-capture; campaign-capture-look: the camp's fire went from 28 pieces to 11, no
  tents or stakes left, the new owner put up its own hut), the state file broken with a backup, broken
  without one (taken from a kept copy), missing (the factions start over) and from a newer format
  (save-recover), the menu's rows (menu), the dialogue of an ally at standing 90 with its way out at
  the tenth row inside the frame (dialogue-buttons), and a rewind on a dedicated server with a guest
  (mp-rewind: the guest goes to epoch 1 with no old reports left).
- Not checked: a guest refusing a host on a newer save format; a rewind with two players in the
  world at once. Known limit: only `lf rewind` starts a new timeline. A world put back from a backup
  while the server is down keeps its timeline; that should need none, since every guest joins again
  and its game drops its copy on leaving, but it was not tried with a guest; the checks of malformed guest messages and the owner of hired fighters (left for
  0.7.1); per-pair relations taken from the faction ids instead of the shared random stream (left
  out: it changes the history of the world, with far fewer places taken in campaigns); why the smoke test once in five runs
  waited at `goto 4` until its 900 s timeout (the other four were ready in 15 s).

## 0.6.5 - 2026-10-14
- Seven faction swords. Six are sold by the traders of their faction as relics, the Greydwarf one
  is taken from the guards of a brood (one guard in four leaves it):
  - Grey Blade (Wolf Clan, +25% damage), standing 50, 480 coins;
  - Antlered Blade (Stag Clan, double durability), standing 30, 300 coins;
  - Black Beak (Raven Clan, half the weight), standing 40, 460 coins;
  - Obsidian Saber (Fuling Horde, +25% damage), standing 20, 380 coins;
  - Rune Sword (Dvergr Hold, +25% damage), standing 40, 520 coins, in place of the iron sword;
  - Root Club (Greydwarf Brood, double durability), a bronze mace in the hand;
  - Rusted Blade (Draugr Court, double durability), standing 10, 450 coins, in place of the iron sword.
  A `trade.cfg` written by an older version gets the new offers added at its end; the rest of it is
  left as the player made it.
- New console command `lf rewind`. Without a number it lists the days it can go back to; `lf rewind
  <day>` takes the factions back to the latest kept copy on or before that day, `lf rewind -30` goes
  thirty days back. The world stays as it is: buildings the factions had not got to by then come
  down, settlements founded since are cleared away. `lf advance` keeps a copy before it jumps, so a
  jump too far is one command to undo (reported by a player who typed `lf advance 755` hoping to
  go to day 755).
- Settlements check themselves once a session: a building the record calls built that has no piece
  in the world (a world backup restored without the faction state, as before 0.6.1) is put up again;
  pieces of buildings the record has not got to come down; ground under a building that is more
  than 0.4 m off level is levelled again, and the pads next to it with it.
- Fixed: a guest on a server with other HUD mods saw no chronicle under the minimap (reported by a
  player). The panel is now drawn in front of the game's interface, and if the minimap has been
  moved off where it can hang from, it goes to the top right corner. The log says where it was put.
- Fixed: the mod's own items were named in English in a game in another language until the next
  start; they are named again once the mod knows its language.
- Fixed: a trader with more than seven offers did not show the last ones (the Stag Clan's sword).
- Fixed: a woodcutter whose tree was cleared away for a building while he was at it threw an error.
- Checked in the game: every relic property in a real fight against a still Draugr (+25% damage,
  fury below a third of health, 5% of damage healing, mending in the hand, double durability,
  stamina 5.36 against 6.70 a swing with the Stag's Horn); five swords bought from five traders, the
  club taken from a Greydwarf guard; a fresh village advanced 30 days and rewound 25: three buildings
  came down, rewound to before it was founded: 525 objects cleared; an old world backup loaded with
  a newer faction state: three missing buildings put up again and the ground under them levelled;
  the chronicle seen by a guest on a dedicated server.
- Not checked: the chronicle with the HUD mods of the player who reported it (EpicMMO, Captain Skill
  Tree and others); two players in one village at once.

## 0.6.1 - 2026-10-14
- Fixed: restoring a backup of the world left the factions in the future, with the deaths, days and
  standing of the later game (reported by a player). The faction state lives beside the world, not
  in it, so the mod now keeps a dated copy at every save (the last 60) and, when a world loads with
  its clock behind the state, takes back the latest copy that is not ahead of it. The newer state is
  kept beside the save as a file of its own, and the player sees a message that the factions went
  back with the world. Copies are kept from this version on: a backup made before updating cannot
  be matched.
- Checked in the game: saved the world, kept a copy of it, played on and turned the Raven Clan
  hostile, put the copy back: the Raven Clan was friendly again and the day went back with it.

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
