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

**Platform без WPF** (2026-09-16, после того как кодировщик картинок
оказался в App). Platform.Windows не ссылается на `PresentationCore` /
`PresentationFramework` и не будет: WPF — отвинчиваемый слой, а
платформенная возможность, реализованная на нём, отвинтилась бы вместе с
ним. Куда что кладётся:

| Что делает код | Где живёт | На чём |
|---|---|---|
| Файл, процесс, реестр, шелл, кодирование в файл, метаданные | Platform | Win32, COM, **WinRT** (`Windows.Graphics.Imaging`, `Windows.Data.Pdf`), `System.Drawing`, MetadataExtractor |
| Картинка для экрана (`BitmapSource`), окно, диалог, буфер как объект WPF | App | WPF |
| Реализация интерфейса Core в App | только когда реализация про экран: `WpfDialogs`, `AppTextSource` | — |

Проверка при ревью: если реализация интерфейса Core в App не рисует и
не спрашивает пользователя — ей место в Platform, и отсутствие API там
значит искать не-WPF API, а не переезжать в App. Прецеденты: `RawThumbnail`
и `PdfPageImage` (WinRT вместо WPF-декодера), `ImageConvertAction`
(перенесён из App на `Windows.Graphics.Imaging`; тот же WIC, что у WPF, без
`PresentationCore`). `Preview/ImageDecoder` остаётся в App правомерно: его
выход — `BitmapImage` для контрола.

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
│   │                   BusyGate (HeldPaths), IFileBusyProbe, FileInUse, SharedRead,
│   │                   FileSystemEntry, EntryKind, EntryComparers, SortKey,
│   │                   SidecarRating + ColorLabels, UndoableActions,
│   │                   FolderStatistics, IVolumeInfoProvider, TransientFiles,
│   │                   BatchGroup, ConflictVerdict, ConflictBatch, ConflictPair,
│   │                   MergeScanner, FileContentComparer
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
│   ├── Operations/     OperationTracker, OperationVerbs, TransferRate,
│   │                   PathClaims, BusyWait
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
│   ├── Undo/           UndoService, IUndoableAction, UndoOutcome
│   └── ServiceLocator.cs
│
├── Wander.Platform.Windows/
│   ├── Diagnostics/    RestartManagerLockInspector
│   ├── FileSystem/     SystemIOFileSystem, ShellRecycleBin, WindowsKnownFolders,
│   │                   WindowsClipboard, WindowsDirectoryWatcher,
│   │                   WindowsFileBusyProbe
│   ├── Icons/          SystemIconProvider, MetadataExtractorImageReader
│   ├── Logging/        FileLogger
│   ├── Persistence/    JsonAppStateStore
│   ├── Search/         FilterTextExtractor, NativeFilter
│   ├── Shell/          ShellLauncher, ShellShortcutService, WindowsShellNamespace,
│   │                   ShellContextMenu, ShellContextMenuInterop, ShellMenuIcons
│   └── PlatformBootstrapper.cs
│
└── Wander.App/
    ├── Conflict/       ConflictWindow (+ ConflictWindowViewModel,
    │                   ConflictRowViewModel), DispatcherConflictResolver,
    │                   InteractiveConflictResolver
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
    │                   NumberFormat, TimeFormat, DurationFormat,
    │                   DispatcherExtensions
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
date   : 2026-09-21
commit : 10d70bf

-- projects --
Wander.App -> Wander.Core   (59 files)
Wander.App -> Wander.Platform.Windows   (1 files)
Wander.Core.Tests -> Wander.Core   (106 files)
Wander.Harness -> Wander.App   (4 files)
Wander.Harness -> Wander.Core   (6 files)
Wander.Harness -> Wander.Platform.Windows   (3 files)
Wander.Platform.Windows -> Wander.Core   (31 files)

-- Wander.Core: folder -> folder --
  Actions        -> FileSystem     (5 files)
  Actions        -> Icons          (2 files)
  Actions        -> Localization   (3 files)
  Actions        -> Logging        (1 files)
  Actions        -> Operations     (1 files)
  Actions        -> Preview        (1 files)
  Actions        -> Undo           (1 files)
  Companions     -> FileSystem     (5 files)
  Companions     -> Icons          (1 files)
  Companions     -> Logging        (1 files)
  Companions     -> Undo           (1 files)
  Diagnostics    -> Logging        (2 files)
  FileSystem     -> Diagnostics    (2 files)
  FileSystem     -> Localization   (2 files)
  FileSystem     -> Logging        (3 files)
  FileSystem     -> Operations     (3 files)
  FileSystem     -> Undo           (3 files)
  Listing        -> Companions     (2 files)
  Listing        -> FileSystem     (6 files)
  Listing        -> Icons          (1 files)
  Listing        -> Search         (1 files)
  Menu           -> Actions        (3 files)
  Menu           -> FileSystem     (2 files)
  Menu           -> Localization   (3 files)
  Menu           -> Persistence    (1 files)
  Menu           -> Rename         (2 files)
  Menu           -> Shell          (2 files)
  Navigation     -> FileSystem     (2 files)
  Persistence    -> Actions        (1 files)
  Persistence    -> Companions     (1 files)
  Persistence    -> FileSystem     (1 files)
  Persistence    -> Navigation     (1 files)
  Persistence    -> Rename         (1 files)
  Preview        -> FileSystem     (6 files)
  Preview        -> Icons          (1 files)
  Rename         -> Companions     (1 files)
  Rename         -> FileSystem     (2 files)
  Search         -> FileSystem     (5 files)
  Search         -> Logging        (1 files)
  Search         -> Preview        (1 files)
  Shell          -> FileSystem     (3 files)
  Shell          -> Localization   (1 files)
  Shell          -> Logging        (2 files)
  Shell          -> Operations     (1 files)
  Shell          -> Persistence    (2 files)
  Shell          -> Undo           (1 files)
  Undo           -> Operations     (1 files)

-- Wander.Core: levels --
  0: (root), Icons, Layout, Localization, Logging, Operations
  1: Diagnostics, Undo
  2: FileSystem
  3: Companions, Navigation, Preview
  4: Actions, Rename, Search
  5: Listing, Persistence
  6: Shell
  7: Menu

-- Wander.Platform.Windows: folder -> folder --
  (root)         -> Diagnostics    (1 files)
  (root)         -> FileSystem     (1 files)
  (root)         -> Icons          (1 files)
  (root)         -> Imaging        (1 files)
  (root)         -> Logging        (1 files)
  (root)         -> Persistence    (1 files)
  (root)         -> Search         (1 files)
  (root)         -> Shell          (1 files)
  FileSystem     -> Shell          (1 files)

-- Wander.Platform.Windows: levels --
  0: Diagnostics, Icons, Imaging, Logging, Persistence, Search, Shell
  1: FileSystem
  2: (root)

