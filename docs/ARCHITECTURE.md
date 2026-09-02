# Wander — архитектура и механизмы

Где что лежит и почему так. Задачи — в PLAN / BACKLOG / TECHDEBT.

## Слои

| Проект | TFM | Роль |
|---|---|---|
| `Wander.Core` | `net10.0` | Логика и абстракции. Не знает про Windows и UI. |
| `Wander.Platform.Windows` | `net10.0-windows10.0.19041.0` | Реализации интерфейсов Core: Win32, Shell COM, WinRT, `System.IO`. |
| `Wander.App` | `net10.0-windows10.0.19041.0` | WPF: окно, ViewModel'и, диалоги, конвертеры. |
| `Wander.Core.Tests` | `net10.0` | xUnit, **только** Core через фейки. |

TFM `10.0.19041.0` у windows-проектов — ради WinRT-проекций (`Windows.Data.Pdf`
для обложки PDF); пакетов не прибавилось. Windows 10 остаётся целью: 19041 =
Win10 2004, сам `Windows.Data.Pdf` есть с 8.1; вызов обёрнут глухим `catch`.

**Жёсткое правило:** в Core нет `using System.Windows.*`, COM, PInvoke.
Нужно — интерфейс в Core, реализация в Platform.

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
    ├── Resources/      Strings*.resx, AppTextSource, MenuStyles, Palette
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

Снимается `tools\deps.ps1` (`-UpdateDoc` перезаписывает блок ниже — руками
не править). Свод `using Wander.*` по папкам, уровни, циклы. **Правило (O7,
2026-09-01): между папками внутри проекта нет циклов, у каждой папки есть
уровень** (0 — ни от кого не зависит; N — самый длинный путь вниз). Новое
ребро, замыкающее цикл, — повод переложить файл или развернуть связь
(событие вместо коллбэка вверх), а не исключение. Ребро `App ->
Platform.Windows` — один файл, `App.xaml.cs` (точка композиции), ему можно.

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

(O7, сверено с кодом.) **Через `IFileSystem`** — всё, что пользователь может
отменить, что обязан подменить тест, и всё, что перечисляет папки:
операции, листинг, сайдкары, перепись. **Напрямую `System.IO`** — байты
одного уже выбранного файла ради раскодирования, когда результат — картинка
или текст на экране, а не решение логики: сегодня так читает **только
`Wander.Core/Preview/`** (обложки, теги, меши, пробы текста), тесты туда не
ходят. Новый `File.` / `Directory.` в Core вне `Preview/` — кандидат в
`IFileSystem` либо осознанное расширение списка с записью здесь.

## Композиция: ServiceLocator

Статический `Dictionary<Type, object>`: `Register<T>`, `Get<T>`,
`TryGet<T>`, `IsRegistered<T>`, `Reset()` (тесты); все под `lock`.
Единственная регистрация — `App.OnStartup` → `PlatformBootstrapper.RegisterDefaults()`,
порядок значим: (1) `ILogger` / `ILogFile` (`FileLogger`) первым — всё
ниже логирует при конструировании; пишет заголовок сессии (версия, ОС,
рантайм, культура, elevated); (2) платформенные абстракции (`IFileSystem`,
`IKnownFolders`, `IShellLauncher`, `IIconProvider`, `IAppStateStore`,
`IFileLockInspector`, `IShortcutService`, `IShellNamespace`,
`IShellContextMenu`, `IImageMetadataReader`); (3) общие синглтоны
`UndoService`, `OperationTracker`, `IRecycleBin`, `FileOperationService`
(один на приложение — иначе undo-стек и прогресс расползутся);
(4) `CompanionResolver`, `CompanionMetadataService` (зависит от
`IFileSystem` и `UndoService`). Тесты в локатор не ходят — фейки
конструкторами.

### Обязательные и необязательные сервисы

Регистрация одна и безусловная, поэтому ветка «не зарегистрирован» либо
описывает реальный режим, либо недостижима. **Сервис, который где-то
читается `Get<T>()`, обязателен везде**: `IFileSystem`, `IShellLauncher`,
`IAppStateStore`, `IRecycleBin`, `IShortcutService`, `IIconProvider`,
`ILogger`, `CompanionResolver`, `UndoService`, `OperationTracker`,
`FileOperationService`, `IDialogs` (App-уровень, регистрируется в
`App.OnStartup` рядом с `ITextSource`) — отсутствие = сломанный
бутстраппер, падение на старте честнее работы вполсилы; хост без
Windows-слоя регистрирует свои реализации сам. **Необязательные** читаются `TryGet<T>()`, у каждого
внятный ответ «нет»:

| Сервис | Чего не будет |
|---|---|
| `IShellNamespace` | закладки на Корзину, `shell:`-пути |
| `IKnownFolders` | закладки по умолчанию |
| `IShellContextMenu` | пункты сторонних приложений в меню |
| `IShellHandlerRegistry` | список обработчиков в настройках |
| `IFileLockInspector` | имя процесса в «файл занят» |
| `IVolumeInfoProvider` | блок тома над переписью корня |
| `ISystemClipboard` | буфер внутренний (конструктор `ClipboardController` по умолчанию) |
| `IDirectoryWatcher` | список не обновляется сам |
| `IImageMetadataReader` | нет EXIF |
| `CompanionMetadataService` | не читаются и не пишутся сайдкары |
| `ContentSearchService` | нет поиска по содержимому |
| `ILogFile` | нет пункта «Журнал» и лога в отчёте о падении |
| `ITextSource` | Core отдаёт ключ вместо надписи |

Только последняя деградация под тестом (`TextFallbackTests`); `ITextSource`
в тестах не регистрируется специально.

### Конструирование в две фазы

Конструктор `MainViewModel` (O6.4): (1) **зависимость строится раньше того,
кто её берёт** — nullable-ворнингов в сборке ноль, и это часть проверки:
новый CS8602 / CS8604 в конструкторе = сломан порядок (так `RatingsController`
однажды получил null вместо `Settings`); (2) **построить, потом включить** —
подписки с побочными эффектами (`Settings.PropertyChanged` → перечитывания,
`Trees.ExpansionChanged` → запись состояния) ставятся в конце, **после**
`RestoreState()`; флага «идёт восстановление» нет, один
`_stateSaveTimer.Stop()` в конце `RestoreState` гасит запись от начальной
навигации.

### Изменяемая статика

Проход O6, категория 6. Правка одна — `lock` в локаторе (xUnit гонит
классы параллельно, `ServiceLocatorTests` пишет, пока соседи читают через
`ITextSource.Text` → `TryGet`; чтение `Dictionary` под запись —
неопределённое поведение). Остальное оставлено сознательно:

| Место | Почему |
|---|---|
| `PerfLog._log`, `_windowStartMs` | диагностика, под `_lock`, тестами не читается |
| `IconImageCache` | под `_lock`, с потолком; второй владелец не нужен, пока провайдер — синглтон |
| `CrashReporter._offeredThisSession` | худшее у гонки нефатальных — второй диалог; fatal-путь флаг игнорирует |
| `UiStallWatch._worker`, `HighlightingCatalog._registered` | once-флаги (второй под `_lock`) |
| `MagnifierCursor._cached`, `ShellHandlerRegistry._searchPath` | ленивые неизменяемые: гонка строит то же значение дважды |

`SystemIconProvider`: `_cache` / `_missing` / `_thumbnailOrder` — поля
**экземпляра** под `_lock`; статика там — set-once `_log` и lock-объекты.

### Как потребляются сервисы

Регистрация до первого потребителя, словарь после этого заморожен, горячей
подмены и плагинов нет (появятся — пересмотреть). Правила: **экземпляры
разрешают сервисы один раз в конструкторе в readonly-поля** (список
зависимостей виден в одном месте — это будущие параметры конструктора);
**статические хелперы** (`Text`, `IconConverter.Load`,
`SystemIconProvider.ResolveShortcut`) ходят в локатор на каждый вызов
(один поиск по словарю не виден на фоне шелла / декодера / диска);
**новых ленивых статических кэшей сервисов** (`_x ??= Get<X>()`) не
заводить без строки в таблице выше (ещё одна статика плюс риск обращения до
бутстраппера).

## Файловые операции

```
VM / drop / hotkey → FileOperationService (фасад: одиночные ops инлайном)
        ├─ *Many / *ManyAsync → BatchExecutor
        │      ├→ IConflictResolver (диалог замены)
        │      ├→ SystemPathGuard   (блок системных путей)
        │      ├→ IRecycleBin       (корзина вместо стирания)
        │      └→ OperationTracker  (прогресс)
        └→ UndoService ← каждая успешная операция кладёт IUndoableAction
```

Чтения остаются на `IFileSystem` и конвейер минуют. `BatchExecutor` —
цикл конфликтов, composite-undo, recycle-vs-permanent; синхронные
`CopyMany` / `MoveMany` для тестов, продакшн — async на пуле с per-item
прогрессом и `CancellationToken`. Отмена и прогресс гранулярны по элементам
верхнего уровня (TECHDEBT). Типы результатов (`BatchItemResult`,
`DeleteResult`) — на уровне namespace.

- **Undo.** Один LIFO-стек. Move ↔ Move обратно, Rename ↔ Rename, Delete →
  Restore из корзины, Create → Delete в корзину. Безвозвратное удаление не
  откатывается и затирает стек. `BeginOperation()` — busy-счётчик,
  `CanUndo == false` в полёте (`Ctrl+Z` игнорируется, как в Explorer).
  Стек под одним локом; `Changed` поднимается вне лока и может прийти с
  фонового потока — подписчик маршалит сам. Не переживает рестарт.
- **Прогресс.** `OperationTracker.Begin(verb, total)` → `IOperationHandle`
  (диспозить всегда); `Snapshot()` — иммутабельный срез; несколько операций
  агрегируются; `Changed` — с фона. `MainViewModel.RunWithProgressDialogAsync`
  оборачивает батчи в модальный `ProgressDialog` с отменой.
- **Конфликты.** `IConflictResolver`: Replace all / Skip all / Resolve
  each; `ConflictDialog`, `BatchConflictDialog`; `DispatcherConflictResolver`
  маршалит на UI. При Replace цель уходит **в корзину**, `DeleteAction` в
  composite перед основным шагом — `Ctrl+Z` возвращает обе стороны
  (Explorer замещает безвозвратно).
- **Защита.** `SystemPathGuard` — чистая функция от пути и окружения, без
  I/O и локатора, зовётся статически: корни дисков, спец-папки (Windows,
  Program Files x86/x64, ProgramData, Users, корень профиля), всё дерево
  `C:\Windows`; содержимое Program Files и чужих профилей намеренно не
  блокируется (чистка остатков деинсталляции легальна). `PathSafety` —
  self-drop с человеческим текстом через `ITextSource`. `IFileLockInspector`
  — «файл открыт в: Word (PID 1234)», по файлам.
