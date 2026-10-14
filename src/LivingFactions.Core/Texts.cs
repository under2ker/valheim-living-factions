using System.Collections.Generic;

namespace LivingFactions.Core
{
    /// <summary>
    /// All player-facing text. English and Russian sit side by side in the table below; every other
    /// language is a table of its own (Texts.pl.cs and so on) and falls back to English for a line
    /// it does not have.
    /// </summary>
    public static partial class Texts
    {
        /// <summary>The languages the mod speaks, in the order of <see cref="Lang"/>.</summary>
        public static readonly string[] Codes = { "en", "ru", "pl", "de", "fr", "es", "pt" };

        /// <summary>Index into <see cref="Codes"/>: 0 = English, 1 = Russian, 2 and up the other tables.</summary>
        public static int Lang;

        private static readonly Dictionary<string, string>[] More = new Dictionary<string, string>[Codes.Length];

        /// <summary>What the game calls its languages, for following the game's own choice.</summary>
        private static readonly Dictionary<string, string> GameNames = new Dictionary<string, string>
        {
            { "English", "en" }, { "Russian", "ru" }, { "Polish", "pl" }, { "German", "de" }, { "French", "fr" },
            { "Spanish", "es" }, { "Portuguese_Brazilian", "pt" }, { "Portuguese_European", "pt" },
        };

        /// <summary>
        /// The language to speak: the player's own choice when they made one, otherwise the game's
        /// language when the mod knows it, otherwise English.
        /// </summary>
        public static int Resolve(string choice, string gameLanguage)
        {
            choice = (choice ?? "").Trim().ToLowerInvariant();
            int index = System.Array.IndexOf(Codes, choice);
            if (index >= 0) return index;
            string code;
            if (gameLanguage != null && GameNames.TryGetValue(gameLanguage, out code)) return System.Array.IndexOf(Codes, code);
            return 0;
        }

        public static string Code { get { return Codes[Lang >= 0 && Lang < Codes.Length ? Lang : 0]; } }

        /// <summary>Every key of the main table (for checking that a language misses nothing).</summary>
        public static IEnumerable<string> Keys { get { return T.Keys; } }

        public static bool Has(string key) { return T.ContainsKey(key); }

        /// <summary>True when the language has its own wording for the line (English and Russian have all of them).</summary>
        public static bool Translated(int lang, string key)
        {
            if (lang < 2) return T.ContainsKey(key);
            return More[lang] != null && More[lang].ContainsKey(key);
        }