-- Wander.App: folder -> folder --
  (root)         -> Controllers    (2 files)
  (root)         -> Controls       (1 files)
  (root)         -> Diagnostics    (1 files)
  (root)         -> Dialogs        (3 files)
  (root)         -> DragPreview    (1 files)
  (root)         -> Menu           (1 files)
  (root)         -> Resources      (4 files)
  (root)         -> Util           (3 files)
  (root)         -> ViewModels     (2 files)
  (root)         -> Views          (2 files)
  Conflict       -> Resources      (2 files)
  Conflict       -> Util           (2 files)
  Conflict       -> ViewModels     (2 files)
  Controllers    -> Converters     (1 files)
  Controllers    -> Preview        (1 files)
  Controllers    -> Resources      (6 files)
  Controllers    -> Util           (1 files)
  Controllers    -> ViewModels     (7 files)
  Controls       -> Converters     (1 files)
  Controls       -> Diagnostics    (1 files)
  Controls       -> Resources      (2 files)
  Controls       -> ViewModels     (1 files)
  Converters     -> Resources      (1 files)
  Converters     -> Util           (1 files)
  Converters     -> ViewModels     (1 files)
  Diagnostics    -> Resources      (1 files)
  Dialogs        -> Conflict       (1 files)
  Dialogs        -> Resources      (1 files)
  DragPreview    -> Converters     (1 files)
  DragPreview    -> Resources      (3 files)
  DragPreview    -> Util           (1 files)
  DragPreview    -> ViewModels     (1 files)
  Preview        -> Resources      (2 files)
  Preview        -> Util           (2 files)
  Util           -> Resources      (1 files)
  ViewModels     -> Resources      (9 files)
  ViewModels     -> Util           (1 files)
  Views          -> Controllers    (1 files)
  Views          -> Controls       (2 files)
  Views          -> Converters     (1 files)
  Views          -> Dialogs        (3 files)
  Views          -> DragPreview    (1 files)
  Views          -> Highlighting   (1 files)
  Views          -> Resources      (6 files)
  Views          -> Util           (3 files)
  Views          -> ViewModels     (6 files)

-- Wander.App: levels --
  0: Highlighting, Menu, Resources
  1: Diagnostics, Util
  2: Preview, ViewModels
  3: Conflict, Converters
  4: Controllers, Controls, Dialogs, DragPreview
  5: Views
  6: (root)

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
`CopyMany` / `MoveMany` для тестов, продакшн — async на пуле с прогрессом
и `CancellationToken`. Прогресс и отмена — по байтам внутри файла (ниже);
отмена посреди элемента даёт `BatchItemStatus.Cancelled`, а не `Failed`, и
кладёт частично скопированное в undo. Типы результатов (`BatchItemResult`,
`DeleteResult`) — на уровне namespace.

- **Undo.** Один LIFO-стек. Move ↔ Move обратно, Rename ↔ Rename, Delete →
  Restore из корзины, Create → Delete в корзину. Безвозвратное удаление не
  откатывается и затирает стек. `BeginOperation()` — busy-счётчик,
  `CanUndo == false` в полёте (`Ctrl+Z` игнорируется, как в Explorer).
  Стек под одним локом; `Changed` поднимается вне лока и может прийти с
  фонового потока — подписчик маршалит сам. Не переживает рестарт.
- **Откат — операция** (2026-09-21). `UndoService.UndoAsync`: с пула, в
  `OperationTracker` (`OperationVerbs.Undo`), под тем же busy-счётчиком.
  Связка разматывается по `IUndoableAction.Steps`, последний шаг первым.
  Отмена — несделанные шаги возвращаются в стек под тем же описанием
  (`WithSteps`), следующий `Ctrl+Z` продолжает. Сбой шага — в
  `UndoOutcome.Failures`, остальные шаги всё равно откатываются, сбойный в
  стек не возвращается: иначе элемент, которого уже нет в корзине, запирает
  всё под собой. `UndoOutcome.Undone` — то, что вернулось на деле, по нему
  `UndoLast` ведёт выделение и `FollowRelocated`.
- **Окно операции — через 400 мс работы, без фокуса** (2026-09-21,
  `RunWithProgressDialogAsync`, у всех операций). Короткое удаление, откат
  переименования, мгновенный отказ окна не показывают вовсе — раньше оно
  мелькало и забирало фокус; строка состояния показывает операцию с первого
  мгновения. Появляется без активации (`ShowActivated = false`): человек к
  этому времени занят другим. Модальный вопрос (окно совпадений задаётся
  изнутри операции) пережидается — `ComponentDispatcher.IsThreadModal`.
- **Корзина — на `IFileOperation`, в обе стороны** (`ShellRecycleBin`,
  стенд 2026-09-21). Причины, все измерены:
  - `SHFileOperation` с `FOF_ALLOWUNDO` файл на пути длиннее `MAX_PATH`
    удалял **безвозвратно и молча** (rc 0, в корзине пусто). Движок же
    говорит заранее: в `PreDeleteItem` нет `TSF_DELETE_RECYCLE_IF_POSSIBLE` —
    значит, уничтожит; приёмник отвечает `E_FAIL`, файл остаётся,
    наверх — `RecycleUnavailableException`. Короткая папка с длинным путём
    внутри проверку движка проходит, и оболочка задаёт свой вопрос поверх
    `FOF_NO_UI` («Да» по умолчанию) — поэтому до движка стоит свой обход
    (`TooLongForBin`, порог 259; граница не измерена, документная).
  - `PostDeleteItem` отдаёт созданный элемент корзины; его id-list
    (base64) — в `RecycleHandle.BinItemId`. `Restore` — один `MoveItem`,
    10–20 мс, без обхода корзины (было 0,35 с на элемент при 1433), без
    глагола по локализованному имени, без разбора даты. Строка панели
    корзины несёт `BinFilePath` (`$R…`) — поиск обходом до совпадения,
    ~80 мс. Путь, удалённый дважды, возвращается правильной версией.
  - Занятое имя при возврате: движок под `FOF_NO_UI` ответил бы «заменить»
    сам, поэтому имя решается до него — `UniqueNames`;
    `FOF_RENAMEONCOLLISION` — сетка на гонку. Пропавшая папка создаётся.
  - `MoveItem` из корзины оставляет `$I…` без пары; корзина его не
    показывает, убираем сами (`RemoveIndexFile`).
  - Каждый прогон — на своём STA-потоке (`OwnApartment`, без очереди
    сообщений — движку она не нужна), все RCW освобождаются до выхода.
  - **Удаление — пачкой, один прогон движка** (`IRecycleBin.SendMany`,
    `BatchExecutor.Recycle`; у интерфейса реализация по умолчанию — цикл
    `Send`, ею живут фейки): 3 мс на файл против 11 (1000 файлов — 3,1 с;
    300 через боевой класс — 0,94 с). Результат, `BinItemId` и колбэк
    прогресса — на элемент, колбэки идут в порядке очереди. Без
    `FOFX_EARLYFAILURE`: занятый файл (`0x80270027`) и папка с занятым
    внутри (`0x80270028`, остаётся **целой**) пропускаются за миллисекунды,
    остальные проходят; с флагом движок думал над тем же 1021 мс и
    останавливался. Ошибка из `PreDeleteItem` останавливает прогон для
    всех следующих элементов, нетронутых: это отмена (`E_ABORT` по токену)
    и цена отказа (не берёт корзина — `E_FAIL`), после отказа остаток
    просто прогоняется заново. Занятый элемент возвращается отказом
    `FileInUse` без ожидания — ждёт операция, в Core (ниже). Возврат —
    прогон на элемент, ~15 мс (TECHDEBT).
  - Файл, открытый с `FileShare.Delete`, уходит в корзину и возвращается
    прямо под читателем — основание решения AF (а). Дешёвая проба
    занятости — `WindowsFileBusyProbe` (`CreateFile(DELETE)`, пара мс).