- **Осознанные отступления от «всё откатываемо»:** безвозвратное удаление
  (подтверждение всегда, независимо от настройки); восстановление из
  корзины — в `UndoService` не кладётся, как в Explorer (откат = удалить
  только что возвращённое, `Ctrl+Z` стал бы деструктивным); операция не
  деструктивна, логируется, `SystemPathGuard` не нужен — место решает шелл.

## Навигация и дерево

- **`NavigationService`** — back / forward; каждая запись несёт
  `NavigationSource`, чтобы дерево и панель просмотра реагировали по-разному.
- **Быстрый фильтр не кончается на текущей папке.** `SearchController`
  мгновенно сужает листинг в памяти; `ContentSearchController` через 400 мс
  и от 2 символов (`MinAutoRunLength` — сторожит только этот незаметный
  путь; в окне порога нет) запускает `ContentSearchService` со
  `SearchScope.Subfolders` (`IsFilterPass`), **засеянный** найденным
  фильтром (не мигает), повторы отсеиваются по `_resultPaths`, `HereFirst`
  держит найденное здесь выше. Окно поиска этим путём не ходит
  (`_fromFilterBox`) — у него своя галочка подпапок.
- **Панель наследуется в обе стороны.** `GoUp` берёт источник текущей
  записи, `DescendSource` — на спуске: шаг вглубь из закладок остаётся
  `Bookmark`. Меняет панель только явный выбор в другой или недостижимый
  путь (`ExpandTreeToCurrent` падает на `Roots`).
- **Пропавшая папка — состояние.** `MissingFolderPath` взводится в фоне по
  `DirectoryNotFoundException` / `DriveNotFoundException`, не проверкой
  перед навигацией (синхронный поход на шару = зависание). Поверх пустого
  списка — «папка удалена или недоступна»; у закладки (`IsMissingBookmark`)
  — «Указать расположение…» / «Убрать».
- **`NavigationController`** (App) — маршрутизация путей и shell-сентинелов
  (`shell:RecycleBinFolder` через `IShellNamespace`, лейбл «Корзина»);
  состояние адресной строки (`Breadcrumbs`, `RecentPaths`,
  `IsEditingAddress`), XAML биндится `Vm.Nav.X`.
- **Адресная строка** — крошки (`PathCrumbs.Split`, плоские кнопки,
  shell-сентинел одной крошкой, `ScrollViewer` проматывается в хвост) и
  текстовое поле (`Ctrl+L` / клик по пустому месту; выход `Esc`, потеря
  фокуса, удавшаяся навигация). `RecentPaths` (Core) — MRU 20 папок без
  дублей, `AppState.Session.RecentPaths`, кнопка-треугольник / `F4`.
- **Память выделения и намерение прибытия** — `FolderSession` (ниже):
  64 папки LRU; подъём вверх выделяет покинутую папку
  (`PlanArrivalSelection`); удаление — следующий уцелевший
  (`NextAfterRemoval`), вставка — вставленное; те два поднимают
  `FocusListAfterRestore` (шли за модальным диалогом, строка перестроена,
  фокус на окне).
- **Дерево** — lazy-load, листья без шеврона, раскрытые пути в `AppState`,
  авто-раскрытие на текущую папку, **никогда не сворачивается само**.
- **Листинг вне UI-потока** — `RefreshFolderAsync` (и `RefreshShellAsync`)
  в `Task.Run` с отменой: следующая навигация отменяет предыдущую, побеждает
  последняя; спиннер только после 150 мс.
- **Иконки и миниатюры в фоне** — `Controls/AsyncIcon` (наследник `Image`):
  кэшированную отдаёт синхронно (`TryGetCachedIcon`), остальное через
  `Task.Run` под шлюзом (4 слота), результат отбрасывается, если контейнер
  переехал на другой файл. Три тира: провайдер хранит `byte[]` (память +
  диск), **`IconImageCache` — декодированные замороженные `BitmapImage`**
  (256; декод был единственным на UI-потоке — 338 декодов и 141 мс за
  секунду при прокрутке; декодирует тот, кто первым дошёл; кнопка очистки
  чистит и его). Четыре ступени: `Small` / `Normal` — значок по расширению
  (`SHGetFileInfo`, один на тип), `Medium` (96) / `Large` (256) — миниатюра
  через `IShellItemImageFactory` только для `IsThumbnailable` (иначе шелл
  пишет значок в общий `thumbcache` и платит обращением за каждый файл).
  Остальные на `Large` идут через `SHIL_JUMBO`, и слот там всегда 256, но
  не масштабируется: приложение с 48-пиксельным ресурсом рисуется в углу
  пустого квадрата. `TrimJumboSlot` режет слот до ближайшей стандартной
  ступени над нарисованным — только после этого натуральный размер
  значка честный. Шелл не увеличивает (`SIIGBF_RESIZETOFIT` только
  ужимает), поэтому растягивал вид: у плиток, значков и галереи
  `StretchDirection=DownOnly`, размер ячейки — потолок, а не цель.
  Ключ кэша: по пути, где картинка своя, по расширению, где общая; бюджет
  считает только первые; на диск — только 256 (дисковый ключ размера не
  несёт, 96 дёшево пересобрать), с поколением: правка того, как рисуется
  миниатюра, инвалидирует диск (`ThumbnailDiskCache.Generation`).
- **Закладки** — drag-add, сворачиваемые, `AppState.Favorites` /
  `IsBookmarksExpanded`; спец-папки через `IKnownFolders` →
  `SHGetKnownFolderPath` плюс Корзина, каждая чекбоксом.

## Shell-namespace: корзина и архивы

Две вещи, которые выглядят папками, а папками не являются: корзина
(`shell:RecycleBinFolder`) и архив, открытый как папка. Обе за одним
контрактом `IShellNamespace` (Core), реализация — `WindowsShellNamespace`,
которая только диспетчеризует: корзина — старый путь через
`Shell.Application`, архив — `ShellArchiveFolder` на `IShellItem`.

- **Путь внутри архива — обычный parsing name.** `D:\pack.7z\sub\b.txt`
  шелл разбирает сам; Wander режет его надвое чистой функцией
  `ArchivePath.Parse(path, extensions)` (Core): первый сегмент с архивным
  расширением — контейнер, хвост — путь внутри, пустой хвост = корень.
  Набор расширений читает Platform из реестра лениво (ProgID расширения ∈
  `CompressedFolder` / `ArchiveFolder` / `CABFolder`, `UserChoice` важнее
  умолчания класса, fallback `.zip`): ассоциация, отданная 7-Zip, закрывает
  просмотр папкой и у нас — как в Проводнике.
- **Один предикат на весь код** — `Archives.Of(path)` →
  `IShellNamespace.ParseArchive`. Никаких `EndsWith(".zip")`.
  `ParseArchive` заодно проверяет `File.Exists` контейнера: настоящая папка
  с именем `backup.zip` открывается папкой. `MainViewModel` кэширует ответ
  на смену пути (`NoteCurrentLocation`) — `CanExecute` опрашивается десятки
  раз в секунду, а ответ упирается в диск.
- **`IShellItem`, не `Shell.Application`.** У последнего `FolderItem.Size`
  и `ModifyDate` для `ArchiveFolder` врут (0 и 1899). Перечисление —
  `BHID_EnumItems`; папка/файл — `SFGAO_FOLDER`; размер и дата —
  `IShellItem2` (`PKEY_Size`, `PKEY_DateModified`), у папок внутри размера
  нет. Корзина остаётся на `Shell.Application`: её колонки он отдаёт верно.
- **Байты — только копирующим движком шелла.** `BHID_Stream` и
  `IDataObject` для `ArchiveFolder` отвечают `E_NOINTERFACE`;
  `IFileOperation::CopyItem` с `FOF_NO_UI` извлекает всё. Отсюда
  `IShellNamespace.CopyOut` и `FileSystem/ExtractionService` (Core) вокруг
  него: `SystemPathGuard` на цель, `IConflictResolver` (Replace → старое в
  корзину), лог, прогресс в `OperationTracker`, отмена через
  `IFileOperationProgressSink.PreCopyItem`, undo — `ExtractAction`
  (извлечённое в корзину) в композите. Не через `BatchExecutor`: тот
  копирует `IFileSystem` → `IFileSystem` и полагается на то, что оба конца
  можно `stat`. Точки входа: `Ctrl+C` внутри архива кладёт пути внутрь как
  есть, `PasteAsync` в обычной папке узнаёт их по `ParseArchive` и зовёт
  сервис; «Извлечь…» (`MenuCommandId.Extract`) — то же плюс `PickFolder`.
- **Внутри архива выключено решением**, а не «пока не сделано»: удаление,
  переименование, вырезать, вставка, создание, drop внутрь, сторож папки,
  оценки, спутники, поиск по содержимому и подпапкам, статистика папки в
  футере, миниатюры (`IsThumbnailable` → false, значок по расширению;
  папке внутри архива `SHGetFileInfo` надо прямо сказать
  `FILE_ATTRIBUTE_DIRECTORY` — иначе рисуется пустой лист).
  Фильтр по имени работает — он в памяти. Меню внутри — четыре строки
  (Открыть, Копировать, Извлечь…, Копировать путь), не серые: писать в
  архив нельзя вообще, а серая строка обещает «потом».
- **Пусто или защищено.** zip с паролем перечисляется, извлечение молча не
  даёт байт; 7z с `-mhe` отдаёт ноль записей — неотличимо от пустого. Оба
  случая — текст в статусной строке, не пустой список без объяснений;
  нечитаемый контейнер — «архив повреждён или недоступен».
- **Отступление от «всё откатываемо»:** «открыть» файл из архива извлекает
  его во временную копию (`AppPaths.Tmp`, подпапка по хешу пути,
  `TempFiles.Sweep` на старте чистит старше суток) и запускает
  ассоциацией — без диалогов и без `IUndoableAction`. Временная копия
  чужого файла не пользовательские данные; статусная строка говорит, что
  правки в архив не попадут.
- **Дерево и закладки.** Архив — узел в обеих панелях (как в панели
  навигации Проводника): `TreeNodeViewModel.ReadChildFolders` добавляет к
  папкам файлы, для которых `Archives.Of(path) is { IsRoot: true }`,
  вставляя их среди папок по имени; узел с `Archives.Contains(FullPath)`
  читает детей через `IShellNamespace.Enumerate` (только папки), не через
  `IFileSystem`. `ProbeForChevrons` архивные узлы пропускает — иначе каждая
  папка с архивами открывала бы их все через шелл в фоне; пустой архив
  теряет шеврон при раскрытии. `ExpandTo(here)` находит путь внутри архива
  как любой другой; программное выделение при этом не навигация
  (`FolderTreesController.IsSyncingSelection` — без неё подсветка
  контейнера уводила из архива). `NodeAt` и `DropTargetController` считают
  архивные узлы, как `shell:`: ни меню, ни цели drop. Закладка на архив —
  раскрываемый узел; на путь внутри — лист, не «пропавшая», как у корзины.
