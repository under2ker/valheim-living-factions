# Публикация Living Factions: что куда вставлять

Готовые тексты для загрузки архива. Сами тексты на английском (сайты англоязычные), пояснения
по-русски. Версия: **0.7.0**, архив `dist/LivingFactions-0.7.0.zip` (собрать, когда код готов: в архив
идут `dist/thunderstore/manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md` и
`plugins/LivingFactions.dll`).

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
- `docs/store/shields.jpg`: все семь щитов в руке (снято на сборке 0.7.0);
- `docs/screenshots/press/*.jpg`, `docs/screenshots/map_zones_0.4.2.jpg`,
  `docs/screenshots/village_paths_0.3.4.jpg`: снимки из игры для галереи.

Перед каждой новой версией: `python tools/store/render.py` перерисовывает иконку, обложку и баннер
с номером из кода мода, и обновляет адреса картинок в README. Для 0.7.0 это уже сделано.

Готово для 0.7.0 (21.10.2026): архив `dist/LivingFactions-0.7.0.zip` собран на финальной сборке, в
логе игры `Living Factions 0.7.0 loaded`, в архиве свежие README и CHANGELOG, иконка 256×256.

Перед публикацией 0.7.0 ещё нужно:

- поставить дату в заголовке `## 0.7.0` в `CHANGELOG.md` (сейчас `not released yet`), затем
  скопировать `CHANGELOG.md` в `dist/thunderstore/` и пересобрать архив (в нём тот же CHANGELOG);
- закоммитить и запушить: картинки в README и на Nexus берутся с GitHub (`main`), до пуша их там нет.
  Как с 0.6.5: коммит в локальную `master`, то же дерево одним коммитом в ветку `public`
  (она следит за `origin/main`), затем `git push origin public:main`;
- GitHub Release: тег `v0.7.0`, заголовок `Living Factions 0.7.0`, текст из
  [github-release.md](github-release.md), приложить `dist/LivingFactions-0.7.0.zip`.