- **Занятые пути** (AF, 2026-09-21). Три слоя, снизу вверх:
  - `PathClaims` (Operations) — кто в Wander над чем работает. Заявка —
    **источник** операции, не каждый файл под ним; вид `UserOperation`
    (владелец — ключ `OperationVerbs`) или `Background` (`ClaimOwners`:
    shell-миниатюра, системный фильтр поиска — то, что прервать нельзя).
    `Covering(path, except)` — заявки на сам путь, на папку выше и на всё
    внутри; `IsClaimed(path, kind)` — вопрос значка, 0,5 мкс; `Changed`
    поднимается только для операций пользователя (фоновые заявки идут
    сотнями при прокрутке, на экране их не видно). Замер: 1,5 мкс на запрос
    при 50 заявках, 28 при 5000, 130 при 50 000 (скан «что внутри»
    линеен); контрольная строка на операцию — `Claims: N lookups, slowest
    X ms, table Y paths`, `WARN` от 5 мс.
  - `BusyWait` (Operations) — чистая политика ожидания: взгляд каждые
    0,2 с, бюджет 2 с **на операцию**; сто файлов, которые не отпустят,
    стоят две секунды, не двести.
  - `HeldPaths` / `BusyGate` (FileSystem; один `BusyGate` на операцию).
    `Check(path)` перед элементом: заявка чужой операции пользователя —
    `ClaimedByOperationException`, элемент не трогается; фоновым —
    `Yield`. `WaitForFile` — файл, через `IFileBusyProbe`
    (`WindowsFileBusyProbe`, `CreateFile(DELETE)`, пара мс); `Retry` —
    папка, у которой пробы нет: сама операция повторяется, пока отвечает
    `FileInUse` — только в пределах тома: между томами перенос папки — это
    копия и удаление, и удаление, упёршееся в занятый, уже снесло
    остальное; `RetryMany` — пачка корзины: занятые возвращаются
    отказом и отправляются снова все вместе каждый шаг ожидания. Держателя
    называет один раз, на первом взгляде (`IFileLockInspector` либо своя
    заявка — «Wander: миниатюра»), в пачке — первых трёх: каждое имя —
    сессия Restart Manager; итог — `BusyReport` в `DeleteResult` /
    `BatchItemResult`, из него строка статуса. Ждут перенос,
    переименование и корзина; безвозвратное удаление — только заявки
    (TECHDEBT).
  - Чтобы ждать приходилось редко: свои читатели открывают файл через
    `SharedRead` (`FileShare.ReadWrite | Delete` — файл уходит в корзину
    прямо под читателем, стенд). Кроме чтения под запись обратно — оценка в
    сайдкар (`IFileSystem.ReadAllBytesForUpdate`, без общего доступа на
    запись): `.pp3`, который RawTherapee как раз сохраняет, иначе читался
    недописанным и так же записывался. Панель просмотра отпускает файл явно,
    до операции (`PreviewController.Release` → `ContentReleased` → WebView2 на
    `about:blank`; `Restore` в `finally` показывает снова, если файл остался,
    выделение то же и его не держит другая идущая операция). Откат панель не
    отпускает (TECHDEBT).
  - Значок «в работе» рисует сам `AsyncIcon` (`ShowsWork`, `OnRender`):
    отметка, которой у большинства ячеек нет, не должна стоить каждой
    ячейке визуала; один статический обработчик `PathClaims.Changed`,
    пачка изменений — один проход по живым иконкам, `Entries` не трогается.
- **Прогресс — в двух счётчиках сразу** (2026-09-04, блок 2). Элементы —
  то, что выделил человек; байты — то, что двигает диск, и без них копия
  одного файла на 5 ГБ держит бар на нуле.
  `OperationTracker.Begin(verb, total, totalBytes, bytesAreWork)` →
  `IOperationHandle` (диспозить всегда) с `Advance` / `AdvanceBytes`
  (можно отрицательной дельтой) / `SetCurrentPath` / `SetTotalBytes`;
  `Snapshot()` — иммутабельный срез с `Id`, `Percent` (по байтам, иначе по
  элементам) и `StartedAtUtc`. `verb` — **ключ ресурса**
  (`OperationVerbs`), не слово: Core своей таблицы строк не имеет.
  `Changed` приходит с фона и троттлится до 10 раз в секунду, последнее
  состояние довозит одноразовый таймер; появление и завершение операции
  идут без троттла — на них открываются и закрываются окна.
  - Откуда байты: `BatchExecutor` взвешивает источники до старта
    (`FolderStatistics.Collect`, глубина 64) и держит вес по пути, чтобы не
    обходить папку дважды; сам перенос идёт через
    `IFileSystem.CopyFile/CopyDirectory/MoveEntry` с `IProgress<long>` и
    токеном, в `SystemIOFileSystem` это `CopyFileEx` с
    `LPPROGRESS_ROUTINE` (та же семантика, что `File.Copy`, плюс отмена
    внутри файла — недописанный файл система убирает сама). Разницу между
    оценкой и тем, что отчитала копия, `ApplyOne` сводит по каждому
    элементу, поэтому счётчик приходит ровно туда, куда обещал план.
  - Извлечение байтов не знает: `IShellNamespace.CopyOut` отдаёт
    `IProgress<CopyOutWork>` от `IFileOperationProgressSink.UpdateProgress`
    — это «работа» движка, не мегабайты, поэтому операция помечена
    `BytesAreWork` и показывается только процентом.
- **Окно операции и статус-бар.** `MainViewModel.RunWithProgressDialogAsync`
  открывает **немодальный** `ProgressDialog` и ждёт задачу, а не окно:
  список остаётся живым. Окно узнаёт свою операцию по токену: хендл
  рождается несколькими слоями ниже, `OperationTracker.Begin` получает
  токен операции и кладёт его в снимок, окно сравнивает со своим. Водяной
  знак по `Id` («первая операция новее моей отметки») был первым вариантом
  и не пережил ревью: извлечение регистрирует операцию только после
  диалога о совпадениях, и вторая операция, запущенная в этот промежуток,
  доставалась чужому окну. Закрыть окно нельзя, пока
  операция идёт (`Closing` отменяется): X, `Alt+F4` и `Esc` = «Свернуть»,
  дальше окно живёт в статус-баре и возвращается кнопкой «Показать»;
  по завершении его закрывает сам `RunWithProgressDialogAsync`
  (`ProgressDialog.Finish` в `finally`), **до** возврата к вызывающему.
  Раньше окно закрывало себя продолжением задачи — отдельной операцией
  диспетчера после продолжения вызывающего, и вопрос об итоге («файл
  занят, повторить?») заставал окно открытым и активным, брал его
  владельцем и уничтожался вместе с ним, а приложение оставалось
  выключенным во вложенном цикле невидимого модального окна
  (2026-09-17). Второй замок на то же: `WpfDialogs.ActiveWindow` никогда
  не отдаёт `ProgressDialog` владельцем вопроса — операций может быть
  несколько, и чужое окно закрывается по своему расписанию. Строку и
  всплывающую панель в статус-баре кормит `OperationViewModel` — обновляется на месте, а не
  пересоздаётся (у него внутри `TransferRate`, скользящее среднее за 3 с,
  и кнопки, которые нельзя ронять под курсором).
