# Wander — индексация и продвижение

Как Wander находят: поисковики, каталоги, сообщества. Что сайт делает
сам — SITE, «Поисковики»; здесь — шаги человека, площадки и правила.

## Где мы (2026-10-07)

`site:lekta.github.io` — ноль в Яндексе, Google и Bing; страница
репозитория тоже не в индексе. Не поломка: сайт с 25 сентября, ссылок на
него нет, в кабинетах вебмастера не зарегистрирован. Имя «Wander» занято
плотно (Wander Browser в Microsoft Store, wander.com, VR-приложение, игра
для PS4, плагин WordPress): голый запрос «скачать wander» не выиграть.
Цель — связки «wander файловый менеджер», «wander медиа-менеджер», «wander
lekta» и запросы по сути: «файловый менеджер windows 11», «замена
проводника», «программа для отбора фотографий», «просмотр raw». Частоты —
Wordstat (wordstat.yandex.ru), под логином.

## Индексация — шаги человека

Сайт отдаёт `sitemap.xml`, canonical, описание и og-теги у каждой
страницы и после каждого тега `site-*` сам сообщает страницы Яндексу и
Bing (IndexNow). Остальное — руками, один раз.

1. **Google Search Console** (search.google.com/search-console). «Добавить
   ресурс» → «Ресурс с префиксом в URL» → `https://lekta.github.io/wander/`.
   Подтверждение «HTML-тег»: строку `<meta name="google-site-verification"
   content="…">` — в `docs/site/index.html` после `<meta name="description">`,
   тег `site-*`, после деплоя — «Подтвердить». Затем «Файлы Sitemap» →
   `sitemap.xml`; «Проверка URL» → лендинг и `guide/nachalo-raboty/` →
   «Запросить индексирование». Строку не удалять: Google перепроверяет.
2. **Яндекс Вебмастер** (webmaster.yandex.ru). Принимает хост, не папку, а
   `lekta.github.io/` отдаёт 404 — нужен корень хоста: репозиторий с
   именем ровно `lekta.github.io` (Settings → Pages → Deploy from a
   branch, `main`, `/ (root)`). `lekta/lekta` — README профиля, Pages из
   него — `lekta.github.io/lekta/`. Папка `D:\Dev\lekta\lekta` готова
   (2026-10-07): `index.html` — лендинг проектов, `robots.txt` со строкой
   `Sitemap: https://lekta.github.io/wander/sitemap.xml`, `.nojekyll`;
   remote сменить на `lekta.github.io`. В кабинете «Добавить сайт» →
   `https://lekta.github.io` → способ «HTML-файл» → скачанный
   `yandex_<код>.html` положить рядом с `index.html`, запушить,
   «Проверить» (до суток). Затем «Индексирование» → «Файлы Sitemap» →
   адрес выше, «Переобход страниц» → лендинг и две-три страницы
   руководства.
3. **Bing Webmaster Tools** (bing.com/webmasters) — «Импорт из Google
   Search Console», без отдельного подтверждения.
4. Через одну-две недели — `site:lekta.github.io` в трёх поисковиках и
   отчёты кабинетов («Страницы в поиске», «Покрытие»). Google — дни,
   Яндекс — до пары недель.

Свой домен GitHub Pages принимает (CNAME); тогда `SiteUrl` в
`SiteBuilder.cs` и шаги выше — заново.

## Площадки

Порядок — по отдаче на час работы. Везде: ссылка на сайт и релиз, хэш и
предупреждение о SmartScreen, скриншоты из `docs/screenshots`.

**Пакетные менеджеры и каталоги** — работают сами, годами.

- winget: PR в `microsoft/winget-pkgs`, `InstallerType: portable`
  («Пошагово» ниже); `winget search wander` находит, winstall.app и
  winget.run индексируются поисковиками. В README — второй способ
  установки.
- Scoop: PR в бакет `extras`; переносимый exe — как раз.
- AlternativeTo: «Suggest new application», отметить альтернативой к
  Windows Explorer, FastStone Image Viewer, XnView MP, Files, Total
  Commander — такие страницы стоят в выдаче по «alternative to …».
- Русские каталоги: comss.ru («Предложить программу»), softportal.com
  («Добавить программу», проверка 1–30 дней), freesoft.ru, softcatalog.
- Международные: Softpedia (submit), MajorGeeks (берут не всё), FileHorse.
- Microsoft Store — после локализации (решение человека 2026-10-07): с
  2025 бесплатно для частных лиц (паспорт и селфи); Win32 — exe или msi с
  установщиком либо MSIX, который Store подписывает сам, и SmartScreen
  молчит. Имя «Wander» в Store может быть занято — проверить при
  резервировании.
- GitHub: темы репозитория добавить `photo`, `raw`, `image-viewer`,
  `culling`, `media-manager`, `windows-10`; Settings → Social preview —
  `og.jpg`; английский блок в README; PR в `Awesome-Windows/Awesome`
  (раздел File Management).

**Русские сообщества.**