- **Наружу пока нельзя.** Перетаскивание из архива и `Ctrl+C` для чужих
  программ (`CF_HDROP` с несуществующим путём) — PLAN, P4; сейчас
  статусная строка честно отсылает к «Извлечь…».

## Выделение, буфер, фильтр

- **`SelectionController`** (App) — deferred selection при click-and-drag
  (drag не сбрасывает мультивыбор), «снять выделение в активном списке».
- **`RubberBandController`** (App) — адорнер, захват мыши, пересечение с
  контейнерами; только с пустого места (`ListVisuals.IsChrome` — полоса
  прокрутки, заголовки, разделители); невиртуализованные не попадают.
- **`EntryVisibility`** (Core) — `ShowHidden` / `ShowSystem` /
  `HideSystemRootFolders` одним значением; список и дерево фильтруют им
  обоим; в фон передаётся снимком. Третий флаг — `SystemRootFolders`
  (`$RECYCLE.BIN`, `System Volume Information`), отдельно от
  `SystemPathGuard`: тот запрещает менять, этот решает показывать.
- **`ClipboardController`** (Core) — пути + флаг copy / move; хранит пути,
  не содержимое (операция в момент Paste против тогдашнего состояния).
- **`ListVisuals.Ancestors`** (App) — единственный способ вверх от
  `e.OriginalSource`: клик может прийтись на `Run`, у которого
  `VisualTreeHelper.GetParent` **бросает**; текстовые элементы шагают по
  логическому дереву до `TextBlock`. Все «на что кликнули» — через него.
- **`TypeAheadController`** (Core) — префикс, таймаут, «та же буква
  перебирает»; часы подставляются.
- **`SearchController`** (Core) — живой фильтр: `SetSource` снапшот после
  hidden/system, проекция на фоне с отменой на keystroke, `FilteredChanged`;
  дерево не трогает. Все в Core — гонки «печатаю + Refresh» были источником
  багов, пока жили в VM.

### Системный буфер обмена

```
Ctrl+C/X → ClipboardController ─┬→ модель в памяти (Paste читает её)
                                └→ ISystemClipboard.SetFiles → Explorer видит
Window.Activated → SyncFromSystem → ISystemClipboard.GetFiles → модель ← Explorer
```

Модель в памяти — потому что буфер эксклюзивен и межпроцессен, а
`RelayCommand` через `CommandManager.RequerySuggested` дёргает `CanExecute`
десятки раз в секунду. Чтение по `Activated`, а не `WM_CLIPBOARDUPDATE`:
чтобы вставить, окно всё равно активируют; ноль P/Invoke; дырка «буфер
поменялся, пока активны» самоисправляется; `AddClipboardFormatListener` —
отдельным классом при нужде. Свой Win32 (`WindowsClipboard`), не
`System.Windows.Clipboard` — Platform не тянет WPF. Неприятности API:
память `SetClipboardData` принадлежит системе; «вырезано» — **бит**
`DROPEFFECT_MOVE` (пишут `COPY|LINK`); ретрай на каждом вызове;
`OpenClipboard(NULL)` + `EmptyClipboard` обнуляет владельца и ломает
`SetClipboardData` — владелец `GetActiveWindow()` вызывающего потока.
Асимметрия: вырезал у нас, вставил в Проводнике — перемещение не наше, в
undo его нет.

### Слежение за папкой

`IDirectoryWatcher` / `WindowsDirectoryWatcher` (`FileSystemWatcher`).
События пачками с фона → троттл на `DispatcherTimer` 500 мс, повторяющемся
(перезапускаемый при непрерывном потоке не сработал бы), спрашивает
`FolderSession.DecideWatchTick`, гасит себя на холостом тике. Пока правится
имя или идёт своя операция — `Hold`, изменения ждут следующего тика.
Ошибка вотчера (переполнение буфера) = изменение, вотчер переподнимается.

## Сессия папки — `Wander.Core/Listing/`

Состояние «папки, на которую смотрят», вынесено из VM в Core (O11): машина
**решений** без ввода-вывода — факты на входе («навигация в X», «листинг
эпохи N долетел», «сторож заметил»), решения на выходе («опубликовать»,
«выделить», «перечитать»); диск, потоки, `Dispatcher`, таймеры и коллекции
остаются в `MainViewModel`.

- **`FolderSession`** — `BeginListing` выдаёт эпоху и признак «прибытие /
  перечитывание»; `IsCurrent(epoch)` — единственный вопрос «мой ли ответ»
  (листинг, проход по оценкам через `RatingsController.isCurrent`, точка
  публикации). `OnNavigating` запоминает выделение покидаемой папки, гасит
  обогнанное намерение, планирует умолчание (подъём — покинутая папка,
  иначе память LRU 64). `DecideArrival` — единственное потребление
  намерения (чужой листинг и пустой список оставляют ждать).
  `DecideWatchTick` поверх `FolderChanges`: стоп / подождать / перечитать /
  перечитать строки; идемпотентен.
- **`ListingDiff`** — «текущие строки + свежий листинг → план»
  (`RemoveAt` / `Insert` / `Move` / `Replace` / пересобрать). Неизменённая
  строка не порождает правки и не теряет контейнер. Порог пересборки
  позиционный: удаление вверху сдвигает всё и честно уходит в один `Reset`
  (`RowGoneNearTheTop_IsWholesale`).
- **`ArrivalIntent`** — одно отложенное намерение «что выделить, когда
  долетит»: установка заменяет, применение одно.

Инварианты — `FolderSessionTests` / `ListingDiffTests`. У VM осознанно:
правило спиннера (тайминг вокруг `Task.WhenAny`), восстановление статуса
операции поверх «N элементов», мост `FocusListAfterRestore` — тестируемого
содержания нет.

## Поиск

```
Маска ─┬ shallow → SearchController (Core): проекция снапшота, на каждую букву
       └ deep ───→ ContentSearchController (App), по таймеру
Текст ───────────→   └ ContentSearchService (Core): обход + IContentExtractor
```

Граница — `ContentSearchController.IsDeep`: есть текст или включены
подпапки. **Навигация сбрасывает поиск целиком** (`Reset`): галка подпапок
переживала навигацию и закрытие окна, `IsDeep` оставался, каждая буква
уходила в обход диска — «фильтр перестал фильтровать» без объяснения. По
той же причине область и галка бинарей не в `state.json`. Пока `IsDeep`
false — набор в `SearchController`; true — `Query` очищается, поле
становится запросом.

- **Два критерия через «И».** Первая версия с «или» дала три бага на одном
  скриншоте (слово в документах возвращало картинки с той же буквой в
  имени; `.t` вытаскивал `.pdf`; галка «в содержимом» жила в попапе). «И»
  делает маску воротами — отвергнутый файл не открывается и не считается.
  Галки «искать в содержимом» нет — её роль играет наличие текста.
- **`NameFilter`** — подстрока по умолчанию; `*` / `?` → шаблон на всё
  имя; части через `;`. Как в Everything: `doc` и `*.cs` по одному
  нажатию. Сопоставление руками без регулярок (перечитывается на каждую
  букву; `*a*a*a*a*b` — катастрофический бэктрекинг; тут одна точка
  возврата). Разбирается один раз на запрос.
- **`IContentExtractor`** — три несовместимых ответа на «текст внутри»:
  байты для декода, zip с XML, COM-фильтр Windows; первые два в Core,
  третий там жить не может. Композиция в `PlatformBootstrapper`, порядок —
  часть контракта: `ZipDocumentExtractor` (`.docx .xlsx .pptx .epub .odt
  .ods .odp`), `FilterTextExtractor` (Platform, `.doc .rtf .pdf .chm .msg
  .mht …`), `PlainTextExtractor` последним (`CanExtract` всегда true,
  решает `TextProbe` по байтам — расширения врут: `.asset` бывает YAML и
  бинарём). **Провал специфичного экстрактора заканчивает файл** (иначе
  `.pdf` без обработчика проваливался в текст и находил слово в `%PDF-1.4
  ReportLab`). Экстракторы не бросают — `null`. `IsExpensive` — что
  кэшировать и что считать непрочитанным (`.dll` по дороге не считается).
- **Почему `IFilter`.** `.doc` — OLE с piece table и сжатыми кусками, свой
  читатель — хвост неправильных ответов; `OffFilt.dll` в Windows с 7, Office
  не нужен, тот же механизм для `.rtf`, `.mht`, `.pdf` с читалкой.
  Неочевидность: `IFilter::Init` без `APPLY_INDEX_ATTRIBUTES` возвращает
  **пустой документ** при любом флаге канонизации (`1|2` → 0 символов,
  `1|2|8|16` → полный текст); флаг идёт вместе, value-чанки отбрасываются.
  Список форматов именованный, не «что скажет реестр»: реестровый фильтр
  текста декодирует системной кодовой страницей, `EncodingProbe` лучше.
- **`BinaryTextSearch`** — отдельный режим, не экстрактор: бинари по
  умолчанию вне (как `grep`, `ripgrep`, VS Code, Windows Search — шум из
  пятисот DLL); по галке побайтово, **только ASCII** (`Supports` говорит
  заранее); экстрактор отвечает «что написано», этот — «да/нет».
- **`ExtractedTextCache`** — LRU «путь + размер + mtime», потолок 32 МБ в
  символах, только дорогие форматы. Индекс на диске — REJECTED.
- **Запуск сам.** `ContentSearchController` получает корень и видимость
  колбэками (меняются под ним). Символ — пауза 400 мс, порога по длине
  **здесь** нет (три символа для тяжёлых областей давали необъяснимую
  тишину на `:no`; обход ограничен 5000 и отменяется буквой);
  переключатель — сразу; `Enter` перебивает паузу. `SearchState` (не
  запускался / ждёт / идёт / готово / остановлен) гасит «Остановить»,
  крутит индикатор, пишет статус. Отмена **забирает владение**: `Cancel`
  поднимает поколение, отменённый проход молчит (иначе объявлял
  «Остановлено» поверх нового состояния). «Остановить» = `Stop()`.
- **Окно, а не панель** — критериев четыре, с диапазонами будет больше;
  попап закрывался от клика мимо (и прятал галку). `SearchWindow` с
  `Owner`, не `Topmost`; скрывается, не уничтожается; строка в тулбаре на
  это время спрятана; рамка стандартная, «свернуть / развернуть» сняты
  `SetWindowLong` в `SourceInitialized` (`ToolWindow` уродует крестик,
  `NoResize` мешает растягивать). `Dismissed` возвращает клавиатуру в список.
- **`SearchExpression`** — `маска:текст`; двоеточие запрещено в именах, не
  экранируется; первое делит, остальные тексту. Нужно для **вывода**
  (настроенный в окне поиск оставлял поле пустым). Флаги в строку не
  попали — `HasNonDefaultOptions` подсвечивает `⋮` (BACKLOG).