- **Выход при идущих операциях** (2026-09-17). `MainWindow.OnClosing`
  первым делом: операции есть — вопрос (`DialogKind.ExitWithOperations`,
  Cancel по умолчанию); «да» — `e.Cancel`, `Vm.CancelAllOperations`
  (`RequestCancel` каждому окну), `await OperationTracker.WhenIdleAsync(10 с)`
  (Core, тест: true — список операций пуст, false — таймаут; продолжение
  не на контексте вызывающего, так что фатальный обработчик может
  блокироваться на нём), потом `Close()` снова по флагу «второй проход».
  Таймаут — `IProcessRunner.KillAll`: `WindowsProcessRunner` ведёт
  статический список живых процессов и убивает деревом. `Shutdown`
  (`SessionEnding`, smoke, харнесс) отменить нельзя — `App.IsShuttingDown`
  и `ShutdownWithoutAsking`: отмена на месте без вопроса. Вылет
  (`AppDomain.UnhandledException`, headless-ветка диспетчера) —
  `App.StopOperationsBeforeDying`: `ProgressDialog.CancelAll` (статический
  список окон, только `_cts.Cancel`, без контролов — диспетчер может быть
  мёртв), `KillAll`, `WhenIdleAsync(3 с).Result`. Пункт «Выход» в меню —
  `MainWindow.Close`, не `Shutdown`, иначе вопрос игнорируется. После
  `Hide()` окно отпускает сторож (`IDirectoryWatcher.Watch(null)`) и
  WebView2 обеих панелей (`PreviewPane.ReleaseWebView`).
- **Конфликты.** `IConflictResolver.ResolveAll(ConflictRequest)` — push:
  все коллизии, найденные до первого касания диска, одним вызовом (плюс
  размер батча для заголовка); ответ — `ConflictAnswer` на каждую
  показанную пару, вложенные (внутри сливаемых папок) включительно,
  привязка по пути источника; null / `Cancel` — отмена всего батча, ничего
  не применено; коллизия, возникшая по ходу или не спрошенная (имя внутри
  сливаемой папки, которую резолвер не обошёл), — второй вызов из одного
  элемента. Каждый файл группы — своя коллизия; связывает их только имя:
  `BatchExecutor.ApplyGroup` уводит спутников за переименованным основным
  файлом. `FileConflictInfo` — две записи + `IsMove` + `SourceReachable`
  (false у записи архива); `ConflictVerdict.Of` — чистый вердикт (вид,
  размер, кто новее, «идентичны»); `FileContentComparer` — побайтово через
  `IFileSystem.OpenRead`, `AutoCompareLimit` делит очередь на два прохода.
  `ConflictResolution.Merge` — слияние папок: `BatchExecutor.MergeFolder`
  обходит исходную папку, ответы берёт по пути, вложенные папки сливает
  рекурсивно, опустевшую при перемещении папку отправляет в корзину;
  `MergeScanner` (Core) даёт окну то же дерево совпадений заранее, предел
  глубины 64. `ConflictBatch` — состояние окна в Core: дерево
  `ConflictPair` (вердикт, ответ, дети сливаемой папки, `IsEffective`),
  очередь сравнения `NextToCompare`, разовый ответ за нерешённые
  (`ConflictBulkAction`) и стоячая политика `SetSkipIdentical` (помнит,
  какие ответы её, и забирает ровно их). UI — `Conflict/ConflictWindow`
  (+ две вьюмодели) поверх него, выбор — две галки на пару (исходник /
  целевой → Replace / Skip / Rename-или-Merge / не решено), слово-подпись
  после имени; `DispatcherConflictResolver` маршалит на UI. При Replace
  цель уходит **в корзину**, `DeleteAction` в composite перед основным
  шагом — `Ctrl+Z` возвращает обе стороны (Explorer замещает
  безвозвратно). Элемент, копируемый в свою же папку, конфликтом не
  считается: `BatchExecutor` отвечает Rename (move — Skip) до того, как
  кого-то спросят, сторож drag & drop пропускает такой бросок через
  `PathSafety.IsAllowedDuplicate`, а вырезание в свою же папку
  `PasteAsync` снимает молча (`PathSafety.AllAlreadyIn`).
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

- **`NavigationFallback`** (Core, тест, 2026-09-17) — куда идти, когда
  папки нет. `AfterDelete(удалённые, текущая)` — ближайший не задетый
  предок или null («не трогать»), вложенность как у `PathRewrite.Under`;
  `AfterRestore(путь, exists, kindOf)` — путь, пока он есть, иначе
  `PathCrumbs.NearestExisting`, но только на `VolumeKind.Fixed`, на всём
  остальном null (буква флешки могла достаться другому носителю) — тогда
  `MainViewModel.OpenStartFolderAsync` берёт `AppSettings.WorkFolder`.
  Историю оба не трогают.
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
- **Переименование папки — тот же путь, что перенос** (2026-09-22):
  `FollowRelocatedAsync` ведёт закладки, историю и листинг; строки
  панелей при том же родителе переписываются на месте
  (`TreeNodeViewModel.Follow`), а не пересобираются — иначе ветка
  сворачивалась бы на каждом переименовании; сверка уровня — правило Core
  `BranchReconcile` (тест), панель только применяет правки. Шеврон
  незагруженного узла перепроверяется при `RefreshFor` — лист, получивший
  подпапку, раньше шеврон не возвращал. Цель панели (`SelectedEntries` без
  `SelectedEntry`) — не строка списка: перечитывание её не ищет и не
  снимает, `Paste` идёт в неё.
- **Листинг вне UI-потока** — `RefreshFolderAsync` (и `RefreshShellAsync`)
  в `Task.Run` с отменой: следующая навигация отменяет предыдущую, побеждает
  последняя; спиннер только после 150 мс.
- **Иконки и миниатюры в фоне** — `Controls/AsyncIcon` (наследник `Image`):
  кэшированную отдаёт синхронно (`TryGetCachedIcon`), остальное через
  `Task.Run` под шлюзом (4 слота), результат отбрасывается, если контейнер
  переехал на другой файл. Синхронный ответ диск не трогает, поэтому «папка
  или файл» говорит строка (`AsyncIcon.RowIsFolder` из `DataContext`): общий
  ключ типа (`file|noext`, `ext|.txt`) иначе отвечал и за ещё не нарисованную
  папку — новая `fast` вставала в дерево со значком файла; без подсказки —
  только ключи этого пути. Декодированный тир помнит, из какого `byte[]`
  картинка, и не отвечает, когда провайдер отдаёт уже другой (путь переживает
  то, что на нём стояло). Три тира: провайдер хранит `byte[]` (память +
  диск), **`IconImageCache` — декодированные замороженные `BitmapImage`**
  (256; декод был единственным на UI-потоке — 338 декодов и 141 мс за
  секунду при прокрутке; декодирует тот, кто первым дошёл; кнопка очистки
  чистит и его). Четыре ступени: `Small` / `Normal` — значок по расширению
  (`SHGetFileInfo`, один на тип; кроме типов, чей значок в самом файле, —
  `.exe`, `.ico`, `.url` и соседи, `_ownIconExtensions`: у них ключ по пути,
  как у `.lnk`), `Medium` (96) / `Large` (256) — миниатюра
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
которая только диспетчеризует: корзина — `ShellRecycleBinFolder`, архив —
`ShellArchiveFolder`, обе на `IShellItem`. Листинг обеих (и обычной папки
в `IFileSystem.Enumerate`) принимает `CancellationToken` и смотрит на него
между элементами: ушёл из папки — чтение обрывается, а не дочитывается.

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
  нет.
- **Корзина — тот же `IShellItem2`** (с 2026-09-21; раньше
  `Shell.Application`, у него `Items()` — один непрерываемый вызов, ~5 с
  на холодной корзине). Имя — `System.FileName`: как на диске, с
  расширением (display name прячет `.lnk` всегда, остальные — по
  настройке Проводника, а `RecycleHandle` несёт настоящий путь; запасное
  имя — последняя часть `SIGDN_NORMALDISPLAY`, у элемента корзины это
  исходный путь). `FullPath` — `SIGDN_FILESYSPATH`
  (`$R…`), время удаления — `PKEY_Recycle_DateDeleted` точным FILETIME (в
  `ModifiedUtc`: по нему сортировка и сопоставление в `Restore`), «откуда»
  — `PKEY_Recycle_DeletedFrom`, у удалённой папки настоящий размер.
  `Restore` находит строку панели по её `$R`-файлу (`RecycleHandle.BinFilePath`).