        private static readonly Dictionary<string, string[]> T = new Dictionary<string, string[]>
        {
            // tiers
            { "tier_Camp", new[] { "camp", "лагерь" } },
            { "tier_Hamlet", new[] { "hamlet", "хутор" } },
            { "tier_Village", new[] { "village", "деревня" } },
            { "tier_Town", new[] { "town", "городок" } },
            { "tier_City", new[] { "city", "город" } },
            // resources
            { "res_Wood", new[] { "wood", "дерево" } },
            { "res_Stone", new[] { "stone", "камень" } },
            { "res_Food", new[] { "food", "еда" } },
            { "res_Metal", new[] { "metal", "металл" } },
            { "res_Coins", new[] { "coins", "монеты" } },
            // buildings
            { "b_Campfire", new[] { "campfire", "костёр" } },
            { "b_Hut", new[] { "hut", "хижина" } },
            { "b_House", new[] { "house", "дом" } },
            { "b_Longhouse", new[] { "longhouse", "длинный дом" } },
            { "b_Storehouse", new[] { "storehouse", "склад" } },
            { "b_Woodcutter", new[] { "woodcutter's lodge", "двор лесоруба" } },
            { "b_Stonecutter", new[] { "stonecutter's yard", "двор каменотёса" } },
            { "b_Farm", new[] { "farm", "ферма" } },
            { "b_HunterLodge", new[] { "hunter's lodge", "охотничий домик" } },
            { "b_Smithy", new[] { "smithy", "кузница" } },
            { "b_Watchtower", new[] { "watchtower", "сторожевая вышка" } },
            { "b_Market", new[] { "market", "рынок" } },
            { "b_Palisade", new[] { "palisade", "частокол" } },
            { "b_GreatHall", new[] { "great hall", "большой чертог" } },
            // standings
            { "st_War", new[] { "at war", "война" } },
            { "st_Hostile", new[] { "hostile", "вражда" } },
            { "st_Wary", new[] { "wary", "настороженность" } },
            { "st_Neutral", new[] { "neutral", "нейтралитет" } },
            { "st_Friendly", new[] { "friendly", "дружба" } },
            { "st_Allied", new[] { "allied", "союз" } },
            // treaties
            { "tr_Peace", new[] { "peace", "мир" } },
            { "tr_War", new[] { "war", "война" } },
            { "tr_Alliance", new[] { "alliance", "союз" } },
            // chronicle
            { "ev_FactionFounded", new[] { "{f} have come to these lands.", "В эти земли пришла новая сила: {f}." } },
            { "ev_SettlementFounded", new[] { "A new settlement is founded: {s} ({f}).", "Основано поселение {s} ({f})." } },
            { "ev_BuildingStarted", new[] { "{s}: work begins on a {b}.", "{s}: начато строительство — {b}." } },
            { "ev_BuildingCompleted", new[] { "{s}: a {b} is finished.", "{s}: построено — {b}." } },
            { "ev_TierUp", new[] { "{s} has grown into a {t}.", "{s} разрастается: теперь это {t}." } },
            { "ev_Starvation", new[] { "Hunger in {s}.", "Голод в поселении {s}." } },
            { "ev_GoodHarvest", new[] { "{s}: a rich harvest fills the stores.", "{s}: богатый урожай наполнил закрома." } },
            { "ev_Sickness", new[] { "{s}: a sickness takes its toll.", "{s}: мор унёс жизни жителей." } },
            { "ev_BeastAttack", new[] { "{s}: beasts from the wilds attack.", "{s}: нападение тварей из дикой глуши." } },
            { "ev_Newcomers", new[] { "{s}: wanderers settle down here.", "{s}: странники осели в поселении." } },
            { "ev_WarDeclared", new[] { "War! {f} take up arms against {o}.", "Война! {f} и {o} теперь враги." } },
            { "ev_PeaceMade", new[] { "Peace: {f} and {o} lay down their arms.", "Мир: {f} и {o} сложили оружие." } },
            { "ev_AllianceFormed", new[] { "Alliance: {f} and {o} swear oaths.", "Союз: {f} и {o} обменялись клятвами." } },
            { "ev_AllianceBroken", new[] { "The alliance of {f} and {o} is broken.", "Союз расторгнут: {f} и {o}." } },
            { "ev_RaidWon", new[] { "Raid: {f} plunder {s}.", "Набег: {s} разграблен. Нападавшие — {f}." } },
            { "ev_RaidRepelled", new[] { "{s} throws back a raid by {f}.", "Набег отбит: {s} выстоял. Нападавшие — {f}." } },
            { "ev_BuildingDestroyed", new[] { "{s}: the {b} burns down.", "{s}: сожжено — {b}." } },
            { "ev_SettlementCaptured", new[] { "{s} is captured by {f}.", "{s} захвачен. Новые хозяева — {f}." } },
            { "ev_SettlementDestroyed", new[] { "{s} lies abandoned in ruins.", "{s} покинут и лежит в руинах." } },
            { "ev_TradeCaravan", new[] { "Caravan between {s} and {s2}: {r0} for {r1}.", "Караван {s} — {s2}. Обмен: {r0} / {r1}." } },
            { "ev_Incident_good0", new[] { "{f} and {o}: a marriage binds two families.", "{f} и {o}: свадьба породнила два рода." } },
            { "ev_Incident_good1", new[] { "{f} and {o}: a shared feast is remembered fondly.", "{f} и {o}: общий пир запомнился добром." } },
            { "ev_Incident_good2", new[] { "{f} and {o}: lost hunters are returned safely.", "{f} и {o}: заблудившихся охотников вернули домой." } },
            { "ev_Incident_good3", new[] { "{f} and {o}: gifts are exchanged.", "{f} и {o}: обмен дарами." } },
            { "ev_Incident_bad0", new[] { "{f} and {o}: a quarrel over hunting grounds.", "{f} и {o}: ссора из-за охотничьих угодий." } },
            { "ev_Incident_bad1", new[] { "{f} and {o}: cattle go missing.", "{f} и {o}: пропал скот, подозревают соседей." } },
            { "ev_Incident_bad2", new[] { "{f} and {o}: a duel ends in blood.", "{f} и {o}: поединок закончился кровью." } },
            { "ev_Incident_bad3", new[] { "{f} and {o}: an insult at the feast.", "{f} и {o}: оскорбление на пиру." } },
            { "ev_PlayerWar_player", new[] { "{p} declares war on {f}.", "{p} объявляет войну. Враг — {f}." } },
            { "ev_PlayerWar_faction", new[] { "{f} will suffer {p} no longer: war!", "{f}: терпение лопнуло. {p} — враг." } },
            { "ev_PlayerPeace", new[] { "Peace between {f} and {p}.", "Мир: {f} и {p} сложили оружие." } },
            { "ev_PlayerAlliance", new[] { "{f} and {p} are now allies.", "Союз: {f} и {p} теперь союзники." } },
            { "ev_PlayerAllianceBroken", new[] { "The alliance of {f} and {p} is over.", "Союз расторгнут: {f} и {p}." } },
            { "ev_PlayerGift", new[] { "{p} brings gifts to {s}.", "{p} приносит дары в {s}." } },
            { "ev_PlayerKilledMember", new[] { "{p} spills blood at {s}. {f} will remember.", "{p} пролил кровь в {s}. {f} этого не забудет." } },
            // dialog
            { "ui_talk", new[] { "Talk", "Поговорить" } },
            { "ui_relation", new[] { "Standing", "Отношение" } },
            { "ui_population", new[] { "People", "Жителей" } },
            { "ui_soldiers", new[] { "warriors", "воинов" } },
            { "ui_stock", new[] { "Stores", "Запасы" } },
            { "ui_needs", new[] { "They are short of", "Им не хватает" } },
            { "ui_gift", new[] { "Give a gift", "Преподнести дар" } },
            { "ui_trade", new[] { "Trade", "Торговать" } },
            { "ui_news", new[] { "Ask for news", "Спросить о новостях" } },
            { "ui_politics", new[] { "Ask about their neighbours", "Спросить о соседях" } },
            { "ui_war", new[] { "Declare war", "Объявить войну" } },
            { "ui_peace", new[] { "Offer peace ({0} coins)", "Предложить мир ({0} монет)" } },
            { "ui_alliance", new[] { "Propose an alliance", "Предложить союз" } },
            { "ui_break", new[] { "Break the alliance", "Расторгнуть союз" } },
            { "ui_leave", new[] { "Leave", "Уйти" } },
            { "ui_back", new[] { "Back", "Назад" } },
            { "ui_buy", new[] { "Buy {0} {1} for {2} coins", "Купить: {1} ×{0} за {2} монет" } },
            { "ui_sell", new[] { "Sell {0} {1} for {2} coins", "Продать: {1} ×{0} за {2} монет" } },
            { "ui_give", new[] { "Give {0} x{1}", "Отдать: {0} ×{1}" } },
            { "ui_war_confirm", new[] { "Declare war on the {0}? Their warriors will attack you on sight, and peace will cost silver.", "Объявить войну? {0} будет нападать на вас при встрече, а мир придётся покупать серебром." } },
            { "ui_your_coins", new[] { "Your coins: {0}", "Ваши монеты: {0}" } },
            { "r_pack_full", new[] { "You cannot carry that much.", "Тебе столько не унести." } },
            { "ui_nothing_to_give", new[] { "You carry nothing they would value.", "У вас нет ничего, что они оценили бы." } },
            { "ui_no_news", new[] { "Nothing worth telling.", "Ничего достойного рассказа." } },
            { "ui_day", new[] { "Day {0}", "День {0}" } },
            { "ui_capital", new[] { "capital", "столица" } },
            { "ui_strength", new[] { "Strength", "Сила" } },
            { "ui_settlements", new[] { "settlements", "поселений" } },
            { "ui_chronicle", new[] { "Chronicle of the factions", "Летопись фракций" } },
            { "ui_factions", new[] { "Factions", "Фракции" } },
            // greetings by standing
            { "greet_War", new[] { "You dare come here? Draw steel or run.", "Ты смеешь приходить сюда? Обнажай сталь или беги." } },
            { "greet_Hostile", new[] { "You are not welcome here. Leave.", "Тебе здесь не рады. Уходи." } },
            { "greet_Wary", new[] { "State your business, stranger, and keep your hands where I can see them.", "Говори, зачем пришёл, чужак, и держи руки на виду." } },
            { "greet_Neutral", new[] { "Well met, traveller. What brings you to our fire?", "Здравствуй, путник. Что привело тебя к нашему огню?" } },
            { "greet_Friendly", new[] { "Good to see you again, friend! Sit, share our fire.", "Рады видеть тебя снова, друг! Садись к огню." } },
            { "greet_Allied", new[] { "Hail, sword-brother! Our hall is your hall.", "Привет тебе, брат по оружию! Наш дом — твой дом." } },
            // outcomes
            { "r_already_war", new[] { "We are already at war.", "Мы и так воюем." } },
            { "r_war_declared", new[] { "So be it. Your blood will water these fields.", "Да будет так. Твоя кровь напоит эти поля." } },
            { "r_not_at_war", new[] { "There is no war between us.", "Между нами нет войны." } },
            { "r_blood_too_fresh", new[] { "The blood is too fresh. Come back in a day.", "Кровь ещё не остыла. Приходи через день." } },
            { "r_not_enough_coins", new[] { "Words are cheap. Bring the silver.", "Слова дёшевы. Неси серебро." } },
            { "r_peace_made", new[] { "Let there be peace. Do not make us regret it.", "Пусть будет мир. Не заставляй нас пожалеть об этом." } },
            { "r_at_war", new[] { "We are at war. Speak of peace first.", "Мы воюем. Сначала говори о мире." } },
            { "r_already_allied", new[] { "We are already bound by oath.", "Мы уже связаны клятвой." } },
            { "r_not_trusted", new[] { "We do not know you well enough for such oaths.", "Мы недостаточно знаем тебя для таких клятв." } },
            { "r_alliance_made", new[] { "Then we are kin. Your enemies are our enemies.", "Отныне мы родня. Твои враги — наши враги." } },
            { "r_not_allied", new[] { "There is no oath between us.", "Между нами нет клятвы." } },
            { "r_alliance_broken", new[] { "Oathbreaker. We will not forget this.", "Клятвопреступник. Мы этого не забудем." } },
            { "r_no_trade", new[] { "We do not trade with the likes of you.", "С такими, как ты, мы не торгуем." } },
            { "r_deal_refused", new[] { "No deal. We cannot spare that.", "Не пойдёт. Этого мы дать не можем." } },
            { "r_deal_done", new[] { "A fair bargain.", "Честная сделка." } },
            { "r_gift_thanks", new[] { "A generous gift. We will remember it. (+{0})", "Щедрый дар. Мы это запомним. (+{0})" } },
            { "r_gift_meh", new[] { "We accept it.", "Принимаем." } },
            // world messages
            { "night_halt", new[] { "Halt! Who goes there? No strangers near our homes after dark. Turn back!", "Стой! Кто идёт? Ночью чужим у наших домов делать нечего. Уходи!" } },
            { "night_pass", new[] { "...Fine. Stay if you must, but keep your hands off your steel.", "...Ладно. Оставайся, раз пришёл, но руки от стали держи подальше." } },
            { "night_attack", new[] { "You were warned! To arms!", "Тебя предупреждали! К оружию!" } },
            { "night_alarm", new[] { "The night watch of {0} attacks!", "Ночная стража поселения {0} нападает!" } },
            { "msg_theft", new[] { "Your theft in {0} has been noticed.", "Вашу кражу в поселении {0} заметили." } },
            { "hud_hunger", new[] { "food is running out", "еда на исходе" } },
            { "relic_title", new[] { "Relic", "Реликвия" } },
            { "relic_unknown", new[] { "Unidentified. Those who made it could tell you what it is - if they trusted you.", "Не опознано. Те, кто это сделал, могли бы рассказать, что это, — если бы доверяли вам." } },
            { "affix_keen", new[] { "keen (+25% damage)", "острая (+25% к урону)" } },
            { "affix_stout", new[] { "stout (+20% armour and block)", "крепкая (+20% к броне и блоку)" } },
            { "affix_light", new[] { "light (half the weight)", "лёгкая (вдвое меньше вес)" } },
            { "affix_lasting", new[] { "lasting (double durability)", "долговечная (двойная прочность)" } },
            { "affix_merchant", new[] { "merchant's (buying from traders -10% while carried; stamina -20% when wielded)", "торговая (покупка у торговцев -10%, пока при вас; выносливость -20% при ударе)" } },
            { "ui_wares", new[] { "Special wares", "Особые товары" } },
            { "ui_ware_sell", new[] { "{0} x{1} - {2} coins", "{0} ×{1} — {2} монет" } },
            { "ui_ware_relic", new[] { "Relic: {0}, {1} - {2} coins", "Реликвия: {0}, {1} — {2} монет" } },
            { "ui_ware_mystery", new[] { "Unidentified: {0} - {1} coins", "Неопознанное: {0} — {1} монет" } },
            { "ui_ware_locked", new[] { " (needs standing {0})", " (нужно доверие {0})" } },
            { "ui_identify", new[] { "Ask what this is: {0}", "Спросить, что это: {0}" } },
            { "r_identified", new[] { "Ah, that one. It is {0}.", "А, это. Она {0}." } },
            { "r_no_wares", new[] { "We have nothing of that kind for outsiders.", "Для чужаков у нас такого нет." } },
            { "msg_chest_trade", new[] { "{0} bought goods from your chest for {1} coins.", "{0}: из вашего сундука куплено товаров на {1} монет." } },
            { "msg_raid", new[] { "Raid! The {0} fall upon {1}!", "Набег! {0}: нападение на {1}!" } },
            { "fc_head", new[] { "{0} — {1}: {2} people, {3} guards", "{0} — {1}: жителей {2}, стражи {3}" } },
            { "fc_ruin", new[] { "{0} lies in ruins", "{0}: одни развалины" } },
            { "fc_build0", new[] { "raising a {0}, {1}% done", "строят: {0}, готово {1}%" } },
            { "fc_build1", new[] { "hammers ring: a {0} is {1}% up", "стучат молотки: {0}, {1}%" } },
            { "fc_build2", new[] { "all hands on the {0} ({1}%)", "все заняты стройкой: {0} ({1}%)" } },
            { "fc_gather0", new[] { "stocking up on wood and stone", "запасают дерево и камень" } },
            { "fc_gather1", new[] { "crews are out felling and quarrying", "артели ушли рубить лес и ломать камень" } },
            { "fc_quiet0", new[] { "quiet days of work", "тихие рабочие дни" } },
            { "fc_quiet1", new[] { "fields and workshops are busy", "поля и мастерские при деле" } },
            { "fc_quiet2", new[] { "nothing new: the stores are filling", "без перемен: склады наполняются" } },
            { "fc_quiet3", new[] { "folk mend tools and wait for traders", "чинят инструмент и ждут торговцев" } },
            { "fc_starving", new[] { "people are starving", "люди голодают" } },
            { "fc_food_low", new[] { "food is running out", "еда на исходе" } },
            { "fc_raided0", new[] { "licking wounds after a raid", "зализывают раны после набега" } },
            { "fc_raided1", new[] { "burying the dead of the last raid", "хоронят погибших при набеге" } },
            { "fc_war", new[] { "at war with the {0}", "идёт война: {0}" } },
            { "fc_crowded", new[] { "no room left: more houses needed", "тесно: нужны новые дома" } },
            { "fc_unguarded", new[] { "nobody stands guard", "некому нести стражу" } },
            { "fc_no_wood", new[] { "the building site waits for wood", "стройка стоит: нет дерева" } },
            { "fc_grows", new[] { "about to become a {0}", "скоро станет: {0}" } },
            { "fc_rich", new[] { "the coffers are full: good time to trade", "казна полна: самое время торговать" } },
            { "fc_faction", new[] { "{0} ({1} settl., {2} people): ", "{0} (поселений {1}, жителей {2}): " } },
            { "fc_homeless", new[] { "{0}: scattered, no home left", "{0}: рассеяны, дома не осталось" } },
            { "fc_f_war", new[] { "fighting the {0}", "воюют: {0}" } },
            { "fc_f_hunger", new[] { "hunger in {0}", "голод в поселении {0}" } },
            { "fc_f_raided", new[] { "{0} was raided", "на {0} был набег" } },
            { "fc_f_grows", new[] { "{0} is growing into a {1}", "{0} скоро вырастет: {1}" } },
            { "fc_f_builds", new[] { "{0} is building a {1}", "{0} строит: {1}" } },
            { "fc_f_quiet0", new[] { "keeping to themselves", "живут своим чередом" } },
            { "fc_f_quiet1", new[] { "trading and tending fields", "торгуют и пашут" } },
            { "fc_f_quiet2", new[] { "quiet for now", "пока тихо" } },
            { "ui_hire", new[] { "Hire two fighters ({0} coins)", "Нанять двух бойцов ({0} монет)" } },
            { "r_hired", new[] { "They are yours until the day is out. Bring them back alive.", "Они с тобой до конца дня. Верни их живыми." } },
            { "r_hire_full", new[] { "You have swords enough at your back already.", "За тобой и так уже достаточно мечей." } },
            { "r_hire_none", new[] { "We have no fighters to spare.", "Лишних бойцов у нас нет." } },
            { "r_hire_poor", new[] { "Steel costs coin, and you have too little.", "Сталь стоит монет, а у тебя их мало." } },
            { "msg_caravan_out", new[] { "A caravan leaves {0}.", "Из поселения {0} уходит караван." } },
            { "msg_caravan_in", new[] { "A caravan is coming to {0}.", "К поселению {0} идёт караван." } },
            { "menu_title", new[] { "Settings", "Настройки" } },
            { "menu_close", new[] { "Close", "Закрыть" } },
            { "menu_on", new[] { "on", "вкл" } },
            { "menu_off", new[] { "off", "выкл" } },
            { "menu_page_general", new[] { "General", "Общее" } },
            { "menu_page_life", new[] { "Villagers", "Жители" } },
            { "menu_page_world", new[] { "World", "Мир" } },
            { "menu_page_sim", new[] { "Factions", "Фракции" } },
            { "menu_note_reload", new[] { "These take effect the next time the world is loaded.", "Эти настройки вступят в силу при следующей загрузке мира." } },
            { "lang_auto", new[] { "as in the game", "как в игре" } },
            { "lang_en", new[] { "English", "English" } },
            { "lang_ru", new[] { "Русский", "Русский" } },
            { "lang_pl", new[] { "Polski", "Polski" } },
            { "lang_de", new[] { "Deutsch", "Deutsch" } },
            { "lang_fr", new[] { "Français", "Français" } },
            { "lang_es", new[] { "Español", "Español" } },
            { "lang_pt", new[] { "Português", "Português" } },
            { "title_Chief", new[] { "chieftain", "вождь" } },
            { "title_Guard", new[] { "guard", "страж" } },
            { "title_Raider", new[] { "raider", "налётчик" } },
            { "title_Mercenary", new[] { "hired sword", "наёмник" } },
            { "title_Trader", new[] { "trader", "торговец" } },
            { "title_Escort", new[] { "caravan guard", "охранник каравана" } },
            { "opt_language", new[] { "Language", "Язык" } },
            { "opt_hud", new[] { "Chronicle on screen", "Хроника на экране" } },
            { "opt_messages", new[] { "Messages about great events", "Сообщения о важных событиях" } },
            { "opt_work", new[] { "Villagers work and carry goods", "Жители работают и носят грузы" } },
            { "opt_errands", new[] { "Visits and walks along the road", "Походы в гости и по дороге" } },
            { "opt_sleep", new[] { "Villagers sleep indoors at night", "Ночью жители спят в домах" } },
            { "opt_watch", new[] { "Night watch challenges strangers", "Ночная стража окликает чужаков" } },
            { "opt_night_attack", new[] { "Chance the night watch attacks", "Шанс нападения ночной стражи" } },
            { "opt_tower", new[] { "A guard on the watchtower", "Стражник на вышке" } },
            { "opt_archers", new[] { "Archers among the fighters", "Лучники среди бойцов" } },
            { "opt_npcs", new[] { "Inhabitants shown per settlement", "Жителей на поселение (видимых)" } },
            { "opt_roads", new[] { "Roads, squares and trails", "Дороги, площади и тропы" } },
            { "opt_lamps", new[] { "Lamp posts along the roads", "Фонари вдоль дорог" } },
            { "opt_road_reach", new[] { "Roads to neighbours within", "Дороги к соседям в пределах" } },
            { "opt_caravans", new[] { "Caravans with an escort", "Караваны с охраной" } },
            { "opt_raids", new[] { "Raids you can see and join", "Набеги, которые видно в мире" } },
            { "opt_chests", new[] { "Goods in chests", "Товары в сундуках" } },
            { "opt_trade", new[] { "Trade", "Торговля" } },
            { "opt_relics", new[] { "Relics and unknown goods", "Реликвии и неопознанные товары" } },
            { "opt_hire", new[] { "Fighters for hire", "Наём бойцов" } },
            { "opt_pace", new[] { "Pace of development", "Скорость развития" } },
            { "opt_wars", new[] { "Wars between factions", "Войны между фракциями" } },
            { "opt_races", new[] { "Factions of the game's races", "Фракции игровых рас" } },
            { "opt_map_zones", new[] { "Faction lands on the map", "Земли фракций на карте" } },
            { "opt_map_all", new[] { "Every settlement on the map", "Все поселения на карте" } },
            { "greet_War1", new[] { "Not one step closer.", "Ни шагу ближе." } },
            { "greet_War2", new[] { "Your kind has spilled enough of our blood.", "Такие, как ты, пролили довольно нашей крови." } },
            { "greet_Hostile1", new[] { "Turn around while you still can.", "Поворачивай, пока можешь." } },
            { "greet_Hostile2", new[] { "We remember what you did.", "Мы помним, что ты сделал." } },
            { "greet_Wary1", new[] { "Hm. A stranger. We are watching you.", "Хм. Чужак. Мы за тобой присматриваем." } },
            { "greet_Wary2", new[] { "Trade if you must, then be on your way.", "Торгуй, если надо, и ступай своей дорогой." } },
            { "greet_Wary3", new[] { "No trouble, and there will be no trouble.", "Не шуми — и шума не будет." } },
            { "greet_Neutral1", new[] { "A traveller! The road has been long, by the look of you.", "Путник! Видно, дорога была долгой." } },
            { "greet_Neutral2", new[] { "Peace to you. The chief is by the fire, if you have business.", "Мир тебе. Вождь у огня, если ты по делу." } },
            { "greet_Neutral3", new[] { "New face. Mind the building site, the logs roll.", "Новое лицо. Осторожней у стройки — брёвна катятся." } },
            { "greet_Friendly1", new[] { "There you are! We were just talking about you.", "А вот и ты! Только что тебя вспоминали." } },
            { "greet_Friendly2", new[] { "Welcome back. The stew is hot.", "С возвращением. Похлёбка горячая." } },
            { "greet_Friendly3", new[] { "Good hunting to you, friend.", "Доброй охоты, друг." } },
            { "greet_Allied1", new[] { "Shield-brother! Say the word and we march.", "Брат по щиту! Скажи слово — и мы выступим." } },
            { "greet_Allied2", new[] { "The hall is brighter when you are in it.", "С тобой в доме светлее." } },
            { "bark_wood", new[] { "Good straight pine. This one will make a roof beam.", "Добрая ровная сосна. Пойдёт на стропило." } },
            { "bark_wood1", new[] { "Mind your head, she is about to go!", "Береги голову, сейчас пойдёт!" } },
            { "bark_wood2", new[] { "Blunt again. I sharpen more than I cut.", "Опять затупился. Точу больше, чем рублю." } },
            { "bark_stone", new[] { "Stone does not hurry, and neither do I.", "Камень не торопится, и я не тороплюсь." } },
            { "bark_stone1", new[] { "Another cartload and the wall can go up.", "Ещё воз — и можно класть стену." } },
            { "bark_stone2", new[] { "My back will remember this quarry.", "Спина этот карьер запомнит надолго." } },
            { "bark_farm", new[] { "If the rain holds off, it will be a fair harvest.", "Если дождь повременит, урожай будет добрый." } },
            { "bark_farm1", new[] { "Turnips. Always turnips.", "Репа. Вечно репа." } },
            { "bark_farm2", new[] { "The boars were at the field again last night.", "Ночью кабаны опять лазили на поле." } },
            { "bark_build", new[] { "Hold that beam! Hold it, I said!", "Держи балку! Держи, говорю!" } },
            { "bark_build1", new[] { "A house by the first snow, if the wood lasts.", "К первому снегу будет дом, если леса хватит." } },
            { "bark_haul", new[] { "Make way, this is heavier than it looks.", "Дорогу! Оно тяжелее, чем кажется." } },
            { "bark_haul1", new[] { "To the storehouse, and then I sit down.", "До склада — и сяду." } },
            { "bark_haul2", new[] { "Count it right this time, steward!", "В этот раз считай как следует, ключник!" } },
            { "bark_visit", new[] { "Just looking in on the neighbours.", "Заглянул к соседям." } },
            { "bark_visit1", new[] { "Have you heard the news? Nor have I. That is why I am asking.", "Слыхал новости? Вот и я нет. Потому и хожу, спрашиваю." } },
            { "bark_visit2", new[] { "I lent them an axe in spring. Time it came home.", "Весной одолжил им топор. Пора бы ему домой." } },
            { "bark_road", new[] { "Down the road and back before dark.", "По дороге и обратно — дотемна." } },
            { "bark_road1", new[] { "Checking the snares. Wish me luck.", "Иду проверить силки. Пожелай удачи." } },
            { "bark_idle", new[] { "A moment's rest never hurt the work.", "Минута отдыха делу не вредит." } },
            { "bark_idle1", new[] { "Fine day. For once.", "Славный денёк. В кои-то веки." } },
            { "bark_idle2", new[] { "My feet ache. Rain tomorrow.", "Ноги ноют. Завтра дождь." } },
            { "bark_guard", new[] { "All quiet. I like quiet.", "Всё тихо. Люблю, когда тихо." } },
            { "bark_guard1", new[] { "Keep your blade sheathed inside the fence.", "За оградой клинок держи в ножнах." } },
            { "bark_guard2", new[] { "Greydwarfs at the treeline last night. Three of them.", "Ночью у опушки видели грейдворфов. Троих." } },
            { "bark_tower", new[] { "I can see the smoke of three hearths from up here.", "Отсюда видно дым трёх очагов." } },
            { "bark_tower1", new[] { "Nothing on the road. Yet.", "На дороге никого. Пока." } },
            { "bark_chief", new[] { "A settlement is its people. Remember that.", "Поселение — это люди. Запомни." } },
            { "bark_chief1", new[] { "Come to the fire if you want to talk terms.", "Хочешь говорить о деле — подходи к огню." } },
            { "bark_chief2", new[] { "More hands, more mouths. So it goes.", "Больше рук — больше ртов. Так оно и идёт." } },
            { "bark_trader", new[] { "Furs, salt and iron, and the best price this side of the sea.", "Меха, соль и железо — лучшая цена по эту сторону моря." } },
            { "bark_trader1", new[] { "The road is long, so I will not haggle long.", "Дорога длинная — торговаться долго не стану." } },
            { "bark_trader2", new[] { "They pay well at the next hearth. I hope.", "У соседнего очага платят хорошо. Надеюсь." } },
            { "bark_escort", new[] { "Keep clear of the goods.", "От товара держись подальше." } },
            { "bark_escort1", new[] { "Wolves on the road last trip. Two-legged ones.", "В прошлый раз на дороге были волки. Двуногие." } },
            { "bark_merc", new[] { "Paid till sundown. Point me at something.", "Плачено до заката. Покажи, кого бить." } },
            { "bark_merc1", new[] { "I follow, you lead. Try not to lead us into a troll.", "Я иду, ты ведёшь. Постарайся не привести к троллю." } },
            { "bark_merc2", new[] { "Coin first, songs later.", "Сперва монета, песни потом." } },
            { "hud_elsewhere", new[] { "Elsewhere", "В других краях" } },
            { "hud_news", new[] { "News", "Вести" } },
            { "msg_enter", new[] { "{0} — {1} of the {2}", "{0} — {1}, {2}" } },
        };

