# Wander — архитектура и механизмы

Как устроен код и как работают ключевые механизмы. Это справочник «где что
лежит и почему так», а не список задач — задачи в [PLAN.md](PLAN.md),
[BACKLOG.md](BACKLOG.md), [TECHDEBT.md](TECHDEBT.md).

Точка входа во всю документацию — [CLAUDE.md](../CLAUDE.md).

---

## Слои

Четыре проекта, `Wander.slnx`:

| Проект | TFM | Роль |
|---|---|---|
| `Wander.Core` | `net10.0` | Чистая логика и абстракции. Не знает про Windows и про UI. |
| `Wander.Platform.Windows` | `net10.0-windows10.0.19041.0` | Реализации интерфейсов Core: Win32, Shell COM, WinRT, `System.IO`. |
| `Wander.App` | `net10.0-windows10.0.19041.0` | WPF UI: окно, ViewModel'и, диалоги, конвертеры. |
| `Wander.Core.Tests` | `net10.0` | xUnit. Тестирует **только** Core через фейки. 648 тестов. |

Версия платформы у двух windows-проектов (`10.0.19041.0` вместо
подразумеваемой `7.0`) — это про доступ к WinRT-проекциям: без неё не виден
`Windows.Data.Pdf`, которым рисуется обложка PDF. Пакетов при этом не
прибавилось — проекции идут вместе с SDK.

**Windows 10 остаётся целью.** 19041 — это Windows 10 2004 (май 2020), а
сам `Windows.Data.Pdf` есть с Windows 8.1, так что цифра ограничивает
компилятор, а не пользователя. Вызов на всякий случай обёрнут глухим
`catch`: на системе, где проекции нет, теряется обложка, а не значок.

**Жёсткое правило:** в `Wander.Core` нет `using System.Windows.*`, нет COM,
нет PInvoke. Если кажется, что нужно — значит нужен новый интерфейс в Core и
реализация в `Platform.Windows`. Это цена возможности отвинтить UI или
платформенный слой целиком.

```
src/
├── Wander.Core/
│   ├── Companions/     CompanionRule, CompanionResolver, CompanionMetadataService,
│   │                   SidecarText, RatingFilter, SidecarFormat,
│   │                   Pp3Sidecar, XmpSidecar, UnityMetaSidecar
│   ├── Diagnostics/    IFileLockInspector, FileLockInfo, PerfLog, BuildInfo
│   ├── FileSystem/     IFileSystem, FileOperationService, BatchExecutor,
│   │                   ClipboardController, ISystemClipboard,
│   │                   TypeAheadController, IDirectoryWatcher, SystemPathGuard,
│   │                   SystemRootFolders, EntryVisibility, FolderChanges,
│   │                   PathSafety, IConflictResolver, IRecycleBin, IKnownFolders,
│   │                   FileSystemEntry, EntryKind, EntryComparers, SortKey,
│   │                   SidecarRating + ColorLabels, UndoableActions,
│   │                   FolderStatistics, IVolumeInfoProvider, TransientFiles,
│   │                   BatchGroup
│   ├── Icons/          IIconProvider, IImageMetadataReader, IconSize, ImageMetadata,
│   │                   ImageFormats, RawPreviewExtractor, ThumbnailCacheOptions
│   ├── Layout/         TileLayout, TileMetrics, GridNavigation,
│   │                   WindowZones, WindowPlacement
│   ├── Listing/        FolderSession, ListingDiff, ArrivalIntent, RatedListing,
│   │                   SearchController, ImageFolderProbe
│   ├── Localization/   ITextSource
│   ├── Logging/        ILogger, ILogFile, NullLogger
│   ├── Menu/           ContextMenuBuilder, ContextMenuTarget, ContextMenuSettings,
│   │                   ContextMenuCatalog, MenuEntry, MenuCommandId
│   ├── Navigation/     NavigationService, NavigationSource, RecentPaths,
│   │                   PathCrumbs
│   ├── Operations/     OperationTracker
│   ├── Persistence/    IAppStateStore, AppState, AppSettings, GalleryBackground
│   ├── Preview/        PreviewRouter, TextProbe, EncodingProbe, AudioTags,
│   │                   BookCover, Fb2Document, MeshFile + Obj/Stl/GltfReader
│   ├── Search/         ContentSearchService, IContentExtractor, ContentMatcher,
│   │                   NameFilter, SearchExpression, SearchRequest, SearchHit,
│   │                   SearchScope, BinaryTextSearch, ExtractedTextCache
│   ├── Shell/          IShellLauncher, IShellNamespace, IShortcutService,
│   │                   IShellContextMenu, IShellHandlerRegistry,
│   │                   ShellHandler, ShellExtensionCatalog, ShellEntryKey,
│   │                   ShellScopes, ShellVerbs, RecentScopes
│   ├── Undo/           UndoService, IUndoableAction
│   └── ServiceLocator.cs
│
├── Wander.Platform.Windows/
│   ├── Diagnostics/    RestartManagerLockInspector
│   ├── FileSystem/     SystemIOFileSystem, ShellRecycleBin, WindowsKnownFolders,
│   │                   WindowsClipboard, WindowsDirectoryWatcher
│   ├── Icons/          SystemIconProvider, MetadataExtractorImageReader
│   ├── Logging/        FileLogger
│   ├── Persistence/    JsonAppStateStore
│   ├── Search/         FilterTextExtractor, NativeFilter
│   ├── Shell/          ShellLauncher, ShellShortcutService, WindowsShellNamespace,
│   │                   ShellContextMenu, ShellContextMenuInterop, ShellMenuIcons
│   └── PlatformBootstrapper.cs
│
└── Wander.App/
    ├── Conflict/       ConflictDialog, BatchConflictDialog,
    │                   DispatcherConflictResolver, InteractiveConflictResolver
    ├── Controllers/    NavigationController, PreviewController, RatingsController,
    │                   BookmarksController, FolderTreesController,
    │                   ContentSearchController, SearchResultsController,
    │                   ShellCommandsController
    ├── Controls/       AsyncIcon + IconLoadGate + FirstScreenWatch, GifImage,
    │                   IconImageCache, MagnifierCursor, NumericField,
    │                   RubberBandAdorner + RubberBandController,
    │                   RenameAdorner, VirtualizingWrapPanel
    ├── Converters/     Icon, EnumEquals, EnumRadio, EnumToVisibility,
    │                   BitmapPixelSize, RankStar + RatingConverters, CutRow,
    │                   TreeIndent, TileSecondLine, PixelsToThickness
    ├── Diagnostics/    CrashReporter, PerfCounters, UiStallWatch
    ├── DragPreview/    DragPreviewWindow, OutgoingDrag, DropTargetController,
    │                   DropTargetAdorner, DragAction, NativeMethods
    ├── Highlighting/   HighlightingCatalog + *.xshd
    ├── Menu/           ContextMenuFactory, ShellMenuCache
    ├── Preview/        ImageDecoder, ModelBuilder + ModelScene, PreviewText,
    │                   SummaryText — раскодирование для панели просмотра
    ├── Resources/      Strings*.resx, AppTextSource, MenuStyles
    ├── Util/           SelectionController, ListVisuals, SizeFormatter,
    │                   NumberFormat, TimeFormat, DispatcherExtensions
    ├── ViewModels/     SettingsViewModel, TreeNodeViewModel,
    │                   OperationViewModel, ColorLabelViewModel, HotkeyCatalog,
    │                   MenuItemRowViewModel, ShellExtensionRowViewModel,
    │                   SettingsCategoryViewModel, BulkObservableCollection,
    │                   GalleryPalette, ObservableObject,
    │                   ViewMode, PreviewKind, DropEffect
    ├── Views/          FileListView, FolderTreesView, PreviewPane, SearchWindow,
    │                   SettingsWindow, ShellScopePicker, ProgressDialog
    ├── MainViewModel.cs — при окне, не в ViewModels/ (см. «Окно и его контролы»)
    ├── MainWindow.xaml(.cs)
    └── App.xaml(.cs)
```

### Граф зависимостей между папками

Снимается скриптом и поддерживается им же — руками этот блок не править:

```pwsh
.\tools\deps.ps1            # отчёт на экран
.\tools\deps.ps1 -UpdateDoc # перезаписать блок ниже
```

Скрипт сводит `using Wander.*` по папкам (см. шапку `tools/deps.ps1` — что
именно видно, а что нет), считает уровни и находит циклы. **Правило (шаг
O7, 2026-09-01): между папками внутри проекта нет циклов, у каждой папки
есть уровень.** Уровень 0 — папка ни от кого в своём проекте не зависит;
дальше N = самый длинный путь вниз. Новое ребро, замыкающее цикл, — повод
переложить файл или развернуть связь (событие вместо коллбэка вверх), а не
пополнить список исключений.

Клубок `[Controllers+ViewModels]` в App разрублен на шаге **O9**
(2026-09-01): `MainViewModel` переехал из `ViewModels/` в корень
`Wander.App` — к окну, которое он наполняет. `ViewModels/` стала слоем
биндабельных типов (`ObservableObject`, `SettingsViewModel`,
`TreeNodeViewModel`, ...) **ниже** контроллеров, и цикла больше нет: у App
теперь шесть уровней и ни одного ребра назад.

Ребро `Wander.App -> Wander.Platform.Windows` — один файл, `App.xaml.cs`:
это точка композиции (`PlatformBootstrapper.RegisterDefaults()`), ей
можно.

<!-- deps:generated:begin -->
```
=== Wander dependency graph (using sweep) ===
date   : 2026-09-02
commit : 9f3273c

-- projects --
Wander.App -> Wander.Core   (49 files)
Wander.App -> Wander.Platform.Windows   (1 files)
Wander.Core.Tests -> Wander.Core   (63 files)
Wander.Platform.Windows -> Wander.Core   (21 files)

-- Wander.Core: folder -> folder --
  Companions     -> FileSystem     (5 files)
  Companions     -> Logging        (1 files)
  Companions     -> Undo           (1 files)
  Diagnostics    -> Logging        (1 files)
  FileSystem     -> Localization   (1 files)
  FileSystem     -> Logging        (2 files)
  FileSystem     -> Operations     (2 files)
  FileSystem     -> Undo           (3 files)
  Listing        -> Companions     (2 files)
  Listing        -> FileSystem     (5 files)
  Listing        -> Icons          (1 files)
  Listing        -> Search         (1 files)
  Menu           -> FileSystem     (1 files)
  Menu           -> Localization   (1 files)
  Menu           -> Persistence    (1 files)
  Menu           -> Shell          (2 files)
  Persistence    -> Companions     (1 files)
  Persistence    -> FileSystem     (1 files)
  Persistence    -> Navigation     (1 files)
  Preview        -> Icons          (1 files)
  Search         -> FileSystem     (5 files)
  Search         -> Logging        (1 files)
  Search         -> Preview        (1 files)
  Shell          -> FileSystem     (1 files)
  Shell          -> Localization   (1 files)
  Shell          -> Persistence    (1 files)

-- Wander.Core: levels --
  0: (root), Icons, Layout, Localization, Logging, Navigation, Operations, Undo
  1: Diagnostics, FileSystem, Preview
  2: Companions, Search
  3: Listing, Persistence
  4: Shell
  5: Menu

-- Wander.Platform.Windows: folder -> folder --
  (root)         -> Diagnostics    (1 files)
  (root)         -> FileSystem     (1 files)
  (root)         -> Icons          (1 files)
  (root)         -> Logging        (1 files)
  (root)         -> Persistence    (1 files)
  (root)         -> Search         (1 files)
  (root)         -> Shell          (1 files)

-- Wander.Platform.Windows: levels --
  0: Diagnostics, FileSystem, Icons, Logging, Persistence, Search, Shell
  1: (root)

-- Wander.App: folder -> folder --
  (root)         -> Conflict       (1 files)
  (root)         -> Controllers    (2 files)
  (root)         -> Diagnostics    (1 files)
  (root)         -> DragPreview    (1 files)
  (root)         -> Menu           (1 files)
  (root)         -> Resources      (4 files)
  (root)         -> Util           (3 files)
  (root)         -> ViewModels     (1 files)
  (root)         -> Views          (1 files)
  Conflict       -> Resources      (2 files)
  Conflict       -> Util           (2 files)
  Controllers    -> Preview        (1 files)
  Controllers    -> Resources      (6 files)
  Controllers    -> Util           (1 files)
  Controllers    -> ViewModels     (7 files)
  Controls       -> Converters     (1 files)
  Controls       -> Diagnostics    (1 files)
  Controls       -> Resources      (1 files)
  Converters     -> ViewModels     (1 files)
  Diagnostics    -> Resources      (1 files)
  DragPreview    -> Converters     (1 files)
  DragPreview    -> Resources      (3 files)
  DragPreview    -> Util           (1 files)
  DragPreview    -> ViewModels     (1 files)
  Preview        -> Resources      (2 files)
  Preview        -> Util           (2 files)
  ViewModels     -> Resources      (4 files)
  Views          -> Controllers    (1 files)
  Views          -> Controls       (2 files)
  Views          -> Converters     (1 files)
  Views          -> DragPreview    (1 files)
  Views          -> Highlighting   (1 files)
  Views          -> Resources      (6 files)
  Views          -> Util           (3 files)
  Views          -> ViewModels     (4 files)

-- Wander.App: levels --
  0: Highlighting, Menu, Resources, Util
  1: Conflict, Diagnostics, Preview, ViewModels
  2: Controllers, Converters
  3: Controls, DragPreview
  4: Views
  5: (root)

-- namespace <> folder mismatches --
  (none)
```
<!-- deps:generated:end -->

### Когда `IFileSystem`, а когда `System.IO`

Core читает диск двумя способами, и оба законны — правило зафиксировано на
шаге O7 (2026-09-01), фактическое положение сверено с кодом тогда же:

- **Через `IFileSystem`** идёт всё, что пользователь может отменить, что
  обязан уметь подменить тест, и всё, что перечисляет папки: операции,
  листинг, сайдкары, перепись папки. Это контракт слоёв — см. «Жёсткое
  правило» выше.
- **Напрямую через `System.IO`** можно читать байты одного файла ради
  раскодирования, когда файл уже выбран и результат — картинка/текст на
  экране, а не решение логики. Сегодня так читает **только
  `Wander.Core/Preview/`** (обложки, теги, меши, пробы текста) — и это
  весь список. Тесты туда не ходят (раскодирование покрывается руками и
  smoke-запуском), подменять нечего.

Новый прямой `File.` / `Directory.` где-либо в Core, кроме `Preview/`, —
это либо кандидат в `IFileSystem`, либо осознанное расширение этого
списка с записью здесь.

---

## Композиция: ServiceLocator

Никаких DI-контейнеров. `ServiceLocator` — статический
`Dictionary<Type, object>` с четырьмя методами: `Register<T>`, `Get<T>`,
`IsRegistered<T>`, `Reset()` (последний — для тестов).

Единственная точка регистрации — `App.OnStartup` →
`PlatformBootstrapper.RegisterDefaults()`. Порядок в нём значим:

1. **`ILogger` / `ILogFile`** (`FileLogger`) — первым, чтобы всё ниже могло
   логировать во время конструирования. Сразу пишет заголовок сессии: версия,
   ОС, архитектура, рантайм, культура, elevated — чтобы одинокий лог был
   самодостаточен для багрепорта.
2. Платформенные абстракции: `IFileSystem`, `IKnownFolders`, `IShellLauncher`,
   `IIconProvider`, `IAppStateStore`, `IFileLockInspector`, `IShortcutService`,
   `IShellNamespace`, `IShellContextMenu`, `IImageMetadataReader`.
3. **Общие синглтоны:** `UndoService`, `OperationTracker`, `IRecycleBin`,
   `FileOperationService`. Именно «один на приложение» — иначе undo-стек и
   прогресс расползутся по вызывающим.
4. **Companion-сервисы:** `CompanionResolver` (набор правил) и
   `CompanionMetadataService` — последний зависит от `IFileSystem` и
   `UndoService`, поэтому идёт после них.

Тесты в локатор **не ходят**: фейки из `tests/Wander.Core.Tests/Fakes/`
передаются конструкторами руками — см. «Тесты → Правила».

### Обязательные и необязательные сервисы

Спрашивать «а вдруг сервиса нет» можно **не про любой сервис**. Точка
регистрации одна и регистрирует всё безусловно, поэтому ветка «не
зарегистрирован» либо описывает реальный режим работы, либо недостижима — а
недостижимая ветка это не осторожность, это код, который никто никогда не
исполнит и не проверит.

Правило простое и проверяется поиском: **сервис, который где-то читается
через `Get<T>()`, обязателен везде.** Ветвиться по нему в другом месте —
неправда: до этой ветки программа не доживёт, `Get<T>()` бросит раньше.

**Обязательные** — читаются `Get<T>()`, ветки нет: `IFileSystem`,
`IShellLauncher`, `IAppStateStore`, `IRecycleBin`, `IShortcutService`,
`IIconProvider`, `ILogger`, `CompanionResolver`, `UndoService`,
`OperationTracker`, `FileOperationService`. Отсутствие любого из них — это
сломанный бутстраппер, и падение на старте здесь честнее тихой работы
вполсилы. Хост без Windows-слоя обязан зарегистрировать свои реализации (в
том числе пустышки) — молча деградировать за него приложение не будет.

**Необязательные** — это возможности, которых у хоста может не быть; каждая
читается `TryGet<T>()`, и у каждой есть внятный ответ «нет»:

| Сервис | Чего не будет |
|---|---|
| `IShellNamespace` | закладки на Корзину, распознавание `shell:`-путей |
| `IKnownFolders` | закладки по умолчанию (Загрузки, Документы, …) |
| `IShellContextMenu` | пункты сторонних приложений в контекстном меню |
| `IShellHandlerRegistry` | список обработчиков в настройках |
| `IFileLockInspector` | имя процесса в ошибке «файл занят» |
| `IVolumeInfoProvider` | блок тома над переписью корня диска |
| `ISystemClipboard` | буфер остаётся внутренним (`ClipboardController` и так умеет без него — это его конструктор по умолчанию) |
| `IDirectoryWatcher` | список не обновляется сам, только по `F5` |
| `IImageMetadataReader` | нет EXIF под снимком |
| `CompanionMetadataService` | не читаются и не пишутся сайдкары (оценки) |
| `ContentSearchService` | нет поиска по содержимому файлов |
| `ILogFile` | нет пункта «Журнал» и лога в отчёте о падении |
| `ITextSource` | Core отдаёт ключ вместо надписи |

Последняя строка — единственная, чью деградацию достаёт тест
(`TextFallbackTests`): всё остальное ветвится в App, куда тесты проекта не
дотягиваются. `ITextSource` в тестах не регистрируется **специально** — так
проверки каталога меню остаются осмысленными, и это же делает поведение
«нет источника» контрактом, а не случайностью.

### Конструирование в две фазы

Конструктор `MainViewModel` — единственное место проекта, где двадцать
сущностей собираются в одну, и у него два явных правила (O6.4, 2026-09-01):

1. **Зависимость строится раньше того, кто её берёт.** Передать свойство
   аргументом за несколько строк до его присваивания компилятор не
   останавливает — только nullable-ворнингом, и ровно так `RatingsController`
   однажды получил null вместо `Settings`. Ворнингов в сборке ноль, и это
   число — часть проверки: новый CS8602/CS8604 в конструкторе означает,
   что порядок снова сломан.
2. **Построить, потом включить.** Подписки, чьи обработчики производят
   побочные эффекты на изменение (`Settings.PropertyChanged` →
   перечитывания, `Trees.ExpansionChanged` → запись состояния), ставятся в
   самом конце конструктора, **после** `RestoreState()`: восстановление —
   это вернувшееся состояние, а не изменения, на которые надо реагировать.
   Флага «сейчас идёт восстановление» больше нет; вместо него один
   `_stateSaveTimer.Stop()` в конце `RestoreState` гасит запись,
   взведённую начальной навигацией.

### Изменяемая статика: где она есть и почему

Полный проход по `static` без `readonly` сделан заходом **O6** (PLAN.md,
категория 6). Правка по итогам одна — **замок в самом локаторе**.
`_services` живёт на весь процесс, а xUnit гонит тестовые классы
параллельно: `ServiceLocatorTests` зовёт `Register` / `Reset`, пока
соседний класс читает локатор через `ITextSource.Text` → `TryGet`. Чтение
`Dictionary` под чужую запись — это неопределённое поведение, а не
устаревший ответ, и цена ему — редкий флейк всего прогона. Все пять
методов обёрнуты в `lock` по приватному `_gate`; сигнатуры и поведение те
же. `ConcurrentDictionary` и DI-контейнер сюда не заводятся — правило
проекта, а каждый метод здесь и так один поиск по словарю.

Остальная изменяемая статика **оставлена сознательно**, вот причины:

| Место | Почему остаётся |
|---|---|
| `PerfLog._log`, `_windowStartMs` (Core) | диагностика; всё под своим `_lock`, тестами не читается |
| `IconImageCache` (App) | под `_lock` и с потолком; второй владелец не нужен, пока провайдер иконок — синглтон |
| `CrashReporter._offeredThisSession` | пишется с потока обработчика; фатальный крах приходит с любого — но fatal-путь флаг игнорирует, а худшее у гонки нефатальных — второй диалог репорта при двух одновременных сбоях |
| `UiStallWatch._worker`, `HighlightingCatalog._registered` | once-флаги «уже запущено / уже зарегистрировано» (второй — под `_lock`) |
| `MagnifierCursor._cached`, `ShellHandlerRegistry._searchPath` | ленивые неизменяемые: худшее, что делает гонка, — вторая одинаковая постройка того же значения |

Отдельно — `SystemIconProvider`, который в первой редакции списка стоял как
«кэши в статике»: это неверно. `_cache` / `_missing` / `_thumbnailOrder` —
поля **экземпляра** под `_lock`; статика там — set-once `_log` и
lock-объекты. Ревизия самих кэшей (границы, ключи) — отдельная тема и
лежит в [TECHDEBT.md](TECHDEBT.md), к недетерминированности отношения не
имеет.

### Как потребляются сервисы: поле в конструкторе, а не поход каждый раз

Решение, вынесенное из этого же разбора (2026-09-01). Опорный факт:
регистрация происходит один раз, в `App.OnStartup`, **до** создания
первого потребителя, и после этого словарь фактически заморожен — никто не
перерегистрирует и не убирает сервисы по ходу сессии, `Reset()` существует
только для тестов. Сервис не может «вдруг пропасть» и не может «вдруг
смениться реализацией» — на сегодня в проекте нет ни горячей подмены, ни
плагинов (появятся плагины из Roadmap — этот абзац пересматривается).
Отсюда три правила:

- **Экземпляры разрешают сервисы один раз, в конструкторе, в
  readonly-поля** — это уже норма (`MainViewModel`, контроллеры).
  Профит не в скорости, а в честности: список зависимостей виден в одном
  месте, и это ровно тот список, который станет параметрами конструктора,
  если локатор однажды решат отвинтить.
- **Статические хелперы** (`Text`, `IconConverter.Load`,
  `SystemIconProvider.ResolveShortcut`) ходят в локатор на каждый вызов.
  Замок не делает это дорогим: после старта он неконкурентен (одни
  чтения), а один поиск по словарю не виден на фоне того, что вызов делает
  дальше — шелл, декодер, диск.
- **Новых ленивых статических кэшей сервисов** (`_x ??= Get<X>()`) **не
  заводить** без строки в таблице выше: каждый такой кэш — ещё одна
  изменяемая статика, плюс риск обращения до бутстраппера (см. комментарий
  у `SystemIconProvider._log`, который строится самим бутстраппером).

---

## Файловые операции

Центральный конвейер проекта. Все мутации файловой системы идут через него —
чтения остаются на `IFileSystem` и его минуют.

```
VM / drop handler / hotkey
        │
        ▼
FileOperationService ──── фасад: одиночные ops инлайном
        │
        ├─ *Many / *ManyAsync ──▶ BatchExecutor
        │                             │
        │                             ├─▶ IConflictResolver   (диалог замены)
        │                             ├─▶ SystemPathGuard     (блок системных путей)
        │                             ├─▶ IRecycleBin         (корзина вместо стирания)
        │                             └─▶ OperationTracker    (прогресс в статус-бар)
        │
        └─▶ UndoService  ◀── каждая успешная операция кладёт IUndoableAction
```

**`FileOperationService`** — тонкий фасад. Одиночные copy / move / delete /
rename / create реализованы прямо в нём; всё батчевое и async делегируется
в `BatchExecutor`. Типы результатов (`BatchItemResult`, `DeleteResult`) лежат
на уровне namespace, чтобы вызывающим не тянуться через фасад.

**`BatchExecutor`** держит тяжёлую логику: цикл разрешения конфликтов,
composite-undo, ветвление recycle-vs-permanent. Синхронные `CopyMany` /
`MoveMany` оставлены для тестов; продакшн-путь — async: работа на тредпуле,
per-item прогресс в общий `OperationTracker`, отмена через `CancellationToken`.

**Отмена гранулярна по элементам верхнего уровня.** Начатое копирование
большой папки доработает до конца — прервать «внутри» нельзя, прогресс тоже
считается по элементам, не по байтам. Это известное ограничение (TECHDEBT).

### Undo

`UndoService` — один LIFO-стек `IUndoableAction` на приложение.

- Каждая успешная операция пушит обратное действие: Move ↔ Move обратно,
  Rename ↔ Rename обратно, Delete → Restore из корзины, Create → Delete
  в корзину (паритет с Explorer).
- **Безвозвратное удаление не откатывается** и затирает стек — единственное
  исключение из «всё обратимо».
- `BeginOperation()` поднимает busy-счётчик; пока операция в полёте
  `CanUndo == false`, и `Ctrl+Z` тихо игнорируется (Explorer ведёт себя так же).
- Потоковая модель: батч-исполнители пушат с тредпула, UI-поток читает и
  попает — весь доступ к стеку под одним локом. Событие `Changed` поднимается
  **вне** лока и может прийти с фонового потока: подписчик сам маршалит на свой
  диспетчер.
- Стек живёт в памяти и **не переживает рестарт**. Файлы при этом остаются в
  системной корзине — их можно вернуть через Explorer.

### Прогресс

`OperationTracker` — реестр текущих операций. `Begin(verb, total)` отдаёт
`IOperationHandle`, который надо задиспозить по завершении (успех или падение);
`Snapshot()` даёт иммутабельный срез для отрисовки. Несколько операций могут
идти одновременно и агрегируются для показа. Событие `Changed` — снова с
фонового потока, маршалит подписчик.

Для крупных операций `MainViewModel.RunWithProgressDialogAsync` оборачивает
`CopyManyAsync` / `MoveManyAsync` / `DeleteManyAsync` в модальный
`Views/ProgressDialog.xaml` с кнопкой отмены.

### Конфликты

`IConflictResolver` — стратегия для батчевых copy/move: Replace all / Skip all /
Resolve each. В UI — `ConflictDialog` (одиночный) и `BatchConflictDialog`.
`DispatcherConflictResolver` маршалит запрос обратно на UI-поток, потому что
батч крутится на тредпуле.

Осознанное отличие от Explorer: при Replace замещаемый target уходит **в
корзину**, а `DeleteAction` кладётся в composite перед основным шагом — так
`Ctrl+Z` восстанавливает обе стороны. Explorer замещает безвозвратно.

### Защита от разрушения

- **`SystemPathGuard`** — чистая функция от пути и окружения, без I/O и без
  локатора; и `FileOperationService`, и `BatchExecutor` зовут её статически.
  Блокирует: корни дисков, сами спец-папки (Windows, Program Files x86/x64,
  ProgramData, папка Users, корень профиля) и **всё дерево** `C:\Windows`.
  Содержимое Program Files и чужих профилей намеренно **не** блокируется —
  чистка остатков после деинсталляции легальна.
- **`PathSafety`** — детект self-drop: перенос папки внутрь самой себя, с
  человеческим текстом («Нельзя переместить «photos» в собственную подпапку
  «2024»»). Сам текст — из ресурсов через `ITextSource`.
- **Подтверждение с Cancel по-умолчанию** — на любой деструктивной операции.
- **`IFileLockInspector`** (`RestartManagerLockInspector`) — кто держит файл,
  чтобы вместо голого `IOException` показать «файл открыт в: Word (PID 1234)».
  Работает по файлам; для папки с залоченным вложенным файлом пока базовый
  `IOException`.

**Осознанные отступления от правила «операция обязана быть откатываемой».**
Правило записано в [CLAUDE.md](../CLAUDE.md); нарушать его молча нельзя, но
два случая разобраны и приняты:

- **Безвозвратное удаление** (`Shift` + `Delete`) — откатывать нечего.
  Поэтому подтверждение спрашивается всегда, независимо от настройки, и
  затирает undo-стек.
- **Восстановление из корзины** — в `UndoService` не кладётся, как и в
  Explorer. Отмена восстановления означала бы «удалить файл, который
  пользователь только что попросил вернуть», то есть `Ctrl` + `Z` стал бы
  деструктивным. Операция при этом не деструктивна сама по себе (создаёт
  файл, ничего не затирает), логируется, и `SystemPathGuard` ей не нужен:
  файл возвращается ровно туда, откуда его удалили, и решает это шелл, а не
  Wander. Идея всё-таки сделать откат — в «Дальнем прицеле»
  [BACKLOG.md](BACKLOG.md).

---

## Навигация и дерево

**`NavigationService`** — browser-style back/forward. Каждая запись несёт
`NavigationSource`, чтобы потребители (раскрытие дерева, preview-панель)
реагировали по-разному в зависимости от того, как пользователь сюда попал.

**Быстрый фильтр не кончается на текущей папке.** Маска, набранная в
панели инструментов, делает две вещи сразу: `SearchController` мгновенно
сужает листинг, который уже в памяти, а `ContentSearchController` через
паузу запускает под этой же папкой обычный проход `ContentSearchService`
со `SearchScope.Subfolders` (`IsFilterPass`). Проход не начинает список с
нуля — он **засевается** тем, что фильтр уже нашёл, поэтому ответ про
текущую папку не мигает; повторы отсеиваются по пути (`_resultPaths`), а
`HereFirst` держит найденное здесь выше найденного ниже, иначе сортировка
по имени растворила бы его среди чужих папок.

Порог в два символа (`MinAutoRunLength`) и пауза в 400 мс — это всё, что
отделяет набор слова от обхода диска на каждую букву. Порог сторожит
**только этот путь** — глубокий проход, стартующий из полосы фильтра
незаметно для пользователя; в окне поиска, где запуск виден и ожидаем,
порога по длине нет (см. «Запуск — сам, но не на каждую букву»). Окно
поиска через этот путь **не** ходит: у него есть галочка «во вложенных
папках», и проход, игнорирующий её, врал бы о себе (`_fromFilterBox`).

**Панель наследуется в обе стороны.** `GoUp` берёт источник текущей записи,
а `MainViewModel.DescendSource` делает то же самое на спуске: двойной клик
по вложенной папке остаётся `Bookmark`, если из закладок и пришли. Иначе
шаг вглубь из закладок раскрывал дерево дисков — панель менялась под
пользователем на ровном месте. Меняет её теперь только явный выбор в другой
панели или путь, до которого из закладок не дойти (`ExpandTreeToCurrent`
падает обратно на `Roots`).

**Пропавшая папка — состояние, а не ошибка.** `MissingFolderPath`
взводится там, где перечисление упало с `DirectoryNotFoundException` /
`DriveNotFoundException`, — то есть уже в фоновом потоке и по факту, а не
проверкой `DirectoryExists` перед навигацией: лишний синхронный поход на
диск это ещё один способ повиснуть на отвалившейся сетевой шаре. Область
файлов показывает поверх пустого списка «папка удалена или недоступна»;
если путь — закладка пользователя (`IsMissingBookmark`), рядом две кнопки:
переставить закладку на новое место или убрать её.

**`NavigationController`** (App) — маршрутизация путей, включая
shell-сентинелы: `shell:RecycleBinFolder` уходит через `IShellNamespace`,
а не через `IFileSystem`, и получает человеческий лейбл («Корзина»).
Он же владеет состоянием адресной строки: `Breadcrumbs`, `RecentPaths`,
`IsEditingAddress`. XAML биндится к нему напрямую через `Vm.Nav.X` — как
к `Preview`.

**Адресная строка** — одна полоса с двумя лицами:

- **хлебные крошки** (по умолчанию) — `PathCrumbs.Split` режет текущий путь
  на сегменты (`D:\` › `Dev` › `Wander`), каждый рисуется плоской кнопкой
  с подсветкой на hover; клик — переход. Shell-сентинел остаётся одной
  крошкой под своим display name. Глубокие пути не распирают полосу:
  крошки живут в `ScrollViewer`, который на каждую навигацию
  проматывается в хвост.
- **текстовое поле** — сырой путь для ввода и вставки. Вход: `Ctrl+L` или
  клик по пустому месту полосы (кнопка-крошка свой клик не пропускает
  наверх); выход: `Esc`, потеря фокуса или удавшаяся навигация.

**Память выделения.** `MainViewModel` помнит, что было выделено в каждой
посещённой папке (`_selectionMemory`, 64 записи, вытеснение по возрасту), и
восстанавливает это при возврате — `Alt` + `←` / `→` или просто повторный
заход. Отдельно от неё: переход на уровень вверх выделяет **папку, из
которой вышли**, — `PlanArrivalSelection` сравнивает новый путь с родителем
покидаемого. Оба решения принимаются до `Refresh()`, потому что применяет их
существующий механизм `_restoreSelection`, срабатывающий, когда придёт
листинг.

Тем же механизмом пользуются операции: удаление выделяет следующий
уцелевший элемент (`NextAfterRemoval`), вставка — то, что вставилось. Обе
к тому же поднимают `FocusListAfterRestore`: они шли за модальным диалогом,
и к моменту его закрытия строка, на которой была клавиатура, уже
перестроена, а фокус остался на окне.

**`RecentPaths`** (Core) — MRU последних посещённых папок (20 штук, свежее
первым, без дублей; регистр и хвостовой разделитель не считаются). Это не
`NavigationService`: там линейная лента с курсором для Back/Forward, здесь —
«где я недавно был». Живёт в `AppState.Session.RecentPaths`, показывается
по кнопке-треугольнику справа в строке (`F4`).

**Дерево** — lazy-load по раскрытию, листовые папки без треугольника,
раскрытые пути сохраняются в `AppState`, авто-раскрытие на текущую папку.
Ключевое обещание проекта: **дерево никогда не сворачивается само** — это
прямой ответ на баг Win11 Explorer.

**Листинг папки — вне UI-потока.** `MainViewModel.RefreshFolderAsync`
(и `RefreshShellAsync` для shell-неймспейсов) перечисляет содержимое в
`Task.Run` с отменой по `CancellationTokenSource`: следующая навигация
отменяет предыдущую, побеждает последняя. Спиннер поднимается только если
папка не отдалась за 150 мс — иначе он мигал бы на каждом переходе.

**Иконки и тумбнэйлы — тоже в фоне.** `Controls/AsyncIcon` (наследник
`Image`) отдаёт уже закэшированную иконку синхронно
(`IIconProvider.TryGetCachedIcon`), а остальные грузит через `Task.Run`
под семафором на 2 потока и отбрасывает результат, если контейнер за это
время переехал на другой файл (виртуализация). Иначе папка с RAW-фото
вешала окно: 256-пиксельная «Large»-иконка — это настоящий тумбнэйл,
который шелл достаёт из 30-мегабайтного .CR3 сотни миллисекунд.

Тиров кэша три, и третий появился по замерам: провайдер хранит `byte[]`
(память + диск), а **`Controls/IconImageCache` хранит уже декодированные
замороженные `BitmapImage`**. Байты в картинку превращает JPEG-декод, и он
единственный оставался на UI-потоке: сам по себе треть миллисекунды, но
прокрутка папки с фото делала это триста раз в секунду — 338 декодов и
141 мс внутри одной секунды в логе, который это и вскрыл. Декодирует
теперь тот, кто первым дошёл до файла (обычно фоновый загрузчик), а все
последующие показы берут готовую картинку. Бюджет — 256 миниатюр,
вытеснение старейших; кнопка «очистить кэш» в настройках чистит и этот
тир, иначе она выглядела бы сломанной.

**Ступеней размера четыре**, и они различаются не только пикселями:
`Small` / `Normal` — значок по расширению (`SHGetFileInfo`, один на весь
тип файлов), `Medium` (96 px) и `Large` (256 px) — настоящая миниатюра
через `IShellItemImageFactory` для того, у чего есть превью, и тот же
значок для всего остального. Разделение — `IsThumbnailable`, список
расширений: спрашивать фабрику про файл без превью означает записать
значок в общий системный `thumbcache`, из которого потом читает
Проводник, — и заодно платить обращением к шелу за каждый файл в папке
с исходным кодом.

Ключ кэша это и отражает: где картинка принадлежит одному файлу — ключ по
пути, где она общая для типа — по расширению. Против бюджета памяти
считаются только первые. На диск идёт лишь 256-пиксельный тир:
96-пиксельные дёшево пересобрать (шелл держит собственный кэш), а на диске
они конкурировали бы с крупными за тот же лимит — тем более что дисковый
ключ размера в себе не несёт.

**Bookmarks-панель** над деревом: drag-add, сворачиваемая, состояние в
`AppState.Favorites` / `IsBookmarksExpanded`. Три спец-папки по умолчанию
(Загрузки / Документы / Изображения) через `IKnownFolders` →
`SHGetKnownFolderPath`, плюс Корзина. Каждая — отдельный чекбокс в настройках.

---

## Выделение, буфер, фильтр

- **`SelectionController`** (App) — deferred selection при click-and-drag,
  чтобы drag не сбрасывал мультивыбор, плюс «снять выделение в активном
  списке».
- **`RubberBandController`** (App) — рамка выделения: адорнер, захват мыши,
  пересечение с контейнерами. Запускается только тогда, когда клик пришёлся
  на пустое место, а не на «мебель» списка (`ListVisuals.IsChrome` —
  полоса прокрутки, заголовки и разделители столбцов). Невиртуализованные
  элементы в пересечение не попадают: они за пределами экрана, а рамка
  без автоскролла туда и не дотягивается.
- **`EntryVisibility`** (Core) — три переключателя видимости одним
  значением (`ShowHidden` / `ShowSystem` / `HideSystemRootFolders`). Список
  и дерево фильтруют им обоим, поэтому спрятанное в одном спрятано и в
  другом; фоновому перечислению он передаётся как снимок, чтобы не читать
  живые настройки с рабочего потока. Третий флаг опирается на
  `SystemRootFolders` — деньлист служебных папок в корне тома
  (`$RECYCLE.BIN`, `System Volume Information`, …), отдельный от
  `SystemPathGuard`: тот запрещает **менять**, этот решает **показывать**.
- **`ClipboardController`** (Core) — файловый буфер: список путей плюс флаг
  «копировать или переместить». Хранит пути, а не содержимое — реальный
  move/copy случается в момент Paste, против того состояния файлов, какое
  будет тогда; паритет с Explorer. **Зеркалируется на системный буфер**
  через `ISystemClipboard`, см. ниже.
- **`ListVisuals.Ancestors`** (App) — единственный правильный способ пойти
  вверх от `e.OriginalSource`. Клик может прийтись на `Run` внутри
  `TextBlock` (строка типа файла в плитке — как раз такая), а `Run` — не
  визуальный элемент: `VisualTreeHelper.GetParent` на нём **бросает
  исключение**, а не возвращает null. Текстовые элементы шагают по
  логическому дереву и на `TextBlock` возвращаются в визуальное. Все вопросы
  «на что кликнули» — и в списке, и в дереве, и в адресной строке, и в
  drop-контроллере — ходят через него.
- **`TypeAheadController`** (Core) — набор имени с клавиатуры: накопление
  префикса, таймаут, «та же буква перебирает совпадения». Часы
  подставляются, поэтому таймаут проверяется тестом, а не ожиданием
  реальной секунды. UI-конец в `FileListView` решает только, предназначено
  ли нажатие списку.
- **`SearchController`** (Core) — живой текстовый фильтр (`Ctrl+F`). Владелец
  отдаёт снапшот после hidden/system-фильтрации через `SetSource`; каждое
  изменение запроса переproeцирует снапшот через case-insensitive
  `Name.Contains` **на фоновом потоке**, с отменой на каждый keystroke.
  Результат — через `FilteredChanged`. Дерево фильтр не трогает.

Все они живут в Core именно чтобы тестироваться без UI: гонки «печатаю
быстро + Refresh одновременно» были повторяющимся источником багов, пока
логика сидела в MainViewModel.

### Системный буфер обмена

```
Ctrl+C / Ctrl+X ─▶ ClipboardController ─┬─▶ своя модель в памяти (Paste читает её)
                                        └─▶ ISystemClipboard.SetFiles  ─▶ Explorer видит

Window.Activated ─▶ ClipboardController.SyncFromSystem
                        └─▶ ISystemClipboard.GetFiles ─▶ своя модель ← Explorer
```

**Почему модель в памяти осталась.** Буфер открывается эксклюзивно, это
межпроцессный вызов, и он падает, когда его держит другой процесс, — а
`RelayCommand` вешает `CanExecuteChanged` на `CommandManager.RequerySuggested`,
то есть `PasteCommand.CanExecute` выполняется десятки раз в секунду. Читать
буфер оттуда нельзя ни при каком раскладе, значит нужен кэшированный флаг —
а это и есть модель в памяти.

**Почему чтение по `Activated`, а не по `WM_CLIPBOARDUPDATE`.** Реальный
сценарий («скопировал в Проводнике → переключился в Wander → `Ctrl+V`»)
покрывается полностью: чтобы вставить, окно всё равно надо активировать.
Цена — ноль P/Invoke и один обработчик. Дырка — буфер поменялся, пока
Wander уже активен, — самоисправляется на следующей активации. Апгрейд до
`AddClipboardFormatListener` остаётся отдельным классом, если понадобится.

**Почему свой Win32, а не `System.Windows.Clipboard`.** Реализация —
платформенная деталь, значит место ей в `Wander.Platform.Windows`, а тот
проект не тянет WPF и не должен. `WindowsClipboard` работает напрямую с
`CF_HDROP` и форматом `Preferred DropEffect`; там же живут все три
неприятности этого API: память, переданная в `SetClipboardData`, принадлежит
системе (иначе копия умрёт вместе с процессом), «вырезано» проверяется как
**бит** `DROPEFFECT_MOVE` (приложения пишут комбинации вроде `COPY|LINK`), и
любой вызов ретраится, потому что буфер — отказывающий ресурс. Плюс
четвёртая, менее известная: `OpenClipboard(NULL)` приводит к тому, что
`EmptyClipboard` обнуляет владельца, после чего `SetClipboardData` по
документации обязан падать, — поэтому владельцем передаётся
`GetActiveWindow()`, то есть активное окно **вызывающего потока**.

**Асимметрия, которая осталась.** Вырезал в Wander, вставил в Проводнике —
перемещение сделали не мы, и в undo-стеке его нет. Обратное направление
корректно: там перемещение делаем мы.

### Слежение за папкой

`IDirectoryWatcher` (Core) + `WindowsDirectoryWatcher` (`FileSystemWatcher`).
Интерфейс сознательно говорит **что-то изменилось**, а не что именно:
ответ всегда «перечитать папку», а событийная модель приглашала бы
подправлять строки по одной — источник всех тонких рассинхронов списка.

События приходят пачками и с фонового потока, поэтому в `MainViewModel`
стоит троттл на `DispatcherTimer`: события копятся, повторяющийся таймер
раз в 500 мс спрашивает у сессии папки (см. ниже), что с ними делать, и
останавливает себя, когда делать нечего. Повторяющийся, а не
перезапускаемый: перезапускаемый одноразовый таймер при непрерывном потоке
событий (распаковка архива в открытую папку) не сработал бы ни разу.

Перечитывание откладывается до следующего тика, пока правится имя в строке
или пока идёт своя файловая операция — изменения при этом не теряются, а
ждут следующего тика. Ошибка вотчера (переполнение его буфера) считается
изменением — состояние папки после неё неизвестно, и перечитать её тем
более нужно — и вотчер переподнимается на той же папке.

---

## Сессия папки — `Wander.Core/Listing/`

Состояние «папки, на которую смотрят», вынесенное из `MainViewModel` в
Core заходом **O11** (PLAN.md): эпохи листинга, намерение на прибытие,
память выделения по папкам и накопленные сторожем изменения. Это машина
**решений**, а не работы: на входе факты («навигация в X», «листинг эпохи
N долетел», «сторож что-то заметил»), на выходе решения («опубликовать»,
«выделить эти строки», «перечитать»). Диск, потоки, `Dispatcher`, таймеры
и связанные с UI коллекции остаются в `MainViewModel`, который исполняет
решения. Смысл разреза — правило из CLAUDE.md «сложность выносится туда,
где её достаёт тест»: каждый вопрос «кто успел первым» здесь отвечается
тестом, а не ручным прогоном.

Три типа:

- **`FolderSession`** — сама сессия. `BeginListing` выдаёт эпоху и признак
  «прибытие или перечитывание»; `IsCurrent(epoch)` — единственный вопрос
  «мой ли ещё этот ответ», который задают и листинг, и проход по оценкам
  (через `RatingsController.isCurrent`), и точка публикации строк.
  `OnNavigating` запоминает выделение покидаемой папки, гасит намерение
  обогнанной навигации и планирует умолчание (подъём вверх выделяет
  покинутую папку, иначе — память выделения, LRU на 64 папки).
  `DecideArrival` — единственное место потребления намерения: чужой листинг
  и пустой список оставляют его ждать. `DecideWatchTick` — развилка
  сторожа поверх `FolderChanges` (стоп / подождать / перечитать /
  перечитать строки), идемпотентная: тик без событий ничего не меняет.
- **`ListingDiff`** — чистая функция «текущие строки + свежий листинг →
  план правок» (`RemoveAt` / `Insert` / `Move` / `Replace` / «пересобрать
  целиком»). Строка без изменений не порождает правки и не теряет свой
  контейнер — то, на чём держится «список не дёргается». Порог пересборки
  позиционный: удаление строки в начале папки сдвигает все нижние и честно
  уходит в один `Reset` вместо каскада `Move` (см. тест
  `RowGoneNearTheTop_IsWholesale`).
- **`ArrivalIntent`** — одно отложенное намерение «что выделить, когда
  листинг долетит» (см. O6.1): установка заменяет, применение одно.

Инварианты закреплены тестами `FolderSessionTests` / `ListingDiffTests` —
включая те, что раньше проверялись только руками по MANUAL-CHECKS
(«навигация во время незавершённого листинга», «правка метаданных не
пересобирает папку», «намерение не переживает навигацию в другую папку»).

Что осталось у вью-модели осознанно: правило спиннера (150 мс и очистка
устаревших строк — тайминг вокруг `Task.WhenAny`), восстановление статуса
операции поверх «N элементов» (однострочное сравнение) и мост
`FocusListAfterRestore` к списку. Выносить их — обёртка ради обёртки:
тестируемого содержания в них нет, а асинхронная оркестровка вокруг —
честная работа исполнителя.

---

## Поиск: два разных механизма и два критерия

Поиск делает две несовместимые вещи, и они разведены по разным классам, а
не по флагу внутри одного.

```
             ┌── shallow ──▶ SearchController (Core)      ── на каждую букву
Маска имени ─┤                └─ проекция снапшота папки, отмена по keystroke
             └── deep ─────▶ ContentSearchController (App) ── сам, по таймеру
Текст ──────────────────────▶   └─ ContentSearchService (Core)
                                     └─ обход дерева + IContentExtractor
```

Граница — `ContentSearchController.IsDeep`: заполнено поле текста, либо
включены подпапки.

**Переход в другую папку сбрасывает поиск целиком** — оба поля и оба флага
(`ContentSearchController.Reset`). Не косметика: галка «искать в
подпапках» переживала и навигацию, и закрытие окна, а пока она включена,
`IsDeep` истинно и каждая буква в поле уходит в обход диска вместо фильтра.
Со стороны это выглядело как «быстрый фильтр перестал фильтровать», и
объяснить это было нечем — окно закрыто, галки не видно. Поиск принадлежит
той папке, в которой его настроили.

По той же причине ни область, ни галка бинарей не сохраняются в
`state.json`: первая же навигация после запуска их всё равно сбросит, а
настройка, которую что-то молча перетирает, — это обещание, которое никто
не держит. Пока false, набор идёт в `SearchController`, как и
раньше. Как только true, `SearchController.Query` очищается и поле
становится запросом: фильтр — это проекция того, что уже на экране, а обход
диска на каждое нажатие — не фильтр, а другая операция с другой ценой.

### Два критерия и «и» между ними

`SearchRequest` несёт **маску имени** и **текст**, и файл проходит, только
если совпали **оба** заданных.

Это не косметика, а исправление. Первая версия объединяла их через «или»,
и это дало три бага сразу, все три на одном скриншоте: поиск слова внутри
документов возвращал каждую картинку, в имени которой попалась та же
буква; фильтр `.t` вытаскивал `.pdf`, потому что «.t» нашлось у него
внутри; а понять, почему, было нельзя — галка «искать в содержимом» жила в
попапе, который закрывался при первом же клике.

«И» заодно делает маску **воротами**: файл, который она отвергла, не
открывается и не считается просмотренным. Поэтому «все `*.cs` со словом X»
стоит доли того, что стоит «все файлы со словом X», — а это и есть тот
запрос, который люди задают.

Галки «искать в содержимом» больше нет: её роль играет само наличие текста
во втором поле. Переключатель, состояние которого видно только в
развёрнутой панели, — это ровно тот механизм, который однажды тихо
изменит смысл следующего запроса.

### `NameFilter` — два языка в одном поле

Подстрока по умолчанию; появился `*` или `?` — вся часть становится
шаблоном на **всё** имя. Несколько частей через `;`, достаточно одной.

Разделение — по самому тексту, как в Everything, и это единственная
раскладка, где оба частых случая стоят по одному нажатию: `doc`, чтобы
сузить папку, и `*.cs`, чтобы сказать «расширение». Один язык испортил бы
один из них: подстрока не умеет «оканчивается на .cs», а глоб превращает
любой беглый фильтр в `*doc*`.

Сопоставление написано руками, без регулярок. Шаблон перечитывается на
каждую букву — регулярка означала бы либо компиляцию на нажатие, либо кэш,
— и `*a*a*a*a*b` это готовая форма катастрофического бэктрекинга. Здесь
одна точка возврата, взорваться нечему.

Тот же `NameFilter` стоит и в `SearchController`, так что маска работает и
в быстром фильтре тулбара, и разбирается один раз на изменение запроса, а
не на каждую строку папки.

### `IContentExtractor` — почему абстракция, а не набор `if`

«Найти текст внутри файла» — один вопрос и три несовместимых ответа: байты,
которые надо раскодировать; zip с XML; COM-фильтр, который несёт сама
Windows. Первые два обязаны жить в Core, третий не может там жить вообще.
Интерфейс — это шов между ними, и точка композиции у него одна, в
`PlatformBootstrapper`:

```
ZipDocumentExtractor (Core)      .docx .xlsx .pptx .epub .odt .ods .odp
FilterTextExtractor  (Platform)  .doc .rtf .pdf .chm .msg .mht …  через IFilter
PlainTextExtractor   (Core)      всё остальное, что оказалось текстом
```

Порядок — часть контракта: специфичные форматы впереди, «согласен на любой
файл» — последним. `PlainTextExtractor.CanExtract` всегда true, а решает
`TextProbe` уже по байтам, потому что список расширений здесь неизбежно
врёт в обе стороны (`.asset` бывает и YAML, и бинарём).

**Провал специфичного экстрактора заканчивает файл.** До этого правила
`.pdf` на машине без PDF-обработчика проваливался в `PlainTextExtractor`,
тот видел в первых восьми килобайтах читаемый ASCII заголовка и объявлял
файл текстом — а поиск потом находил слово в
`%PDF-1.4 ReportLab Generated PDF`. Формально совпадение, практически —
попадание в документ, который никто не смог прочитать.

Экстракторы **не бросают**: неудача — это `null`. Один нечитаемый файл не
имеет права закончить поиск по десяти тысячам других.

`IsExpensive` служит двум вещам сразу: что кэшировать и что считать
непрочитанным. Дорогие — те же, что специфичные; поэтому «формат, который
мы брались открыть, и не смогли» отличается от «`.dll` по дороге», и
счётчик в строке состояния не тонет в шуме.

### Почему `IFilter`, а не свой разбор и не пакет

`.doc` — OLE-контейнер с piece table, быстрыми сохранениями и сжатыми
кусками в 8-битной кодировке; свой читатель для него — длинный хвост
неправильных ответов. Windows несёт правильный (`OffFilt.dll`) с Windows 7,
Office для этого не нужен, и тот же механизм закрывает `.rtf`, `.mht` и
`.pdf` там, где стоит PDF-читалка. Ноль зависимостей, столп «интеграция с
системой» — буквально: читаем тем же, чем индексирует Проводник.

Одна неочевидность зафиксирована в коде: `IFilter::Init` без
`APPLY_INDEX_ATTRIBUTES` возвращает **пустой документ**, если задан хоть
один флаг канонизации. Замерено на этой Windows: `1|2` и `1|2|8` дают ноль
символов, а `0`, `16` и `1|2|8|16` — полный текст. Канонизация нужна (это
она превращает абзац Word в строку), поэтому флаг идёт вместе с ней, а
value-чанки, которые он порождает, отбрасываются при чтении.

Список форматов у `FilterTextExtractor` — именованный, а не «что скажет
реестр». Реестр отвечает и за обычный текст, а тамошний фильтр декодирует
байты системной кодовой страницей — заметка в Windows-1251 вернулась бы
кракозябрами вместо того, что даёт `EncodingProbe`.

### `BinaryTextSearch` — отдельный режим, а не ещё один экстрактор

Файлы, которые текстом не являются, по умолчанию не участвуют — так делают
все: `grep` и `ripgrep` требуют `-a`, VS Code пропускает молча, Windows
Search видит только то, что отдал фильтр. Причина не в цене, а в шуме:
частое слово встречается почти в каждом крупном бинаре, и список из
пятисот DLL — это не ответ.

Галка включает побайтовое сравнение, и оно **только ASCII**. В файле, про
который уже решено, что он бинарь, нечего декодировать: ни BOM, ни единой
кодовой страницы, часто несколько сразу. Запрос с кириллицей поэтому не
находит ничего и говорит об этом заранее (`Supports`), а не возвращает
пустоту, которую нельзя объяснить. Разбор альтернатив — в
[BACKLOG.md](BACKLOG.md).

Не экстрактор, потому что отвечает на другой вопрос: экстрактор говорит
«вот что здесь написано», а этот — «да/нет». Смешивать их означало бы
выдумывать текст, которого нет, ради сниппета, который был бы враньём.

### `ExtractedTextCache` — в памяти и маленький

LRU по «путь + размер + mtime», потолок в символах (32 МБ по умолчанию),
кладутся только дорогие форматы. Почему не индекс на диске — в
[REJECTED.md](REJECTED.md) с замерами; коротко: дерево размером с
репозиторий просматривается за 0,2 с в один поток, а поиск ходит в восемь.

### Запуск — сам, но не на каждую букву

`ContentSearchController` решает, когда искать, поэтому корень и видимость
он получает колбэками, а не значениями: и то и другое меняется под ним,
пока запрос набирают.

- набранный символ — пауза 400 мс, и это единственное условие: любой ввод,
  меняющий критерии, перезапускает поиск. Порога по длине **здесь** нет.
  Он был — три символа для тяжёлых областей, — и оказался хуже того, от
  чего защищал: `:no` и `a` просто ничего не делали, а объяснить это на
  экране было нечем. Обход и так ограничен лимитом в 5000 строк и
  отменяется следующей же буквой. Порог в два символа
  (`MinAutoRunLength`) стоит только на пути **из полосы фильтра** — где
  глубокий проход стартует незаметно, под уже сузившимся листингом
  (см. «Навигация и дерево»);
- переключатель (область, галка бинарей) — сразу: это решение, а не
  нажатие;
- `Enter` перебивает паузу.

`SearchState` (не запускался / ждёт / идёт / готово / остановлен)
остаётся моделью состояния: он гасит кнопку «Остановить», крутит индикатор
в области файлов и решает, что напишет строка состояния.

Отмена **забирает владение состоянием**: `Cancel` поднимает счётчик
поколений, и отменённый проход, дойдя до конца, не сообщает ничего. Без
этого проход, отменённый из-за смены критериев, через мгновение объявлял
«Остановлено» поверх состояния, которое новые критерии уже выставили.
Кнопка «Остановить» — это `Stop()`: та же отмена, сказанная вслух.

### Окно, а не панель

Критериев стало четыре, а с диапазонами дат и размеров (G3-хвост, J3)
будет больше — в полосу над списком это не влезает, не съедая список. И
попап закрывался от первого же клика мимо, что и было механизмом «поиск
идёт с галкой, которую не видно».

Поэтому `Views/SearchWindow.xaml` — обычное окно с `Owner`, а не
`Topmost`: `Owner` держит его над своим главным окном и не лезет поверх
чужих приложений. Оно скрывается, а не уничтожается при закрытии (запрос в
нём остаётся), а строка поиска в тулбаре, пока оно открыто, спрятана —
одни и те же критерии в двух местах это способ развести их состояния.

Рамка — стандартная, а кнопки «свернуть» и «развернуть» снимаются двумя
строками `SetWindowLong` в `SourceInitialized`. Такой комбинации в
`WindowStyle` нет: `ToolWindow` тоже без них, но с укороченным заголовком,
где кнопка закрытия занимает часть угла и читается как красный квадратик, а
не как кнопка; `NoResize` убирает их ценой растягивания, которое этому окну
нужно.

Закрытие поднимает событие `Dismissed`, и главное окно по нему возвращает
клавиатуру в список. Без этого `Esc` оставлял фокус на окне, которого уже
нет, и стрелки не работали — а «непонятно, что происходит» это ровно тот
момент, когда клавиатура обязана оказаться в рабочей области.

### `SearchExpression` — критерии как одна строка

`маска:текст`. Двоеточие — потому что Windows запрещает его в именах
файлов: разделитель не может оказаться частью маски и не нуждается в
экранировании. Первое двоеточие делит, остальные принадлежат тексту, иначе
`:http://example.com` пришлось бы экранировать. Нет двоеточия — вся строка
маска, то есть поле работает ровно как раньше.

Нужно это не для ввода, а для **вывода**: поиск, настроенный в окне,
оставлял поле пустым, и суженный список стоял с крестиком рядом, ничего о
себе не сообщая. Ввод — то же самое в обратную сторону, бесплатно.

Флаги (подпапки, двоичные) в строку не попали намеренно:
для них пришлось бы выдумать второй синтаксис поверх первого, а их наличие
достаточно отметить — `HasNonDefaultOptions` подсвечивает значок `⋮`.
Разбор варианта с флагами в строке — в [BACKLOG.md](BACKLOG.md).

### Результаты — тот же список, плоско

Найденное складывается в `_searchResults` (MainViewModel) и выливается в
`Entries` пачками не чаще раза в 200 мс: обновление коллекции — это один
Reset и полный пересчёт раскладки, а поиск по дереву находит что-нибудь в
сотнях папок. `FileSystemEntry` получил `MatchSnippet` и вычисляемый
`ParentFolder` — по той же логике, по какой в нём живёт `OriginalLocation`:
строку на экране читает ровно одно место, и параллельную таблицу по путям
пришлось бы синхронизировать с коллекцией, которая и так всё это несёт.

Пока результаты на экране, `Refresh()` не пересобирает список — иначе
watcher, завершившаяся файловая операция и `F5` затирали бы найденное. Он
делает единственное, что там честно: выбрасывает строки, чьих файлов больше
нет (`PruneMissingResults`), — благодаря чему удаление и переименование
результата убирают его из списка. Повторить поиск целиком — это `F5`.

## Контекстное меню

Меню списка файлов строится заново на каждый правый клик. Разделение то же,
что и везде: **что показать** решает Core, **чем нарисовать** — App,
**откуда взять чужие пункты** — Platform.

```
правый клик
     │
     ├─▶ MainWindow: чинит выделение (клик вне выделения переносит его,
     │                клик по пустому месту снимает → меню папки)
     │
     ├─▶ ShellMenuCache.Acquire(paths, folder)   ─── App
     │        └─▶ IShellContextMenu.Open           ─── Platform.Windows
     │                SHParseDisplayName → IShellFolder
     │                GetUIObjectOf / CreateViewObject → IContextMenu
     │                QueryContextMenu(HMENU) → обход HMENU → ShellMenuEntry[]
     │
     ├─▶ ContextMenuBuilder.Build(target, settings, shellItems)  ─── Core
     │        чистая функция → MenuEntry[]
     │
     └─▶ ContextMenuFactory.Build(model, session)  ─── App
              MenuEntry[] → WPF ContextMenu
```

**`ContextMenuBuilder`** (Core) — единственное место, где живут правила
«Rename только на одном элементе», «в корзине ничего деструктивного»,
«у папки нет Open with». Чистая функция от `ContextMenuTarget` (снапшот
выделения и состояния вида) и `ContextMenuSettings` — поэтому покрыта
тестами без всякого UI. Она же схлопывает разделители: группы пишутся
вместе со своими `---`, а осевшие после скрытия пунктов пустые группы,
ведущие/двойные/хвостовые линии убираются в `Normalize`.

Два разных меню, а не одно с половиной серых строк:

- **по выделению** — `Открыть`, подменю `Открыть с помощью`, пункты
  расширений, подменю **«Файл»**, `Свойства`;
- **по фону** — подменю `Создать`, `Открыть в терминале`, `Копировать путь`,
  пункты расширений, `Свойства`. Вида, сортировки, обновления и отмены здесь
  нет: это состояние окна, а не действия над папкой, и живут они в меню
  `Вид` на панели и на хоткеях.

Системное подменю «Создать» **вливается** в наше, как и «Открыть с помощью»
— иначе в меню два раза подряд стоит слово «Создать». Опознаётся оно не по
подписи (локализована), а по каноническому глаголу дочерней строки:
`NewFolder`. Своя строка «Папка» идёт первой и заменяет шелловскую — у нашей
есть откат и переименование на месте, — а `Ярлык` и все шаблоны файлов идут
следом такими, как их дал шелл.

**Порядок — по частоте, а не по категориям.** Сверху то, ради чего меню
открыли: для фотографии это «Редактировать в …», для папки с репозиторием —
«Git Commit». Свои файловые операции нужны реже, поэтому собраны внизу в
одно подменю «Файл»: `Вырезать` / `Копировать` / `Вставить`, `Копировать
путь` / `Копировать имя`, `Переименовать` / `Создать ярлык`, `Удалить`.
У половины из них есть хоткеи — тем более незачем занимать верхний уровень.

### Куда попадает пункт расширения

`SplitShell` раскладывает то, что вернул шелл, на три кучки — и решает
это **по каноническому глаголу**, никогда по подписи: подписи локализованы и
меняются вместе с именем файла («Добавить к "README.7z"»), глаголы — нет.

| Кучка | Признак | Куда |
|---|---|---|
| «Открыть с помощью» | подменю, у ребёнка глагол `openas` | вливается в наше подменю `Открыть с помощью` |
| Файловые операции | глагол в списке (`PreviousVersions`) **или** динамическое системное подменю | вниз, в конец подменю «Файл» |
| Всё остальное | — | верхний уровень |

«Динамическое системное подменю» — это «Отправить» и «Передать на
устройство»: шелл собирает их в момент показа из содержимого папки и списка
устройств, поэтому **ни один** их пункт не несёт канонического глагола.
Сторонние обработчики свои глаголы регистрируют всегда, так что «подменю
верхнего уровня, внутри которого ни у кого нет глагола» отделяет одно от
другого без сопоставления локализованных названий. Это эвристика, а не
контракт — см. TECHDEBT.

Системное подменю «Открыть с помощью» не отбрасывается, а **вливается** в
наше: там живой список приложений, собрать который самим невозможно. Своя
строка `Выбрать приложение...` остаётся запасным вариантом на случай, когда
шелл подменю не дал (расширения выключены).

### Чем помнится выключённый пункт

`ShellEntryKey.For(verb, header)` — **канонический глагол, если он есть;
нормализованная подпись, если нет**. Не наоборот, и это не косметика.
У TortoiseGit подпись верхней строки — «Git Commit → "master"…», с именем
текущей ветки внутри. По подписи выключение отваливается на первом же
`git switch`, а список «встреченных» растёт по строке на ветку. Глагол той
же строки — `Git Commit...`, ветки в нём нет.

Глагол есть не у всех: 7-Zip своего верхнего пункта его не публикует, и там
ключом остаётся подпись — она стабильна, потому что это имя приложения.
`IsBlocked` проверяет **обе** формы, поэтому файлы настроек, написанные до
перехода на глаголы, продолжают работать без миграции.

### Откуда берутся «Приложение» и «Типы»

`IContextMenu` отдаёт одно склеенное меню от всех расширений сразу и не
говорит, какая строка от какого хендлера. Поэтому эти две колонки в
настройках приходят с другой стороны — из реестра
(`IShellHandlerRegistry` → `ShellHandlerRegistry` в Platform.Windows):

- `<scope>\shellex\ContextMenuHandlers\<имя>` → CLSID → `InprocServer32` →
  версия DLL даёт имя приложения;
- `<scope>\shell\<verb>` → подпись и командная строка лежат прямо там,
  и имя ключа — это и есть канонический глагол, так что для таких пунктов
  сопоставление со строкой меню **точное**;
- то же самое под `SystemFileAssociations\<scope>`.

Читаются `HKLM\SOFTWARE\Classes` и `HKCU\SOFTWARE\Classes` по отдельности,
а не `HKEY_CLASSES_ROOT`: склеенное представление перечисляется
катастрофически медленно (замеряно — минуты против сотни миллисекунд).
Только чтение, без прав администратора; ничего не пишется. Замеры на живой
машине: базовые области — 40–50 мс на холодную и ~10 мс с прогретым кэшем
CLSID → DLL, все 848 областей (включая каждое зарегистрированное
расширение) — около 150 мс, перечисление имён расширений — 20 мс.

`ShellExtensionCatalog` (Core, чистая функция) сливает найденное в реестре
с тем, что Wander реально встретил в меню, по тому же `ShellEntryKey`.
Строка, известная только одному источнику, всё равно попадает в таблицу:
установленный-но-молчащий хендлер иначе нельзя было бы выключить заранее, а
встреченный-но-не-найденный — вообще никак.

Не в меню намеренно: **`Удалить безвозвратно`** (остаётся хоткеем
`Shift+Del` — необратимое действие не должно лежать в клике от обычного
удаления), **закладки** (для них есть панель слева), **«Показать в
Проводнике»** (в файловом менеджере — лишний пункт) и системное «Добавить в
избранное» (глагол `pintohomefile`). `Открыть в терминале` показывается
только для папки и для фона: на файле он молча означал бы «в папке, где он
лежит», а строка обещает другое.

**`ShellContextMenu`** (Platform.Windows) читает **классическое** меню
шелла — то самое, что Win11 прячет под «Показать дополнительные параметры»,
и куда по-прежнему регистрируются 7-Zip, TortoiseGit, Notepad++, антивирусы.
Оттуда же бесплатно приезжает системное подменю «Создать» со всеми
`ShellNew`-шаблонами.

Wander не отдаёт `HMENU` в `TrackPopupMenu`, а обходит его и перерисовывает
пункты обычными WPF-строками — иначе чужое меню было бы *рядом* с нашим, а не
*внутри*. Цена этого решения:

- ленивые подменю приходится будить руками — `IContextMenu2::HandleMenuMsg`
  с `WM_INITMENUPOPUP` перед обходом;
- owner-drawn строки (текст лежит в `dwItemData` в приватном формате
  обработчика) прочитать нельзя — они пропускаются и логируются;
- иконки берутся из `hbmpItem` и конвертируются в PNG (`ShellMenuIcons`),
  как и всё остальное на границе с Core.

Дубли фильтруются по **каноническому глаголу** (`GetCommandString`,
`GCS_VERBW`), а не по подписи: подписи локализованы, глаголы — нет. Так из
чужого меню выпадают `cut` / `copy` / `paste` / `delete` / `rename` /
`properties` / `link` / `openas` / `copyaspath`, которые Wander рисует сам.

**`ShellMenuCache`** (App) держит последнюю сессию живой: повторный правый
клик по тому же выделению не ходит в шелл вообще. Это не преждевременная
оптимизация — на рабочей машине запрос стоит 0.4–1.1 с в первый раз
(грузятся DLL обработчиков) и 80–260 мс дальше, потому что каждый
обработчик успевает подумать: TortoiseGit читает статус репозитория, а у
картинки обработчиков просто много (25 против 12 у текстового файла).

Время жизни держится на одном факте: **правый клик, открывающий меню, уже
закрыл то, что было открыто**. Значит момент, когда `Acquire` спрашивают про
*другую* цель, — это момент, когда прошлая сессия заведомо никому не нужна,
и там она и освобождается. Ничего не считает «открыто ли ещё меню» — а
считать было бы неверно: `ContextMenu.Closed` уходит в `BeginInvoke` с
приоритетом `Background`, то есть выполняется **после** следующего клика.
`Invalidate` поэтому только отвязывает сессию от ключа, не освобождая её:
она может прямо сейчас стоять за меню, на которое пользователь смотрит.

**Расширения выполняются в нашем процессе** — это неизбежная цена их
поддержки (Explorer делает то же самое). Отсюда три вещи: запрос обёрнут в
`try/catch` с логом, выключенный `ShellExtensionsEnabled` означает, что
чужие DLL вообще не грузятся, а сама команда вызывается **после** закрытия
меню — обработчики любят открывать свои модальные диалоги.

**Кастомизация** (`AppSettings` → `ContextMenuSettings`): мастер-выключатель
расширений, чёрный список по имени и список скрытых собственных пунктов.
Скрытое хранится как
«что выключено», по строковым именам `MenuCommandId` — новый пункт в будущем
релизе появится сам, а переименование enum-члена не воскресит спрятанное
молча. Имена расширений узнаются только из самого шелла, поэтому
`KnownShellExtensions` накапливается по мере открытия меню — иначе диалогу
настроек нечего было бы показать.

---

## Companion-файлы («интегрированные элементы»)

Служебный файл рядом с основным — Unity `.meta`, RawTherapee `.pp3` — с точки
зрения пользователя не отдельная сущность, а довесок. Wander показывает пару
одной строкой и уносит спутника вместе с основным файлом при любой операции.
Флаг `AppSettings.IntegrateCompanions`, по-умолчанию включён.

Механизм разложен на три куска, и граница между ними — это граница «чистое
сопоставление имён / чтение-запись содержимого / решение UI».

```
CompanionRule            суффикс + шаблон имени. Формат — это данные,
   │                     а не код: новый сайдкар = ещё одна строка
   ▼
CompanionResolver        Collapse()      список папки → свёрнутый список
   │                     FindCompanions() путь → что лежит рядом на диске
   │                     RenamePlan()     путь + новое имя → план группы
   │
   ├──▶ MainViewModel.RefreshFolderAsync  (свёртка листинга, на тредпуле)
   ├──▶ MainViewModel.WithCompanions()    (расширение путей перед batch-ops)
   └──▶ FileOperationService.RenameMany() (группа как один undo-шаг)

CompanionMetadataService  чтение/запись содержимого сайдкара
   ├──▶ UnityMetaSidecar.Read()   GUID, импортёр, folderAsset
   ├──▶ Pp3Sidecar.Read()         Rank, ColorLabel
   ├──▶ Pp3Sidecar.WithRank()  ─▶ IFileSystem.ReplaceAtomic()
   └──▶ CreateRatingSidecar()     сайдкара нет → создать (по согласию)

Listing/RatedListing      WithRatings()  листинг → тот же листинг с Rating
                                         (читалка приходит делегатом)
```

**Два шаблона имён**, оба обязательны — иначе половина форматов отваливается:

| Шаблон | Пример | Кто |
|---|---|---|
| `Appended` — дописывается к полному имени | `Sprite.png.meta`, `IMG.CR2.pp3` | Unity, RawTherapee, Google Takeout |
| `Replaced` — заменяет расширение | `IMG_1234.xmp` | Adobe/darktable, iPhone `.AAE` |

`Appended` разрешается по точному имени, `Replaced` — по stem'у, и если на
stem претендует больше одного файла (`IMG.CR2` + `IMG.jpg` при `IMG.xmp`),
спутник не привязывается ни к кому: угадывать тут хуже, чем ничего не делать.

**Что где происходит:**

- **Свёртка листинга** — в воркере `RefreshFolderAsync`, **после** фильтров
  Hidden/System. Спутник рядом с отфильтрованным файлом остаётся видимым сам
  по себе; спутник-сирота — тоже. Ничего не исчезает молча.
- **`FileSystemEntry.Companions`** — список путей, который свёртка кладёт в
  основную запись. Пусто у обычного файла и всегда пусто при выключенном
  флаге. Отсюда блок «Вместе с файлом:» в футере просмотра. Значок в самом
  списке пробовали и убрали — см. [REJECTED.md](REJECTED.md).
- **Контекстное меню** ничего про спутников не знает и знать не должно:
  их нет в выделении, значит меню (включая шелловское) строится для
  основного файла само собой.
- **Групповые операции** идут списком `BatchGroup` («основной + спутники»),
  а не плоским списком путей. Это и есть механизм «один вопрос на группу»:
  `BatchExecutor` считает конфликты по группам, спрашивает про основной файл
  и применяет ответ ко всем членам сразу. Undo — composite, группа
  возвращается одним `Ctrl+Z`.
- **Откуда берутся группы.** Из выделения — бесплатно, `FileSystemEntry`
  уже несёт `Companions` (важно: Copy / Cut / Delete / старт перетаскивания
  живут на UI-потоке, и опрос диска там встал бы поперёк). Из плоского
  списка путей — буфер обмена, дроп из Explorer — через
  `CompanionResolver.Group()`, и это уже с обращением к диску, поэтому
  вызывающие уводят его в `Task.Run`.
- **Авто-переименование при конфликте** тянет спутников за собой:
  `Sprite (1).png` → `Sprite (1).png.meta`. Имя выводится подстановкой общей
  части (полное имя для `Appended`, stem для `Replaced`), поэтому знание
  форматов в `BatchExecutor` не протекает.
- **Переименование** идёт мимо batch-механизма: спутнику нужно новое **имя**,
  а не новая папка, поэтому `RenamePlan` + `RenameMany`, и последний
  откатывает уже сделанное, если упал на середине.

**Оценки** (`Rank` / `ColorLabel`) читаются и пишутся через один тип
`SidecarRating` — формат прячется за `CompanionMetadataService`, который
выбирает парсер по расширению. Цветовые метки нумерованы одинаково в обоих
форматах (`ColorLabels`), поэтому один ряд кружков правит и `.pp3`, и `.xmp`;
XMP при этом хранит имя (`Red`), а pp3 — номер. Общая байтовая обвязка
(BOM, переводы строк) — в `SidecarText`.

**Запись в чужой формат** — первое, что Wander делает с файлами, которых не
создавал, поэтому путь узкий намеренно:

- только поля оценки. В **уже существующем** сайдкаре это правка одной
  строки; создание нового — отдельный путь (`CreateRatingSidecar`, ниже) и
  только по явному согласию;
- XMP правится строковой хирургией, а не через `XDocument`: round-trip через
  XML-парсер переписал бы порядок атрибутов, префиксы неймспейсов и
  `<?xpacket?>` с его padding'ом — то есть вернул бы другим программам
  переформатированный пакет. Если нужного свойства в пакете нет, оно
  добавляется атрибутом в `rdf:Description`, но **только** когда неймспейс
  `xmp:` уже объявлен: дописывать чужие объявления мы отказываемся вслух
  (`NotSupportedException`), а не угадываем;
- только через `IFileSystem.ReplaceAtomic` (temp рядом → `File.Replace`);
- правится ровно одна строка, остальные байты переносятся как есть —
  в `.pp3` лежит вся работа пользователя по проявке;
- файл ходит байтами: BOM распознаётся и возвращается на место, `\r\n` / `\n`
  сохраняются построчно;
- прежнее значение — в `SidecarRatingAction` на undo-стеке.

**Создание сайдкара** (`CreateRatingSidecar`) — единственное место, где
Wander создаёт файл, которого пользователь не называл, поэтому вокруг него
весь проектный набор: подтверждение с Cancel по-умолчанию (спрашивает
`MainViewModel`, потому что диалоги — его), лог, `SystemPathGuard`,
`IUndoableAction` (`SidecarCreatedAction`) — и undo **удаляет** файл, а не
обнуляет в нём оценку: пустой `.pp3`, оставшийся после отмены, это не
«как было». Уже существующий файл — отказ (`InvalidOperationException`):
это правка, и у неё свой путь. Снятие оценки ничего не создаёт.

**Почему `.xmp` по умолчанию, а не `.pp3`.** Это не выбор расширения, а
выбор побочного эффекта. RawTherapee применяет профиль обработки по
умолчанию (Auto-Matched Curve) только к снимкам, у которых сайдкара нет; как
только `.pp3` появляется, читается он, а ключи, которых в нём нет, берутся
из жёстко зашитых нейтральных значений, а **не** из профиля. То есть `.pp3`
с одной строкой `Rank=3` меняет то, как снимок открывается. `.xmp` такого
эффекта не даёт ни в одной программе, а RawTherapee читает оценки из XMP с
5.7 и синхронизирует их с 5.11. Формат — настройка
(`AppSettings.RawRatingFormat`); при выборе `.pp3` предупреждение
повторяется в диалоге. Разбор — на `SidecarFormat.Pp3` в коде.

`.meta` **только читается**. Unity владеет этим файлом и перегенерирует его
на своих условиях; наша перезапись способна отвязать ассет от всех ссылок во
всех сценах.

---

## Галерея и оценки

### Запись оценки не пересобирает папку

Это правило, а не оптимизация (см. [CLAUDE.md](../CLAUDE.md)). Раньше клик по
звезде приводил к `Refresh()`: строки пересоздавались, выделение уезжало
вместе с ними, сортировка по оценке переставляла сетку, и снимок, на который
человек смотрел, уходил из-под курсора — в ответ на изменение одного числа
в одном файле.

Как устроено сейчас:

```
MainViewModel.ApplyRating(строки, поле, значение)
   ├── делит выделение на «сайдкар есть» / «сайдкара нет»
   ├── спрашивает про вторую группу ОДИН раз на всю пачку
   ├── CompanionMetadataService.ApplyRatingToMany(...)   один CompositeAction
   └── ApplyRatingResults(...)
          ├── глушит сторожевой таймер папки на три интервала
          ├── SearchController.Replace(обновлённые строки)
          │      ├── состав видимого не изменился → ItemsChanged (только эти строки)
          │      └── строка выпала из фильтра   → обычный полный проход
          └── ReplaceRows(...)  Entries[i] = новая строка, затем выделение назад
```

Три места, где это могло сломаться, и что их держит:

- **Выделение.** `record` нельзя поправить на месте, поэтому строка
  заменяется, а список выкидывает заменённый объект из `SelectedItems`.
  `ReconcileEntries` оборачивает **любую** пересборку `Entries` — и точечную
  замену, и полный `SyncEntries` — запоминая выделение по путям до неё и
  возвращая его после. На время пересборки `_rowsReplacing` глушит
  `SelectedEntry` и `SelectedEntries` целиком: список по дороге выкидывает
  заменённые объекты и переносит `SelectedItem` на следующий уцелевший, и
  без этого обновление трёх строк успевало провести панель просмотра по трём
  чужим фотографиям. Возвращает выделение отдельное событие
  `SelectionRefreshRequested`: в отличие от `SelectionRestoreRequested` оно
  **не** прокручивает и **не** трогает фокус.

  Порядок внутри неочевиден и важен: `SelectedEntry` привязан к
  `SelectedItem` списка, а присвоение `SelectedItem` схлопывает
  множественное выделение в одну строку. Поэтому сначала возвращается
  «главный», потом остальной набор — наоборот набор тут же терялся бы.

- **Сторожевой таймер.** Теперь он говорит, **что** изменилось
  (`DirectoryChange`), а `FolderChanges` копит пачку до тика и решает:
  изменился состав — `Refresh()`; изменилось содержимое файлов, и все они
  принадлежат строкам, которые мы показываем, — перечитать эти строки.
  Файл, которого листинг не знает, — тоже `Refresh()`: гадать здесь значит
  тихо разойтись с диском.

  Предыдущая попытка глушила тик по времени, и это было хуже, чем кажется:
  настоящее внешнее изменение, попавшее в окно, **пропадало насовсем**, а
  событие, опоздавшее на миллисекунду, всё равно пересобирало папку.

- **Панель просмотра.** `SetPrimary` сравнивает не ссылку, а путь, размер и
  время правки: тот же файл в новой строке — это перечитать блок спутников,
  а не декодировать заново 30-мегабайтный RAW.

**Служебные файлы записи.** `ReplaceAtomic` пишет `<файл>.wander-tmp` и
переименовывает его на место, а `File.Replace` вдобавок создаёт **свой**
бэкап `<файл>~RF<hex>.TMP` и удаляет его через мгновение. Оба видны
сторожу, оба выглядят как «в папке появился и исчез файл», то есть как
изменение состава — и именно они пересобирали папку на каждую звезду. Оба
отфильтрованы в `TransientFiles`, в одном месте на весь проект. Виндовый
не описан рядом с API, который его порождает; поиск занял отдельный заход
с логом сторожа, поэтому он записан здесь, а не только в коде.

Переименование **из** нашего служебного файла — не изменение состава, а
запись содержимого: имя, которое исчезло, в листинге не было, а имя, которое
появилось, в нём уже было (`WindowsDirectoryWatcher.OnRenamed`).

**Порядок при этом не меняется**, даже когда список отсортирован по оценке:
пересортировка под курсором — ровно тот прыжок, которого всё это избегает.
Новый порядок приезжает со следующим листингом.

**`Ctrl+Z` идёт тем же путём.** `IUndoableAction.MetadataTargets` называет
файлы, у которых действие меняет только метаданные — снимки, а не сайдкары.
Непустой список означает «состав папки не изменился», и `UndoLast`
перечитывает эти строки (`RefreshMetadataRowsAsync`) вместо `Refresh()`.
`CompositeAction` возвращает объединение только если **все** его члены —
метаданные: смешанная пачка «переместили файл и поставили оценку» меняет
состав папки, и дешёвый путь показал бы папку, которой больше нет.

### Проход по оценкам — второй, а не часть листинга

`FileSystemEntry.Rating` заполняется не обходом каталога (он про сайдкары
ничего не знает), а отдельным проходом после того, как листинг уже на
экране:

```
RefreshFolderAsync            листинг + свёртка спутников (тредпул)
   ├──▶ AutoSelectViewMode()  только при входе в папку, не на F5
   ├──▶ _search.SetSource()   строки появляются на экране
   └──▶ StartRatingPass()
          └──▶ RatedListing.WithRatings()   (тредпул, с отменой)
                 └──▶ _search.SetSource()  ещё раз, уже с Rating
```

Почему вторым проходом: папка из пятисот RAW — это пятьсот маленьких чтений,
и папка должна появиться раньше, чем они закончатся. Почему дёшево: проход
трогает только строки, у которых **уже есть** `Companions`, поэтому папка без
сайдкаров не стоит ни одного обращения к диску, а `RatedListing.WithRatings`
в этом случае возвращает **тот же самый список по ссылке** — вызывающий
сравнивает ссылки и пропускает весь UI-проход. Живёт проход в
`Wander.Core/Listing/`, а не рядом с сайдкарами: прочитать оценку одного
файла — вопрос про файл, а пройти строки папки и решить, какие из них
заменить, — вопрос про листинг. Как читается
оценка, приходит делегатом (`CompanionMetadataService.ReadRatingFor`).

Отмена — та же схема, что у листинга: свой `CancellationTokenSource`,
проверка `_listedPath` перед публикацией. Проход, добежавший после ухода в
другую папку, накрыл бы её строки чужими.

Второй `SetSource` не перерисовывает список: `SyncEntries` сверяет строки
через `SameRow`, куда добавлено сравнение `Rating` (обычное равенство
record'а — это единственное, что меняет проход). Заменяются только строки,
у которых оценка появилась, поэтому выделение, фокус и прокрутка на месте.

**Сортировка по оценке** — единственный ключ, которого обход каталога не
знает. `SortKey.Rating` существует в `EntryComparers` наравне с остальными,
первый проход сортирует по нему при пустых оценках (то есть фактически по
имени), а проход по сайдкарам пересортировывает результат через
`EntryComparers.Sort` — ту же функцию, которой пользуется
`SystemIOFileSystem.Enumerate`, чтобы разбиение «папки сверху» не оказалось
в двух местах в двух версиях. Известная неточность: наверху компаратор имён
ординальный, а не `StrCmpLogicalW` — записано в
[TECHDEBT.md](TECHDEBT.md).

Неоценённое сравнивается как ноль, а не ниже нуля: «нет сайдкара» и «ноль
звёзд» — одно и то же утверждение о снимке, и папка на середине прохода не
должна переставлять строки, пока null'ы превращаются в нули.

### Фильтр — внутри SearchController, а не рядом

Фильтр по звёздам и цвету (`RatingFilter`) живёт там же, где фильтр по
имени. Проекция папки на экран одна, и два независимых фильтра, гоняющих
друг друга за право её построить, — ровно та гонка, ради которой
`SearchController` когда-то и вынесли из view-model. `Reset()` (навигация)
снимает оба.

**Набор, а не порог.** `RatingFilter` держит два битовых набора — какие
оценки проходят и какие цветные метки, — а не нижнюю границу. Порог отвечает
ровно на один вопрос («что оставить»), а «что я отложил на два» и «что я ещё
не смотрел» — уже другие, и оба выпадают из набора бесплатно: обычный клик
берёт элемент и всё выше него, `Ctrl` + клик добавляет или убирает один.

Ранг 0 — такой же член набора и означает **без оценки**; это единственный,
который обычный клик берёт в одиночку, потому что «без оценки и выше» — это
вся папка. В полосе он нарисован перечёркнутой звездой слева от остальных:
пустая звезда уже значит «этот ранг не в фильтре», а перечёркнутая говорит
«ранга нет вовсе» — другое утверждение, и именно оно тут делается.

Набор видно с экрана — горит ровно то, что выбрано (`RatingFilter.HasRank`,
`FilterStarConverter`), поэтому «три и четыре, но не пять» не приходится
держать в голове.

`Alt` не делает ничего: он раньше означал «ровно этот ранг», а набор говорит
это лучше и без модификатора, которого не видно.

Сам клик разложен надвое (`ReadFilterGesture` читает клавиатуру,
`ClickRankFilter` / `ClickColorFilter` делают дело) ровно по одной причине:
офлайн-харнесс не имеет права трогать клавиатуру — синтезированный
зажатый `Ctrl` это настоящий ввод на настоящей машине, — а поведение клика
проверять надо.

Папки фильтр не отбрасывает никогда: у папки нет звёзд, а спрятать выход из
папки со снимками — не то, о чём просили.

### Что решает, что папка — со снимками

`ImageFolderProbe.IsImageFolder` — чистая функция от листинга и набора
правил спутников. Вся сложность в знаменателе: считаются только
содержательные файлы, то есть не спутники (правила берутся у
`CompanionResolver`, чтобы «что такое спутник» было в одном месте), не
резервные копии и не подпапки. Без этого папка, где у каждого RAW лежит
`.pp3`, набирает ровно 50% и галерея не включается именно там, где нужна.
Минимума по количеству нет — четыре фотографии и ничего больше это папка с
фотографиями.

Набор расширений — `Wander.Core/Icons/ImageFormats`, один на проект: им
пользуется и панель просмотра, и проба. Раньше это были два списка в
`PreviewController`, и два списка, которые обязаны совпадать, рано или
поздно расходятся.

### Автовыбор вида: две переменные, а не одна

`MainViewModel` держит `_viewMode` (что на экране) и `_userViewMode` (что
человек выбрал последним). Сохраняется в `state.json` второе.

- `SetViewMode` (меню, хоткей, контекстное меню) пишет обе и помечает
  текущую папку в `_manualViewModeFolders`;
- `AutoSelectViewMode` вызывается только при **входе** в папку и ставит
  `Gallery` либо `_userViewMode` — вторая половина так же важна, как первая,
  иначе галерея, включившись однажды, расползается на все папки;
- в помеченной папке автоматика молчит и ставит **тот вид, который там
  выбрали**.

**Где живут пометки.** В `state.json`, в сессионном ведре:
`SessionState.ManualViewModes` — список пар «путь → имя режима», с потолком
в 128 записей и вытеснением самых старых. Не в `AppSettings`, потому что это
не предпочтение, а «где я остановился в той папке», рядом с `LastPath` и
`ExpandedPaths`; и с потолком, потому что список рос бы по записи на каждую
папку, в которой когда-либо переключали вид, а `state.json` читается на
каждом запуске. Имя режима — строка по той же причине, что и у соседей:
переставленный enum не должен молча переосмыслить сохранённое.

Это **не** `desktop.ini` (PLAN H1) — тот пишет вид в саму папку, виден
Проводнику и требует решения про чужие файлы. Здесь всё остаётся внутри
Wander.

### Фон галереи — палитра, а не цвет

`GalleryBackground` (Light / Grey / Dark) — значение в Core; яркость двух
затемнённых вариантов — две настройки (`GalleryGreyLevel`,
`GalleryDarkLevel`), потому что нейтральный, который читается правильно,
зависит от монитора и от освещения комнаты не меньше, чем от снимков.
`GalleryPalette` в App собирает из этих трёх чисел **весь** набор кистей:
фон, подпись, приглушённый текст, ховер, выделение активное и неактивное,
плюс рамки к ним.

Один тип, потому что роли обязаны двигаться вместе. Тёмный фон со светлой
темой подписи — это не тёмная тема, а нечитаемая; тёмный фон с проводниковым
бледно-голубым выделением — это ряд лайтбоксов, которые ярче фотографий под
ними, и глаз уходит на рамку вместо картинки. Поэтому на затемнённом фоне
подсветка — это **подъём самого фона** (`Lift`), а не цвет поверх него, и
светлым он остаётся только там, где фон светлый: на нём проводниковые
`#CCE8FF` / `#E8E8E8` берутся как есть, чтобы галерея совпадала с
остальными видами везде, где может.

`Light` — это `SystemColors.WindowColor`, а не белый нашего изготовления:
смысл этого варианта в том, что галерея перестаёт выглядеть отдельным
приложением внутри окна. Он же по умолчанию — вид, который в первый раз
открывается тёмным, читается как включённая кем-то тема, а не как
инструмент, который сейчас настроят; серый, который нужен фотографу, стоит
в одном клике и запоминается.

**Панель просмотра берёт тот же фон** — но только под картинкой (`Image` и
`Gif`, плюс холст лупы). Снимок, оценённый на сером и открытый на белом, —
это два разных снимка для глаза; а текст, код и документы приносят свой фон
и на этом смотрелись бы сломанными.

Тот же набор — первый кусок тёмной темы из Roadmap: переключение палитры
области расширяется, а один захардкоженный цвет пришлось бы выдирать. Второй кусок —
общий словарь цветов, см. «Цвета — один словарь на всё приложение».

---

## Окно и его контролы

`MainWindow` держит то, что окружает содержимое, и ничего из самого
содержимого:

| Кто | За что отвечает |
|---|---|
| `MainWindow` | Тулбар, адресная строка, статус-бар, глобальные хоткеи, сборка контекстного меню, исполнение клавиатурных областей и геометрии окна (решения — в `Wander.Core/Layout/`) |
| `Views/FolderTreesView` | Обе панели папок и всё, что делает строка дерева: клик открывает, шеврон только раскрывает, drag узла, правый клик как цель операции, `Shift` + колесо, коалесированный клавиатурный обход, полоса «+» для закладок |
| `Views/FileListView` | Все режимы отображения списка и их общие жесты: выделение, рамка, взведение drag, двойной клик, переименование на месте, набор имени, `Ctrl` + колесо, вызов меню |
| `Views/PreviewPane` | Всё, что рисуется в панели просмотра, плюс зум по правой кнопке, транспорт видео и инициализация WebView2 |
| `DragPreview/DropTargetController` | Приём drop'а: какая папка под курсором, разрешён ли туда drop, что он сделает, подсветка цели |
| `DragPreview/OutgoingDrag` | Перетаскивание наружу, пока оно идёт: плашка у курсора, курсор, формулировка «что и куда» |

**Главная вью-модель живёт при окне, а не в `ViewModels/`.**
`MainViewModel.cs` лежит в корне `Wander.App`, рядом с `MainWindow.xaml.cs`,
и её namespace — `Wander.App`. Это не оплошность: она хостит контроллеры, а
контроллеры берут базовые типы из `ViewModels/`, и пока она лежала там же,
между двумя папками стоял цикл. Переезд (шаг O9, 2026-09-01) оставил
`ViewModels/` слоем биндабельных типов **ниже** контроллеров. Искать её
поэтому надо там, где окно, а не там, где остальные вью-модели.

**`DropTargetController` решает, но не действует.** Он отвечает планом
(`DropPlan`: что, куда, каким действием), а выполняет план вью-модель —
чтобы файловая операция шла тем же единственным путём, который её логирует,
проверяет `SystemPathGuard` и делает откатываемой. Зовёт её та поверхность,
на которую бросили, одной строкой: `Execute` держит у себя всё вокруг плана
(отказ, `Handled`, снятие подсветки), потому что забыть `Clear()` в
`finally` — это подсветка, оставшаяся висеть после драга. Один
контроллер обслуживает все поверхности, принимающие drop (список во всех
трёх режимах, дерево дисков, закладки-папки), потому что ответ в них обязан
быть одинаковым. Проверки повторяются на самом drop'е, а не берутся с
последнего `DragOver`: между последним движением и отпусканием кнопки
модификаторы успевают измениться, и ровно так перемещение становится
копированием.

Граница между окном и списком — два события и несколько методов:

- `FileListView.DragStartRequested` — «пользователь потащил выделение».
  Жест принадлежит тому, за что схватились, — списку или строке дерева, —
  а сам drag ведёт `OutgoingDrag`: плашка, курсор и формулировка одни на
  все источники. Куда бы упало, спрашивается у `DropTargetController`;
  единственное, чего drag не видит сам, — загорание полосы закладок, и о
  нём ему сообщает окно.
- `FileListView.ContextMenuRequested` — «покажи меню вот здесь». Модель
  меню собирает Core (`ContextMenuBuilder`), шелловские пункты добавляет
  окно.
- Наружу контрол отдаёт `FocusList()`, `FocusRow()`, `ClearSelection()`,
  `StartRename()` — то, что окну нужно после навигации, `Esc` и `F2`.

Граница между окном и панелями папок устроена так же — три события наверх
и горсть методов вниз:

- `ContextMenuRequested` — меню собирает окно (модель из Core плюс пункты
  шелла), панель знает только, какую папку кликнули и где показать.
- `FolderTargeted` — «операции теперь про эту папку». Список обязан отдать
  за это своё выделение, а список — не её сосед, а сосед окна.
- `FocusListRequested` — `Esc`: клавиатура возвращается в список.
- Вниз идут `FocusBookmarks()` / `FocusDrives()` / `HasBookmarks` /
  `PaneOf()` / `ShowFocusOutline()` / `RevealAndFocus()` — то, что нужно
  порядку областей, который остался в окне.
- `Connect(drops, drag)` передаёт два общих объекта drag & drop: и панели,
  и список обязаны отвечать на одно и то же перетаскивание одинаково, а
  для этого им нужны **те же** `DropTargetController` и `OutgoingDrag`.

### Клавиатурные области

`Tab` в окне переключает не контролы, а **области**: тулбар → адресная
строка → фильтр → закладки → дерево дисков → список.

Сам порядок и обход по нему живут в Core — `Wander.Core/Layout/WindowZones`
(`WindowZone`, `Order`, `Ring`, `FolderPane`): это кольцевая арифметика и
лестница умолчаний, то есть ровно те два места, где прячется ошибка на
единицу и забытый случай «а если ни та, ни другая?». Окну осталось
исполнение: принадлежность элемента области считается подъёмом по
визуальному дереву (`ZoneOf`), переход — `CycleZone` поверх `Ring`, вход —
`FocusZone`.

**Почему обходом, а не средствами WPF.** Родной `Tab` идёт по дереву
контролов в порядке их объявления и заходит внутрь каждого; чтобы получить
из него нужный порядок, пришлось бы расставлять `TabNavigation` и
`IsTabStop` по всей разметке и следить за ними при каждой правке вёрстки.
Список из шести элементов в одном месте — то же поведение, но его видно
целиком. Побочный эффект: `Tab` теперь **всегда** значит «следующая
область», в том числе из текстового поля, — как в Проводнике.

Область умеет отказаться: `FocusZone` возвращает `false`, когда фокусировать
нечего (свёрнутые закладки, все три кнопки навигации выключены при пустой
истории), и обход идёт дальше. Панель просмотра в список намеренно не
входит — клавиатурного поведения у неё пока нет, и остановка на ней была бы
тупиком (запись в [BACKLOG.md](BACKLOG.md)).

**Сочетания с `Alt` не могут быть `KeyBinding`.** В верхней панели живёт
настоящий `Menu`, а `Alt` переводит окно в режим меню — аккорд тратится там
раньше, чем доходит до маршрутизации команд, и `KeyBinding` в
`Window.InputBindings` не срабатывает нигде, кроме самой панели. Поэтому
`Alt` + `←` / `→` / `↑`, `Alt` + `Enter` и `Alt` + `D` разбираются в
`MainWindow.OnPreviewKeyDown`: туннелирование от окна идёт впереди и режима
меню, и `InputBindings`. Там же `Esc` для всей адресной области — он должен
работать и с кнопки-крошки, а не только из поля ввода.

**Фокус на самом списке — тупик, и он лечится, а не запрещается.** Фокус
клавиатуры обязан где-то быть; когда его не удаётся поставить на строку
(ничего не выделено, строку снёс перестроенный листинг), WPF роняет его на
`ItemsControl`. У списка в этом состоянии нет каретки, от которой отсчитывать
шаг, и WPF на стрелку не отвечает ничем. Три места закрывают это:

- `FileListView.TryEnterList` — первая стрелка входит в список сверху
  (`↓` / `→`) или снизу (`↑` / `←`), а если выделение всё же стоит, каретка
  возвращается на него;
- `FocusVisualStyle` у стиля `ListGestures` снят — системный пунктир вокруг
  половины окна дублировал рамку активной области и читался как поломка;
- `FileListView.TakeKeyboardOnClick` — обе ветки нажатия, которые помечают
  событие обработанным (лассо по пустому месту и удержание мультивыделения
  до отпускания кнопки), забирают клавиатуру сами: обработанное нажатие до
  контрола не доходит, и без этого клик в область файлов оставлял
  клавиатуру в той панели, из которой пришёл.

Активная область обведена рамкой. Рамка — не отдельный слой, а
`BorderBrush` самих контролов (`BorderThickness` всегда 1, меняется только
цвет), поэтому переключение области ничего не двигает. Красит
`OnZoneFocusChanged`, подписанный на `GotKeyboardFocus` **окна**: фокус
может оказаться где угодно, включая хром, который ни к какой области не
относится.

Разделители панелей из обхода убраны насовсем: WPF делает `GridSplitter`
фокусируемым ради изменения размера стрелками, и он оказывался посреди
цепочки как невидимая остановка. Цена решения — размер панелей меняется
только мышью; добраться до клавиатурного варианта всё равно можно было
только слепым перебором.

**`Ctrl` + `1`** пользуется тем, что `NavigationSource` уже лежит в истории
(решение — `WindowZones.FolderPane`, пять фактов на входе и панель на
выходе):
он раскрывает дерево до текущей папки в **той** панели, из которой её
открыли (`MainViewModel.RevealCurrentIn`), и встаёт на её узел. Нажатый
повторно — переключает панель. Открыли не из дерева (адресная строка,
восстановление сессии) — берётся панель, в которой клавиатура была
последней (`_lastFolderPane`, обновляется тем же `OnZoneFocusChanged`).
`Ctrl` + `Shift` + `E` — то же самое без переключения. `Ctrl` + `2`
возвращает клавиатуру в список: цифры идут слева направо, как сами области
на экране.

**В дереве стрелки не навигируют.** `TreeView.SelectedItemChanged` летит и
от мыши, и от клавиатуры, и от программного выделения;
`OnTreeSelectionChanged` навигирует только по первому — флаг взводится в
`Tree_PreviewMouseLeftButtonDown` на время нажатия. Движение клавиатурой
вместо навигации переносит **цель операций** на узел под курсором
(`TargetTreeNode` — тот же `SelectExternalPath`, что и у правой кнопки), а
список на это время отпускает выделение: ровно один подсвеченный набор на
экране и есть ответ на вопрос «к чему относится `Delete`». Клавиатурный вход в
папку — `Enter`, перехваченный в `Tree_PreviewKeyDown` (иначе сработала бы
`KeyBinding` окна и открылось бы выделенное **в списке**). Проводник
навигирует на каждое изменение выделения, и проход стрелкой мимо десяти
папок читает все десять.

Обратная сторона того же правила: клик по строке, на которой курсор уже
стоит, выделения не меняет и события не рождает — навигировать было бы не с
чего. Поэтому такой клик `Tree_PreviewMouseLeftButtonDown` отрабатывает сам:
мышью «открыть» значит всегда.

**Зачем так.** Пока три `ItemsControl` и их обработчики жили в окне, каждый
новый режим означал правку окна в четырёх местах и шанс забыть один из
жестов. Теперь режим — это контейнер и триггер видимости внутри
`FileListView`; общий набор жестов навешивается одним стилем
(`ListGestures`), так что «забыть» его на новом контейнере не выйдет.

Обещание проверено на четвёртом режиме: галерея добавилась контейнером и
триггером здесь, а окно узнало о ней ровно двумя строками — пунктом меню и
`KeyBinding`. Полоса фильтра оценок тоже живёт тут, `DockPanel.Dock="Top"`
над контейнерами: она про то, какие строки показывает список, а не про то,
что окружает список.

### Цвета — один словарь на всё приложение

Все цвета хрома лежат в `Resources/Palette.xaml` — именованные кисти,
сгруппированные по тому, **что они красят** (поверхности, линии, текст,
строки списка, контролы, акцент, метки, меню), а не по оттенку.
Словарь влит в `App.xaml` первым, поэтому `{StaticResource}` виден из
любого окна и шаблона; `MenuStyles.xaml` вливает его и сам — этот
словарь грузится и отдельно, а `StaticResource` разрешается на разборе.

Цвет в code-behind берётся из того же словаря через `Resources/Palette.cs`
— там, где кисть не привязать: адорнер рисует `Pen`'ом, плашка драга
выбирает цвет по глаголу, рамка активной области зажигается из обработчика
фокуса. Все поля там — `static readonly` на одном классе нарочно:
обращение к любому разрешает все, и опечатка в ключе падает громко на
первой отрисовке, а не на единственном жесте, который никто не пробовал.

**Зачем.** Тёмная тема из Roadmap — это второй набор тех же значений и
больше ничего; работает это только тогда, когда больше ничего и нет. Оставленный в вьюхе
`Foreground="#888"` — это угол окна, который останется светлым, и увидеть
его можно только после того, как это уже случилось.

Что в словарь намеренно не попало — перечислено в шапке самого
файла: `GalleryPalette` (там цвета вычисляются, а не выбираются, см. выше),
`Highlighting/*.xshd` (свой формат AvalonEdit для токенов кода),
`DefaultBackgroundColor` у WebView2 (цвет `System.Drawing` на нативном
контроле, и это бумага документа, а не хром окна), сборка обложки книги
в `SystemIconProvider` (там рисуется битмап, и платформенный слой всё равно
не видит ресурсов App). Свет трёхмерной сцены в словаре **есть**, но
отдельным разделом «Not chrome»: он освещает модель, а не окно, и тема
его не трогает.

Лестница из семи градаций серого текста (`TextSecondary` … `TextDisabled`)
унаследована как есть: свести её значило бы поменять то, что на экране
сегодня. Нужно ли семь ступеней — вопрос тёмной темы, запись в
[TECHDEBT.md](TECHDEBT.md).

### Подсветка плитки

Подсветку рисует **контейнер строки** (`ListBoxItem`) — своим
`ControlTemplate`, по property-триггерам на себе самом: `TileChrome` даёт
форму, `TileItem` — проводниковые цвета плиток и значков, `GalleryItem` —
цвета из палитры галереи, и только в сеттерах триггеров, то есть лениво.

Ключ ко всему — **отступ ячейки задан `Margin`-ом контейнера**
(из `TileMetrics`, приходит ресурсом от `ApplyTileMetrics`). Контейнер
из-за этого равен ячейке минус её поля, то есть равен самой плитке, а не
всей ячейке — и ряд выделенных файлов не сливается в сплошное полотно, как
было бы, подсвечивай контейнер целую ячейку. Шаблон при этом не тратит ни
пикселя раскладки: `Padding` нулевой, рамка внутри `Border`-а контейнера.

Так было не всегда. Раньше контейнер не рисовал ничего (голый
`ContentPresenter`), а подсветку рисовал `TileHighlight` — пустой `Border`
соседом содержимого внутри плитки, с семью `DataTrigger`-ами, каждый через
`RelativeSource`-привязку вверх к `ListBoxItem`, **в каждой плитке**. После
шаблона переименования это была вторая по цене вещь в шаблоне (PLAN R,
2026-09-02). Единственное видимое следствие переезда: в зазоре между
плитками ничего не подсвечивается — там и раньше не было хит-теста, теперь
это просто явно.

### Шаблон плитки — что нельзя

Шаблон строки в плиточных видах — это то, что оплачивается на **каждой**
навигации, помноженное на число видимых плиток, а проход по папкам
фотографий не состоит больше ни из чего. Отсюда список запретов; всё в нём
измерено, а не предположено (PLAN R2/R3):

- **Никакого `TextBox`** — ни редактора имени, ни чего-либо ещё. Редактор
  один на весь контрол и лежит поверх подписи (`RenameAdorner`); подпись в
  каждом шаблоне называется `TextBlock x:Name="NameLabel"`, и это контракт,
  по которому адорнер её находит.
- **Никаких `Style.Triggers` на элементах шаблона.** Состояние строки —
  дело контейнера и его `ControlTemplate`; всё, что зависит от данных,
  считает конвертер (вторая строка плитки — `TileSecondLineConverter`).
- **Никаких `RelativeSource`-привязок.** Размеры приходят ресурсами
  (`DynamicResource`, переписывает `ApplyTileMetrics`) или наследованием
  (кегль подписи — `FontSize` на `ListBox`). `RelativeSource` — это обход
  дерева и подписка на каждый контейнер, на каждой папке.
- **Ничего, что видно у меньшинства строк, — безусловно.** Бейдж оценки в
  галерее строится только у оценённых: пустой `ContentControl`, которому
  `DataTemplate.Trigger` подкладывает `Content` и `ContentTemplate`.
  Замерено на настоящей ячейке: 11 визуалов у каждой → 9 у неоценённой и
  13 у оценённой.

Нижняя планка, с которой это сравнивалось, — шаблон из одной картинки и
одной подписи, все размеры числами (5 визуалов): продуктовые 6–9 против
прежних 18–23. Строка `LAYOUT <вид> container: N visuals` в журнале
сеанса пишет число визуалов один раз на шаблон, так что регресс тут виден
без замера.

### Плиточные режимы: TileLayout + VirtualizingWrapPanel

WPF не поставляет виртуализирующий wrap-панель, а обычный `WrapPanel`
в папке на десятки тысяч файлов строит все контейнеры до первой отрисовки —
и каждый из них просит у `AsyncIcon` миниатюру. Отсюда своя панель.

Она разделена надвое, и это главный вывод из истории её багов:

- **`Wander.Core/Layout/TileLayout`** — вся арифметика: сколько колонок
  влезает, где лежит ячейка с номером N, какой высоты вся сетка, какой
  диапазон элементов стоит реализовать при данном сдвиге прокрутки, куда
  доскроллить, чтобы показать элемент. Ни одного типа WPF; неизменяемое
  значение, которое пересчитывается с нуля на каждый проход раскладки.
  Покрыто тестами (`TileLayoutTests`).
- **`Wander.Core/Layout/TileMetrics`** — размер ячейки и размер того, что
  в ней нарисовано, посчитанные из настроек. Панель раскладывает по этим
  числам, шаблон рисует по ним же. Тоже покрыто тестами
  (`TileMetricsTests`). У «Плитки» и «Крупных значков» свои наборы
  настроек и своя фабрика (`ForTiles` / `ForLargeIcons`) — удобная сетка
  фотографий и удобная строка плиток задаются разными числами. Производные
  величины (второй кегль в плитке) считаются здесь же, а не заводятся
  отдельной настройкой.
- **`Wander.App/Controls/VirtualizingWrapPanel`** — обвязка: спросить у
  генератора контейнеры, померить их, расставить туда, куда сказал
  `TileLayout`.

**Почему именно так.** Все три бага плиточных режимов были в арифметике, и
ни один нельзя было ни увидеть в отладчике, ни закрыть тестом, пока она
сидела внутри панели вперемешку с изменяемым состоянием:

1. Число колонок считалось от размера ячейки, известного с прошлого
   прохода, а расставлялись ячейки уже по новому. Список рисовался в три
   колонки с шагом на шесть, экстент (и бегунок) считались по неверному
   числу строк, а половина файлов не реализовывалась вовсе. Свежесозданные
   контейнеры — как раз тот момент, когда размеры расходятся: их привязки
   к настройкам разрешаются на такт позже.
2. `ArrangeOverride` при расхождении вьюпорта просил новый measure. Стоит
   ширине вьюпорта отличаться от ограничения на ширину полосы прокрутки —
   и measure с arrange начинают гонять друг друга по кругу. Это и было
   зависание при промотке вниз.
3. `BringIndexIntoView` дёргал `UpdateLayout()`. Его зовут из
   `ScrollIntoView`, в том числе изнутри прохода раскладки, — то есть
   раскладка входила сама в себя.
4. **Дребезг размера ячейки — то самое зависание.** Высота контейнера
   возвращается разной на доли пикселя в зависимости от положения
   прокрутки: 56,59 на одном сдвиге, 56,00 на другом. Панель принимала
   новую высоту → менялся экстент (на 980 px при пяти тысячах файлов) →
   менялся предел прокрутки → сдвиг подстраивался → высота возвращалась
   обратно. Замкнутая петля «прокрутка → размер → экстент → прокрутка»,
   ровно по сценарию «мотнул вниз, потом чуть вверх».

   Видно это было прямо в трейсе: `MaxVerticalOffset` чередовался между
   92207,6 и 93187,0. Лечится двумя вещами вместе — подтверждением
   (новый размер принимается только если два прохода подряд показали
   одно и то же) и полосой нечувствительности в целую единицу раскладки:
   подтверждения мало, потому что каждое из двух значений держится
   десятками проходов подряд и успевает «подтвердиться».

**Чем это ловилось.** Офлайн-харнесс в scratchpad: настоящие
`FileListView` и `MainViewModel`, настоящий диспетчер и настоящее окно —
но за пределами экрана и с подменённым `IAppStateStore`, чтобы не трогать
ни чужой экран, ни `state.json`. Жесты подаются на UI-поток, а сторожевой
поток считает проходы раскладки, пока UI-поток занят: если счётчик не
останавливается — окно заклинило. На папке в пять тысяч файлов прыжок
в конец давал 1396 проходов и продолжал; после починки — 5.

Инвариант, который держит первый случай, вынесен в тест буквально:
`ExtentWidth` не превышает ширину вьюпорта, пока колонок больше одной.
Панель после этого не хранит производного состояния — есть размер ячейки,
вьюпорт и сдвиг, всё остальное считается, поэтому рассинхронизироваться
нечему.

**Размер ячейки — вход, а не выход (2026-08-26).** Дольше всего держался
пятый баг того же рода: размер ячейки панель *узнавала*, меряя
реализованный контейнер. Это замыкало раскладку в кольцо — контент решал
геометрию, геометрия решала, какие контейнеры существуют, — и папка в
итоге сама выбирала, какого размера у неё ячейки. Мерилом был случайный
контейнер, а контейнер настолько же велик, насколько в этот момент успели
разрешиться его привязки, доехать миниатюра и посчитаться метрики подписи.
В трейсе, который это закрыл, сетка LargeIcons жила на ячейках 70×40
(контейнер, померенный до появления значка: одна подпись и больше ничего)
при плитках 104×114; в другой сессии залипла ячейка с пропорцией 2:3 —
это соотношение сторон фотографии, а не шаблона. `CellSizeProbe`
(подтверждение из двух совпавших замеров) гасил дребезг, но ровно тем же
механизмом намертво залипал на мусорном значении: разные контейнеры между
собой не совпадают, а значит принятое однажды число уже не сменится.

Теперь `TileMetrics` считает ячейку из настроек, ViewModel отдаёт её одним
значением (`Settings.IconsMetrics` / `TilesMetrics`), а панель и шаблон
привязаны к этому одному значению. Дети меряются **ровно ячейкой** — им
сообщают, сколько места есть, а не спрашивают, сколько они хотят. Кругов
в measure больше нет: один проход считает колонки, диапазон и позиции.
Живое изменение размеров из диалога настроек при этом сохранилось — оно
теперь просто `PropertyChanged` на `IconsMetrics`.

Проверялось тем же офлайн-харнессом (см. выше): 300 файлов с именами
разной длины, ячейка 104×114 неизменна при прокрутке, все контейнеры
разложены ровно в неё, в простое 0 проходов раскладки, на щелчок колеса
8–14 (было 40 и 384 в простое), после смены значка 72 → 128 ячейка сразу
104×170, переключение режимов туда-обратно не сбивает ни то, ни другое.

**Скроллер — свой у каждого вида.** Политика прокрутки задана на самом
контейнере, а не в общем стиле: плиточным видам горизонтальная прокрутка
не нужна по устройству (перенос — это и есть плиточный вид), поэтому она
`Disabled`; вертикальная — `Auto`, папка, которая влезла, полосы не
показывает. `Details` — таблица, ей горизонтальная полоса нужна, когда
колонки перерастут панель, поэтому там `Auto`/`Auto`.

Автоматическая полоса — это ловушка, и панель обходит её сама
(`VirtualizingWrapPanel.ColumnWidth`). `ScrollViewer` меряет содержимое
сначала во всю ширину и только потом обнаруживает, что полоса нужна, —
второй замер приходит на ширину полосы уже. Wrap-раскладка законно
может хотеть полосу при одной из этих двух ширин и не хотеть при другой
(девять колонок влезли без полосы, восемь тех же ячеек — нет), и тогда
два ответа гоняют друг друга, пока окно открыто. Поэтому колонки считаются
по ширине, из которой полоса вычтена заранее, если содержимое её всё равно
потребует: ответ одинаков в обоих проходах, гонять нечего. Проверено
харнессом ровно на пограничном случае — 54 ячейки при ширине, где полоса
стоит колонки: 0 проходов раскладки в простое и полоса не показана; 60
ячеек там же — полоса показана, колонок восемь, тоже 0 проходов.

**Контейнеры переиспользуются** (`VirtualizationMode.Recycling` +
`generator.Recycle`). Сборка шаблона была самым дорогим, что UI-поток
делал при прокрутке: в логе реальной сессии на папке с RAW — 260–400 мс
в секунду на `layout.realise` и подвисания по 300–450 мс. После —
5–10 мс на пачку строк вместо ~22.

Побочный эффект виртуализации: рамка выделения видит только реализованные
элементы. Для `DataGrid` это было так и раньше, а дотянуться рамкой за
пределы экрана всё равно нельзя — автоскролла при протяжке нет.

### Вид, которого не видно, не строит ничего

Четыре вида смотрят в **одну** коллекцию `Entries`, и `Reset` на ней
пачкает измерение всех четырёх панелей разом. Свёрнутый `ListBox` от этого
не спасает: `Visibility="Collapsed"` проверяется у самого элемента, а
менеджер раскладки меряет грязную панель напрямую, её предок ему не указ.
Так каждая навигация реализовывала папку трижды и на следующей сносила все
три (`COUNT layout.new: 96 in 3 passes` при одном видимом виде, 2026-09-02).

Держится это двумя разными способами, потому что панелей две:

- **Свои панели** (`VirtualizingWrapPanel`, три плиточных вида) знают об
  этом сами: при `owner.IsVisible == false` `MeasureOverride` только
  перемеряет уже существующих детей и сбрасывает маркеры диапазона, а
  подписка на `IsVisibleChanged` владельца инвалидирует измерение, когда
  вид показывают. Перемерить существующих обязательно — грязный ребёнок,
  которого родитель не мерит, держит очередь раскладки грязной вечно.
- **Чужая панель** (`DataGrid`, вид «Таблица») так не умеет, поэтому
  таблице просто не дают строк: `FileListView.ApplyViewAttachment`
  отвязывает `ItemsSource`, пока таблица не на экране, и привязывает
  обратно при переключении на неё. Порядок жёсткий — сначала отвязать,
  потом привязать активному, — потому что вид, теряющий строки, сообщает
  пустое выделение, и это сообщение не должно лечь после того, как
  входящий вид выделение восстановил. `SelectedItem` при отвязке гасится
  **до** строк и двумя шагами: у таблицы привязка стоит на элементе
  (`ClearBinding`), у плиточных видов приходит из стиля `TilePanel`
  (локальный `null` перекрывает, не записывая наружу).

Цена — реализация входящего вида в момент переключения режима: одна
заминка на смену вида вместо трёх на каждую навигацию. Многовыделение при
этом не теряется: `ApplyViewAttachment` снимает его до отвязки и ставит
привязанному виду обратно (`SelectedEntry` в одиночку донёс бы одну
строку из трёх). Прокрутка таблицы переключение не переживает — вид
начинает с выделенной строки.

### Что на экране — читается первым

Значки и миниатюры идут через один шлюз на четыре одновременных загрузки
(`AsyncIcon._gate`). Обычный семафор отдаёт слоты в порядке обращения, а
обращаются в порядке создания контейнеров, не в порядке видимости:
таблица держит по странице строк над и под окном, дерево — каждый
развёрнутый узел, и все они просят значок в момент появления. С
настройкой «Быстрое чтение первых файлов при открытии папки»
(`AppSettings.VisibleFirstLoading`, зеркало `AsyncIcon.VisibleFirst`)
шлюз становится `IconLoadGate` с двумя очередями: запрос значка,
лежащего в окне своего `ScrollViewer`, обгоняет запрос значка за краем.
Где именно лежит значок, известно только после раскладки, поэтому с
настройкой запрос откладывается до `DispatcherPriority.Loaded` — одна
раскладка для всех, порядок между ними не меняется. Без настройки всё
помечено срочным, и шлюз — тот же семафор, что был.

Судить о настройке по строке `First screen painted in N ms: K icons, M
awaited - путь` (`FirstScreenWatch`): часы запускаются в навигации
(`RefreshFolderAsync`), вью после приземления и раскладки отдаёт сторожу
значки реализованных строк в окне, сторож ждёт `AsyncIcon.Painted` от
каждого без картинки. Папка, покинутая раньше, закрывается строкой
`abandoned` с числом недождавшихся; значок, уехавший из дерева
(прокрутка), из ожидания выбывает и считается отдельно. Одна строка на
вход в папку — это и есть замер «сколько ждать, пока папка выглядит
готовой», которого в журнале не было.

---

## Замеры производительности

`Wander.Core/Diagnostics/PerfLog` + `Wander.App/Diagnostics/UiStallWatch`.
Обе штуки постоянные, и обе молчат, пока всё быстро.

`PerfLog.Measure("имя")` замеряет блок, суммирует замеры в окно длиной
секунда и выписывает в сессионный лог только те категории, которые в этом
окне обошлись дороже **100 мс суммарно или 33 мс за один вызов** — то
есть шестнадцать кадров подряд занятой секунды или один вызов длиной в два
кадра. Пороги подняты по итогам первых замеров: всё, что ниже, — ровная
работа, про которую никто не хочет читать. Цена самого замера — два
таймстампа и словарь под локом, порядка сотни наносекунд на вызов; при
пятистах вызовах в секунду это десятые доли миллисекунды.

```
PERF layout.realise: 202 ms in 9 calls, worst 38,4 ms
PERF ui.stall: 1018 ms in 1 calls, worst 1017,8 ms
```

Что где замеряется:

| Имя | Что это |
|---|---|
| `layout.measure` | проход `MeasureOverride` плиточной панели; **включает** `layout.realise` |
| `layout.realise` | создание контейнеров: шаблон, привязки, измерение — всё на UI-потоке |
| `layout.arrange` | проход `ArrangeOverride` |
| `icon.decode-ui` | декод миниатюры в картинку — только для файла, которого ещё нет в `IconImageCache` |
| `list.apply` | момент, когда листинг папки заезжает в `Entries` на UI-потоке |
| `ui.stall` | сколько UI-поток не отвечал (замеряется снаружи, см. ниже) |
| `bg.*` | фоновая работа: `bg.icon-load` целиком, внутри `bg.thumb-disk` / `bg.thumb-shell` / `bg.thumb-disk-write` |

Префикс `bg.` — не UI-поток. Такие категории законно набирают больше
секунды на секунду (миниатюры тянутся в два потока) и окно не подвешивают;
они здесь, чтобы было видно, **как долго миниатюры едут** — это ощущается
как тормоза, ничего при этом не блокируя.

Отдельно от окон `PerfLog` в лог пишется **время до первого кадра** —
`MainWindow.OnFirstFrame` на `ContentRendered`, от старта процесса, то есть
вместе с загрузкой рантайма:

```
Startup: first frame 1583 ms after process start
```

Мерить по `Loaded` бессмысленно: оно срабатывает примерно на 900 мс раньше,
чем окно появляется на экране. Снятые замеры веса, скорости и памяти, а
также разбор вариантов публикации — в [PERFORMANCE.md](PERFORMANCE.md);
состав портативного exe по категориям считает `tools/size-report.ps1`.

`UiStallWatch` — фоновый поток, который раз в 200 мс просит у диспетчера
момент внимания (`DispatcherPriority.Input`) и меряет, сколько его ждали.
Изнутри UI-потока это измерить нельзя: замеряющий код стоит в той же
очереди, что и всё остальное. Ожидание дольше 120 мс идёт в `ui.stall` —
это и есть «подвисло», а остальные категории в том же окне говорят, на чём.
Тот же удар сердца закрывает окно `PerfLog`, чтобы медленный момент попал
в лог, пока он ещё интересен.

---

## Отзывчивость: приоритеты и что где приземляется

Итог разбора фризов 2026-09-01. Ключевое наблюдение: «всё асинхронно» — не
то же самое, что «не мешает». Континуации `await` и `BeginInvoke` встают в
очередь диспетчера на `Normal`, а ввод обрабатывается на `Input` — **ниже**;
поток фоновых результатов, приземляющийся на Normal, физически заслоняет
клики и клавиши, ничего при этом не «блокируя». Отсюда правила:

- **Результат фоновой работы приземляется ниже ввода, и в два яруса.**
  Листинг папки (`RefreshFolderAsync` делает `Dispatcher.Yield(Background)`
  перед очисткой и перед `PublishRows`) ставится на `Background`, миниатюры
  (`AsyncIcon`, размеры Medium/Large) — на `ContextIdle`, ярусом ниже: что
  бы ни делал пользователь — оно раньше, а строки папки — раньше картинок
  к ним. Один ярус на всё не работает: очередь FIFO, и папка, открытая из
  папки с фотографиями, показывала старые строки, пока не приземлится
  каждая из сотен их миниатюр. Устаревшее приземление отбрасывают токен и
  epoch-проверка.
- **Исключение — лёгкие иконки** (Small/Normal: дерево, закладки, столбец
  таблицы). Они копеечные, их мало, и панель без иконок читается как
  сломанная — доставка на `Normal`, кешированные декодируются синхронно
  (см. `AsyncIcon.IsLightweight`).
- **Синхронный путь навигации не трогает диск.** `NavigateTo` не проверяет
  существование пути (пробник на спящем диске — секунды на каждый переход);
  ошибку скажет листинг. Набранный руками путь (`NavigationSource.Address`)
  проверяется в фоне, с гардом «пользователь уже ушёл». Ретаргет
  `IDirectoryWatcher` — на пуле, последний вызов выигрывает по номеру
  поколения. `state.json` пишется по дебаунсу (`_stateSaveTimer`, 500 мс)
  с флашем из `MainWindow.OnClosing`.
- **Очистка при входе в папку — ниже ввода и не всегда.** Строки
  покидаемой папки убираются (решение 2026-09-01: контекст переключается
  сразу), но не внутри клика: демонтаж их контейнеров — 50–100 мс на папке
  с миниатюрами, единственная дорогая UI-точка навигации — ставится на
  `Background`, после того как адресная строка и панели нарисовали переход.
  Если к этому моменту листинг уже пришёл (обычный локальный случай),
  очистка пропускается и новые строки встают одним свопом; медленная папка
  чистится сразу и получает спиннер (150 мс). Порядок держится
  приоритетом: приземление стоит в той же очереди `Background`, позади
  очистки (`RefreshFolderAsync`).
- **Остальной диск — на пуле.** `F5` и тумблеры Hidden/System перечитывают
  уровни дерева через `TreeNodeViewModel.RefreshChildrenAsync` (сверка на
  диспетчере, `Enumerate` — нет), чистка результатов поиска после операции
  делает свои `stat`'ы там же (`SearchResultsController.PruneMissingAsync`),
  проверка последней папки при старте (`MainViewModel.OpenStartFolderAsync`),
  открытие файла для панели просмотра и подсчёт размера кэша миниатюр в
  настройках тоже не ждут диск на UI-потоке. Что осталось синхронным —
  `Enumerate` уровня при первом раскрытии ветки (см. TECHDEBT).
- **Клавиатурная навигация из дерева коалесируется**
  (`FolderTreesView.NavigateFromTree` + `TreeNavBurstMs`/`TreeNavSettleMs`):
  одиночное нажатие — сразу, серия — по остановке курсора. Тот же метод
  гасит навигацию в уже текущий путь — это эхо `ExpandTo`, которое иначе
  затирает `ArrivalIntent` и съедает выделение «папки, из которой вышли».
- **Шевроны дерева — оптимистичные.** Уровень грузится одним `Enumerate`,
  без `HasSubdirectories` на каждого ребёнка; `ProbeForChevrons`
  (`TreeNodeViewModel`) снимает шеврон с листьев фоном. Сам `Enumerate`
  уровня пока синхронный — см. TECHDEBT.

Отдельно — иконки (`SystemIconProvider` + `AsyncIcon`):

- `SHGetFileInfo` сериализован (`_shellIconLock`): под конкурентными
  вызовами с пула он перемежающе возвращал пусто для папок, чья иконка
  идёт через handler (desktop.ini, спецпапки), и жертва менялась от запуска
  к запуску.
- Негативный кеш `_missing` — только для миниатюрных размеров, где «нет
  превью» — стабильный ответ. Для Small/Normal иконка есть у любого файла,
  null там — сбой, и его запоминание оставляло строку пустой всю сессию.
- Уход из дерева — конец запроса: `Unloaded` в `AsyncIcon` поднимает
  поколение, и загрузка контейнера, снесённого при смене папки, отступает
  у шлюза (`_gate`, четыре слота) и у декодера, а не занимает их для тайла,
  которого нет. Без этого сотни shell-вызовов покинутой папки шли впереди
  миниатюр открытой — так «открывающаяся папка подтормаживала». Контейнер,
  вернувшийся в дерево на тот же файл (recycling), переспрашивает по
  `Loaded`.
- `AsyncIcon` переспрашивает несостоявшуюся иконку один раз через секунду:
  список лечится перереализацией контейнеров при скролле, а панели строят
  строки один раз за сессию. Провал после ретрая — строка `[icon-diag]` в
  логе, медленный вызов оболочки (> 1 с) — строка `slow shell load` с путём.

### Таймеры: троттл решения, а не место решения

Правило, зафиксированное разбором O6 (PLAN.md, категория 5). Таймер в
проекте существует ровно для одного — **разредить поток событий**. Из этого
три требования, и они обязательны для каждого нового таймера:

- **Решение отделимо от таймера.** То, что тик делает, — метод, который
  можно позвать напрямую: `Enter` в поиске зовёт `RunNow()` мимо паузы,
  `MainWindow.OnClosing` зовёт `FlushState()` мимо дебаунса, `Finish`
  флашит результаты до того, как напишет статус. Если решение живёт внутри
  обработчика `Tick`, его нельзя ни позвать раньше, ни проверить.
- **Тик идемпотентен.** Лишний тик — no-op, а не вторая порция работы:
  `SearchResultsController.Flush` при чистом `_dirty` выходит сразу,
  `FolderSession.DecideWatchTick` без накопленных изменений отвечает
  `Idle`. Идемпотентность — то, что позволяет таймеру быть повторяющимся,
  а не перезапускаемым.
- **Таймер останавливает себя, когда делать нечего, и не теряет
  накопленное под занятостью.** Сторож папки гасит себя на первом холостом
  тике (`WatchOutcome.Idle`) и **откладывает**, а не выбрасывает изменения,
  пока правится имя или идёт своя файловая операция (`WatchOutcome.Hold`).
  Дебаунсы (`_stateSaveTimer`, `_debounce` поиска, `_treeNavDebounce`)
  гасят себя первой строкой тика — они одноразовые по смыслу.

Прецеденты, на которые опираться: **сторож папки** (`OnWatchTick` →
`FolderSession.DecideWatchTick`, 500 мс) — решение целиком в Core и под
тестами; **флаш результатов поиска** (`SearchResultsController`, 200 мс) —
решение в App, но идемпотентное и вызываемое напрямую.

Инвентарь на 2026-09-01: сторож папки (500 мс), флаш результатов поиска
(200 мс), дебаунс автозапуска поиска (400 мс), дебаунс `state.json`
(500 мс), коалесценция клавиатуры дерева (90 мс). Отдельно от правила стоят
**часы воспроизведения** — кадры GIF (`GifImage`) и позиция видео
(`PreviewPane._videoTimer`): они не разреживают события, а тикают, пока
идёт показ. Требование «останавливаться, когда нечего делать» на них
распространяется (`GifImage` гасится на `Unloaded`; по видео — запись в
[TECHDEBT.md](TECHDEBT.md)).

«Дебаунса панели просмотра» не существует и не подразумевается: устаревший
запрос там гасится отменой `CancellationTokenSource`, то есть защитой
поколением, а не таймером.

**Абстракции часов/тика в проекте нет и не заводится.** Все таймеры живут в
App-слое, куда тесты не достают, а абстракция, которую не дёргает ни один
тест, мёртвая по критерию O2. Поедут контроллеры поиска в Core — заводить
её тогда, вместе с тестами.

---

## Preview pane

`PreviewController` (App) — асинхронный конвейер с отменой и спиннером.
`PreviewKind`: `None`, `Image`, `Gif`, `Text`, `Code`, `Web`, `Document`,
`Video`, `Audio`, `Model`, `Folder`, `Unsupported`.

| Kind | Чем рендерится |
|---|---|
| `Image` | `BitmapImage`, `StretchDirection=DownOnly`; RAW — через встроенный превью, см. ниже |
| `Gif` | `Controls/GifImage` — анимация, `BitmapImage` её не умеет |
| `Video` | WPF `MediaElement` |
| `Audio` | тот же `MediaElement` и тот же транспорт, что у `Video`, плюс карточка трека из тегов |
| `Text` | обычный `TextBox` |
| `Code` | AvalonEdit с подсветкой |
| `Document` | `RichTextBox` — RTF, WPF читает его сам |
| `Web` | WebView2 — PDF / HTML / MHTML / отрендеренный Markdown / FB2 |
| `Model` | WPF `Viewport3D` — STL / OBJ / glTF / GLB |
| `Folder` | перепись папки, плюс блок тома на корне диска |

`Audio` и `Video` делят одну `MediaUri` и один транспорт (кнопка, часы,
перемотка) намеренно: музыка — это видео, которому нечего рисовать, и второй
транспорт означал бы вторую копию того же автомата состояний. А вот
**проигрыватель у них разный, и это обязательно**: `MediaElement` работает
только пока его рисуют. У трека рисовать нечего, элемент измеряется в ноль —
и такой элемент открывает файл, сообщает длительность, принимает `Play()` и
навсегда оставляет позицию на нуле. Замерено: 200×120 играет, 1×1 молчит.
Поэтому звук ведёт `MediaPlayer` — тот же движок без элемента, — а транспорт
обращается к тому из двух, который сейчас держит файл. Выбор делается по
`Kind`, поэтому `PreviewController` выставляет `Kind` **до** `MediaUri`:
наоборот — и трек достаётся видеоэлементу, который его не сыграет.

Фон области контента — `MainViewModel.ContentPalette`, **а не**
`Settings.GalleryPalette` напрямую. Разница смысловая: затемнённый фон это
настройка галереи, а таблица, плитки и значки рисуются на обычном фоне окна
при любом её значении. Панель стоит рядом с областью файлов и обязана
повторять её, поэтому спрашивает «какого цвета область сейчас», а не «что
выбрано в настройках» — иначе рядом с белым списком оказывается чёрная
панель. Сама галерея по-прежнему читает настройку: она видна только в том
режиме, где эти два ответа совпадают.

Подписи на этом фоне берут `Foreground` / `Dim` из палитры, и оба тона
**считаются от фона по контрасту**, а не выбираются из готовой пары.
Фиксированная пара работала на краях диапазона и разваливалась в середине:
на среднем сером — том, что стоит по умолчанию, — «приглушённый» #AAA
измерялся в 2.2:1, то есть не приглушённый, а отсутствующий.

Один подводный камень стоит отдельного слова, потому что он тихий. Стиль,
который красит подпись из палитры, обязан писать путь как
`DataContext.ContentPalette.…`: `RelativeSource` возвращает **элемент**, и
без `DataContext.` путь ищется на самом `UserControl`, где такого свойства
нет. Биндинг тогда не падает и ничего не сообщает — подпись просто остаётся
унаследованно чёрной, что на тёмном фоне значит «текста нет». Ловится
прослушиванием `PresentationTraceSources.DataBindingSource` в харнессе.

Текст, код и документы остаются на своём светлом фоне — это страницы, и
страница на тёмном фоне читается как светлый прямоугольник с рамкой, а не
как тёмная страница.

Строка оценки лежит **вне** блока спутников. Внутри она показывалась только
у снимка, у которого сайдкар уже есть, — то есть ровно не у того, на котором
ставят первую оценку; `OfferRating` предлагает её и файлу без спутников, а
блок это предложение прятал.

**Куда файл пойдёт, решает `PreviewRouter`** (Core) — таблица «расширение →
`PreviewRoute`», без единого чтения с диска. Она отделена от `PreviewKind`
намеренно: `Kind` говорит, какой контрол рисует результат, а `Route` — каким
загрузчиком он получен, и это разные вопросы. Markdown, FB2 и PDF приходят в
один и тот же WebView2 тремя разными путями, а Unity-ассет читается как код
или отвергается в зависимости от того, чем окажутся его первые байты.

Содержание таблицы — **порядок правил**, а не списки: расширения состоят в
двух списках сразу. `.webp` — и картинка (`ImageFormats`, по которому галерея
считает папку папкой снимков), и многокадровый контейнер, который надо
собирать по кадрам; `.mtl` лежит рядом с моделями и является текстом; `.svg`
считается исходником, потому что растра в нём нет и показывать надо разметку.
Побеждает первое подошедшее правило, и перестановка двух строк меняет то, что
видит пользователь, — поэтому таблица лежит в Core под тестами
(`PreviewRouterTests`), а не разветвлённым `if` внутри контроллера.

**Разбор форматов живёт в Core, отрисовка — в App.** `Wander.Core/Preview/`
не знает ни про WPF, ни про Windows и покрыт тестами:

- `AudioTags` — теги MP3 и FLAC: ID3v2.2/2.3/2.4, ID3v1 и
  Vorbis-комментарии, плюс обложка и длительность (у FLAC точная из
  `STREAMINFO`, у MP3 — по счётчику кадров `Xing` / `Info` / `VBRI`, иначе
  VBR-рип ошибается на минуты). Кодировка однобайтовых полей угадывается
  **один раз по всем полям тега сразу** через `EncodingProbe`: спецификация
  ID3 называет кодировку 0 латиницей, а половина файлов держит в ней кодовую
  страницу машины, и одного поля в полтора десятка символов на угадывание не
  хватает.
- `MeshFile` + `StlReader` / `ObjReader` / `GltfReader` — геометрия моделей
  как плоский массив координат плюс список `MeshPart`: у каждой части свои
  индексы и свой цвет. Не `MeshGeometry3D`: чем это рисуют — не дело Core.
  Части, а не один меш, потому что так устроены форматы — OBJ делится по
  `usemtl`, glTF даёт материал каждому примитиву; координаты при этом
  общие, и копировать их на каждый материал значило бы умножить память
  крупной модели на число её материалов. Из материала берётся только цвет
  (`Kd`, `baseColorFactor`) — карты требуют разворота вершин по UV, а это
  отдельный скоуп (BACKLOG). Нормали не читаются вовсе — WPF считает
  пофасеточные сам, и это ровно та заливка, которая нужна превью
  нетекстурированного тела.

- `Fb2Document` — FictionBook в HTML-фрагмент плюс отдельный потоковый
  `ReadCover` для миниатюр. Пространства имён сверяются по локальному имени:
  FB2 в природе делают полтора десятка конвертеров, часть из которых
  ошибается в URI или не пишет его вовсе, и отказывать такому файлу — это
  педантизм за счёт читателя. Бюджет в 400 000 символов проверяется **по
  ходу обхода**, а не между детьми `<body>`: книга сплошь и рядом состоит
  ровно из одной `<section>`, и предел «между секциями» на ней не сработал
  бы ни разу. При обрыве закрываются уже открытые теги — наружу уходит
  целый фрагмент, а не обрубок посреди тега.
- `BookCover` — обложка из `.fb2` и `.epub` для плиток. В EPUB путь до
  обложки идёт `container.xml` → OPF → манифест, и каждый шаг может
  отсутствовать, поэтому каждый падает в следующую догадку; разметок
  «вот обложка» две (EPUB 3 `properties="cover-image"` и EPUB 2
  `<meta name="cover">`), плюс поиск по имени файла для тех, где нет ни
  одной. Форматы, до обложки которых не дотянуться (DjVu, CHM, старый
  `.doc`), просто не перечислены — `Supports` отвечает `false`, и
  вызывающий остаётся на системном значке.
- **Markdown** идёт через свой `MarkdownPipeline`, а не через голый
  `Markdown.ToHtml`. Markdig по умолчанию говорит на чистом CommonMark, а в
  CommonMark нет таблиц — блок `| … | … |` выходил одним слипшимся абзацем
  из палочек и дефисов, то есть ровно тем, ради чего таблицы в README и
  пишут. Расширения перечислены поимённо, а не включены пачкой
  `UseAdvancedExtensions()`: та пачка заодно превращает ссылки на YouTube в
  `iframe` (который панель всё равно не пустит в сеть и покажет пустой
  рамкой) и читает `{#id .class}` из текста как разметку.
- `EncodingProbe` — в какой кодировке файл и как его раскодировать. По
  порядку: BOM, строгая проверка на UTF-8 (невалидная последовательность —
  это и есть признак «не UTF-8»), затем счёт между Windows-1251 и DOS-866.
  Счёт построен на регистре: кириллические строчные весят втрое против
  прописных, потому что настоящий текст на девять частей строчный — и
  именно те байты, что в 1251 строчные буквы, в 866 псевдографика. У
  кириллических кандидатов есть порог в 8 букв: `ä ö ü` — вполне себе
  кириллица в 1251, и без порога немецкий файл с тремя умляутами
  «определялся» бы как русский. Порог при этом не ломает нужный случай —
  один русский комментарий в ASCII-скрипте: этот комментарий и есть
  единственное, что неверная догадка может испортить. Таблицы двух
  кодировок выписаны здесь же: .NET из коробки знает только Unicode, ASCII
  и Latin-1, остальное живёт в пакете `System.Text.Encoding.CodePages`, а
  256 символов данных дешевле зависимости в `Wander.Core`, где их нет
  вообще.
- `TextProbe` — «это вообще текст?» по первым 8 КБ. Нужен там, где
  расширение не отвечает: Unity-ассет (`.asset`, `.prefab`, `.unity`,
  `.mat`) — YAML в проекте с форсированной текстовой сериализацией и
  непрозрачный блоб в любом другом. BOM решает вопрос сразу (UTF-16 иначе
  выглядит как сплошные NUL), нулевой байт — приговор, а доля управляющих
  символов проверяется пропорцией: одиночный escape в логе не делает файл
  бинарным.

**Отрисовка разложена по `Wander.App/Preview/`**, а не живёт в контроллере:
`ImageDecoder` (правила декодера — кэш URI, декод обложки сразу в нужный
размер, встроенный превью RAW, поворот по EXIF), `ModelBuilder` + `ModelScene`
(геометрия Core → `MeshGeometry3D`, центр и радиус сцены), `PreviewText`
(чтение с бюджетом и определением кодировки, обрезка с подписью, Markdown,
общая HTML-обёртка) и `SummaryText` (подпись под содержимым). Контроллеру
остаётся конвейер: что грузим, в каком порядке, кто отменяет и что показать,
пока грузится.

**Ярлык прозрачен для панели.** `.lnk` в `UpdatePreviewAsync` резолвится
через `IShortcutService`, и дальше рисуется цель: папка — переписью, файл —
своим обычным путём в `LoadFileAsync`. Отсюда же `LinkTarget` в футере и
кнопка «Перейти к оригиналу», которая зовёт `MainViewModel.RevealPath` —
навигация принадлежит вьюмодели, панель знает только путь. `RevealPath`
кладёт путь в `_revealPathAfterListing` и выделяет его, когда придёт
листинг нужной папки (`ApplyPendingReveal`); если пользователь уже стоит в
этой папке, навигации не будет и выделение применяется сразу.

**Диск описывает `IVolumeInfoProvider`** (Core) / `WindowsVolumeInfo`
(Platform, поверх `DriveInfo`). Блок показывается только на корне тома:
ёмкость над переписью обычной папки отвечала бы про диск, показывая числа про
папку. Каждое свойство `DriveInfo` бросает на неготовом устройстве (пустой
привод, спящая сетевая шара), поэтому чтение обёрнуто целиком, а «не
готово» возвращается описанным томом с нулевой ёмкостью — не исключением
и не `null`.

**Подсветка кода.** AvalonEdit закрывает мейнстрим и отвечает по расширению,
включая `.diff` / `.patch` (определение `Patch`). Чего в поставке нет —
лежит в `src/Wander.App/Highlighting/*.xshd` встроенными ресурсами и
регистрируется один раз из `HighlightingCatalog.EnsureRegistered()`:
`Batch` (`.bat`, `.cmd`), `ShaderLab` (`.shader`, `.cginc`, `.hlsl`,
`.compute`) и `YAML` — последний заодно закрывает сериализованные ассеты
Unity. Битый `.xshd` пропускается, а не роняет панель: сломанная цветовая
схема не должна быть причиной, по которой файл не открывается.

Выбор папки в дереве или в закладках доходит до панели в обход
`SelectedEntry`: та двусторонне связана с `SelectedItem` списка, и
присвоение элемента, которого в списке нет, список тут же откатывает
обратно в `null` — а папки в собственном листинге по определению нет.
Поэтому `SelectExternalPath` ставит `SelectedEntries` (для операций) и
дёргает `Preview.SetPrimary` напрямую (для содержимого панели), а сам
выбор применяется после прихода листинга, иначе его затрёт перестроение
списка.

Footer-summary считает контекст: пустой выбор → текущая папка (рекурсивный
count+size, async), файл → name/size/modified + EXIF, папка → count+size,
мульти → агрегат. EXIF включая RAW (CR2/CR3/NEF/ARW/DNG) через
`MetadataExtractor`.

Под summary — блок спутников (см. «Companion-файлы» выше): список
интегрированных файлов, GUID из `.meta` с кнопкой копирования, звёзды
`Rank` из `.pp3` (кликабельные). Читается своим конвейером с отменой,
как и остальное в панели.

**WebView2 изолирован:** `NavigationStarting` пропускает только
`file:` / `about:` / `data:`, попапы режутся. Скрипты внутри локального `.html`
при этом исполняются — `IsScriptEnabled` глобальный, а встроенный
PDF-viewer без JS не работает (TECHDEBT).

**RAW не декодируется.** Отдавать `.CR3` в WIC значит декодировать сенсорные
данные: замеряно **~1150 мс** на 33 МБ файле (и ни `DecodePixelWidth`, ни
`BitmapDecoder.Thumbnail` этого не сокращают — оба в пределах 20 % от полного
декода). Вместо этого `RawPreviewExtractor` (Core) достаёт из контейнера
JPEG, который камера туда уже положила: **8–13 мс** на тот же файл, включая
декод. Два формата контейнера — ISO-BMFF (`uuid`-бокс Canon с `PRVW`) и TIFF
(IFD с указателем на JPEG: CR2, NEF, ARW, DNG). Ничего не ломается, если
формат не разобрался: `null` означает «иди обычным путём», то есть в худшем
случае получаем прежнее поведение. Кандидаты перебираются от большего к
меньшему с проверкой маркера кадра — в DNG и NEF самый большой JPEG-поток
внутри это сами raw-данные (lossless JPEG, SOF3), показать его нельзя.

Цена: в панели теперь превью-разрешение (у Canon 1620×1080), а не полный
кадр, — на лупу это влияет, на «посмотреть, что за снимок» нет. Размеры в
футере по-прежнему настоящие: они из EXIF, а не из картинки.

Вторая цена, обнаруженная позже: этот JPEG лежит в контейнере
**неповёрнутым**, своего EXIF не несёт, а поворот камера пишет в IFD0
контейнера — и камера, настроенная «вращать только на компьютере», кладёт
туда 6 или 8. Системный декодер RAW поворот тоже не применяет, так что
доворачивать приходится в обеих ветках. `ImageMetadata` возит
`Orientation`, `PreviewController.ApplyOrientation` доворачивает
`TransformedBitmap`-ом. Только для RAW: обычный JPEG или PNG показывается
ровно так, как выглядит везде ещё, и лишний поворот сделал бы хуже.

**`IgnoreImageCache` — только для файлов.** Флаг нужен, чтобы
перезаписанный файл не показался старым содержимым: кэш WPF ключуется по
URI и подмены байтов не замечает. У картинки, собранной из
`MemoryStream`, никакого URI нет, и `BitmapImage.FinalizeCreation` на
.NET 10 падает на попытке вычистить из кэша `null`. Стоило это дорого и
незаметно: декод встроенного превью бросал исключение, `Decode` гасил его
и возвращал `null`, вызывающий читал это как «встроенного превью нет» и
уходил на полное декодирование — то есть весь быстрый путь для RAW был
мёртв, а выглядело это просто как «превью думает секунду».

**WebView2 не ходит в сеть.** `NavigationStarting` и раньше пропускал только
`file:` / `about:` / `data:`, но это про навигации; подресурсы шли мимо, и
локальная `.html` могла позвать домой трекинг-пикселем или `fetch()`.
`WebResourceRequested` теперь режет всё, что уходит в сеть (`http`, `https`,
`ws`, `wss`, `ftp`). Запрет **deny-list, а не allow-list** намеренно: свою
обвязку рендерер (особенно встроенный PDF-viewer) раздаёт по внутренним
схемам, перечислять которые — не наше дело. Побочный эффект, о котором надо
знать: в отрендеренном Markdown внешние картинки (те же бейджи в README)
теперь не грузятся.

**Иконки:** `SystemIconProvider` — системные иконки + `.lnk` overlay-стрелка
(включая jumbo-композит), thumbnails через `IShellItemImageFactory`. Кэш
мелких иконок ключуется по расширению (и поэтому ограничен сам собой),
тумбнэйлы — по пути, с FIFO-пределом в 512 записей.

Две ступени с миниатюрами (`Medium`, `Large`) сначала спрашивают
`BookCover`: у `.fb2` и `.epub` картинка рисуется своя — обложка,
вписанная в квадрат «страницей» (белая подложка, рамка, тень), — и поэтому
получает ключ кэша по пути, а не по расширению. Мелкие ступени обложек не
получают: в 16 пикселях от неё остаётся мазок.

У PDF обложки внутри нет, поэтому её место занимает первая страница:
`PdfPageImage` рисует её через `Windows.Data.Pdf` — часть самой Windows, не
пакет. Единственная цена — минимальная версия системы у двух
windows-проектов (`net10.0-windows10.0.19041.0` вместо `net10.0-windows`),
без которой WinRT-проекции недоступны. Делаем это **всегда**, а не только
когда шелл промолчал: миниатюра у PDF появляется, лишь если что-то
установленное зарегистрировало thumbnail provider, а половина читалок
забирает ассоциацию и не регистрирует ничего. Вызов синхронный
(`.AsTask().GetAwaiter().GetResult()`) и это безопасно: все вызывающие уже
на фоновом потоке миниатюр, WinRT-операция завершается в пуле, контекст
синхронизации не захватывается. Синхронный `IconConverter` сюда не
доходит — он просит только `IconSize.Small`, а обложки живут на `Medium` и
`Large`.

Дальше `LinkThumbnailTarget` подменяет `.lnk` на его цель, когда та
существует и у неё есть превью. Стрелку в этом случае накладываем сами
(`DrawLinkOverlay`): шелл запекает её в **значок**, но не в миниатюру,
которую у него попросили по другому пути. Известное следствие — в
[TECHDEBT.md](TECHDEBT.md): ключ кэша считается по самому `.lnk`, так что
подменённый оригинал не сбрасывает уже сохранённую миниатюру. `AsyncIcon` пропускает
через семафор четыре загрузки разом и **перепроверяет актуальность после
очереди**: при быстром скролле контейнер уже переиспользован под другой файл,
и делать ради него вызов шелла незачем.

**RAW идёт мимо шелла.** До обращения к `IShellItemImageFactory`
`SystemIconProvider` пробует `RawThumbnail`: встроенный в контейнер JPEG
(`RawPreviewExtractor`, тот же, что у панели просмотра) плюс декод через
WinRT `Windows.Graphics.Imaging` с масштабированием на этапе разжатия. На
холодном кэше это 3 мс на файл против 75 — замеры и разбор в
[PERFORMANCE.md](PERFORMANCE.md). Две детали обязательны: ориентация берётся
из **контейнера**, а не из вынутого JPEG (своего EXIF у него нет, и без
этого все вертикальные кадры лежат на боку), и декод именно WinRT, а не
`System.Drawing` — GDI+ сериализуется на внутренней блокировке и в восемь
потоков работает ровно так же, как в один. Семафор `AsyncIcon` поднят с двух
до четырёх по той же причине: двойка была подобрана под медленный вызов
шелла и после ускорения сама стала ограничителем.

---

### Smoke-запуск

`Wander.exe --smoke` — режим «поднимись, нарисуй кадр, уйди и скажи кодом
возврата, получилось ли». Всё, ради чего smoke-прогон существует (разбор
XAML, поиск ресурсов, регистрация сервисов, первая отрисовка окна и первый
листинг), происходит одинаково независимо от того, смотрит на это кто-нибудь
или нет, — поэтому окно уезжает за пределы экрана (`Left = -32000`), не
забирает фокус (`ShowActivated = false`) и не появляется в панели задач.

Три места, где режим виден в коде:

- `App.IsSmokeRun` — разбор аргумента, и он же выключает диалог
  `CrashReporter`: некому нажать кнопку, а проверка, повисшая на модальном
  окне, хуже упавшей. Вместо диалога — `Shutdown(1)`.
- `MainWindow` — координаты ставятся **в конструкторе**: `ShowActivated`
  учитывается только до показа окна, а показывает его `StartupUri`. Там же
  геометрия окна не читается из `state.json` и не пишется в него, иначе
  реальная сессия уехала бы в (-32000, -32000).
- `StartSmokeCountdown` — таймер на две секунды и `Shutdown(0)`. Секунды не
  на запуск (окно к этому моменту уже загружено), а на то, что запуск
  раздаёт дальше: первый листинг, первые значки, наблюдатели за папкой.

`tools\check.bat run` вызывает exe напрямую (не через `start`), чтобы
дождаться его и прочитать код. Две ловушки cmd, на которые он уже
наступал: `if errorlevel 1` сравнивает со знаком и не видит .NET-падения
(0xE0434352 — отрицательное число в int32), а `exit /b` изнутри скобочного
блока не доносит код до вызывающего. Поэтому — `neq 0` и выход за пределами
блока.

## Состояние и логи

Всё на диске лежит в `%LOCALAPPDATA%\Wander\`.

**`state.json`** (`IAppStateStore` → `JsonAppStateStore`) — record `AppState`:

- `Session` — `LastPath` (`NavigationStop?`), `ExpandedPaths`, `ViewMode`,
  `IsPreviewVisible`, `PreviewWidth`, `IsBookmarksExpanded`.

  `ExpandedPaths` — то, что было **видно** раскрытым:
  `CollectExpandedRecursive` останавливается на свёрнутом узле и внутрь не
  идёт. Сворачивание не гасит флаги внутри ветки (повторное раскрытие в той
  же сессии должно показать то же, что и раньше), поэтому запись потомков
  означала бы, что восстановление раскроет путь до каждого из них — а
  вместе с ними и только что свёрнутого родителя.
- `Favorites` — закладки, в порядке, который задал пользователь
  (`MoveBookmark`). Стандартные папки сюда не попадают: они включаются
  галочками в `Settings` и всегда идут выше, отделённые чертой
  (`TreeNodeViewModel.StartsUserSection`). Путь, которого больше нет на
  диске, из списка **не** выбрасывается — строка остаётся с флагом
  `IsMissing`, см. «Пропавшая папка» выше.
- `Window` — `WindowGeometry` (Left/Top/Width/Height/Maximized).

  Обратно геометрия кладётся не как её сохранили: `WindowPlacement`
  (`Wander.Core/Layout/`) отбрасывает размер меньше 320×240 (такой — не
  выбор, а обрезок) и прижимает позицию к виртуальному экрану так, чтобы
  осталась полоса заголовка, за которую можно схватиться. Оба случая
  молчаливые и проявляются только на чужой конфигурации мониторов, так что
  арифметика лежит там, где её достаёт тест.
- `Settings` — `AppSettings`: `RestoreLastFolder`, `ShowHidden`, `ShowSystem`,
  `ConfirmRecycle`, `SortKey` / `SortAscending` / `GroupFoldersFirst`,
  геометрия LargeIcons-ячеек, чекбоксы закладок, `ShowDebugMenu`,
  настройки контекстного меню (`ShellExtensionsEnabled`,
  `BlockedShellExtensions`, `KnownShellExtensions`,
  `HiddenContextMenuItems`). Последний из списков подрезается при
  сохранении (`ContextMenuSettings.TrimKnownExtensions`): часть обработчиков
  пишет в подпись имя ветки или файла, иначе он рос бы бесконечно.

Миграционного слоя **нет**: `JsonAppStateStore.Load` ловит исключение и
возвращает `new AppState()`. До 1.0 это осознанный выбор — схема ещё ломается
(см. TECHDEBT про schema break и `WindowGeometry`).

**`logs\session-yyyymmdd-hhmmss.log`** — файл на каждую сессию, `FileLogger`.
Логируются открытие папки, все файловые операции, конфликты, ошибки. В тестах
подменяется на `NullLogger`, чтобы не сорить в реальный AppData. Ротации нет —
каталог растёт бесконечно (TECHDEBT).

**`thumbs\*.png`** — кэш миниатюр (`ThumbnailDiskCache`, слой
Platform.Windows). Подрезка и очистка всегда уходят в фон: оба вызывающих
(старт приложения и диалог настроек) сидят на UI-потоке. Один PNG на миниатюру, имя — SHA-256 от «путь + время
правки + размер файла», поэтому изменившийся файл автоматически получает
другое имя, а старая запись осиротеет и уедет при подрезке: инвалидации
как отдельного механизма нет и не нужно. Запись — во временный файл рядом
с последующим `File.Move(overwrite)`, чтобы два окна Wander не оставили
половину PNG. Все ошибки диска глотаются: кэш, который не читается, — это
потеря скорости, а не причина не показать иконку. Подрезка по времени
последнего обращения (чтение «трогает» файл), раз в 64 записи и сразу при
уменьшении лимита; чистится до 80 % бюджета, чтобы не срабатывать на каждой
следующей миниатюре. Лимиты приходят из настроек через
`IIconProvider.ConfigureCache(ThumbnailCacheOptions)` — провайдер про
`AppSettings` не знает.

**`crashes\*.zip`** — `CrashReporter`. `App.HookCrashLogging` вешает три
обработчика: `DispatcherUnhandledException` (лог + предложить репорт,
`Handled = true` — UI-поток не роняем), `AppDomain.UnhandledException`
(фатальный, флашим что успели), `TaskScheduler.UnobservedTaskException`.
Репорт — пре-заполненный GitHub issue + локальный zip-бандл. **Ничего не
уходит без действия пользователя** — телеметрии нет.

---

## Строки интерфейса

Весь пользовательский текст приложения — в
`src/Wander.App/Resources/Strings.resx` (встроенный ресурс). Типизированный
доступ — `Resources/Strings.cs`: одна строка на ключ поверх
`ResourceManager`, XAML берёт их через `{x:Static res:Strings.Key}`.
Ненайденный ключ возвращает сам себя — пропажу видно в интерфейсе, а не в
исключении посреди отрисовки.

Класс написан руками, а не сгенерирован `MSBuild:Compile`: markup-компилятор
WPF собирает XAML во временном проекте (`*_wpftmp.csproj`), куда
сгенерированный в `obj/` designer-файл не попадает, и `{x:Static}` на него
не разрешается.

Второй язык — это файл `Strings.<culture>.resx` рядом, без правок в коде;
`ResourceManager` выберет его по `CultureInfo.CurrentUICulture`. Сейчас язык
один — русский, переключателя нет намеренно (см. BACKLOG).

**Граница слоёв.** У `Wander.Core` ссылки на ресурсы App нет и быть не
должно, но пару строк Core всё же отдаёт пользователю: подписи контекстного
меню (`ContextMenuCatalog`) и объяснение, почему нельзя перетащить
(`PathSafety.FormatReason`). Они идут через `ITextSource` —
интерфейс в `Wander.Core.Localization`, реализация `AppTextSource` в App,
регистрация в `App.OnStartup`. Core хранит **ключи**, текст остаётся в одном
resx на всё приложение.

Если источник не зарегистрирован, `Text.Get` возвращает сам ключ. Это режим
тестов: `ContextMenuCatalogTests` проверяют, что у каждого пункта ключ есть
и ключи не совпадают, и им не нужно поднимать локализацию.
`PathSafety.FormatReason` принимает `ITextSource?` последним параметром —
тесты передают свои шаблоны явно, а не пишут в глобальный `ServiceLocator`,
который параллельные тестовые классы делят между собой.

---

## Тесты

xUnit, проект `tests/Wander.Core.Tests`. **Покрывается только `Wander.Core`** —
UI и платформенный слой проверяются smoke-запуском (`.\tools\check.bat run`,
см. «Smoke-запуск» ниже).
Это прямое следствие слоёного деления: логика, которую стоит тестировать, по
определению не должна сидеть в WPF-коде. Если тест написать не получается —
это сигнал, что логика не в том слое.

### Фейки

Живут в `tests/Wander.Core.Tests/Fakes/`, реализуют интерфейсы Core в памяти:

| Фейк | Что даёт |
|---|---|
| `FakeFileSystem` | `IFileSystem` целиком в памяти: `Directories` (`HashSet`), `Files` (`Dictionary<string, byte[]>`), плюс `CallLog` со списком вызовов. `RenameFailures` заставляет отдельный путь падать — для проверки откатов |
| `FakeConflictResolver` | Сценарий разрешения конфликтов: `batchOverride` на весь батч и `perItem`-очередь на отдельные. Пишет `StartBatchCalls` / `ResolveCalls` |
| `FakeRecycleBin` | Корзина поверх `FakeFileSystem`, поддерживает `Send` / `Restore` и тоже ведёт `CallLog` |

`CallLog` — основной инструмент проверки: он позволяет утверждать не только
«результат такой», но и «сходили ровно туда, куда надо, и ровно столько раз».

### Правила

- **Локатор — не канал доставки фейков.** Фейк передаётся конструктором
  руками; глобальный локатор тест не наполняет и косвенно из него не
  читает. Причина в параллельности: xUnit гонит тестовые классы
  одновременно, локатор один на процесс, и класс, который его правит,
  гоняется со **всеми** соседями сразу — а не только с теми, кто читает
  тот же тип. Практически: регистрирует и зовёт `Reset()` (в
  конструкторе и в `Dispose`) единственный `ServiceLocatorTests`, и только
  `IFileSystem` — тип, которого не читает ни один другой тест. Новому
  тесту, которому «нужен сервис из локатора», нужен не локатор, а параметр
  конструктора. Единственное намеренное исключение — `ITextSource`: его не
  регистрирует никто, и Core на пустом локаторе отдаёт ключ вместо надписи
  (`TextFallbackTests`); сам локатор при этом под замком — см.
  «Композиция → Изменяемая статика».
- **Пути.** Фейки сравнивают пути case-insensitive (как NTFS). Не завязывайся
  на регистр.
- **Никакого реального I/O и реального времени.** Тест не должен создавать
  файлы в настоящей файловой системе и не должен писать в `%LOCALAPPDATA%` —
  логгер в тестах это `NullLogger`.
- **Никаких гонок в качестве утверждения.** Асинхронные штуки
  (`SearchController`, батч-операции) проверяются детерминированной
  синхронизацией, а не расчётом на то, что фоновая задача «не успеет».
  Тест, который проходит только под нагрузкой, — сломанный тест
  (живой пример есть в [TECHDEBT.md](TECHDEBT.md)).
- **Новая абстракция в Core → фейк рядом.** Интерфейс без фейка означает, что
  всё, что его использует, становится непокрываемым.

---

## Осознанные границы

Это не баги и не техдолг, а решённые «нет» — не переоткрывать без разговора.

- **Только Windows.** Core платформонезависим по дисциплине, но второй
  платформы не планируется.
- **Нет DI-контейнера, нет MVVM-фреймворка.** Ни CommunityToolkit.Mvvm, ни
  `Microsoft.Extensions.DependencyInjection`, ни ModernWpfUI/MahApps.
- **Нет телеметрии, аналитики, сети.** Приложение работает локально.
- **`PublishTrimmed` не включать** — WPF его не поддерживает.
- **Только `.lnk`.** NTFS symlinks / junctions / hard links не создаются и не
  разрешаются; рекурсивный обход по ним может зациклиться (TECHDEBT).
- **Long paths (>260 без `\\?\`)**, UNC-таймауты, лимит FAT32 4 ГБ — сырые
  исключения, см. BACKLOG.
- **Undo не персистится** между запусками.
- **Тесты только для Core.** UI и платформенный слой не покрыты — проверяются
  smoke-запуском.

---

## Как добавлять новое

1. Нужна платформенная возможность? Сначала **интерфейс в Core**, потом
   реализация в `Platform.Windows`, потом регистрация в
   `PlatformBootstrapper`. Не тащить PInvoke/COM/`System.IO` в Core напрямую.
2. Операция меняет файлы? Она обязана: пройти `SystemPathGuard`, залогировать
   себя, положить `IUndoableAction` в `UndoService` и — если деструктивна —
   спросить подтверждение с **Cancel по-умолчанию**. Без undo операцию в UI
   не выпускать.
3. Логика распухла в `MainViewModel`? Выносить в отдельный контроллер; если
   в ней нет WPF — выносить **в Core**, чтобы покрылась тестами (так появились
   `BatchExecutor`, `ClipboardController`, `SearchController`,
   `SelectionController`, `PreviewController`, `NavigationController`).
4. Новый формат сайдкара? Одна строка в `CompanionResolver.Default` —
   и отображение, и групповые операции заработают сами. Разбор содержимого
   (как `Pp3Sidecar` / `UnityMetaSidecar`) нужен только если есть что
   показать в футере просмотра.
5. Проверка перед коммитом — `tools\check.bat` (build + `dotnet format
   --verify-no-changes` + тесты). `tools\check.bat run` добавляет
   smoke-запуск, `tools\check.bat format` — пишет форматирование.