- **Корзина перечисляется на своём STA-потоке** (`OwnApartment.Run`). Её
  shell-папка — apartment-threaded; созданная с потока пула, она попадает в
  общий host-STA процесса: вызовы маршалятся, а долгий вызов там держит
  overlay-запросы значков (`SHGetFileInfo`) — четыре слота `AsyncIcon` и
  следующую папку с ними. Контрольные строки лога: `Recycle bin: opened …
  first row after … rows in …` и `Recycle bin: listing abandoned at …`.
- **Байты — только копирующим движком шелла.** `BHID_Stream` и
  `IDataObject` для `ArchiveFolder` отвечают `E_NOINTERFACE`;
  `IFileOperation::CopyItem` с `FOF_NO_UI` извлекает всё. Отсюда
  `IShellNamespace.CopyOut` и `Shell/ExtractionService` (Core) вокруг
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
- **Отступление от «всё откатываемо»:** временная копия записи —
  `Core/Shell/TempExtraction.CopyOutAsync` мимо всех правил: без гарда, без
  диалогов, без `IUndoableAction`. Временная копия чужого файла не
  пользовательские данные. Папка — `AppPaths.Tmp` по хешу пути записи
  (`TempFiles.FolderFor`): либо `DataTmp` (`<DataRoot>\tmp`), либо
  `SystemTmp` (`%TEMP%\Wander`) — настройка `AppSettings.UseSystemTemp`
  (`bool?`: пока человек не сказал, следует режиму — системная Temp в
  портативном, где папка данных лежит на флешке; `AppPaths.UseSystemTemp`
  ставит вьюмодель при загрузке и при смене). Чистка — `TempFiles.Sweep`
  на старте по **обеим** папкам, старше суток: настройку могли
  переключить после того, как копии сделаны. Три потребителя делят одну
  копию: «открыть» (запуск ассоциацией, статусная строка говорит, что
  правки в архив не попадут), панель просмотра и окно конфликтов.
- **Конфликты: «извлёк — сравнил»** (2026-09-03). Запись архива через
  `IFileSystem` не открыть, поэтому `ExtractionService` перед `ResolveAll`
  распаковывает те пары, где байты что-то решают: файл против файла равного
  размера, от мелких к крупным в пределах
  `FileContentComparer.AutoCompareLimit`. Куда читать байты —
  `FileConflictInfo.ReadablePath` (`SourceReadPath`); ключ ответов остаётся
  `Source.FullPath`. Папка внутри архива не распаковывается никогда
  (`SourceReachable` = false): слияние — это обход, обойти может только
  шелл; её ответы — заменить / оставить / под новым именем.
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
- **Наружу — шелловским объектом данных** (2026-09-03). `CF_HDROP` с путём
  внутри архива принимающая программа читает как несуществующий файл,
  поэтому `IShellNamespace.CreateDataObject(paths)` собирает тот же объект,
  что отдаёт Проводник: `SHCreateItemFromParsingName` на каждый путь →
  `SHGetIDListFromObject` → `SHCreateShellItemArrayFromIDLists` →
  `BindToHandler(BHID_DataObject)` (Platform, `ShellDataObject`). Не
  `…FromShellItems`: SDK её объявляет, но shell32 по имени не экспортирует,
  и `DllImport` падал на первом вызове (ревью 2026-09-04, стенд в
  scratchpad). Внутри — `CFSTR_SHELLIDLIST`, у zip ещё
  `FileGroupDescriptor`; байты принимающая сторона берёт у шелла. Две
  точки: `OutgoingDrag.Run` оборачивает его в `DataObject(comObject)` и
  предлагает **только** `Copy` (Move попросил бы источник удалить запись);
  `Ctrl+C` идёт через `ISystemClipboard.SetShellObject` (`OleSetClipboard`,
  OLE поднимается по первому `CO_E_NOTINITIALIZED`). Свои же приёмники
  (`DropTargetController`) списка файлов в таком объекте не находят и
  берут пути у самого перетаскивания — `OutgoingDrag.InFlightPaths`, живёт
  на время `DoDragDrop` (он качает сообщения на том же потоке, так что
  читающий его приёмник заведомо внутри того же жеста); дописать формат в
  обёрнутый OLE-объект WPF не даёт. Для источников из архива приёмник
  отвечает только `Copy` (источник больше и не предлагает: `Move` удалял бы
  из архива), drop в папку — `MainViewModel.ExtractAsync`, тем же путём,
  что вставка; папка внутри архива и листинг архива как цель — отказ, для
  листинга нейтральный (стрелка и плашка «что в руках»). Какой объект отдать,
  решает `MainViewModel` и передаёт вторым аргументом
  `ClipboardController.Copy`: контроллер живёт в `Core/FileSystem`, а
  `IShellNamespace` — в `Core/Shell` этажом выше, и зависимость обратно
  дала бы цикл. Свой список путей остаётся как был — вставка внутри Wander
  работает по нему; чтобы `SyncFromSystem` его не стёр (чужой взгляд на
  шелловский объект — «файлы не на диске»), контроллер помнит флаг
  `_sharedShellObject`.
- **Панель просмотра** (2026-09-03). Выделен архив в обычной папке —
  `PreviewRoute.Archive`, первый уровень через `Enumerate` на пуле, папки
  сверху, потолок 200 строк и «и ещё N», заголовок — числа и размер файла
  архива. Решает не расширение: `PreviewRouter.Route(path, isArchive)`
  берёт ответ фактом от вызывающего (`Archives.Of` плюс `CanNavigate` на
  пуле) — таблица расширений такого знать не может. Тот же список — для
  текущей папки архива, когда внутри него ничего не выделено или выделена
  папка (клик в пустое место; заголовок без размера архива). Файл
  **внутри** архива до 32 МБ — временная копия и обычный конвейер по ней;
  копия на диске с размером записи переиспользуется, не распаковывается
  заново; распаковка идёт **одна за раз** (`SemaphoreSlim` в
  `PreviewController`): движок шелла не останавливается внутри записи,
  отменённый запрос держит поток пула до конца, и стрелки по RAR сканов
  набрали 85 потоков и подвесили всё, что ходит через пул (сессия
  2026-09-04); больше 32 МБ — карточка с отсылкой к «Открыть». Миниатюр и
  поиска внутри по-прежнему нет.

## Выделение, буфер, фильтр

- **`SelectionController`** (App) — deferred selection при click-and-drag
  (drag не сбрасывает мультивыбор), «снять выделение в активном списке».
  Отложены оба смысла нажатия на строку: схлопывание мультивыбора и
  `Ctrl`-переключение. Оба применяются на отпускании, начавшееся
  перетаскивание их отменяет; единственное исключение — `Ctrl` по
  невыделенной строке при старте перетаскивания: она добавляется, иначе
  поедет не то, за что взялись.
- **`RubberBandController`** (App) — адорнер, захват мыши, пересечение с
  контейнерами; только с пустого места (`ListVisuals.IsChrome` — полоса
  прокрутки, заголовки, разделители); невиртуализованные не попадают.
  Нажатие взводит (`Arm`), прямоугольник появляется на системном пороге
  перетаскивания (`Begin`): без порога клик в зазор между плитками ловил
  обоих соседей.