        // People and places in Russian. Settlements are a first part and a second part put together,
        // so both halves are listed; the saved name stays as it was made and only what is shown changes.
        private static readonly Dictionary<string, string> Ru = new Dictionary<string, string>
        {
            { "Hrafn", "Храфн" }, { "Ulf", "Ульф" }, { "Bjorn", "Бьёрн" }, { "Stein", "Стейн" }, { "Eik", "Эйк" }, { "Skog", "Ског" },
            { "Vind", "Винд" }, { "Frost", "Фрост" }, { "Gull", "Гулль" }, { "Jarn", "Ярн" }, { "Hval", "Хваль" }, { "Thor", "Тор" },
            { "Sig", "Сиг" }, { "Ask", "Аск" }, { "Dal", "Даль" }, { "Fjall", "Фьялль" }, { "Brim", "Брим" }, { "Ketil", "Кетиль" },
            { "Geir", "Гейр" }, { "Orm", "Орм" },
            { "heim", "хейм" }, { "stad", "стад" }, { "vik", "вик" }, { "gard", "гард" }, { "by", "бю" }, { "holt", "хольт" },
            { "nes", "нес" }, { "dal", "даль" }, { "borg", "борг" }, { "fell", "фелл" }, { "lund", "лунд" }, { "thorp", "торп" },
            { "Grak", "Грак" }, { "Zug", "Зуг" }, { "Mok", "Мок" }, { "Urg", "Ург" }, { "Naz", "Наз" }, { "Bol", "Бол" }, { "Dru", "Дру" },
            { "Skar", "Скар" }, { "Ghaz", "Газ" }, { "Tor", "Тор" },
            { "gash", "гаш" }, { "dum", "дум" }, { "rok", "рок" }, { "nak", "нак" }, { "ghul", "гул" }, { "tar", "тар" }, { "mog", "мог" },
            { "Dur", "Дур" }, { "Nid", "Нид" }, { "Brok", "Брок" }, { "Sind", "Синд" }, { "Dval", "Дваль" }, { "Mot", "Мот" }, { "Reg", "Рег" },
            { "Alv", "Альв" }, { "Thra", "Тра" }, { "Fund", "Фунд" },
            { "hold", "хольд" }, { "smid", "смид" }, { "berg", "берг" }, { "grof", "гроф" }, { "hall", "халль" },
            { "Moss", "Мохо" }, { "Root", "Корне" }, { "Thorn", "Терно" }, { "Bark", "Коро" }, { "Gloom", "Мрако" }, { "Fern", "Папоро" },
            { "Mire", "Топе" }, { "Knot", "Узло" },
            { "hollow", "лог" }, { "nest", "гнездо" }, { "thicket", "чаща" }, { "den", "нора" }, { "grove", "роща" }, { "burrow", "лаз" },
            { "Hel", "Хель" }, { "Nar", "Нар" }, { "Dauth", "Дауд" }, { "Grav", "Грав" }, { "Myrk", "Мюрк" }, { "Sot", "Сот" }, { "Rot", "Рот" },
            { "Bein", "Бейн" },
            { "haug", "хауг" }, { "grav", "грав" }, { "myr", "мюр" }, { "tun", "тун" }, { "hjem", "хьем" }, { "vard", "вард" },

            { "Ragnar", "Рагнар" }, { "Ivar", "Ивар" }, { "Gunnar", "Гуннар" }, { "Harald", "Харальд" }, { "Leif", "Лейф" },
            { "Torsten", "Торстен" }, { "Hakon", "Хакон" }, { "Eirik", "Эйрик" }, { "Knut", "Кнут" }, { "Olaf", "Олаф" },
            { "Sigurd", "Сигурд" }, { "Arne", "Арне" }, { "Rolf", "Рольф" }, { "Sten", "Стен" },
            { "Sigrid", "Сигрид" }, { "Astrid", "Астрид" }, { "Freydis", "Фрейдис" }, { "Ingrid", "Ингрид" }, { "Ylva", "Ильва" },
            { "Solveig", "Сольвейг" }, { "Thyra", "Тира" }, { "Gudrun", "Гудрун" }, { "Helga", "Хельга" }, { "Ragnhild", "Рагнхильд" },
            { "Liv", "Лив" }, { "Runa", "Руна" },
            { "Grok", "Грок" }, { "Zarg", "Зарг" }, { "Muk", "Мук" }, { "Urzh", "Урж" }, { "Nagh", "Наг" }, { "Bolg", "Болг" },
            { "Druz", "Друз" }, { "Skrag", "Скраг" },
            { "Durin", "Дурин" }, { "Brokk", "Брокк" }, { "Sindri", "Синдри" }, { "Dvalin", "Двалин" }, { "Nori", "Нори" },
            { "Regin", "Регин" }, { "Alviss", "Альвис" }, { "Thrain", "Траин" },
            { "Hrrk", "Хррк" }, { "Gnarl", "Гнарл" }, { "Mossk", "Мосск" }, { "Thrum", "Трум" }, { "Krrn", "Кррн" }, { "Brak", "Брак" },
            { "Hrolf the Drowned", "Хрольф Утопленник" }, { "Old Styr", "Старый Стюр" }, { "Agnar Bog-King", "Агнар, король болот" },
            { "Thorkel Rot", "Торкель Гниль" }, { "Yrsa Pale", "Бледная Ирса" },
        };