- **Результаты** — `_searchResults` → `Entries` пачками не чаще 200 мс
  (обновление = Reset и пересчёт раскладки). `FileSystemEntry.MatchSnippet`
  и `ParentFolder` — как `OriginalLocation`: одна строка на экране, без
  параллельной таблицы. Пока результаты на экране, `Refresh()` не
  пересобирает — только `PruneMissingResults`; повторить — `F5`.

## Контекстное меню

Что показать — Core, чем нарисовать — App, откуда чужие пункты — Platform.

```
правый клик
  ├→ MainWindow: чинит выделение (вне — переносит, пусто — снимает → меню папки)
  ├→ ShellMenuCache.Acquire(paths, folder)            App
  │     └→ IShellContextMenu.Open                     Platform
  │           SHParseDisplayName → IShellFolder → GetUIObjectOf / CreateViewObject
  │           → IContextMenu → QueryContextMenu(HMENU) → обход → ShellMenuEntry[]
  ├→ ContextMenuBuilder.Build(target, settings, shellItems)   Core, чистая → MenuEntry[]
  └→ ContextMenuFactory.Build(model, session)                 App → WPF ContextMenu
```

- **`ContextMenuBuilder`** — правила («Rename на одном», «в корзине ничего
  деструктивного», «у папки нет Open with»), чистая функция от
  `ContextMenuTarget` и `ContextMenuSettings`, под тестами; схлопывает
  разделители (`Normalize`). Два меню: **по выделению** — `Открыть`, «Открыть
  с помощью», расширения, подменю «Файл», `Свойства`; **по фону** —
  `Создать`, `Открыть в терминале`, `Копировать путь`, расширения,
  `Свойства` (вид, сортировка, обновление — состояние окна, живут в «Вид»).
- Системное «Создать» **вливается** в наше (опознаётся по глаголу
  `NewFolder` дочерней строки, не по подписи); своя «Папка» первой (откат и
  rename на месте), `Ярлык` и шаблоны следом от шелла. «Открыть с помощью»
  тоже вливается (живой список приложений не собрать самим), своя «Выбрать
  приложение…» — запасная при выключенных расширениях.
- **Порядок по частоте**: сверху то, ради чего открыли («Редактировать в…»,
  «Git Commit»); свои операции внизу в «Файл» (Вырезать / Копировать /
  Вставить, путь / имя, Переименовать / ярлык, Удалить) — у половины хоткеи.
- **`SplitShell`** — по каноническому глаголу, никогда по подписи
  (локализована, меняется с именем файла): подменю с ребёнком `openas` →
  в «Открыть с помощью»; глагол в списке (`PreviousVersions`) **или**
  динамическое системное подменю → в конец «Файл»; остальное — верх.
  «Динамическое» = «Отправить» / «Передать на устройство»: шелл собирает их
  при показе, ни один пункт не несёт глагола; сторонние глаголы
  регистрируют всегда. Эвристика (TECHDEBT).
- **`ShellEntryKey.For(verb, header)`** — глагол, если есть; нормализованная
  подпись, если нет. TortoiseGit пишет в подпись имя ветки («Git Commit →
  "master"…») — по подписи выключение отваливалось на `git switch`; 7-Zip
  верхнему пункту глагол не публикует — там подпись стабильна (имя
  приложения). `IsBlocked` проверяет обе формы — старые настройки без
  миграции.
- **«Приложение» и «Типы»** — из реестра (`IShellHandlerRegistry`):
  `<scope>\shellex\ContextMenuHandlers\<имя>` → CLSID → `InprocServer32` →
  версия DLL = приложение; `<scope>\shell\<verb>` — подпись и команда там,
  имя ключа = глагол (точное сопоставление); то же под
  `SystemFileAssociations\<scope>`. `HKLM\SOFTWARE\Classes` и `HKCU\…` по
  отдельности, не `HKCR` (склеенное — минуты против сотни мс). Только
  чтение. Замеры: базовые области 40–50 мс холодно, ~10 прогрето; все 848
  областей ~150 мс; имена расширений 20 мс. `ShellExtensionCatalog` (Core)
  сливает реестр и встреченное по `ShellEntryKey`; строка от одного
  источника тоже попадает.
- **Не в меню намеренно:** «Удалить безвозвратно» (только `Shift+Del`),
  закладки (панель слева), «Показать в Проводнике», `pintohomefile`.
  «Открыть в терминале» — только папка и фон.
- **`ShellContextMenu`** читает **классическое** меню (то, что Win11 прячет
  под «дополнительные параметры»; 7-Zip, TortoiseGit, антивирусы там же;
  оттуда же «Создать» с `ShellNew`). `HMENU` не отдаётся в `TrackPopupMenu`,
  а обходится и перерисовывается WPF-строками (иначе чужое меню рядом, не
  внутри). Цена: ленивые подменю будить `IContextMenu2::HandleMenuMsg` с
  `WM_INITMENUPOPUP`; owner-drawn (`dwItemData` в приватном формате)
  пропускаются с логом; иконки из `hbmpItem` → PNG (`ShellMenuIcons`).
  Дубли по глаголу (`GetCommandString`, `GCS_VERBW`): `cut` / `copy` /
  `paste` / `delete` / `rename` / `properties` / `link` / `openas` /
  `copyaspath` рисуем сами.
- **`ShellMenuCache`** — последняя сессия жива: повтор по тому же выделению
  не ходит в шелл (0,4–1,1 с первый раз, 80–260 мс дальше: TortoiseGit
  читает статус, у картинки 25 обработчиков против 12). Время жизни: правый
  клик, открывающий меню, уже закрыл предыдущее — `Acquire` про *другую*
  цель освобождает прошлую сессию; «открыто ли меню» не считается
  (`ContextMenu.Closed` уходит в `BeginInvoke(Background)` — **после**
  следующего клика); `Invalidate` только отвязывает от ключа.
- **Расширения в нашем процессе** (как в Explorer): `try/catch` с логом;
  `ShellExtensionsEnabled = false` — чужие DLL не грузятся; команда
  вызывается **после** закрытия меню (обработчики открывают модальные
  диалоги).
- **Кастомизация** (`ContextMenuSettings`): мастер-выключатель, чёрный
  список, скрытые свои пункты — как «что выключено» по строковым именам
  `MenuCommandId` (новый пункт появится сам, переименование enum не
  воскресит спрятанное). `KnownShellExtensions` накапливается по мере
  открытия меню и подрезается при сохранении (`TrimKnownExtensions`).

## Companion-файлы

Служебный файл рядом с основным (`.meta`, `.pp3`, `.xmp`) — довесок, одной
строкой, едет вместе. `AppSettings.IntegrateCompanions`, по умолчанию вкл.

```
CompanionRule            суффикс + шаблон имени; формат — данные, не код
CompanionResolver        Collapse() список → свёрнутый; FindCompanions() путь → рядом;
   │                     RenamePlan() путь + имя → план группы
   ├→ RefreshFolderAsync (свёртка на пуле) ├→ WithCompanions() (перед батчами)
   └→ FileOperationService.RenameMany() (группа = один undo-шаг)
CompanionMetadataService чтение/запись содержимого: UnityMetaSidecar.Read (GUID,
   импортёр), Pp3Sidecar.Read (Rank, ColorLabel), WithRank → IFileSystem.ReplaceAtomic,
   CreateRatingSidecar (по согласию)
Listing/RatedListing     WithRatings() листинг → тот же с Rating (читалка делегатом)
```

| Шаблон | Пример | Кто |
|---|---|---|
| `Appended` — к полному имени | `Sprite.png.meta`, `IMG.CR2.pp3` | Unity, RawTherapee, Takeout |
| `Replaced` — заменяет расширение | `IMG_1234.xmp` | Adobe / darktable, `.AAE` |

`Appended` по точному имени, `Replaced` по stem'у; два претендента на stem
(`IMG.CR2` + `IMG.jpg` при `IMG.xmp`) — не привязывается ни к кому.

- Свёртка — в воркере `RefreshFolderAsync` **после** Hidden/System:
  спутник у отфильтрованного файла и сирота остаются видимыми.
- `FileSystemEntry.Companions` — пути, пусто у обычного файла и при
  выключенном флаге; блок «Вместе с файлом:» в футере. Значок в списке —
  REJECTED.
- Меню про спутников не знает: их нет в выделении.
- Групповые операции — `BatchGroup` (основной + спутники): один вопрос
  на группу, ответ ко всем, composite-undo. Группы из выделения бесплатно
  (`Companions` уже в записи; Copy / Cut / Delete / drag на UI-потоке); из
  плоского списка (буфер, drop из Explorer) — `CompanionResolver.Group()` с
  диском, в `Task.Run`.
- Авто-переименование тянет спутников (`Sprite (1).png.meta`) подстановкой
  общей части — знание форматов в `BatchExecutor` не протекает.
- Переименование мимо батча: `RenamePlan` + `RenameMany`, откат середины.
- **Оценки** — `SidecarRating` (`Rank` / `ColorLabel`), формат за
  `CompanionMetadataService` по расширению; `ColorLabels` нумерованы
  одинаково (XMP хранит имя `Red`, pp3 — номер); `SidecarText` — BOM,
  переводы строк.