- Хабр: статья в своём блоге — техническая история (WPF и .NET 10, почему
  Проводник тормозит, как проверяется надёжность), одна ссылка на GitHub в
  конце; либо один пост в хабе «Я пиарюсь». Второй промо-пост — минус в
  карму и скрытие. Аккаунт без инвайта — через «Песочницу»: регистрация
  даёт read-only, статья пишется в Песочницу, одобрение модератора
  открывает полный аккаунт; тема — своя, техническая, не реклама (первая
  статья о Wander подходит). Инвайт может дать и любой полноправный
  пользователь. Q&A (qna.habr.com), как правило, доступен и с read-only.
- Хабр Q&A, Пикабу (профильные сообщества по программированию и
  фотографии, тег «своё»), vc.ru (личный блог); DTF — не про то.
- 4PDA, раздел «Софт» для ПК: тема о программе от разработчика (статус
  «Разработчик», полноразмерные скриншоты без водяных знаков, exe на
  сервере 4PDA, обновления не чаще раза в неделю).
- Форумы: ru-board («Программы»), forum.ixbt.com (фото-софт),
  club.foto.ru; группы фотографов во ВКонтакте и Telegram — только по
  правилам группы, лучше через администратора.
- Видео: короткий ролик на YouTube, VK Видео, RuTube; предложить
  обзорщикам софта.

**Международные** — решение человека 2026-10-07: после локализации
приложения и витрины (PLAN, «Витрина и продвижение»); AlternativeTo —
листинг, не продвижение, можно раньше.

- Reddit: r/dotnet и r/csharp (флаер Showcase), r/SideProject, r/software
  и r/Windows11 (правила про self-promotion у каждого свои),
  r/photography — только в ответ на вопрос. r/opensource — нет: лицензия
  не OSI, назовут обманом.
- Hacker News: «Show HN: Wander – a Windows file manager built for photo
  culling» — что и зачем, без маркетинга; отвечать в комментариях.
- Product Hunt — когда есть английская страница.
- Форумы фотографов: dpreview (PC Talk), fredmiranda.com.

## Пошагово: winget и AlternativeTo

**winget** — манифест в общем репозитории Microsoft; после слияния
`winget install Lekta.Wander` ставит, `winget upgrade` обновляет. Подпись
exe не нужна. Предупреждений станет меньше — нет страницы «скачать» в
браузере и вопроса «открыть файл?», ссылку и хэш проверил Microsoft, — но
SmartScreen на неподписанный exe при первом запуске может сработать как
прежде: его снимают только подпись (Store подписывает MSIX бесплатно) или
накопленная репутация файла, которая обнуляется с каждой версией.

1. Что нужно: прямая ссылка на exe релиза
   (`…/releases/download/v0.5.0/Wander.exe`) и аккаунт GitHub. Хэш
   считает инструмент.
2. `winget install Microsoft.WingetCreate`, затем
   `wingetcreate new https://github.com/lekta/wander/releases/download/v0.5.0/Wander.exe`.
   Мастер спросит: идентификатор — `Lekta.Wander`; версия — `0.5.0`; тип
   установщика — `portable`; алиас команды — `wander`; издатель — Lekta;
   название — Wander; короткое описание по-английски (локаль манифеста
   `en-US`): «File and media manager for Windows 10/11: instant photo,
   RAW and video preview, photo culling, batch operations. Russian UI»;
   лицензия — `PolyForm Noncommercial 1.0.0` и ссылка на LICENSE; сайт —
   `https://lekta.github.io/wander/`; теги — file-manager, photo-viewer,
   raw, culling. Получится папка `manifests/l/Lekta/Wander/0.5.0/` с тремя
   YAML: версия, установщик, локаль; русскую локаль
   `Lekta.Wander.locale.ru-RU.yaml` можно добавить руками по образцу.
3. Проверить у себя (один раз от администратора `winget settings
   --enable LocalManifestFiles`): `winget validate <папка>`, `winget
   install --manifest <папка>`, запустить `wander`, `winget uninstall
   Lekta.Wander`. Переносимый пакет winget кладёт в
   `%LOCALAPPDATA%\Microsoft\WinGet\Packages\…` и делает ссылку
   `wander.exe` в `…\WinGet\Links` (он в PATH); папки `data` рядом нет,
   данные — в `%LOCALAPPDATA%\Wander`, как обычно.
4. Отправить: `wingetcreate submit <папка>` (форк и PR от твоего имени,
   спросит токен GitHub) либо форк `microsoft/winget-pkgs` руками. В PR
   бот попросит подписать Microsoft CLA — один раз, кнопкой.
   Автопроверка — час-два, модератор — день-три; метки
   `Validation-Completed` → `Moderator-Approved` → слияние, в каталоге —
   через час.
5. Каждый релиз: `wingetcreate update Lekta.Wander --urls <ссылка на
   новый exe> --version 0.5.1 --submit`. Позже — шагом в `release.yml` с
   токеном.
6. После слияния — в README раздел «Установка»: скачать exe или `winget
   install Lekta.Wander` (PLAN, «Витрина и продвижение»).

**AlternativeTo** — каталог «чем заменить»: страница Wander и места в
списках альтернатив Проводнику, FastStone, XnView. Листинг, не реклама;
язык — английский.

