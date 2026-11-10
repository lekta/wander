# Wander — QA

Как проверяется продукт: уровни проверки, матрица «трогал → проверь», кто и
когда что гоняет, предрелизный прогон, метрики. Тесты живут только на Core;
всё выше (вьюмодели, XAML, фокус, DnD, COM) покрыто компилятором, smoke,
харнессом и чек-листом. Разделы (2026-11-09):

- [qa_harness.md](qa_harness.md) — стенды без окна, где всё крутится,
  песочница данных, оконный харнесс, шаги и сценарии;
- [qa_checklist.md](qa_checklist.md) — ручной чек-лист: только то, что
  видит человек, и костяк перед релизом.

## Уровни проверки

| # | Уровень | Ловит | Не ловит | Есть |
|---|---|---|---|---|
| 1 | Тесты Core (xUnit, фейки) | логику, машины решений, гонки в контроллерах | UI, COM, диск | да, `check.bat` |
| 2 | Сборка, `format`, `check-strings`, `deps.ps1` | компилируемость, ключи ресурсов, циклы папок | всё поведение | да |
| 3 | Smoke (`--smoke`) | XAML, ресурсы, регистрация, первый кадр и листинг | жесты, содержимое | да, `check.bat run` |
| 4 | Консольный харнесс на класс | конвертеры, `TreeNodeViewModel`, контроллеры, замер одной операции | окно | частично: `Wander.Harness selfcheck` (генераторы и фикстуры против читателей: RAW, EXIF, zip-документы, кодировки, теги WAV / mp3 / flac / m4a, меши); разовые — в scratchpad |
| 5 | Оконный харнесс за экраном | фокус, выделение, rename, раскладка, приземления, скриншоты, PerfLog / GC / память — на песочнице | обмен с системой, настоящий ввод ОС | **есть**: `tests/Wander.Harness`, [qa_harness.md](qa_harness.md) |
| 6 | UIA (FlaUI) на релизном exe | настоящий ввод, поставленный бинарник, DnD с Проводником | ничего сверх 5 по коду; хрупкие селекторы | нет; только Sandbox / VM |
| 7 | Человек по чек-листу | «прыгает или нет», обмен с системой, внешний вид | масштаб | [qa_checklist.md](qa_checklist.md) |
| 8 | Claude через 5 / 6 | сценарии, оценка скриншотов и логов, разведочные прогоны | то же, что 5 / 6 | qa_harness, «Автоматизация — харнесс» |

Правило: проверка, которую можно сделать тестом Core или харнессом, уходит
из чек-листов в код.

**Smoke** (уровень 3). `Wander.exe --smoke` = `App.Headless` (qa_harness, «Окно не
должно оказаться на экране») + `StartSmokeCountdown`: две секунды на
листинг, значки, наблюдателей, затем `Shutdown(0)`. `check.bat run` зовёт
exe напрямую (не `start`) с `--folder` на `tests\Fixtures` и читает код;
ловушки cmd: `if errorlevel 1` не видит .NET-падения (0xE0434352 —
отрицательное), нужно `neq 0`; `exit /b` внутри скобок код не доносит.

Стенды без окна (уровень 4) — qa_harness, «Стенды без окна».

## Матрица «трогал → проверь»