- **Каретка** (App, 2026-09-03) — `MainViewModel.CaretPath`, строка, от
  которой пойдёт следующая стрелка, и рамка в шаблоне контейнера
  (`CaretRowConverter`, триггер последним). Путь, а не строка: строки
  заменяются на каждом перечитывании. Ставится там, где клавиатуру кладут
  на строку осознанно (`FocusRow`, нажатие на строку), и подхватывается из
  `List_SelectionChanged` для нажатий, на которые отвечает WPF. Читают
  `TryEnterList` и `CaretIndex`.
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

Решение тика несёт ещё и `Stale` — все пути, которые сторож назвал в этой
пачке, **включая** структурные. Это не про строки, а про кэши: миниатюры
ключуются путём, а путь не меняется, когда файл под ним заменили другим с
тем же именем. `MainViewModel` чистит по этим путям оба уровня
(`IIconProvider.Forget` + `IconImageCache`) и поднимает `AsyncIcon`
перерисоваться; дисковая запись удаляется на пуле, потому что тик живёт на
UI-потоке. Второй заход на ту же проблему — сверка при публикации листинга:
`ForgetIfChanged(path, FileStamp)` сравнивает mtime и размер строки с тем,
что было у закэшированной картинки. Он и закрывает случай, которого сторож
не видел: папку посмотрели, ушли, файл изменили снаружи, вернулись.
`FileStamp` живёт в `Core/Icons` — значение, которое одинаково читают
листинг (`FileSystemEntry.ModifiedUtc` + `Size`) и провайдер (`FileInfo`).

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
  строка не порождает правки и не теряет контейнер. Пересборка (`Reset`,
  панель при нём уходит в начало прокрутки) — только когда общего нет,
  общие строки идут в другом порядке (сортировка) или ушло + пришло больше
  256. Порядок сравнивается по общим строкам, не позиция в позицию: до
  2026-09-21 один файл, удалённый в первой половине папки, сдвигал всё ниже
  и отматывал список наверх.
- **`CurrentRowFallback`** — текущий файл ушёл из папки (`Del`, удалён в
  программе, где был открыт, перенесён): текущим становится следующий
  уцелевший, иначе ближайший перед ним — **выделен**, каретка и клавиатура
  на нём, без прокрутки [решение 2026-09-21: одно правило на все случаи; в
  Проводнике — рамка без выделения, и панель просмотра осталась бы пустой].
  `ReconcileEntries` уход только замечает (`Departure`) и выделение не
  гасит; `SettleDeparture` решает после `ApplyArrival`: намерение поставило
  своё (переименование, откат, `Del` со своим `NextAfterRemoval` — то же
  правило, спрошенное заранее) — больше ничего; иначе преемник. Поэтому
  панель идёт с ушедшего снимка сразу на следующий, не через «ничего не
  выделено». Скрытая фильтром строка ушедшей не считается — выделение
  просто снимается. `CurrentRowLeft` → `FileListView`: клавиатура на строку,
  если была в списке или нигде; окно не активно — после `Activated`.
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
- **Меню «Действия» в шапке — третья форма тех же правил** (2026-09-16,
  PLAN AC; в коде `MenuOperations` / `OperationsMenu`). `ContextMenuTarget.
  Place = MenuPlace.Header` → `ContextMenuBuilder.BuildHeader`: постоянный
  порядок (подпись выделения · Переименовать группой · Частные ▸ ·
  Конвертировать ▸ · Извлечь · Ярлык · Копировать путь · Терминал), но
  **неприменимое не показывается**, как и в контекстном. Серое с
  тултипом — только действие, которому не хватает программы (подсказка
  поставить её); `ActionApplicability` проверяет инструмент последним,
  чтобы подсказка не шла к действию, неприменимому по типу. Подпись —
  «4 изображения» через `Text.Plural` (формы в ресурсе через `|`). Один
  каталог: `HideableTree` и галочки Параметров действуют на оба места.
  Шелл в шапке не опрашивается; перестройка — на `SubmenuOpened` самого
  меню (пункт без детей WPF считает кнопкой — в XAML строка-заглушка).
  Пустое подменю билдер не создаёт (`AddSubmenu`): `Normalize`
  выбрасывает только подменю, опустевшее от скрытия, а `Sub` без детей —
  это лист с именем подменю.

## Свои действия и групповое переименование

Один каталог `CustomAction` (`Core/Actions/`) — строка «название · для
каких файлов · программа или встроенный обработчик · шаблон аргументов ·
режим · где показывать · объявленный выход»; хранится в
`AppSettings.CustomActions`, пресеты приходят из кода и сливаются по `Id`.
Что решает Core, а UI только исполняет:

- **Применимость** — `ActionApplicability.For`: все выделенные подходят
  под `FileTypeSelector` (группа из `FileTypeGroups` — те же списки, что
  у панели просмотра, — или маска `*.psd;*.ai`), иначе действие не
  предлагается; пустое выделение — только действия для папок, на текущую
  папку; `RequiredTool` без инструмента — `ToolMissing`. Частичное
  совпадение не считается (BACKLOG).
