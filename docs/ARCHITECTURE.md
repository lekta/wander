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
| `Wander.Platform.Windows` | `net10.0-windows` | Реализации интерфейсов Core: Win32, Shell COM, `System.IO`. |
| `Wander.App` | `net10.0-windows` | WPF UI: окно, ViewModel'и, диалоги, конвертеры. |
| `Wander.Core.Tests` | `net10.0` | xUnit. Тестирует **только** Core через фейки. 173 теста. |

**Жёсткое правило:** в `Wander.Core` нет `using System.Windows.*`, нет COM,
нет PInvoke. Если кажется, что нужно — значит нужен новый интерфейс в Core и
реализация в `Platform.Windows`. Это цена возможности отвинтить UI или
платформенный слой целиком.

```
src/
├── Wander.Core/
│   ├── Diagnostics/    IFileLockInspector, FileLockInfo
│   ├── FileSystem/     IFileSystem, FileOperationService, BatchExecutor,
│   │                   ClipboardController, SearchController, SystemPathGuard,
│   │                   PathSafety, IConflictResolver, IRecycleBin, IKnownFolders,
│   │                   FileSystemEntry, EntryKind, EntryComparers, SortKey
│   ├── Icons/          IIconProvider, IImageMetadataReader, IconSize, ImageMetadata
│   ├── Logging/        ILogger, ILogFile, NullLogger
│   ├── Menu/           ContextMenuBuilder, ContextMenuTarget, ContextMenuSettings,
│   │                   ContextMenuCatalog, MenuEntry, MenuCommandId
│   ├── Navigation/     NavigationService, NavigationSource
│   ├── Operations/     OperationTracker
│   ├── Persistence/    IAppStateStore, AppState, AppSettings
│   ├── Shell/          IShellLauncher, IShellNamespace, IShortcutService,
│   │                   IShellContextMenu, ShellMenuEntry
│   ├── Undo/           UndoService, IUndoableAction, UndoableActions
│   └── ServiceLocator.cs
│
├── Wander.Platform.Windows/
│   ├── Diagnostics/    RestartManagerLockInspector
│   ├── FileSystem/     SystemIOFileSystem, ShellRecycleBin, WindowsKnownFolders
│   ├── Icons/          SystemIconProvider, MetadataExtractorImageReader
│   ├── Logging/        FileLogger
│   ├── Persistence/    JsonAppStateStore
│   ├── Shell/          ShellLauncher, ShellShortcutService, WindowsShellNamespace,
│   │                   ShellContextMenu, ShellContextMenuInterop, ShellMenuIcons
│   └── PlatformBootstrapper.cs
│
└── Wander.App/
    ├── Conflict/       ConflictDialog, BatchConflictDialog,
    │                   DispatcherConflictResolver, InteractiveConflictResolver
    ├── Controls/       GifImage, MagnifierCursor, RubberBandAdorner
    ├── Converters/     Icon, EnumEquals, EnumToVisibility, BitmapPixelSize
    ├── Diagnostics/    CrashReporter
    ├── DragPreview/    DragPreviewWindow, DropTargetAdorner, DragAction, NativeMethods
    ├── Menu/           ContextMenuFactory, MenuBinding
    ├── Util/           SelectionController, SizeFormatter
    ├── ViewModels/     MainViewModel, NavigationController, PreviewController,
    │                   TreeNodeViewModel, SettingsViewModel, MenuToggleViewModel,
    │                   OperationViewModel,
    │                   ObservableObject, ViewMode, PreviewKind, DropEffect
    ├── Views/          SettingsWindow, ProgressDialog
    ├── MainWindow.xaml(.cs)
    └── App.xaml(.cs)
```

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

Тесты вместо этого регистрируют фейки из `tests/Wander.Core.Tests/Fakes/`.

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
  человеческим текстом («Cannot move 'photos' into its own subfolder '2024'»).
- **Подтверждение с Cancel по-умолчанию** — на любой деструктивной операции.
- **`IFileLockInspector`** (`RestartManagerLockInspector`) — кто держит файл,
  чтобы вместо голого `IOException` показать «file is open in: Word (PID 1234)».
  Работает по файлам; для папки с залоченным вложенным файлом пока базовый
  `IOException`.

---

## Навигация и дерево

**`NavigationService`** — browser-style back/forward. Каждая запись несёт
`NavigationSource`, чтобы потребители (раскрытие дерева, preview-панель)
реагировали по-разному в зависимости от того, как пользователь сюда попал.

**`NavigationController`** (App) — маршрутизация путей, включая
shell-сентинелы: `shell:RecycleBinFolder` уходит через `IShellNamespace`,
а не через `IFileSystem`, и получает человеческий лейбл («Корзина»).