| Трогал | Автоматически | Руками (раздел qa_checklist) | В логе |
|---|---|---|---|
| Навигация, `FolderSession`, приземление, место выделенной строки | `FolderSessionTests`, `ListingDiffTests`, `ListRulesTests` (`ListingArrival`), `RowFollowingTests`, `PathFollowingTests`; харнесс: серия навигаций, выделение после подъёма, `follow-row` (строка на месте: сторож, `F5`, порядок, фильтр, выдача поиска, смена вида) | Скорость; Фокус («Выделенный файл не уходит с экрана сам») | `nav.*`, `list.apply`, `ui.stall`, `First screen painted`, `WS ListingLanded` (с `LogActions`) |
| Иконки, миниатюры, шлюз, приоритеты | `FolderPictureTests`, `DesktopIniTests`, `WaveformSketchTests`; харнесс: холодная `photos`, счётчики | Скорость (все пункты — трижды ломали друг друга); Дерево (иконки после запуска); Просмотр и галерея («Миниатюры папок», «Заглушка, пока миниатюра делается», «Волна на плитке трека») | `bg.thumb-*`, `bg.folder*`, `bg.waveform-sketch`, `icon.decode-ui`, `layout.*`, `[icon-diag]`, `slow shell load`, `folder picture failed`, `First screen painted` (`apply` — десятки мс) |
| Шаблон плитки, `VirtualizingWrapPanel`, `TileLayout` | `TileLayoutTests` (в том числе якорь перелива), `TileMetricsTests`; харнесс: проходы раскладки в простое = 0, `visuals` на шаблон | Скорость (смена вида); Фокус (`F2` в четырёх видах); Просмотр (миниатюры не прыгают, перелив плиток держит видимое); Файловые операции (корзина в «Плитке») | `LAYOUT … visuals`, `COUNT layout.*`, `layout.realise` |
| Выделение, фокус, области, rename; модель окна (`Core/Workspace`, `WorkspaceController`, исполнитель в `MainWindow`) | `GridNavigationTests`, `WindowZonesTests`, `KeyboardRulesTests`, `ListRulesTests`, `PanelRulesTests`, `TargetRulesTests`, `MenuContextTests`, `PreviewSubjectTests`; харнесс: клавиатурные сценарии, `post`, `assert-state`, `assert-focus` | Фокус, выделение и клавиатура — целиком; Дерево и закладки | `WS …`, `WS target:` (с `LogActions`) |
| Файловые операции, undo, конфликты, спутники | `BatchExecutorTests`, `FileOperationServiceTests`, `UndoServiceTests` (`Forget` после безвозвратного удаления), `CompanionResolverTests`, `ConflictVerdictTests`, `ConflictBatchTests`, `MergeScannerTests`, `FileContentComparerTests`, `PathSafetyTests`, `SystemPathGuardTests`; харнесс: `file-ops` | Файловые операции; **Окно совпадений имён** (харнесс его не поднимает: `ScriptedDialogs` отвечает по политике); Обмен с системой | `ERROR` / `WARN`, строки операций |
| Прогресс, окно операции, строка состояния, выход при операциях, часы на значке | `OperationTrackerTests` (в том числе `WhenIdle`), `TransferRateTests`, `BatchExecutorTests` (байты, отмена внутри файла, откат группы с другого диска), `PathClaimsTests` (задержка часов) | **Ход операции** — целиком, окно и выход при операциях харнесс не проверяет; Файловые операции (короткая операция без часов) | `cancelled part-way`, `Cannot size … for the progress bar` |
| Буфер обмена, DnD | `ClipboardControllerTests`, `DragHoverTests`, `EdgeScrollTests`; харнесс: системный буфер читается из процесса | Обмен с системой — только вживую; Дерево и закладки (вглубь, у края) | — |
| Поиск, экстракторы, поле над списком | `ContentSearchServiceTests`, `SearchControllerTests`, `SearchExpressionTests`, `TypeFilterTests`, `ShotQueryTests`, `NameFilterTests`, `ContentExtractorTests`, `ResultPathTests`; харнесс на `docs` / `big` | Просмотр (результаты в видах); Фокус (поле поиска, тип, «в подпапках», параметры съёмки) | счётчик «не прочитано», время поиска |
| Панель просмотра, форматы, сплит, «Сравнить», полный экран | `AudioTagsTests`, `MeshFileTests`, `ModelFormatsTests`, `FbxReaderTests`, `BlendReaderTests`, `MediaProbeTests`, `SheetReaderTests`, `EmbeddedThumbnailTests`, `ContentSnifferTests`, `PsdDecoderTests`, `FontHeaderTests`, `Fb2DocumentTests`, `EncodingProbeTests`, `PreviewRouterTests`, `PreviewPairTests`, `SplitOrientationTests`, `ShotSummaryTests`, `FullscreenPlanTests`, `PictureWalkTests`, `ZoomLinkTests`, `PictureZoomTests`, `PictureFitTests`, `PreviewNeighborsTests`, `TextFindTests`, `TgaDecoderTests`, `PeHeaderTests`, `ContentExtractorTests` (абзацы), `ThumbnailCacheOptionsTests`, `PictureMemoryTests`, `SizedCacheTests`, `UnifiedDiffTests`, `FileDiffTests`; харнесс: скриншот каждой ветки (сплит, полный экран и сводку по нескольким не рисует) | Просмотр и галерея | `nav.preview`, `Preview pair: shapes not read`, `Compare: <ответ> in N ms`, ошибки биндингов (трассировка) |
| Хелперы отсмотра (пикинг, резкость, клиппинг, гистограмма, кривые, точка AF) | `SharpnessTests`, `FocusPeakingTests`, `ToneCurveTests`, `CanonAfInfoTests`, `AfGeometryTests`, `LumaTests`, `HistogramTests`, `ClippingTests`, `SharpListingTests`; харнесс: шаг `helpers` в `preview-formats` (скриншоты) | Просмотр (совпадение наложения с картинкой при разной ширине панели, в лупе, в сплите, при 125 / 150 %); Скорость (прокрутка галереи с включённым пикингом) | `bg.helpers`, `bg.sharpness` |
| Галерея, оценки, сайдкары | `RatingFilterTests`, `RatingToggleTests`, `ImageFolderProbeTests`, `DesktopIniTests`, `CompanionMetadataServiceTests`; харнесс: клик по звезде не пересобирает | Фокус (клик по звезде, `Shift`+цифры); Просмотр | `bg.ratings`, тик сторожа |
| Контекстное меню, shell | `ContextMenuBuilderTests`, `ShellExtensionCatalogTests`; харнесс: меню строится без исключений | Обмен с системой (сторонние пункты запускаются); Просмотр и галерея («Сортировка — у папки, меню пустого места») | время `QueryContextMenu`, owner-drawn в логе |
| Настройки, `state.json`, `folders.json`, палитра | `AppPathsTests`, `PaneSizesTests`, `ViewChoiceTests`, `FolderSettingsBookTests`; `check-resources.ps1` (ключи XAML); харнесс: `state` из фикстур (`state-upgrade`: `assert-folders`) | После обновления; две копии на одних данных (`--yield`) — только вживую; Просмотр и галерея («Вид — у папки», «Сортировка — у папки») | `WARN` при загрузке состояния, `State loaded` / `State written`, `Pane sizes:`, `Started with --yield`, `Sort pinned:` / `Default sort:` |
| Дерево, закладки, сторож | `PanelRulesTests`, `PanelKeyNavigationTests`, `PanelViewTests`, `PanelPathsTests`, `TreeNavThrottleTests`, `FolderChangesTests`, `NavigationFallbackTests`, `BranchReconcileTests` (уровень панели: строки живут, вставки / перестановки / удаления вокруг), `PathFollowingTests`, `UndoableActionsTests` (пары `MovesOnUndo`), `NavigationServiceTests` (`RewritePaths`), `VolumeInfoTests` (строка диска в «Компьютере»), `BookmarkTitlesTests` (своё название, следование, показ закладки устройства); харнесс: `tree-bookmarks` (таблица сценариев), внешнее создание файла в песочнице → строка появилась | Дерево и закладки | `nav.trees`, `nav.watch`, `WS …` (с `LogActions`) |
| Архивы как папки, извлечение, панель просмотра | `ArchivePathTests`, `ExtractionServiceTests`, `PreviewRouterTests`, `ContextMenuBuilderTests` («Извлечь рядом»); харнесс: `selfcheck` (ассоциации машины) + сценарий `archives` | Архивы как папки (извлечь рядом, перетаскивание из дерева); Файловые операции; Обмен с системой (наружу — только глазами) | `Extract:`, `Extract (temporary copy)`, `Archive enumerate`, «Archives open as folders» в первых строках лога |
| Телефон и камера: `DevicePath`, `Containers`, импорт и пауза, WPD, узел устройств | `DevicePathTests`, `DeviceClutterTests`, `DeviceImportServiceTests` (пауза, в том числе при подсчёте; 10 минут; совпадения; сбой файла папки), `HeadStreamTests` (голова RAW для просмотра), `ConflictVerdictTests` («идентичны» по размеру и дате), `NavigationFallbackTests`, `ContextMenuBuilderTests`; харнесс: `devices` (телефон сценариев — WPD и шелл не проверяет) | живой прогон с телефоном и камерой (FILEOPS «Телефон и камера»): листинг большой папки, миниатюры, копия, выдернутый кабель (посреди файла и при подсчёте большой папки), запертый телефон, другой порт | `Devices:`, `Device arrived / removed`, `Import:`, `the device went away` / `is back` |
| Действия, каталог, запуск; групповое переименование | `ActionApplicabilityTests`, `CommandLineTests`, `ExternalActionRunnerTests`, `ActionCatalogTests`, `ActionReportTests`, `BuiltinArgumentsTests`, `ContextMenuBuilderTests`, `RenamePlannerTests`, `DatePatternTests`, `HoldFileActionTests`; харнесс: `smoke-walk` (меню), `focus-keys` (`F2`) — окна действий и переименования он не поднимает | **Действия** — целиком; Ход операции (окно действия) | `Action '…'`, `image-convert:`, `Batch rename:`, `Rename:` |
| Цвет: палитра, `StockControls.xaml`, тема, `ThemeScope`, `GalleryPalette` | `ToneTests`, `UiThemesTests`; `check-resources.ps1` (ключи двух тем совпадают); стенд отрисовки: светлая тема против Aero2 попиксельно, страницы настроек в обеих темах | Настройки («Цветовая схема»); Окружения (тёмная Windows); Просмотр и галерея (фон галереи) | `Theme: …` |
| Строки, язык, `Strings.en.resx`, руководство и сайт | `check-strings.ps1` (ключи, плейсхолдеры и формы обоих языков), `UiLanguagesTests`, `PluralFormTests`; шаг «site» (`GUIDE.en.md` в пару, цели `guideEn`) | Настройки («Интерфейс»); английский интерфейс — окна по ширине | `Interface language: …` |
| Сборка, поставка, версия, номер сборки | `check.bat run`, `size-report.ps1`, `AppStateVersionTests` | После обновления; чистая машина | `Startup: first frame`, первая строка лога — версия с номером сборки |