- **Запись в чужой формат — узкий путь**: только поля оценки; в
  существующем — правка одной строки, остальные байты как есть (в `.pp3`
  вся проявка); XMP — строковая хирургия, не `XDocument` (round-trip
  переписал бы атрибуты, префиксы, `<?xpacket?>` с padding'ом); нет свойства
  — добавляется атрибутом в `rdf:Description` **только** при объявленном
  `xmp:`, иначе `NotSupportedException`; только `ReplaceAtomic` (temp →
  `File.Replace`); BOM и `\r\n` / `\n` сохраняются; прежнее значение в
  `SidecarRatingAction`.
- **`CreateRatingSidecar`** — единственное создание файла, которого не
  называли: подтверждение (спрашивает `MainViewModel`), лог,
  `SystemPathGuard`, `SidecarCreatedAction` — undo **удаляет** файл;
  существующий — `InvalidOperationException`; снятие оценки не создаёт.
- **`.xmp` по умолчанию** — выбор побочного эффекта: RawTherapee применяет
  профиль по умолчанию только без сайдкара, `.pp3` с `Rank=3` меняет
  проявку; `.xmp` не влияет, читается с 5.7, синхронизируется с 5.11.
  `AppSettings.RawRatingFormat`, при `.pp3` предупреждение в диалоге.
- **`.meta` только читается** — Unity владеет им, перезапись отвяжет ассет.

## Галерея и оценки

### Запись оценки не пересобирает папку

Правило (CLAUDE.md). Раньше клик по звезде → `Refresh()`: строки
пересоздавались, выделение и сортировка уезжали.

```
MainViewModel.ApplyRating(строки, поле, значение)
   ├ делит на «сайдкар есть / нет», спрашивает про вторую группу один раз
   ├ CompanionMetadataService.ApplyRatingToMany → один CompositeAction
   └ ApplyRatingResults
       ├ SearchController.Replace: состав видимого тот же → ItemsChanged (эти строки);
       │                            строка выпала из фильтра → полный проход
       └ ReplaceRows: Entries[i] = новая, выделение назад
```

- **Выделение.** `record` не правится на месте — замена, список выкидывает
  объект из `SelectedItems`. `ReconcileEntries` оборачивает **любую**
  пересборку `Entries` (точечную и `SyncEntries`): выделение по путям до и
  после; `_rowsReplacing` глушит `SelectedEntry` / `SelectedEntries` на
  время (иначе три замены проводили панель просмотра по трём чужим фото).
  Возврат — `SelectionRefreshRequested`: без прокрутки и фокуса (в отличие
  от `SelectionRestoreRequested`). Порядок: сначала «главный»
  (`SelectedItem` схлопывает множественное), потом набор.
- **Сторож** — `DirectoryChange` + `FolderChanges`: изменился состав →
  `Refresh()`; изменилось содержимое известных строк → перечитать их;
  неизвестный файл → `Refresh()`. Прежнее глушение по времени **теряло**
  настоящие изменения.
- **Панель просмотра** — `SetPrimary` сравнивает путь + размер + mtime:
  та же строка = перечитать спутников, не декодировать RAW.
- **Служебные файлы.** `ReplaceAtomic` пишет `<файл>.wander-tmp`,
  `File.Replace` создаёт **свой** бэкап `<файл>~RF<hex>.TMP` (не описан у
  API; найден логом сторожа) — оба в `TransientFiles`. Переименование
  **из** нашего служебного — запись содержимого, не состав
  (`WindowsDirectoryWatcher.OnRenamed`).
- Порядок не меняется даже при сортировке по оценке — новый приезжает со
  следующим листингом.
- **`Ctrl+Z`** — `IUndoableAction.MetadataTargets`: непустой = состав не
  изменился, `UndoLast` → `RefreshMetadataRowsAsync`; `CompositeAction`
  отдаёт объединение только если **все** члены — метаданные.

### Проход по оценкам — второй

```
RefreshFolderAsync (листинг + свёртка, пул)
   ├→ AutoSelectViewMode() — только при входе, не на F5
   ├→ _search.SetSource() — строки на экране
   └→ StartRatingPass() → RatedListing.WithRatings() (пул, отмена) → SetSource() с Rating
```

Папка из пятисот RAW — пятьсот чтений, папка должна появиться раньше.
Трогает только строки с `Companions`; без сайдкаров возвращает **тот же
список по ссылке** — UI-проход пропускается. Живёт в `Listing/`: пройти
строки и решить, какие заменить, — вопрос про листинг; как читать —
делегат `ReadRatingFor`. Отмена по эпохе. `SyncEntries` сравнивает `SameRow`
с `Rating`. **Сортировка по оценке** — `SortKey.Rating` в
`EntryComparers`, первый проход по ней при пустых оценках (= по имени),
второй пересортировывает через ту же `EntryComparers.Sort`, что и
`SystemIOFileSystem.Enumerate` (компаратор имён ординальный — TECHDEBT).
Неоценённое = 0, не ниже нуля — папка не переставляется, пока null'ы
становятся нулями.

### Фильтр — внутри `SearchController`

`RatingFilter` там же, где фильтр по имени: проекция одна, два фильтра —
гонка. `Reset()` снимает оба. **Набор, а не порог** — два битовых набора
(оценки, метки): клик — элемент и выше, `Ctrl` + клик — один. Ранг 0 —
«без оценки», единственный, который клик берёт в одиночку; перечёркнутая
звезда. Горит выбранное (`RatingFilter.HasRank`, `FilterStarConverter`).
`Alt` ничего. Клик разложен (`ReadFilterGesture` / `ClickRankFilter`,
`ClickColorFilter`): харнесс не имеет права трогать клавиатуру. Папки не
отбрасываются.

### Папка со снимками и автовыбор вида

`ImageFolderProbe.IsImageFolder` — чистая функция от листинга и правил
спутников: знаменатель — содержательные файлы (не спутники — правила у
`CompanionResolver`, не бэкапы, не подпапки), иначе папка с `.pp3` у каждого
RAW набирает ровно 50 %. Минимума нет. Расширения — `Icons/ImageFormats`,
один список (раньше два в `PreviewController` расходились).

`_viewMode` (на экране) и `_userViewMode` (выбор человека; в `state.json`).
`SetViewMode` пишет обе и помечает папку в `_manualViewModeFolders`;
`AutoSelectViewMode` только при **входе**: `Gallery` либо `_userViewMode`
(иначе галерея расползается); в помеченной — тот вид, что там выбрали.
Пометки — `SessionState.ManualViewModes` (пары путь → имя режима строкой,
потолок 128, вытеснение старых): не предпочтение, а «где остановился»,
рядом с `LastPath`. Не `desktop.ini` (PLAN H1).

### Фон галереи — палитра

`GalleryBackground` (Light / Grey / Dark) в Core, яркость двух тёмных —
`GalleryGreyLevel` / `GalleryDarkLevel`. `GalleryPalette` (App) из трёх чисел
собирает **весь** набор: фон, подпись, приглушённый, ховер, выделение
активное / неактивное, рамки. Один тип — роли двигаются вместе: тёмный фон
со светлой подписью нечитаем, с проводниковым голубым — лайтбоксы ярче
фото; на тёмном подсветка — `Lift` фона, на светлом — проводниковые
`#CCE8FF` / `#E8E8E8` как есть. `Light` = `SystemColors.WindowColor`, по
умолчанию (тёмный при первом открытии читается как чужая тема). Панель
просмотра берёт фон только под картинкой (`Image`, `Gif`, лупа).

## Окно и его контролы

| Кто | За что |
|---|---|
| `MainWindow` | тулбар, адрес, статус-бар, глобальные хоткеи, сборка меню, исполнение областей и геометрии (решения — `Core/Layout/`) |
| `Views/FolderTreesView` | обе панели папок: клик открывает, шеврон раскрывает, drag узла, правый клик — цель, `Shift` + колесо, коалесированный обход, полоса «+» |
| `Views/FileListView` | все виды и общие жесты: выделение, рамка, взведение drag, двойной клик, rename на месте, type-ahead, `Ctrl` + колесо, меню |
| `Views/PreviewPane` | панель просмотра, зум, транспорт, WebView2 |
| `DragPreview/DropTargetController` | приём drop: папка под курсором, разрешён ли, что сделает, подсветка |
| `DragPreview/OutgoingDrag` | перетаскивание наружу: плашка, курсор, формулировка |

**`MainViewModel` живёт при окне** (корень `Wander.App`, namespace
`Wander.App`), не в `ViewModels/`: она хостит контроллеры, контроллеры берут
базовые типы из `ViewModels/` — был цикл (O9).

**`DropTargetController` решает, но не действует**: отвечает `DropPlan`,
выполняет VM (тем же путём с логом, guard, undo). `Execute` держит обвязку
(отказ, `Handled`, снятие подсветки в `finally`). Один на все поверхности.
Проверки повторяются на самом drop'е, не с последнего `DragOver`
(модификаторы меняются между движением и отпусканием).

Граница окно ↔ список: `DragStartRequested` (жест у того, за что
схватились; drag ведёт `OutgoingDrag`; загорание полосы закладок сообщает
окно), `ContextMenuRequested` (модель — Core, шелл добавляет окно); вниз
`FocusList()`, `FocusRow()`, `ClearSelection()`, `StartRename()`. Окно ↔
панели: `ContextMenuRequested`, `FolderTargeted` (список отдаёт выделение),
`FocusListRequested` (`Esc`); вниз `FocusBookmarks()` / `FocusDrives()` /
`HasBookmarks` / `PaneOf()` / `ShowFocusOutline()` / `RevealAndFocus()`;
`Connect(drops, drag)` — общие `DropTargetController` и `OutgoingDrag`.

### Клавиатурные области

`Tab` переключает **области**: тулбар → адрес → фильтр → закладки → дерево
→ список. Порядок и обход — `Core/Layout/WindowZones` (`WindowZone`, `Order`,
`Ring`, `FolderPane`): кольцевая арифметика и лестница умолчаний — где
прячется ошибка на единицу. Окну — `ZoneOf` по визуальному дереву,
`CycleZone`, `FocusZone`. Не средствами WPF: родной `Tab` идёт по дереву
объявления и внутрь каждого контрола — пришлось бы расставлять
`TabNavigation` / `IsTabStop` по всей разметке. `Tab` **всегда** «следующая
область», и из текстового поля. `FocusZone` возвращает `false` — обход идёт
дальше (свёрнутые закладки, выключенные кнопки). Панель просмотра не в
списке (BACKLOG).

- **`Alt`-сочетания не `KeyBinding`**: в тулбаре настоящий `Menu`, `Alt`
  переводит окно в режим меню раньше маршрутизации. `Alt+←/→/↑`,
  `Alt+Enter`, `Alt+D` — в `MainWindow.OnPreviewKeyDown` (туннель впереди
  режима меню и `InputBindings`); там же `Esc` для адресной области (с
  кнопки-крошки тоже).
- **Фокус на самом списке — тупик, лечится**: `TryEnterList` — первая
  стрелка входит сверху (`↓` / `→`) или снизу, при выделении каретка на
  него; `FocusVisualStyle` у `ListGestures` снят; `TakeKeyboardOnClick` —
  ветки, помечающие нажатие обработанным (лассо, удержание мультивыделения),
  забирают клавиатуру сами.
- **Рамка области** — `BorderBrush` самих контролов (`BorderThickness` 1,
  меняется цвет), `OnZoneFocusChanged` на `GotKeyboardFocus` **окна**.
  `GridSplitter` из обхода убран (WPF делает его фокусируемым) — размер
  панелей только мышью.
- **`Ctrl+1`** — `WindowZones.FolderPane`: раскрыть дерево в **той** панели,
  из которой открыли (`RevealCurrentIn`), повтор переключает; не из дерева —
  `_lastFolderPane`. `Ctrl+Shift+E` — то же без переключения; `Ctrl+2` —
  список.
- **В дереве стрелки не навигируют**: `SelectedItemChanged` летит от мыши,
  клавиатуры и программно; навигирует только мышь (флаг в
  `Tree_PreviewMouseLeftButtonDown`); клавиатура переносит цель операций
  (`TargetTreeNode` = `SelectExternalPath`), список отпускает выделение;
  вход — `Enter` в `Tree_PreviewKeyDown` (иначе `KeyBinding` окна открыл бы
  выделенное в списке). Клик по уже выделенной строке события не рождает —
  `PreviewMouseLeftButtonDown` отрабатывает сам: мышью «открыть» всегда.
- **Зачем контролы**: пока три `ItemsControl` жили в окне, каждый режим —
  правка в четырёх местах. Режим = контейнер и триггер видимости в
  `FileListView`, жесты — стиль `ListGestures`; галерея добавилась
  контейнером, окно узнало двумя строками. Полоса фильтра оценок — там же,
  `Dock="Top"`.

### Цвета — один словарь

`Resources/Palette.xaml` — кисти по тому, **что красят** (поверхности,
линии, текст, строки, контролы, акцент, метки, меню); влит в `App.xaml`
первым; `MenuStyles.xaml` вливает сам (грузится отдельно). Code-behind —
`Resources/Palette.cs`, все поля `static readonly` на одном классе: опечатка
падает громко на первой отрисовке. Тёмная тема — второй набор тех же
значений, работает только если больше ничего нет (`Foreground="#888"` в
вьюхе — светлый угол). Не в словаре намеренно (шапка файла):
`GalleryPalette` (вычисляется), `*.xshd`, `DefaultBackgroundColor` WebView2
(бумага документа), обложка книги в `SystemIconProvider` (битмап в
Platform). Свет 3D-сцены — раздел «Not chrome». Семь градаций серого текста
унаследованы (TECHDEBT).

### Подсветка плитки и что шаблону нельзя

Подсветку рисует **контейнер** (`ListBoxItem`) своим `ControlTemplate` по
property-триггерам: `TileChrome` форма, `TileItem` цвета плиток,
`GalleryItem` из палитры (в сеттерах триггеров, лениво). Отступ ячейки —
`Margin` контейнера (из `TileMetrics` ресурсом от `ApplyTileMetrics`):
контейнер = плитка, а не ячейка, выделенные не сливаются; `Padding` 0.
Раньше `TileHighlight` — `Border` с семью `DataTrigger` через
`RelativeSource` в каждой плитке — вторая по цене вещь (R, 2026-09-02).

Шаблон оплачивается на каждой навигации × видимые плитки. Запреты (измерены,
PLAN R2/R3):
- **Никакого `TextBox`** — редактор один на контрол (`RenameAdorner`),
  подпись `TextBlock x:Name="NameLabel"` — контракт.
- **Никаких `Style.Triggers`** — состояние строки у контейнера, данные —
  конвертер (`TileSecondLineConverter`).
- **Никаких `RelativeSource`** — размеры `DynamicResource` (переписывает
  `ApplyTileMetrics`) или наследование (`FontSize` на `ListBox`).
- **Ничего, что видно у меньшинства, — безусловно**: бейдж оценки — пустой
  `ContentControl`, `DataTemplate.Trigger` подкладывает `Content` (11 → 9 /
  13 визуалов).

Нижняя планка — картинка + подпись, 5 визуалов; продуктовые 6–9 против
18–23. `LAYOUT <вид> container: N visuals` в журнале — регресс виден.

### TileLayout + VirtualizingWrapPanel

WPF не даёт виртуализирующий wrap; `WrapPanel` строит все контейнеры.
Разделено: **`Core/Layout/TileLayout`** — вся арифметика (колонки, позиция
N, высота, диапазон реализации, куда доскроллить), неизменяемое значение с
нуля на проход, `TileLayoutTests`; **`TileMetrics`** — размер ячейки и
содержимого из настроек, `ForTiles` / `ForLargeIcons`, производные
(второй кегль) там же, `TileMetricsTests`; **`App/Controls/VirtualizingWrapPanel`**
— спросить генератор, померить, расставить.

Пять багов, все в арифметике, ни один не ловился в отладчике: (1) колонки
от размера ячейки с прошлого прохода, расстановка по новому — три колонки с
шагом на шесть; (2) `ArrangeOverride` при расхождении вьюпорта просил
measure — цикл; (3) `BringIndexIntoView` дёргал `UpdateLayout()` изнутри
раскладки; (4) **дребезг размера ячейки** — высота 56,59 / 56,00 по
положению прокрутки → экстент ±980 px → предел → сдвиг → высота
(`MaxVerticalOffset` 92207,6 ↔ 93187,0); лечилось подтверждением + полосой
нечувствительности; (5) **размер ячейки — вход, а не выход** (2026-08-26):
панель узнавала размер, меряя реализованный контейнер — кольцо «контент →
геометрия → контейнеры»; ячейки 70×40 (контейнер до значка) и 2:3
(пропорция фото); `CellSizeProbe` залипал на мусоре. Теперь `TileMetrics` из
настроек, VM отдаёт одним значением (`Settings.IconsMetrics` /
`TilesMetrics`), дети меряются **ровно ячейкой**; кругов нет.

Инвариант тестом: `ExtentWidth` не превышает вьюпорт при колонках > 1.
Панель не хранит производного состояния. Харнесс: настоящие `FileListView` и
`MainViewModel`, окно за экраном, подменённый `IAppStateStore`, сторожевой
поток считает проходы (5000 файлов: прыжок в конец 1396 проходов и
продолжал → 5; 300 файлов: 0 в простое, 8–14 на щелчок колеса против 40 и
384).

**Скроллер свой у каждого вида**: плиточные — горизонтальная `Disabled`,
вертикальная `Auto`; `Details` — `Auto` / `Auto`. Автополоса — ловушка
(`ColumnWidth`): `ScrollViewer` меряет во всю ширину, потом на ширину минус
полоса, wrap законно хочет полосу при одной и не хочет при другой →
бесконечная гонка; колонки считаются по ширине с вычтенной полосой, если
содержимое её потребует (54 ячейки — 0 проходов, полосы нет; 60 — полоса,
восемь колонок, 0 проходов).

**Recycling** (`VirtualizationMode.Recycling` + `generator.Recycle`):
`layout.realise` 260–400 мс/с и подвисания 300–450 → 5–10 мс на пачку.
Рамка выделения видит только реализованные (автоскролла нет).

### Вид, которого не видно, не строит ничего

Четыре вида на одной `Entries`; `Reset` пачкает измерение всех панелей,
`Collapsed` предка менеджеру раскладки не указ — каждая навигация
реализовывала папку трижды (`COUNT layout.new: 96 in 3 passes`). Свои
панели: при `owner.IsVisible == false` `MeasureOverride` только перемеряет
существующих детей (грязный ребёнок, которого не мерят, держит очередь
грязной вечно) и сбрасывает маркеры; `IsVisibleChanged` инвалидирует.
`DataGrid`: `FileListView.ApplyViewAttachment` отвязывает `ItemsSource`,
пока не на экране; порядок — сначала отвязать, потом привязать (уходящий
вид сообщает пустое выделение); `SelectedItem` гасится **до** строк двумя
шагами (таблица — `ClearBinding`, плиточные — локальный `null` поверх стиля
`TilePanel`); многовыделение снимается до и ставится обратно. Цена — одна
заминка на смену вида; прокрутка таблицы не переживает.

### Что на экране — читается первым

Шлюз `AsyncIcon._gate` на четыре загрузки. Семафор отдаёт в порядке
обращения = создания контейнеров, не видимости (таблица держит страницу
над и под, дерево — каждый узел). С `AppSettings.VisibleFirstLoading`
(зеркало `AsyncIcon.VisibleFirst`) — `IconLoadGate` с двумя очередями:
значок в окне своего `ScrollViewer` обгоняет; где значок — известно после
раскладки, запрос откладывается до `DispatcherPriority.Loaded`. Без
настройки всё срочное. `FirstScreenWatch`: часы с `RefreshFolderAsync`, вью
после раскладки отдаёт значки реализованных строк в окне, ждёт
`AsyncIcon.Painted`; `abandoned` при уходе; уехавший из дерева выбывает.

## Замеры производительности

`PerfLog.Measure("имя")` суммирует в секундные окна, в лог только > 100 мс
суммарно или > 33 мс за вызов; цена — два таймстампа и словарь под локом.
`PERF layout.realise: 202 ms in 9 calls, worst 38,4 ms`.

| Имя | Что |
|---|---|
| `layout.measure` | `MeasureOverride` плиточной панели, **включает** `layout.realise` |
| `layout.realise` | создание контейнеров: шаблон, привязки, измерение |
| `layout.arrange` | `ArrangeOverride` |
| `icon.decode-ui` | декод миниатюры, только если нет в `IconImageCache` |
| `list.apply` | листинг заезжает в `Entries` |
| `ui.stall` | UI не отвечал (снаружи) |
| `bg.*` | фон: `bg.icon-load`, внутри `bg.thumb-disk` / `-shell` / `-disk-write` |

`bg.` — не UI-поток, законно > 1 с/с; показывает, как долго едут миниатюры.
`Startup: first frame N ms` — `MainWindow.OnFirstFrame` на
`ContentRendered` от старта процесса (`Loaded` на ~900 мс раньше).
`UiStallWatch` — фоновый поток раз в 200 мс просит диспетчер
(`DispatcherPriority.Input`), ждёт > 150 мс → `ui.stall`; тот же heartbeat
закрывает окно `PerfLog`, флашит `PerfCounters` и дёргает `SystemVitals`.

`SystemVitals` (`App/Diagnostics/`) — раз в 5 с и на каждый `ui.stall` одна
строка о процессе:

```
SYS ws=431 private=360 gen=167/155/134 alloc=+45 loh=6 handles=1060 threads=40 cpu=8,0
```

МБ, счётчики `GC.CollectionCount` нарастающим итогом, `alloc` — прирост с
прошлой строки (`GC.GetTotalAllocatedBytes`), `loh` —
`GetGCMemoryInfo().GenerationInfo[3]`, `cpu` — доля одного ядра в %
(`TotalProcessorTime` / стена / `ProcessorCount`). `Process` берётся один
раз (`GetCurrentProcess` открывает хэндл на каждый вызов), `Refresh()`
перед чтением. Одна строка сама по себе не значит ничего; смысл — форма за
сессию: растущий `ws`, невозвращающиеся `handles`, `gen2` на каждую папку.
Это то, ради чего существует сценарий `soak`.

## Отзывчивость: приоритеты

«Всё асинхронно» ≠ «не мешает»: континуации `await` и `BeginInvoke` на
`Normal`, ввод на `Input` — **ниже**; поток результатов заслоняет клики.

- **Результат фона приземляется ниже ввода, в два яруса**: листинг
  (`RefreshFolderAsync` делает `Dispatcher.Yield(Background)` перед
  очисткой и `PublishRows`) на `Background`, миниатюры (Medium / Large) на
  `ContextIdle`. Один ярус — FIFO: старые строки стояли, пока не долетят
  сотни их миниатюр. Устаревшее отбрасывают токен и эпоха.
- **Лёгкие иконки** (Small / Normal: дерево, закладки, таблица) — на
  `Normal`, кешированные синхронно (`AsyncIcon.IsLightweight`).
- **Синхронный путь навигации не трогает диск**: `NavigateTo` не проверяет
  путь; `NavigationSource.Address` проверяется в фоне с гардом «уже ушёл»;
  ретаргет вотчера на пуле по поколению; `state.json` по дебаунсу
  (`_stateSaveTimer` 500 мс) с флашем из `OnClosing`.
- **Очистка при входе — ниже ввода и не всегда**: строки покидаемой папки
  убираются (контекст переключается сразу), но не внутри клика — демонтаж
  на `Background` после отрисовки перехода; листинг уже пришёл — очистка
  пропускается, один своп; медленная — очистка сразу и спиннер. Порядок
  держится очередью `Background`.
- **Остальной диск на пуле**: `TreeNodeViewModel.RefreshChildrenAsync`
  (`F5`, тумблеры; сверка на диспетчере), `PruneMissingAsync`,
  `OpenStartFolderAsync`, открытие файла для панели, размер кэша. Синхронным
  остался `Enumerate` уровня при первом раскрытии (TECHDEBT).
- **Клавиатура дерева коалесируется** (`NavigateFromTree`, `TreeNavBurstMs`
  / `TreeNavSettleMs`); тот же метод гасит навигацию в уже текущий путь —
  эхо `ExpandTo`, которое затирало `ArrivalIntent`.
- **Шевроны оптимистичные**: уровень одним `Enumerate`, `ProbeForChevrons`
  снимает у листьев фоном.
- **Иконки**: `SHGetFileInfo` сериализован (`_shellIconLock` — под
  конкуренцией возвращал пусто для handler-иконок, жертва менялась);
  негативный кэш `_missing` только для миниатюр (у Small / Normal null —
  сбой); `Unloaded` поднимает поколение — загрузка снесённого контейнера
  отступает у шлюза и декодера, вернувшийся (recycling) переспрашивает по
  `Loaded`; ретрай через секунду (панели строят строки раз за сессию);
  провал — `[icon-diag]`, > 1 с — `slow shell load`.

### Таймеры: троттл решения, а не место решения

(O6, категория 5.) Таймер — только разредить события. Обязательно:
**решение отделимо** (метод зовётся напрямую: `RunNow()`, `FlushState()`,
`Finish` флашит); **тик идемпотентен** (`Flush` при чистом `_dirty` выходит,
`DecideWatchTick` без изменений — `Idle`) — потому таймер повторяющийся;
**останавливает себя и не теряет накопленное под занятостью** (`Idle` /
`Hold`; дебаунсы гасят себя первой строкой). Инвентарь 2026-09-01: сторож
500 мс, флаш результатов 200, автозапуск поиска 400, `state.json` 500,
клавиатура дерева 90. Часы воспроизведения (`GifImage`, `_videoTimer`)
тикают, пока показ, гасятся на `Unloaded` / `ResetVideoTransport`.
«Дебаунса панели просмотра» нет — защита поколением через отмену.
Абстракции часов нет и не заводится: таймеры в App, тесты не достают.

## Preview pane

`PreviewController` (App) — конвейер с отменой и спиннером. `PreviewKind`:

| Kind | Чем |
|---|---|
| `Image` | `BitmapImage`, `DownOnly`; RAW — встроенное превью |
| `Gif` | `Controls/GifImage` |
| `Video` | `MediaElement` |
| `Audio` | тот же транспорт, карточка трека; играет `MediaPlayer` |
| `Text` | `TextBox` |
| `Code` | AvalonEdit |
| `Document` | `RichTextBox` (RTF) |
| `Web` | WebView2 — PDF / HTML / MHTML / Markdown / FB2 |
| `Model` | `Viewport3D` — STL / OBJ / glTF / GLB |
| `Folder` | перепись + блок тома на корне |

- `Audio` и `Video` делят `MediaUri` и транспорт (второй — копия автомата).
  Проигрыватель разный обязательно: `MediaElement` работает, пока его
  рисуют; без площади открывает файл и стоит на нуле (200×120 играет, 1×1
  молчит). Выбор по `Kind` — контроллер ставит `Kind` **до** `MediaUri`.
- Фон — `MainViewModel.ContentPalette`, не `Settings.GalleryPalette`
  (затемнение — свойство галереи; таблица светлая — панель тоже). Подписи
  `Foreground` / `Dim` **считаются от фона по контрасту** (фиксированная
  пара на среднем сером давала 2.2:1). Ловушка: путь `DataContext.ContentPalette.…`
  — `RelativeSource` возвращает элемент, без `DataContext.` биндинг молча
  не находит и подпись остаётся чёрной; ловится
  `PresentationTraceSources.DataBindingSource`. Текст, код, документы —
  светлые (страницы).
- Строка оценки **вне** блока спутников: `OfferRating` предлагает и файлу
  без сайдкара.
- **`PreviewRouter`** (Core) — «расширение → `PreviewRoute`», без диска;
  `Route` (каким загрузчиком) ≠ `Kind` (каким контролом): Markdown, FB2, PDF
  — три пути в один WebView2. Таблица — **порядок правил**: `.webp` —
  картинка и многокадровый контейнер, `.mtl` — текст, `.svg` — исходник;
  побеждает первое; `PreviewRouterTests`.
- **Разбор в Core (`Preview/`), отрисовка в App.** `AudioTags` (ID3v2.2 /
  2.3 / 2.4, ID3v1, Vorbis; длительность FLAC из `STREAMINFO`, MP3 по `Xing`
  / `Info` / `VBRI`; кодировка 0 угадывается **по всем полям сразу**);
  `MeshFile` + `Stl` / `Obj` / `GltfReader` (плоские массивы + `MeshPart` с
  индексами и цветом, координаты общие; только `Kd` / `baseColorFactor`;
  нормали не читаются); `Fb2Document` (HTML-фрагмент, потоковый
  `ReadCover`; namespace по локальному имени — конвертеры ошибаются в URI;
  бюджет 400 000 по ходу обхода — книга бывает одной `<section>`; при
  обрыве закрываются теги); `BookCover` (`.fb2`, `.epub`: `container.xml` →
  OPF → манифест, EPUB 3 `cover-image` / EPUB 2 `<meta name="cover">` /
  по имени; `Supports` false для DjVu, CHM, `.doc`); Markdown — свой
  `MarkdownPipeline` (CommonMark без таблиц; `UseAdvancedExtensions` тянет
  iframe и `{#id}`); `EncodingProbe` (BOM → строгий UTF-8 → счёт 1251 / 866
  по регистру: строчные ×3; порог 8 кириллических букв — `ä ö ü` это
  кириллица в 1251; таблицы кодировок в Core, .NET знает только Unicode /
  ASCII / Latin-1); `TextProbe` (8 КБ: BOM, нулевой байт — приговор, доля
  управляющих пропорцией; для Unity-ассетов).
- **`App/Preview/`**: `ImageDecoder` (кэш URI, обложка в размер, встроенное
  превью RAW, поворот по EXIF), `ModelBuilder` + `ModelScene` (Core →
  `MeshGeometry3D`, центр и радиус), `PreviewText` (бюджет, кодировка,
  обрезка, Markdown, HTML-обёртка), `SummaryText` (подпись). Контроллеру —
  конвейер.
- **Ярлык прозрачен**: `.lnk` резолвится `IShortcutService`, рисуется цель;
  `LinkTarget` в футере и «Перейти к оригиналу» → `MainViewModel.RevealPath`
  (`_revealPathAfterListing`, `ApplyPendingReveal`; в той же папке — сразу).
- **`IVolumeInfoProvider`** / `WindowsVolumeInfo` поверх `DriveInfo`: только
  на корне тома; каждое свойство бросает на неготовом — чтение обёрнуто,
  «не готово» = описанный том с нулевой ёмкостью.
- **Подсветка кода**: AvalonEdit по расширению включая `.diff` / `.patch`;
  свои `Highlighting/*.xshd` (`Batch`, `ShaderLab`, `YAML` — ассеты Unity)
  через `HighlightingCatalog.EnsureRegistered()`; битый `.xshd` пропускается.
- **Выбор папки в дереве** — в обход `SelectedEntry` (двусторонне связан с
  `SelectedItem`, элемент не из списка откатывается в `null`):
  `SelectExternalPath` ставит `SelectedEntries` и `Preview.SetPrimary`,
  применяется после листинга.
- Футер: пусто → папка (рекурсивно, async); файл → имя / размер / дата +
  EXIF (`MetadataExtractor`, RAW включая CR2 / CR3 / NEF / ARW / DNG); папка
  → count + size; мульти → агрегат. Под ним — спутники: список, GUID из
  `.meta` с копированием, звёзды.
- **WebView2 изолирован**: `NavigationStarting` — только `file:` / `about:`
  / `data:`, попапы режутся; `WebResourceRequested` режет `http` / `https` /
  `ws` / `wss` / `ftp` (deny-list: рендерер раздаёт обвязку по внутренним
  схемам); побочный эффект — внешние картинки в Markdown не грузятся.
  Скрипты локального `.html` исполняются (TECHDEBT).
- **RAW не декодируется**: `.CR3` в WIC — ~1150 мс на 33 МБ (`DecodePixelWidth`
  и `Thumbnail` не помогают). `RawPreviewExtractor` (Core) достаёт JPEG из
  контейнера — 8–13 мс: ISO-BMFF (`uuid` Canon с `PRVW`) и TIFF (IFD → JPEG:
  CR2, NEF, ARW, DNG); `null` = обычный путь; кандидаты от большего к
  меньшему с проверкой маркера — в DNG / NEF самый большой поток это
  raw-данные (SOF3). Цена: превью-разрешение (Canon 1620×1080); размеры в
  футере из EXIF. JPEG лежит **неповёрнутым**, поворот в IFD0 контейнера
  (6 / 8) — `ImageMetadata.Orientation`, `ApplyOrientation` через
  `TransformedBitmap`, только для RAW.
- **`IgnoreImageCache` — только для файлов**: кэш WPF по URI не замечает
  подмены байтов; у картинки из `MemoryStream` URI нет, и
  `FinalizeCreation` на .NET 10 падает на `null` — `Decode` гасил
  исключение, вызывающий читал «превью нет» и шёл на полный декод: быстрый
  путь RAW был мёртв и выглядел как «думает секунду».
- **Иконки**: `SystemIconProvider` — системные + `.lnk` overlay (включая
  jumbo-композит), миниатюры через `IShellItemImageFactory`; мелкие по
  расширению, миниатюры по пути с FIFO 512. `Medium` / `Large` сначала
  спрашивают `BookCover` (обложка «страницей»: подложка, рамка, тень; ключ
  по пути; 16 / 32 не получают). PDF — первая страница `PdfPageImage`
  (`Windows.Data.Pdf`), **всегда** (половина читалок не регистрирует
  provider); вызов синхронный `.AsTask().GetAwaiter().GetResult()` — все
  вызывающие на фоне, контекст не захватывается. `LinkThumbnailTarget`
  подменяет `.lnk` на цель, стрелку накладывает `DrawLinkOverlay` (шелл
  запекает её в значок, не в миниатюру); ключ по `.lnk` (TECHDEBT).
  `AsyncIcon` перепроверяет актуальность после очереди. **RAW мимо шелла**:
  `RawThumbnail` — тот же `RawPreviewExtractor` + WinRT
  `Windows.Graphics.Imaging` с масштабированием на разжатии, 3 мс против 75;
  ориентация из контейнера; не `System.Drawing` (GDI+ сериализуется);
  шлюз 2 → 4.

### Диалоги — один шов

Каждый модальный вопрос идёт через `Wander.App/Dialogs/IDialogs`:
`Ask(DialogRequest)` (вид `DialogKind`, заголовок, текст, кнопки, значок;
кнопка по умолчанию всегда отменяющая — поэтому не поле), `Prompt`,
`PickFolder`, `CreateConflictResolver()`. Продакшн — `WpfDialogs`
(`MessageBox` поверх активного окна, `PromptDialog`, `OpenFolderDialog`,
`DispatcherConflictResolver(InteractiveConflictResolver)`); харнесс
подставляет `ScriptedDialogs` до постройки вью-модели. Голых
`MessageBox.Show` в коде не осталось, кроме аварийного в `CrashReporter`.

### Smoke-запуск и headless

`App.Headless` — окно за экраном (`Left = -32000`), `ShowActivated = false`,
не в панели задач, геометрия не читается и не пишется в `state.json`,
крах — лог и `Shutdown(1)` вместо диалога. Ставится смоком и харнессом
(`internal set`, `InternalsVisibleTo("Wander.Harness")`). В `App.OnStartup`
флаг только **включается** (`Headless |= IsSmokeRun`): харнесс выставляет
его до конструирования `App` и командной строки не имеет, а присваивание
затирало это — окно выходило на настоящий рабочий стол и забирало фокус
(2026-09-02). `HarnessApp` перепроверяет флаг и отказывается работать при
выключенном; позиция окна пишется в лог строкой `HARNESS window at`.
`Wander.exe
--smoke` = `Headless` + `StartSmokeCountdown` (две секунды на первый
листинг, значки, наблюдателей, `Shutdown(0)`). Координаты — **в
конструкторе** `MainWindow` (`ShowActivated` учитывается до показа).
`check.bat run` зовёт exe напрямую (не `start`) и читает код; ловушки cmd:
`if errorlevel 1` не видит .NET-падения (0xE0434352 отрицательное) — `neq
0`; `exit /b` внутри скобок не доносит код — выход за пределами блока.

### QA-харнесс

`tests/Wander.Harness` — `HarnessApp : App`: свой `OnStartup` после
базового подменяет `ILogger` на `CapturingLogger` и `IDialogs` на
`ScriptedDialogs`, показывает `MainWindow` сам (`InitializeComponent` не
зовётся — BAML ищется в сборке наследника; словари ресурсов вливаются
руками) и стартует `ScenarioRunner` на `ApplicationIdle`. Данные — через
`WANDER_DATA_DIR` в папку прогона. Шаги, профили песочницы, генераторы
CR3 / DNG — QA.md.

**Лог берётся у источника, а не у обёртки.** `CapturingLogger` подписан на
событие `FileLogger.Written`, а не запоминает то, что прошло через него
самого. Причина конкретная: сервисы, которые строит
`PlatformBootstrapper`, получают логгер в конструкторе и больше никогда его
не ищут — `FileOperationService`, шелл, сторож папки. Логгер,
зарегистрированный поверх после `base.OnStartup`, их строк не видит, и
`assert-log noErrors` был утверждением про половину приложения: `ERROR
Delete failed` лежал в файле, а прогон отчитывался «ошибок нет»
(2026-09-02). Событие поднимается под замком записи — подписчик обязан не
логировать и не блокировать.

`state.json` прошлой версии кладётся в data-dir **до** старта `App` (поле
сценария `"state"`, `Program.SeedState`): это не профиль песочницы, потому
что читается раньше, чем любой профиль мог бы отработать. Файла нет —
прогон падает сразу, а не проходит молча.

## Состояние и логи

Корень — `AppPaths.DataRoot` (Core, `Persistence/`): `--data-dir <путь>` →
`--portable` (`data` рядом с exe, `Environment.ProcessPath`) →
`WANDER_DATA_DIR` → `%LOCALAPPDATA%\Wander`; `AppPaths.Resolve(args)` в
`App.OnStartup` до логгера, `Override` для харнесса и тестов. `LOCALAPPDATA`
из среды не читается — рантайм берёт папку у оболочки. Пять потребителей:
`FileLogger`, `JsonAppStateStore`, `ThumbnailDiskCache` (бутстраппер),
`CrashReporter`, `PreviewPane` (WebView2). Источник корня — в заголовке
сессии (`Data root: … (arg|portable|env|override|default)`).

**`state.json`** (`JsonAppStateStore`, record `AppState`):
- `Session` — `LastPath` (`NavigationStop?`), `ExpandedPaths` (только
  **видимо** раскрытые: `CollectExpandedRecursive` останавливается на
  свёрнутом; флаги внутри ветки не гасятся при сворачивании, иначе
  восстановление раскроет свёрнутого родителя), `ViewMode`,
  `IsPreviewVisible`, `PreviewWidth`, `IsBookmarksExpanded`,
  `RecentPaths`, `ManualViewModes`, `BookmarksHeight`.
- `Favorites` — закладки в порядке пользователя (`MoveBookmark`);
  стандартные не здесь; пропавший путь не выбрасывается (`IsMissing`).
- `Window` — `WindowGeometry`; обратно через `WindowPlacement`
  (`Core/Layout/`): размер < 320×240 отбрасывается, позиция прижимается к
  виртуальному экрану с полосой заголовка.
- `Settings` — `AppSettings`: `RestoreLastFolder`, `ShowHidden`, `ShowSystem`,
  `ConfirmRecycle`, сортировка, метрики видов, чекбоксы закладок,
  `ShowDebugMenu`, `VisibleFirstLoading`, галерея, контекстное меню
  (`ShellExtensionsEnabled`, `BlockedShellExtensions`,
  `KnownShellExtensions` — подрезается при сохранении,
  `HiddenContextMenuItems`).

Миграционного слоя **нет**: `Load` ловит исключение → `new AppState()`
(до 1.0 схема ломается).

**`logs\session-*.log`** — `FileLogger`: открытие папки, операции, конфликты,
ошибки; в тестах `NullLogger`. Ротации нет (TECHDEBT).

**`thumbs\*.png`** — `ThumbnailDiskCache` (Platform): имя SHA-256 от «путь +
mtime + размер» (изменившийся файл — другое имя, инвалидации не нужно);
запись во временный + `File.Move(overwrite)` (два окна не оставят половину
PNG); ошибки диска глотаются; подрезка по времени обращения раз в 64 записи
и при уменьшении лимита, до 80 % бюджета, всегда в фоне; лимиты через
`IIconProvider.ConfigureCache(ThumbnailCacheOptions)`.

**`crashes\*.zip`** — `CrashReporter`; `App.HookCrashLogging`:
`DispatcherUnhandledException` (лог + репорт, `Handled = true`),
`AppDomain.UnhandledException` (флаш), `TaskScheduler.UnobservedTaskException`.
Репорт — пре-заполненный GitHub issue + локальный zip; **ничего не уходит
без действия пользователя**.

## Строки интерфейса

`Resources/Strings.resx` (встроенный ресурс), `Resources/Strings.cs` — одна
строка на ключ поверх `ResourceManager`, XAML — `{x:Static res:Strings.Key}`;
ненайденный ключ возвращает себя. Класс руками, не `MSBuild:Compile`:
markup-компилятор WPF собирает XAML во временном проекте (`*_wpftmp.csproj`),
куда designer-файл из `obj/` не попадает. Второй язык —
`Strings.<culture>.resx` (BACKLOG). **Граница слоёв**: Core отдаёт
пользователю подписи меню (`ContextMenuCatalog`) и причину отказа drop'а
(`PathSafety.FormatReason`) через `ITextSource` (`AppTextSource` в App);
Core хранит ключи. Без источника `Text.Get` возвращает ключ — режим тестов
(`ContextMenuCatalogTests`); `FormatReason` принимает `ITextSource?`
параметром.

## Тесты

xUnit, `tests/Wander.Core.Tests`, **только Core**; UI и Platform — smoke.
Если тест не пишется — логика не в том слое.

| Фейк | Что |
|---|---|
| `FakeFileSystem` | `Directories` (`HashSet`), `Files` (`Dictionary<string, byte[]>`), `CallLog`; `RenameFailures` роняет путь — для откатов |
| `FakeConflictResolver` | `batchOverride` на батч, `perItem`-очередь; `StartBatchCalls` / `ResolveCalls` |
| `FakeRecycleBin` | поверх `FakeFileSystem`, `Send` / `Restore`, `CallLog` |

`CallLog` — «сходили ровно туда и ровно столько раз».

Правила: **локатор — не канал доставки фейков** (конструктором; xUnit
параллелен, локатор один на процесс; регистрирует и `Reset()` только
`ServiceLocatorTests`, и только `IFileSystem`; исключение — `ITextSource`
не регистрирует никто, `TextFallbackTests`); пути case-insensitive; никакого
реального I/O и времени (`NullLogger`); никаких гонок как утверждения
(детерминированная синхронизация; тест, проходящий под нагрузкой, —
сломан); новая абстракция в Core → фейк рядом.

## Осознанные границы

Только Windows (Core платформонезависим по дисциплине). Нет DI и
MVVM-фреймворка. Нет телеметрии, аналитики, сети. `PublishTrimmed` не
включать. Только `.lnk` (symlinks / junctions не создаются и не
разрешаются; обход может зациклиться). Long paths, UNC-таймауты, FAT32 —
сырые исключения (BACKLOG). Undo не персистится. Тесты только Core.

## Как добавлять новое

1. Платформенная возможность — интерфейс в Core → реализация в Platform →
   регистрация в `PlatformBootstrapper`.
2. Операция меняет файлы — `SystemPathGuard`, лог, `IUndoableAction`,
   подтверждение с Cancel по умолчанию, если деструктивна.
3. Логика распухла в `MainViewModel` — контроллер; без WPF — **в Core** под
   тесты (так появились `BatchExecutor`, `ClipboardController`,
   `SearchController`, `SelectionController`, `PreviewController`,
   `NavigationController`, `FolderSession`).
4. Новый сайдкар — строка в `CompanionResolver.Default`; разбор содержимого
   (как `Pp3Sidecar`) — только если есть что показать в футере.
5. Перед коммитом — `tools\check.bat` (`run` — со smoke, `format` — пишет).