**Дерево** — lazy-load по раскрытию, листовые папки без треугольника,
раскрытые пути сохраняются в `AppState`, авто-раскрытие на текущую папку.
Ключевое обещание проекта: **дерево никогда не сворачивается само** — это
прямой ответ на баг Win11 Explorer.

**Bookmarks-панель** над деревом: drag-add, сворачиваемая, состояние в
`AppState.Favorites` / `IsBookmarksExpanded`. Три спец-папки по умолчанию
(Загрузки / Документы / Изображения) через `IKnownFolders` →
`SHGetKnownFolderPath`, плюс Корзина. Каждая — отдельный чекбокс в настройках.

---

## Выделение, буфер, фильтр

- **`SelectionController`** (App) — extended-выделение во всех трёх view-mode,
  deferred selection при click-and-drag (чтобы drag не сбрасывал мультивыбор),
  rubber-band через `RubberBandAdorner`.
- **`ClipboardController`** (Core) — «файловый буфер» в памяти, **не** OS
  clipboard. Хранит пути, а не содержимое: реальный move/copy случается в
  момент Paste, против того состояния файлов, какое будет тогда. Паритет с
  Explorer.
- **`SearchController`** (Core) — живой текстовый фильтр (`Ctrl+F`). Владелец
  отдаёт снапшот после hidden/system-фильтрации через `SetSource`; каждое
  изменение запроса переproeцирует снапшот через case-insensitive
  `Name.Contains` **на фоновом потоке**, с отменой на каждый keystroke.
  Результат — через `FilteredChanged`. Дерево фильтр не трогает.

Обе последние живут в Core именно чтобы тестироваться без UI: гонки
«печатаю быстро + Refresh одновременно» были повторяющимся источником багов,
пока логика сидела в MainViewModel.

---

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
     ├─▶ IShellContextMenu.Open(paths, folder)   ─── Platform.Windows
     │        │  SHParseDisplayName → IShellFolder
     │        │  GetUIObjectOf / CreateViewObject → IContextMenu
     │        │  QueryContextMenu(HMENU) → обход HMENU → ShellMenuEntry[]
     │        └─ сессия живёт до закрытия меню: id команд валидны
     │           только внутри неё
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

- **по выделению** — Open / Open with, подменю **File** (весь буфер обмена,
  `Copy path`, `Copy name`, `Create shortcut`), Rename / Delete /
  Delete permanently, закладка / Проводник / терминал, пункты расширений,
  Properties;
- **по фону** — Paste и New folder первыми (это девять кликов из десяти),
  подменю View / Sort by, Refresh, путь / закладка / Проводник / терминал,
  Undo, пункты расширений, Properties папки.

Буфер обмена уехал в подменю намеренно: у `Cut` / `Copy` / `Paste` есть
хоткеи, а верхний уровень стоит тратить на то, у чего их нет.

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

**Расширения выполняются в нашем процессе** — это неизбежная цена их
поддержки (Explorer делает то же самое). Отсюда три вещи: запрос обёрнут в
`try/catch` с логом, выключенный `ShellExtensionsEnabled` означает, что
чужие DLL вообще не грузятся, а сама команда вызывается **после** закрытия
меню (обработчики любят открывать свои модальные диалоги) и только потом
сессия диспозится — `IContextMenu` нужен живым до момента вызова.

**Кастомизация** (`AppSettings` → `ContextMenuSettings`): мастер-выключатель
расширений, чёрный список по имени, сворачивание расширений в одно подменю
«More options» и список скрытых собственных пунктов. Скрытое хранится как
«что выключено», по строковым именам `MenuCommandId` — новый пункт в будущем
релизе появится сам, а переименование enum-члена не воскресит спрятанное
молча. Имена расширений узнаются только из самого шелла, поэтому
`KnownShellExtensions` накапливается по мере открытия меню — иначе диалогу
настроек нечего было бы показать.

---

## Preview pane

`PreviewController` (App, ~600 строк) — асинхронный конвейер с отменой и
спиннером. `PreviewKind`: `None`, `Image`, `Gif`, `Text`, `Code`, `Web`,
`Video`, `Unsupported`.

| Kind | Чем рендерится |
|---|---|
| `Image` | `BitmapImage`, `StretchDirection=DownOnly` |
| `Gif` | `Controls/GifImage` — анимация, `BitmapImage` её не умеет |
| `Video` | WPF `MediaElement` |
| `Text` | обычный `TextBox` |
| `Code` | AvalonEdit с подсветкой |
| `Web` | WebView2 — PDF / HTML / отрендеренный Markdown |