## Кто и когда гоняет

**Кто что делает** (команда и правило раздела — WORKFLOW.md: Фабле —
решения и риск, Опус — механика):

| Роль | Кто |
|---|---|
| Спроектировать сценарии под правку, разобрать падение, оценить скриншоты на UX-регрессию, разведочный «ручной» прогон через харнесс шаг за шагом | Фабле |
| Прогнать набор, сверить лог и метрики с порогами, собрать отчёт, разложить находки по спискам | Опус |
| Всё с настоящим Проводником, мышью и глазом; экзотические тома; решение «релиз / не релиз» | человек |

Claude никогда не ведёт настоящий рабочий стол: харнесс — только окно за
экраном; UIA — только внутри Sandbox / VM, где чужого экрана нет.

**Когда что гоняется.** Чем дороже прогон, тем ближе он к концу работы, а
не к каждой правке.

| Повод | Что запускается |
|---|---|
| Любая правка кода | `check.bat` |
| Тронуты XAML, ресурсы, регистрация сервисов, старт | + `check.bat run` |
| Тронуты генераторы песочницы или состав `tests/Fixtures/` | `selfcheck` (секунды) |
| Правка в области, у которой есть сценарий (матрица «трогал → проверь») | этот сценарий, `--rebuild` если менялся состав песочницы — по слову «прогони автотесты <сценарий>» |
| Не воспроизводится по коду: «прыгает фокус», «пропала миниатюра», «что там в панели» | сценарий-однодневка рядом с нужной папкой, находки — `note` в отчёт — по просьбе |
| Финализация куска работ («финализируй») | `check.bat run`; `check.bat qa` и сценарии задетой фичи Claude называет и ждёт слова «прогони» |
| **Подготовка к релизу версии** (`X.Y.0`, PRERELEASE.md) | lint Rider по `src` + весь набор + `soak` с `"minutes": 30` + замеры версии |
| **Подготовка к патч-релизу** (`X.Y.Z`, PRERELEASE.md, «Ветка патча») | lint изменённых файлов, `check.bat run`, `check.bat qa`, сценарии задетого по матрице; `soak` — если тронуты память, кэши, миниатюры, сторож |