        /// <summary>
        /// A person's or a settlement's name as the player reads it: itself in English, in Russian
        /// letters and words when the mod speaks Russian. Names it does not know are left alone.
        /// </summary>
        public static string Name(string name)
        {
            if (Lang != 1 || string.IsNullOrEmpty(name)) return name;
            string known;
            if (Ru.TryGetValue(name, out known)) return known;
            // "Hrafnheim 12": the number some settlements carry stays as it is.
            int space = name.LastIndexOf(' ');
            string tail = "";
            int number;
            if (space > 0 && int.TryParse(name.Substring(space + 1), out number))
            {
                tail = name.Substring(space);
                name = name.Substring(0, space);
            }
            for (int i = 2; i <= name.Length - 2; i++)
            {
                string first, second;
                if (Ru.TryGetValue(name.Substring(0, i), out first) && Ru.TryGetValue(name.Substring(i), out second)) return first + second + tail;
            }
            return name + tail;
        }

        public static string Get(string key)
        {
            string[] entry;
            string other;
            if (Lang >= 2 && Lang < More.Length && More[Lang] != null && More[Lang].TryGetValue(key, out other)) return other;
            if (!T.TryGetValue(key, out entry)) return key;
            return entry[Lang == 1 ? 1 : 0];
        }

        /// <summary>
        /// One of the wordings of a line: `key` itself, `key1`, `key2`... as many as the table has.
        /// The salt picks which, so the same speaker says different things at different times.
        /// </summary>
        public static string Vary(string key, int salt)
        {
            int count = 1;
            while (T.ContainsKey(key + count)) count++;
            int pick = ((salt % count) + count) % count;
            return Get(pick == 0 ? key : key + pick);
        }