1. Аккаунт на alternativeto.net (email или GitHub).
2. Меню пользователя → «Suggest new application». Поля: название Wander;
   сайт `https://lekta.github.io/wander/`; описание по-английски, два-три
   предложения, с оговоркой «Russian-language UI»; платформы — Windows;
   лицензия — Free (не Open Source: PolyForm Noncommercial —
   source-available); категории — File Manager, Image Viewer, Photo
   Management; теги — file-manager, photo-viewer, raw-viewer,
   photo-culling, explorer-alternative; иконка — `docs/app_ico.png` (64 px;
   попросят крупнее — отрисовать 256); скриншоты — `docs/screenshots`
   (WebP могут не принять — конвертировать в PNG).
3. «Alternative to»: Windows File Explorer, FastStone Image Viewer, XnView
   MP, IrfanView, FastRawViewer, Photo Mechanic, Files, Total Commander,
   Directory Opus.
4. Модерация — от часов до дней. После — на странице приложения «Claim
   this app» как разработчик: правка описания и пометка.
5. За себя не голосовать, друзей не звать: накрутку ловят и снимают
   приложение. Отзывы — только настоящие.

## Конкуренты

Живые на 2026-10; их отзывы смотреть раз в полгода: AlternativeTo
(комментарии и лайки), Reddit r/Windows11 и r/photography, форум
dpreview, Хабр Q&A, темы на 4PDA, отзывы в Microsoft Store и на comss.

**Файловые менеджеры.**

- **Files** (files.community) — открытый, WinUI 3: вкладки, две панели,
  теги, вид «как в Windows 11»; жалобы на скорость и стабильность.
- **File Pilot** (filepilot.tech, 2025) — очень быстрый, новый, инспектор
  файлов, пакетное переименование; платный после беты. Ближайший по
  «быстро и чисто».
- **Directory Opus** — эталон мощности: две панели, просмотрщик, скрипты,
  всё настраивается; дорогой.
- **Total Commander** — две панели, плагины, скорость; интерфейс из 90-х.
- **XYplorer** — портативный, вкладки, скрипты, быстрый поиск; платный.
- **One Commander** — современный вид, колонки Миллера, превью; бесплатен
  для личного.
- **Explorer++**, **Q-Dir**, **Double Commander**, **FreeCommander** —
  бесплатные с вкладками и панелями; старомодные.

**Просмотр и отбор фото.**

- **FastStone Image Viewer** — бесплатный, быстрый, полный экран с
  выезжающими панелями, RAW; давно не меняется. Ориентир по скорости.
- **FastRawViewer** — RAW как есть, не вшитый JPEG: фокус-пикинг,
  экспозиция, оценки в XMP; платный. Ближайший по отбору.
- **Photo Mechanic** — стандарт отбора у репортёров: скорость,
  метаданные, IPTC; дорогой.
- **XnView MP** — 500 форматов, каталог, пакетная конвертация; бесплатен
  для личного.
- **IrfanView** — скорость, плагины; интерфейс старый.
- **ImageGlass**, **nomacs**, **qView**, **Honeyview** — бесплатные
  просмотрщики.
- **Adobe Bridge** (бесплатен), **Lightroom**, **ACDSee**, **digiKam** —
  каталоги и обработка.
- **AfterShoot**, **Narrative Select**, **FilterPixel** — отбор нейросетью
  (глаза, резкость, дубли); тренд, следить.

## Как не выпереть

1. **Правила площадки прочитать** до поста: пункт про рекламу или
   self-promotion, нужный флаер или тег, порог кармы и возраста аккаунта.
2. **Сначала участник, потом автор**: неделя-две обычных ответов; на
   Reddit — не больше десятой части постов о своём.
3. **«Я автор» — первой строкой.** Скрытая реклама — бан, открытая —
   обсуждение.
4. **Польза, не реклама**: история, проблема и решение, что узнал,
   скриншот или GIF; ссылка одна, в конце; без «лучший» и «уникальный».
5. **Один пост на площадку.** Не копировать текст по всем сразу, не
   поднимать, удалённое не перепостить; вернуться с «что изменилось» —
   через месяцы, с новой версией.
6. **В чужих темах** — только когда спрашивают «чем заменить», с пометкой
   автора, не шаблоном.
7. **Отвечать всем**, критику принимать, с минусами не спорить; баг из
   комментария — в issue и починить.
8. **Без накрутки**: ни вторых аккаунтов, ни просьб «плюсаните» — Reddit
   и HN это видят.
9. **Честные слова**: «source-available, бесплатно для личного», не «open
   source»; про SmartScreen и хэш — сразу.
10. **Язык площадки**: по-английски — туда, где есть английская страница.

## Что подготовить

- Два текста: два-три предложения и абзац, по-русски и по-английски.
- GIF на десять секунд: открыть папку, галерея, оценки, панель просмотра.
- Ссылка VirusTotal на `Wander.exe` релиза и хэш со страницы релиза.
- Ответы на частое: SmartScreen, почему не подписан, лицензия, WebView2.