Footer-summary считает контекст: пустой выбор → текущая папка (рекурсивный
count+size, async), файл → name/size/modified + EXIF, папка → count+size,
мульти → агрегат. EXIF включая RAW (CR2/CR3/NEF/ARW/DNG) через
`MetadataExtractor`.

**WebView2 изолирован:** `NavigationStarting` пропускает только
`file:` / `about:` / `data:`, попапы режутся. Скрипты внутри локального `.html`
при этом исполняются — `IsScriptEnabled` глобальный, а встроенный
PDF-viewer без JS не работает (TECHDEBT).

**Иконки:** `SystemIconProvider` — системные иконки + `.lnk` overlay-стрелка
(включая jumbo-композит), thumbnails через `IShellItemImageFactory`. Кэш
per-path без ограничения размера — на больших папках это заметная память
(TECHDEBT).

---

## Состояние и логи

Всё на диске лежит в `%LOCALAPPDATA%\Wander\`.

**`state.json`** (`IAppStateStore` → `JsonAppStateStore`) — record `AppState`:

- `Session` — `LastPath` (`NavigationStop?`), `ExpandedPaths`, `ViewMode`,
  `IsPreviewVisible`, `PreviewWidth`, `IsBookmarksExpanded`.
- `Favorites` — закладки.
- `Window` — `WindowGeometry` (Left/Top/Width/Height/Maximized).
- `Settings` — `AppSettings`: `RestoreLastFolder`, `ShowHidden`, `ShowSystem`,
  `ConfirmRecycle`, `SortKey` / `SortAscending` / `GroupFoldersFirst`,
  геометрия LargeIcons-ячеек, чекбоксы закладок, `ShowDebugMenu`,
  настройки контекстного меню (`ShellExtensionsEnabled`,
  `ShellExtensionsInSubmenu`, `BlockedShellExtensions`,
  `KnownShellExtensions`, `HiddenContextMenuItems`).

Миграционного слоя **нет**: `JsonAppStateStore.Load` ловит исключение и
возвращает `new AppState()`. До 1.0 это осознанный выбор — схема ещё ломается
(см. TECHDEBT про schema break и `WindowGeometry`).

**`logs\session-yyyymmdd-hhmmss.log`** — файл на каждую сессию, `FileLogger`.
Логируются открытие папки, все файловые операции, конфликты, ошибки. В тестах
подменяется на `NullLogger`, чтобы не сорить в реальный AppData. Ротации нет —
каталог растёт бесконечно (TECHDEBT).

**`crashes\*.zip`** — `CrashReporter`. `App.HookCrashLogging` вешает три
обработчика: `DispatcherUnhandledException` (лог + предложить репорт,
`Handled = true` — UI-поток не роняем), `AppDomain.UnhandledException`
(фатальный, флашим что успели), `TaskScheduler.UnobservedTaskException`.
Репорт — пре-заполненный GitHub issue + локальный zip-бандл. **Ничего не
уходит без действия пользователя** — телеметрии нет.

---

## Тесты

xUnit, проект `tests/Wander.Core.Tests`. **Покрывается только `Wander.Core`** —
UI и платформенный слой проверяются smoke-запуском (`.\tools\check.bat run`).
Это прямое следствие слоёного деления: логика, которую стоит тестировать, по
определению не должна сидеть в WPF-коде. Если тест написать не получается —
это сигнал, что логика не в том слое.

### Фейки

Живут в `tests/Wander.Core.Tests/Fakes/`, реализуют интерфейсы Core в памяти:

| Фейк | Что даёт |
|---|---|
| `FakeFileSystem` | `IFileSystem` целиком в памяти: `Directories` (`HashSet`), `Files` (`Dictionary<string, byte[]>`), плюс `CallLog` со списком вызовов |
| `FakeConflictResolver` | Сценарий разрешения конфликтов: `batchOverride` на весь батч и `perItem`-очередь на отдельные. Пишет `StartBatchCalls` / `ResolveCalls` |
| `FakeRecycleBin` | Корзина поверх `FakeFileSystem`, поддерживает `Send` / `Restore` и тоже ведёт `CallLog` |

`CallLog` — основной инструмент проверки: он позволяет утверждать не только
«результат такой», но и «сходили ровно туда, куда надо, и ровно столько раз».

### Правила

- **Изоляция локатора.** Всё, что трогает `ServiceLocator`, обязано
  вызвать `ServiceLocator.Reset()` — иначе регистрации протекут в соседние
  тесты. Локатор статический, порядок тестов не гарантирован.
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
4. Проверка перед коммитом — `tools\check.bat` (build + `dotnet format
   --verify-no-changes` + тесты). `tools\check.bat run` добавляет
   smoke-запуск, `tools\check.bat format` — пишет форматирование.