        public static string Get(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        public static string FactionName(Faction f)
        {
            if (f == null) return "?";
            CultureDef c = Defs.Culture(f.CultureId);
            string other;
            if (Lang >= 2 && Lang < More.Length && More[Lang] != null && More[Lang].TryGetValue("f_" + c.Id, out other)) return other;
            return Lang == 1 ? c.NameRu : c.NameEn;
        }

        public static string Describe(SimEvent e, WorldState state)
        {
            string key = "ev_" + e.Kind;
            string[] a = e.Args ?? new string[0];
            if (e.Kind == EventKind.Incident && a.Length > 2) key += "_" + a[2];
            if (e.Kind == EventKind.PlayerWar && a.Length > 2) key += "_" + a[2];

            Settlement s = state.GetSettlement(e.SettlementId);
            Settlement s2 = state.GetSettlement(e.OtherSettlementId);
            string text = Get(key);
            text = text.Replace("{f}", FactionName(state.GetFaction(e.FactionId)));
            text = text.Replace("{o}", FactionName(state.GetFaction(e.OtherFactionId)));
            text = text.Replace("{s2}", s2 != null ? Name(s2.Name) : "?");
            text = text.Replace("{s}", s != null ? Name(s.Name) : "?");

            switch (e.Kind)
            {
                case EventKind.BuildingStarted:
                case EventKind.BuildingCompleted:
                case EventKind.BuildingDestroyed:
                    text = text.Replace("{b}", a.Length > 1 ? Get("b_" + a[1]) : "?");
                    break;
                case EventKind.TierUp:
                    text = text.Replace("{t}", a.Length > 1 ? Get("tier_" + a[1]) : "?");
                    break;
                case EventKind.TradeCaravan:
                    text = text.Replace("{r0}", a.Length > 2 ? Get("res_" + a[2]) : "?").Replace("{r1}", a.Length > 3 ? Get("res_" + a[3]) : "?");
                    break;
                case EventKind.PlayerWar:
                case EventKind.PlayerPeace:
                case EventKind.PlayerAlliance:
                case EventKind.PlayerAllianceBroken:
                    text = text.Replace("{p}", a.Length > 1 ? a[1] : "?");
                    break;
                case EventKind.PlayerGift:
                case EventKind.PlayerKilledMember:
                    text = text.Replace("{p}", a.Length > 0 ? a[0] : "?");
                    break;
            }
            return text;
        }
    }
}
