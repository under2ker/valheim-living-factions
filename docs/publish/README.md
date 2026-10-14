# Публикация Living Factions: что куда вставлять

Готовые тексты для загрузки архива. Сами тексты на английском (сайты англоязычные), пояснения
по-русски. Версия: **0.6.5**, архив `dist/LivingFactions-0.6.5.zip`.

| Сайт | Файл с текстами |
|---|---|
| Thunderstore | [thunderstore.md](thunderstore.md) |
| Nexus Mods | [nexus.md](nexus.md) |
| Hexium | [hexium.md](hexium.md) |
| Discord и соцсети | [announce.md](announce.md) |
| GitHub Release | [github-release.md](github-release.md) |

Картинки для сайтов лежат в `docs/store/` и `docs/screenshots/`:

- `docs/store/icon.png`: иконка 256×256 (Thunderstore берёт её из архива сама);
- `docs/store/cover.jpg`: обложка 1280×720, главная картинка на Nexus;
- `docs/store/banner.png`: баннер 960×240, шапка для Discord или Nexus;
- `docs/store/axes.jpg`: все семь топоров в руке;
- `docs/store/swords.jpg`: все семь мечей в руке;
- `docs/screenshots/press/*.jpg`, `docs/screenshots/map_zones_0.4.2.jpg`,
  `docs/screenshots/village_paths_0.3.4.jpg`: снимки из игры для галереи.

Перед каждой новой версией: `python tools/store/render.py` перерисовывает иконку, обложку и баннер
с номером из кода мода, и обновляет адреса картинок в README.