**Сценарии харнесса запускаются по словам** — слова, кто запускает и что
кладёт в отчёт: WORKFLOW, «Автотесты»; причина — qa_harness, «Диалоги тоже».

## Предрелизный прогон

Дополняет PRERELEASE.md; порядок — от дешёвого к дорогому, регрессия на
любом шаге останавливает релиз.

1. **База**: `check.bat run`, `deps.ps1` без циклов, `metrics.ps1` снимок,
   доки сверены (FINALIZING, п. 5).
2. **Релизная сборка**: `publish.ps1`, размер и SHA, `size-report.ps1`
   против PERFORMANCE (категории не выросли без причины).
3. **Первый запуск и обновление**: чистая машина (Sandbox) — SmartScreen,
   без `state.json`, умолчания, первая папка; поверх прошлой версии —
   `state.json` и кэш.
4. **Функциональный прогон** на песочнице релизным exe: «Всегда» + все
   разделы чек-листов; автоматизированная часть — харнессом, остальное
   руками.
5. **Окружения** — раздел qa_checklist, хотя бы DPI и один «странный» том.
6. **Замеры** — таблица ниже, все строки; плюс 30-минутный soak: случайная
   навигация по песочнице — память выходит на плато, handles стабильны, лог
   без `ERROR`, CPU в простое ~0 %.