- **Командная строка** — `CommandLine.Expand`: `{path} {name} {ext} {dir}
  {paths} {list} {out}`, кавычки ставит подстановка всегда, хвостовой `\`
  корня удваивается; `ValidationKey` ловит режим «на каждый файл» с
  `{paths}` и наоборот. **Выход** — `OutputNames.Resolve`: рядом с
  источником или в папке, которую человек выбрал («В другую папку…»,
  `RunAsync(..., outputFolder)`), источник считается занятым; занятое имя
  — `FileSystem/UniqueNames`, **номер после наибольшего** («clip (3)»,
  «clip (4)» → «clip (5)»): одно правило на копии при «оставить обе»,
  извлечение и выходы действий (решение 2026-09-16). Проводник, Finder и
  браузеры заполняют первый пропуск; выбрано иначе, чтобы новое было
  последним в списке и номер не переезжал на другой файл.
- **Хранение пресетов** — `ActionCatalog.ToStored` / `Merge`: у пресета
  в `state.json` только `Id`, `Enabled` и свой `Program`
  (`ActionCatalog.Override`), остальное берётся из кода при слиянии —
  улучшенная команда доезжает и до того, кто пресет выключал. Свои строки
  хранятся целиком. Старый полный снимок пресета читается тем же путём.
- **Кодировщик картинок** — `Platform/Imaging/ImageConvertAction`
  (WinRT, см. «Platform без WPF»): метаданные из `ImageMetadata`
  (MetadataExtractor, с RAW тоже) пишутся номерами тегов
  (`Imaging/ExifTags`, GPS включительно), слова — из свойств декодера;
  JPEG без изменения пикселей идёт transcoding'ом без пережатия.
- **Исполнение** — `ExternalActionRunner`: по одному, не параллельно;
  `OperationTracker` (`OperationVerbs.RunAction`, по элементам); гард
  `SystemPathGuard` на папку выхода; `{list}` — временный файл в
  `AppPaths.Tmp` через `IFileSystem`; код возврата ≠ 0 — `Failed` с
  хвостом stderr; отмена убивает процесс (`IProcessRunner.WasKilled`),
  недописанный выход — в корзину, остальные `Cancelled`. **Undo — только
  объявленный выход** (`CreateAction` → корзина); сам процесс не
  откатывается, это осознанное отступление. `IProcessRunner` в Core,
  `WindowsProcessRunner` в Platform: `UseShellExecute = false`, оба потока
  сливаются на ходу (иначе полный пайп вешает программу), stderr читается
  только при скрытой консоли, `Kill(entireProcessTree)`. Встроенные
  обработчики — `IBuiltinAction` по имени в `Program`.
- **Групповое переименование** — `Core/Rename/`: `RenameRules` (найти /
  заменить, шаблон, регистр — фиксированный порядок применения;
  расширение меняется только регистром) → `RenamePlanner.Preview` — чистая
  функция от правил, элементов и `RenameContext` (`exists`, спутники,
  дата съёмки): «было → станет» со статусом, дубликаты внутри пачки,
  совпадения снаружи; имя, которое пачка сама освобождает, совпадением не
  считается. `BatchRenameGate` пускает в окно два и более **одного вида**
  (файлы или папки), смешанное — отказ. Применение —
  `FileOperationService.RenameMany`, **двухфазный**: член, чьё имя ещё
  занято поздним членом, паркуется на `…<8 hex>.wander-tmp` (сторож такие
  не видит) и переезжает после; один composite, откат в обратном порядке.
  Память окна — `AppState.RenameRules` и пять последних применённых
  шаблонов `AppState.RenameTemplates` (`RenameTemplateHistory`, `[N]` не
  запоминается); пишутся по ОК из `MainViewModel`.

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
— сайдкар отдаётся RAW, если RAW среди них ровно один (`IMG.CR2` +
`IMG.jpg` при `IMG.xmp`: пара RAW+JPEG с камеры или JPEG, сделанный из
RAW, — XMP в обоих случаях у RAW; 2026-09-16, после «Превью из RAW»
сайдкар и оценка оставались сиротами); иначе (два JPEG, два RAW) — ни к
кому. То же в `Group` и в `FindCompanions`: JPEG рядом с RAW того же
имени свой `.xmp` при переносе не забирает (`CompanionResolver.Owner`).

- Свёртка — в воркере `RefreshFolderAsync` **после** Hidden/System:
  спутник у отфильтрованного файла и сирота остаются видимыми.
- `FileSystemEntry.Companions` — пути, пусто у обычного файла и при
  выключенном флаге; блок «Вместе с файлом:» в футере. Значок в списке —
  REJECTED.
- Меню про спутников не знает: их нет в выделении.
- Групповые операции — `BatchGroup` (основной + спутники): один шаг
  прогресса и один результат на группу, composite-undo; коллизия — у
  каждого файла своя (см. «Конфликты»), переименованный основной файл
  уводит спутников за собой. Группы из выделения бесплатно
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
- **Клик по звезде или свотчу** (2026-09-15) уходит хозяину неразрешённым:
  `RatingRequestedEventArgs` несёт `Clicked` и `Current`, а «поставить или
  снять» решает `RatingToggle.Resolve` (Core, тест) против **всех** целей —
  всё выделение, если показанный файл в нём (в футере «и ещё N»), иначе
  один файл; вторая половина сплита всегда про свой файл. `Shift`+цифры в
  галерее — `SetColorForSelection` тем же правилом; цифры без `Shift` —
  как были: ставят, `0` снимает.
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
- **`Ctrl+Z` переноса** — `IUndoableAction.MovesOnUndo`: пары «где
  сейчас → куда вернётся» (`MoveAction`; `CompositeAction` — в порядке
  отката), `UndoLast` ведёт по ним листинг, историю и закладки через
  `FollowRelocatedAsync` — тот же шаг, что после броска или `Ctrl+V`
  (`PathRewrite.Under`, `NavigationService.RewritePaths`,
  `Bookmarks.Follow`); открытая папка, вернувшаяся на место, не
  оставляет список на опустевшем пути.

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

Одно исключение из «дети меряются ровно ячейкой» (2026-09-03):
**единственная выделенная** ячейка меряется с бесконечной высотой и
расставляется на ту, которая получилась (`MeasureChild` / `IsExpanding`), —
её подпись снимает потолок и показывает имя целиком. Должна расти вниз
поверх соседей (`ZIndex` из `ArrangeOverride`), а не раздвигать их:
раскладка по-прежнему считается от размера ячейки, поэтому сетка не
переливается. При двух и больше выделенных не растёт никто: выросшие соседи
по вертикали закрывали бы друг другу подписи. **Незакрытое:** на стенде
`ZIndex` кладёт выросшую ячейку поверх (проверено растеризатором и
`PrintWindow`), в живом окне сосед всё равно сверху — стенд разницу не
воспроизводит, чинить в живом окне (BACKLOG, «Интерфейс»).

Двух вещей это стоило, и обе неочевидны:

- потолок подписи вешает **триггер «не выделен»**, а не атрибут в шаблоне:
  `DynamicResource` в шаблоне ложится значением на сам элемент и обходит
  триггер шаблона по приоритету, так что снять его триггером невозможно
  (первая версия так и не работала — многоточие уходило, высота
  оставалась);
- панель **сама подписывается на `SelectionChanged` владельца** и
  инвалидирует себя. Иначе её никто не разбудит: контейнер, чей constraint
  не изменился, возвращает прежний `DesiredSize` (обрезанный ячейкой), WPF
  не видит, что распространять, и панель не помечается грязной — подпись
  снимала потолок, её собственный `DesiredSize` рос, а контейнер оставался
  ростом в ячейку. Замерено на стенде (QA.md, «что можно проверить без
  окна»): 159 px до инвалидации, 173,84 после.

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

`PreviewController` (App) — конвейер с отменой и спиннером. `PreviewPane`
берёт контроллер **своим `DataContext`** (2026-09-15; раньше — окно, и
биндинги шли через `Preview.*`): всё, что панели нужно от хозяина, —
свойства контроллера (`ContentPalette` кладёт `MainViewModel.PushPalette`),
события наружу — `RatingRequested`, `RevealRequested`. Поэтому панель
живёт вторым экземпляром: `MainViewModel.PreviewSecond` (`ShowFooter =
false` — сплит только на картинку, футер один и описывает всё выделение;
`ShowRawDecode` зеркалится с первой) + второй `PreviewPane`, созданный
при первой паре (`MainWindow.ApplyPreviewSplit`) и вложенный в первый
(`PreviewPane.ShowSecond`, 2026-09-17): под картинкой, пока колонка выше,
чем шире, иначе рядом; футер первой — под обеими. Зум удержанием
синхронный: `ZoomMoved` отдаёт точку долями области, вторая половина
повторяет её `FollowZoom` без захвата мыши. Пара — чистое правило
`PreviewPair.Of(выделение, листинг)` (Core, тест): ровно два файла, оба с
маршрутом, порядок — по списку. Какой файл панель показывает при
множественном выделении — `ActiveEntry`: каретка (`CaretPath`, её ставит
список на клик и фокус), если она внутри выделения, — то есть файл,
добавленный `Ctrl`+кликом последним; `SelectedItem` WPF остаётся на
первом выделенном и показывал бы не то. `RefreshPreviewPrimary` зовётся
из трёх сеттеров (`SelectedEntry`, `SelectedEntries`, `CaretPath`) —
список сообщает их в произвольном порядке; в паре главный — верхний
(`PrimaryForPane`). `PreviewKind`:

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
  raw-данные (SOF3). У CR3 быстрый `PRVW` — 1620×1080, полноразмерный JPEG —
  первая дорожка `moov` (`Extract(fullSize: true)`, 60–110 мс декода против
  ~10). Панель берёт оба (`PreviewController.LoadImageAsync` →
  `LoadFullSizeAsync`): быстрый — сразу и остаётся вписанным (`Image`),
  большой — после 150 мс стояния на файле (удержанная стрелка 24 Мп не
  декодирует) и идёт **только в зум** (`ZoomSource` = большой, иначе
  `Image`; `ImgZoom`, `IsImageDownscaled`, `UpdateZoomPosition` меряют по
  нему): поставленный на место `Image`, он пересэмплировал ту же картинку
  через четверть секунды после каждой стрелки — заметнее всего на
  вертикальных кадрах, их панель рисует крупнее. Одинаковые байты (форматы
  с одним JPEG) второй раз не декодируются; зум под сменой картинки —
  `PreviewPane.RefreshImageZoom`. Миниатюрам —
  только быстрый. Размеры в футере из EXIF. **Панель между картинками не
  гаснет**: прежняя стоит до готовности следующей
  (`ClearPreviewContent(keepImage)`), вуаль с крутилкой — только если
  загрузка дольше 250 мс (`VeilDelayMs`). Футер держится вместе с картинкой
  (`FooterWaitsForPicture`, EXIF не гасится; блок спутников очищается в тот
  же такт, что заполняется): иначе футер дважды менял высоту на каждый
  файл, и вертикальный снимок — он вписан по высоте — дёргался. Высоту
  футера держит его содержание, не подпорки: спутники — серое «(+.xmp)»
  после имени (`CompanionLabel` → `SummaryNote`; `Summary` делится на
  `SummaryHead` / `SummaryRest` ради этого `Run`), своих строк у них нет,
  кроме Unity `.meta`; факты — без подписей, через `SummaryText.Gap`. JPEG лежит **неповёрнутым**, поворот в IFD0 контейнера
  (6 / 8) — `ImageMetadata.Orientation`, `ApplyOrientation` через
  `TransformedBitmap`. С 2026-09-16 тег применяется к **каждой** картинке,
  не только к RAW: `BitmapImage` JPEG с камеры тоже не поворачивает, а
  Проводник и любой просмотрщик поворачивают — «оставить как есть» было
  ошибкой, всплывшей на превью, взятом из RAW как есть вместе с тегом.
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
кнопка по умолчанию всегда отменяющая — поэтому не поле),
`Choose(ChoiceRequest)` (ответы названы на кнопках, «Отмена» — по
умолчанию; индекс или -1), `Prompt`, `PickFolder`,
`CreateConflictResolver(skipIdentical)`. Продакшн — `WpfDialogs`
(`MessageBox` поверх активного окна — но никогда поверх окна операции,
`ITransientWindow`; `ChoiceDialog`, `PromptDialog`,
`OpenFolderDialog`, `DispatcherConflictResolver(InteractiveConflictResolver)`); харнесс
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

**Две копии на одних данных** (2026-09-15): установленная (`C:\Programs\
Wander`, `publish.ps1 -Install`) и Debug из Rider делят `%LOCALAPPDATA%\
Wander` — так задумано, отдельной папки для отладки нет: отлаживаться
удобно на своих закладках и раскладке. Кто пишет `state.json`, решает
`InstanceLock` (Platform): экземпляр без ключа держит именованный мьютекс
`Local\Wander.state.<хэш корня>` (не захватывает — только существует, пока
жив процесс); экземпляр с `--yield` (`AppPaths.Yields`, ставит профиль
`launchSettings.json` для Rider) ничего не держит, а перед каждой записью
смотрит, есть ли владелец, и, увидев его раз, больше не пишет до конца
сеанса (`IAppStateStore.IsReadOnly`, в заголовке окна «— настройки не
сохраняются», строка в логе). Читает состояние он как обычно. Имя с хэшем
корня — харнесс в песочнице и установленная копия друг друга не видят.
Кэш миниатюр и профиль WebView2 общие: один рантайм, одни опции.

**`state.json`** (`JsonAppStateStore`, record `AppState`):
- `Session` — `LastPath` (`NavigationStop?`), `ExpandedPaths` (только
  **видимо** раскрытые: `CollectExpandedRecursive` останавливается на
  свёрнутом; флаги внутри ветки не гасятся при сворачивании, иначе
  восстановление раскроет свёрнутого родителя), `ViewMode`,
  `IsPreviewVisible`, `PreviewWidth`, `IsFoldersVisible` (панель папок
  убрана — колонка и её сплиттер в 0, `FoldersWidth` ждёт), `IsBookmarksExpanded`,
  `RecentPaths`, `ManualViewModes`, `BookmarksHeight`, `FoldersWidth`,
  `LayoutWindowWidth` / `LayoutWindowHeight` — окно, долей которого были
  три размера панелей: `PaneSizes.Restore` (Core/Layout, тест) возвращает
  пиксели как были, если окно того же размера, и ту же долю нового окна,
  если нет. Считается при `Loaded` и ещё раз при `ContentRendered`: в
  `Loaded` окно ещё не того размера, каким откроется, — восстановленное
  развёрнутым стоит там в обычных границах (1762×700 против 2062×1118), и
  панели, смасштабированные под них, оставались короткими каждый старт
  (2026-09-17; вызов — чистая функция пары и окна, сторожа «то же окно»
  нет). Потолок при перетаскивании — окно минус резерв соседа
  (`MainViewModel.PaneCeiling`), чтобы drag и восстановление не спорили.
  В файл уходит **пара, выставленная пользователем** (`_saved*`), а не
  масштабированные размеры с экрана: перебазирование (`RebasePaneSizes`)
  — только при перетаскивании разделителя; легаси-файл без размера окна
  возвращает панель как была, но не шире «окно минус резерв»
  (`PaneSizes.Restore`), до первого перетаскивания; иначе круг монитор →
  ноутбук → монитор возвращал панели на пиксели не туда (округление и
  минимумы в обе стороны, 2026-09-16). Контроль в логе: `State loaded`
  (что прочитано), `Pane sizes: window WxH, set at WxH; …` (оба вызова),
  `State written` (что ушло на диск; только когда пара изменилась).
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
ошибки; в тестах `NullLogger`. Ротация — `LogFolders.Sweep` при старте на
пуле: 200 последних `session-*` / `journal-*` и 20 `crashes\crash-*.zip`,
правило отбора — `Core/Logging/LogRetention` (тест). Повторы `WARN` /
`ERROR` схлопывает `Core/Logging/RepeatCollapser` (подпись — уровень,
сообщение, тип и первый кадр исключения): первое появление пишется всегда,
та же подпись в течение 5 с считается, итог «`ERROR repeated N times over
M s: сообщение`» — при смене строки, раз в минуту, пока повторы идут, и при
закрытии лога. `INFO` через коллапсер не проходит: это хронология, и две
одинаковые строки подряд — два события. Событие `Written` поднимается на
каждый вызов, схлопнутые включая. Smoke-прогон `check.bat run` пишет в
`artifacts\smoke\data`, не сюда. Долгие фоновые ожидания — спиннер, за
которым в логе ничего, — называет `Core/Diagnostics/LongWait.WatchAsync`
(тест): `SLOW wait: что - still running after 5 s` один раз и `SLOW done:
что - took N s` по концу; висит на листинге архива в списке
(`RefreshShellAsync`), в дереве (`TreeNodeViewModel.LoadChildrenAsync`) и в
панели просмотра, и на распаковке записи для панели (очередь и распаковка
одним ожиданием).

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
| `FakeConflictResolver` | `batchOverride` на всё, `perItem`-очередь; `ResolveAllCalls` (размер каждого вызова) / `Conflicts` (что показали) |
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