7. **Итог**: числа — `measurements/<версия>.md` (PRERELEASE, шаг 6),
   находки разложены по спискам, exit-критерии: 0 открытых P0 / P1,
   `WARN` / `ERROR` в сессиях релизного прогона — ноль, метрики в порогах.

## Метрики и пороги

Порог = базлайн прошлой версии (PERFORMANCE, `measurements/`) + 20 %; хуже
— регрессия. Первые базлайны для новых строк снимаются первым прогоном.

| Метрика | Как снять | Порог / базлайн |
|---|---|---|
| Первый кадр | `Startup: first frame N ms`, три запуска релизного exe | ~1650–2050 мс (PERFORMANCE) |
| Неответ UI | `PERF ui.stall` | одиночные < 400 мс, частые < 100; на холодной папке — сотни, не секунды |
| Первый экран | `First screen painted in N ms` по профилю `photos` холодно / тепло | базлайн снять |
| Шаблон плитки | `LAYOUT <вид> container: N visuals` | Tiles 8, Icons 8 (место под бейдж оценки, J4), Gallery 9 (без оценки) |
| Реализации на навигацию | `COUNT layout.new: N in K passes` | K = 1 |
| Раскладка в простое | харнесс: проходы за 5 с без ввода | 0 |
| Миниатюры RAW | `bg.thumb-shell` / `RawThumbnail`, мс на файл | ~3 мс WinRT, 75 через оболочку — регресс |
| Память | `SYS` или харнесс: `WorkingSet64`, `PrivateMemorySize64` — пик и плато после 200 навигаций | smoke 344 / 225 МБ; плато — снять |
| GC | `GC.CollectionCount(0/1/2)`, `GetTotalAllocatedBytes` за окно, `GetGCMemoryInfo` (LOH, % в GC) | gen2 не растёт линейно с навигациями; аллокации на навигацию — снять |
| Handles, потоки | `Process.HandleCount`, `Threads.Count` в soak | стабильны после разогрева |
| CPU | `TotalProcessorTime` дельта: в простое 10 с | ~0 %; таймеры останавливают себя |
| Диск | размер `thumbs` до / после, `bg.thumb-disk-write` | в пределах лимита, подрезка срабатывает |
| Закрытие | `OnClosing` → смерть процесса | окно исчезает сразу; процесс < 2 с |
| Лог | размер файла сессии, повторяющиеся строки | нет заливки одинаковыми исключениями |
| Поиск | время по `big` (5000 файлов) и `docs` с кэшем / без | 0,2 с обход репозитория; кэш ×5 |

**Строка `SYS` в логе** (`SystemVitals`): heartbeat `UiStallWatch` раз в
5 с и на каждый `ui.stall` пишет `SYS ws=… private=… gen=0/1/2 alloc=+N
loh=… handles=… threads=… cpu=… gcpause=+N` (МБ, мс) — замеры и в живых
сессиях, не только в харнессе. Глубже — `dotnet-counters`, `dotnet-trace` /
PerfView, dotTrace из Rider: по требованию, не в прогон.

## Что ещё берут из практики QA

- **Находка = шаги, ожидание, факт, строка версии (`v… R, коммит, дата`),
  лог сессии, скриншот.** Для лога, который читать самому, — в «Отладке и
  сбросе» «Писать настоящие пути к файлам» и «Записывать клавиши, клики и
  выделение» (по умолчанию пути — метки, действий нет). Severity: P0
  потеря данных / падение, P1 не работает фича, P2 неудобно, P3 косметика.
- **Теги набора**: smoke (минуты) / regression (час) / release (день);
  флаки считаются и чинятся, не «перезапустил».
- **Базлайны по релизам** — `measurements/`, по файлу на версию; тренд
  важнее одного числа.
- **Сценарии потери данных**: kill на середине копии, диск заполнен (VHDX
  50 МБ), `state.json` при внезапном закрытии, два экземпляра Wander на
  одном кэше.
- **Первый запуск / обновление / удаление** (портативный: удалить папку
  данных) — отдельный проход, не «и так работает».
- **Доступность-минимум**: клавиатурой всё, DPI, контраст; Narrator и
  `AutomationProperties` — когда дойдут руки.
- **Безопасность превью**: html с JS и внешними ресурсами, имена с `..`,
  циклы junction, будущие архивы — zip-бомба и `../` в записях.
