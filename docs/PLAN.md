# Wander — план

Список **обязательных целей** проекта и их детализация. Снапшот сделанного
здесь же — поэтому при старте новой сессии этот файл читается первым.

В отличие от соседей:

- [`TECHDEBT.md`](TECHDEBT.md) — мелкая шероховатость (дальние чистки).
- [`BACKLOG.md`](BACKLOG.md) — конкретные второстепенные задачи (полезное, но не сейчас).
- [`CLAUDE.md`](../CLAUDE.md) Roadmap — стратегические направления продукта.
- [`ARCHITECTURE.md`](ARCHITECTURE.md) — как устроен код и механизмы.

---

## Столпы проекта

1. **UX** — убрать лишнее из Win11 Explorer, починить его баги, добавить удобств.
2. **Надёжность** — деструктивные операции всегда требуют подтверждения с Cancel
   по-умолчанию; явные ошибки вместо тихих сбоев; обратимость через `Ctrl+Z`.

---

## Приоритеты

| | Что значит | Когда брать |
|---|---|---|
| **P0** | Багфиксы, незавершённое из готового, блокеры | Прямо сейчас, до новых фич |
| **P1** | Обязательное для proper file-manager UX | Следующая итерация |
| **P2** | Желательное, заметное улучшение | После P1 |
| **P3** | Опциональное / нишевое | Когда дойдём |

## Ближайший фокус

### ~~Спринт 1 — багфиксы + связать готовое (P0)~~ — **закрыт**
Все позиции отмечены ✓ выше: Ctrl+Z, Shift+Delete, FileLogger, DnD баги
(#1/#2/#3), двойное выделение (#5), StretchDirection=DownOnly (B1).

### ~~Спринт 2 — async операций~~ — **закрыт** (через параллельную сессию)
- `OperationTracker` в Core, async batch ops в `FileOperationService`,
  `DispatcherConflictResolver` для marshal обратно на UI, `OperationViewModel`
  для отображения. См. секцию E ниже.
- ProgressDialog (E2) и тест на UndoService busy-guard — **остаются**.

### ~~Спринт 3 — техническая чистка #1 + добить P1~~ — **закрыт**
1. ~~**Технические чистки #1**~~ — **сделано**. См. секцию ниже: `BatchExecutor`,
   `PreviewController`, `SelectionController`, `WindowGeometry` подrecord.
2. ~~**E2 ProgressDialog**~~ — **сделано (базовый)**. Modal-окно
   `Views/ProgressDialog.xaml` с заголовком, current-path лейблом,
   ProgressBar (0..100), счётчиком `done / total` и кнопкой «Отмена».
   `MainViewModel.RunWithProgressDialogAsync` оборачивает
   `CopyManyAsync` / `MoveManyAsync` / `DeleteManyAsync` (HandleDrop /
   Paste / DeleteSelected). Кнопка дёргает `CancellationTokenSource`,
   batch-операция сама раскладывает Cancelled-результаты, диалог
   авто-закрывается при завершении задачи через `Task.ContinueWith` +
   `Dispatcher.BeginInvoke`. Без Hide-в-статус-бар — это P2-доработка.
3. ~~**A5 Sort menu** + **A6 .lnk-папки рядом с папками**~~ — **сделано**.
   View-меню получило Sort-подменю (Name / Modified / Size / Type +
   Ascending + Group folders first) через `SetSortKeyCommand` /
   `ToggleSortAscendingCommand`, состояние в `AppSettings.SortKey` /
   `SortAscending` / `GroupFoldersFirst`, применение — через
   `EntryComparers`.
4. ~~**#4 "Cannot drop here" не пугает внутри окна**~~ — **сделано**.
   `UpdatePreviewForCurrentTarget` теперь прячет `DragPreviewWindow`,
   когда `Effects=None` и нет конкретной `SelfDropReason` —
   системный no-drop-курсор остаётся, красная плашка не маячит над
   соседними валидными папками. Self-drop с причиной по-прежнему
   показывается громко (там сообщение действительно полезно).
5. ~~**A4 opacity для visible-hidden**~~ — **сделано**. Implicit-стили
   `DataGridRow` / `ListBoxItem` в `MainWindow.xaml` получили
   DataTrigger по `IsHidden` / `IsSystem` → `Opacity=0.55`. Заметно,
   но не отвлекает.
6. ~~**A9 Click-empty=unselect**~~ — **сделано** (через A10:
   `StartRubberBand` без Ctrl вызывает `ClearListSelection` сразу при
   MouseDown, прежде чем растягивать лассо; чистый клик в пустоту с
   немедленным MouseUp оставляет пустое выделение).
   **A10 Rubber-band selection** — **сделано**.
7. ~~**A2 Затемнить дерево**~~ — **сделано**. `Background="#F8F8F8"` на
   левой колонке (обёртка над bookmarks-панелью и деревом дисков).
8. ~~**G1+G2 Search basic**~~ — **сделано**. Узкая TextBox справа от
   адресной строки, фокус по `Ctrl+F` (с пропуском, когда фокус внутри
   AvalonEdit code-preview). `MainViewModel.SearchQuery` фильтрует
   `Entries` по `Name.Contains(query, OrdinalIgnoreCase)`. Фильтрация
   асинхронная (Task.Run + CancellationTokenSource на каждый keystroke,
   снапшоты `query` / `_allEntries` чтобы не гоняться с Refresh).
   `Esc` чистит запрос, повторный `Esc` / `Enter` отдают фокус активному
   списку. Навигация в новую папку сбрасывает фильтр. Status-bar
   показывает `X of Y items match "query"` при активном фильтре.
   Дерево не трогается. Расширенный поиск — G3 (по-прежнему P3).
9. ~~**D Favorites**~~ — **частично сделано (фаза 1)**. См. секцию D ниже:
   панель закладок над деревом дисков, сворачиваемая, drag-add, контекстное
   меню «Убрать из закладок», persist в `AppState.Favorites` /
   `AppState.IsBookmarksExpanded`. Три дефолта-спец-папки: «Загрузки»,
   «Документы», «Изображения» — через `IKnownFolders` + `SHGetKnownFolderPath`
   (FOLDERID_Downloads / Documents / Pictures). Каждая — отдельный чекбокс
   в категории «Закладки». Корзина — отдельный шаг ниже.
10. ~~**D5. Корзина внутри Wander (shell-namespace)**~~ — **шаги 1-2 сделаны**
    (viewer готов; Restore / Delete внутри корзины — шаг 3, см. спринт 4). Было:
    `IShellNamespace` в Core: enumerate shell items под `FOLDERID_RecycleBinFolder`,
    маппинг shell-item → `FileSystemEntry`-аналог, операции Restore /
    Empty / Delete-permanent через `IFileOperation` (Vista shell). После —
    добавить «Корзину» как третий дефолт в bookmarks + третий чекбокс в
    настройках. Влечёт shell-интеграцию, отложили намеренно.

### Спринт 4 (текущий фокус — P2)

Закрытые P1 из спринта 3:

1. ~~**A5 Sort menu (UI)**~~ — **сделано**, см. спринт 3 п.3.
2. ~~**D5 шаг 2 — Корзина в bookmarks**~~ — **сделано**. Bookmark-узел
   «Корзина» (`ShellPaths.RecycleBin`), чекбокс
   `AppSettings.ShowBookmarkRecycleBin`, роутинг `shell:`-путей через
   `NavigationController` → `IShellNamespace`.
3. ~~**Чистка #2**~~ — **сделано**. `ClipboardController` и
   `SearchController` уехали в Core, `SessionState` выделен подrecord'ом
   в `AppState`.

4. ~~**L. Контекстное меню**~~ — **сделано**. Меню в духе Win10, буфер обмена
   в подменю «File», пункты сторонних приложений (7-Zip / TortoiseGit /
   Notepad++ / системное «Создать») с иконками и подменю, страница настроек
   с выключением по одному. Детали — секция **L** ниже и
   [ARCHITECTURE.md](ARCHITECTURE.md).

Открытые P2:

5. **A3 Inline rename** — сейчас только `PromptDialog`; нет rename-in-place
   в строке списка (как в Explorer по F2).
6. **A7 Thumbnails картинок** + **A8 Folder content preview** (требуют E3).
7. **F2 SettingsDialog: расширить** (Theme, Behavior, Advanced) + **F3 Reset**.
8. **B2** — content для папки в preview (после согласования вида).
9. **A1** — применить settings-binding к шаблонам Tiles/Details, перебрать
   defaults.
10. **D5 шаг 3 — Restore/Delete в корзине** — операции через `IFileOperation`
   (Vista shell COM). Без них корзина в Wander — только viewer.

### Дальше (P3)
- **G3** Advanced search.
- **H1** desktop.ini.
- **H2** ~~Unity .meta-партнёрство~~ → поглощено секцией **I** (companion-файлы).

### Новые направления (добавлены 2026-08-25)

- **I. Companion-файлы** — объединённое отображение файла со спутниками
  (`.meta`, `.pp3`, XMP), групповые операции, оценки фото. **[P2]**
- **J. Режим галереи** — автовключение на графических папках, день/ночь,
  фильтр по ISO / выдержке / рейтингу. **[P2]**, опирается на I и E3.
- **K. Надёжное копирование больших файлов** — robocopy + verify через
  certutil. **[P2/P3, сначала исследование]**

---

## Технические чистки

Между крупными вехами проекта останавливаемся и спрашиваем: "а структуру кода
надо ли освежить?". Это не TECHDEBT (там мелочёвка по ходу) и не BACKLOG (там
конкретные фичи). Это **code health pass** — ревизия структуры, тестов и
документации без новых пользовательских фич.

**Что смотрим на каждом проходе:**

- Не пора ли **разделить класс**? Признаки: длинный файл (>500 строк), несколько
  смешанных ответственностей, тесты на разные сценарии живут в одном файле.
- Не пора ли **слить два класса**? Признаки: один всегда таскает второй, второй
  без первого не используется, проектные зависимости циклические.
- **Public API**: какие методы / типы перестали быть нужны снаружи и могут уйти
  во `internal`.
- **Тесты**: что покрыто, что нет, нет ли мёртвых fakes.
- **Doc-комментарии**: появились ли публичные типы без XML-doc.
- **`TECHDEBT.md`**: пора ли выбрать пакет пунктов на разовое закрытие.

**Когда проводить:**

Не по календарю, а по большим вехам. Грубый ориентир: после фичи, которая
**меняет публичный API Core** (новые async-overload-ы, новый интерфейс,
изменение сигнатур service-классов).

### Запланированные проходы

- ~~**Чистка #1 — после E (async операций).**~~ — **сделано**.
  - `BatchExecutor` принял на себя `ApplyBatch` / `DeleteManyCore` /
    `PushComposite` / `ApplyOne` и conflict-loop; `FileOperationService`
    стал тонким фасадом ~120 строк (был ~370).
  - Типы результатов (`BatchItemResult`, `DeleteResult` + соотв. enum) подняты
    на уровень namespace `Wander.Core.FileSystem`, чтобы caller не лез
    через фасад.
  - VM-сторона: `PreviewController` забрал всю preview-логику (Kind /
    Image / Text / Code / Web / Summary + расширения, async-pipeline,
    cancellation), MainVM стал ~970 строк (был ~1330) и кормит контроллер
    через `SetVisible / SetPrimary / SetSelection / SetCurrentFolder`.
  - `SelectionController` забрал deferred-selection + active-list clear
    из MainWindow.xaml.cs — отдельная коробка под будущие A9 / A10.
  - `WindowGeometry` подrecord на `AppState` — geometry больше не
    разъезжается по top-level полям. Migration: state.json от старых
    билдов потеряет позицию окна один раз (pre-1.0, ОК).
  - Не вошло (отложено как самостоятельные задачи):
    - Унифицировать sync- и async-public API — sync single-item Copy/Move/Delete
      ещё нужны тестам, объединять не стали; запись в TECHDEBT уже была.

  Сразу же подняли покрытие тестами (см. `Wander.Core.Tests`, 98 тестов
  на момент закрытия): `BatchExecutorTests` (19 кейсов, конфликты /
  отмена / failed items / прогресс), `UndoServiceTests` (busy-guard,
  nested guards, async race), `OperationTrackerTests` (Begin/Advance/
  Dispose + конкуррентный Advance), `UndoableActionsTests` (composite
  reverse-order, single-action round-trips), `PathSafetyTests` (после
  переноса `PathSafety` из `Wander.App.Util` в `Wander.Core.FileSystem`
  ради тестируемости — sibling-substring kейс, case-insensitive,
  нормализация trailing slash, и т.д.).
- ~~**Чистка #2 — после D + G + Settings**~~ — **сделано**.
  - `ClipboardController` (Core/FileSystem) — забрал Copy/Cut/Paste
    state (`_paths`, `_isCut`) + событие `Changed` для рефреша
    `PasteCommand.CanExecute`. MainVM делегирует через тонкий фасад.
  - `SearchController` (Core/FileSystem) — забрал `SearchQuery` +
    async-фильтр + кэш `_allEntries` + cancel-on-keystroke. Источник
    данных — `IReadOnlyList<FileSystemEntry>` от MainVM через
    `SetSource(...)`; результат назад через `FilteredChanged`.
    INPC реализован inline (без ObservableObject) — Core-чистота.
  - `NavigationController` (App/ViewModels) — обернул
    `NavigationService` + `AddressText` + Back/Forward/Up/NavigateCommand
    + `WindowTitle` derivation. MainVM держит только side-effects
    (Refresh / Preview / SaveState) через подписку на `CurrentChanged`.
    Path-validation и shell-namespace lookup инжектятся как callbacks.
  - `SessionState` подrecord (Core/Persistence) — поднял из top-level
    `AppState` (`LastPath`, `ViewMode`, `ExpandedPaths`, `IsPreviewVisible`,
    `PreviewWidth`, `IsBookmarksExpanded`). Теперь structure:
    `Session` / `Favorites` / `Window` / `Settings` — четыре чётких
    бакета. Migration: state.json от старых билдов потеряет session
    один раз (pre-1.0, ОК).
  - **MainViewModel** ужался ~1380 → ~1230 строк. Чистые контроллеры
    тестируются напрямую (`ClipboardControllerTests` 10 кейсов,
    `SearchControllerTests` 13 кейсов — async race / case-insensitive /
    cancel-on-keystroke / source rotation). Покрытие: 104 → 126 тестов.
  - Не вошло (отложено как самостоятельные задачи):
    - `MainWindow.xaml.cs` — drag/drop / tree handlers / rubber-band
      / hotkeys всё в одном файле; behavior-классы — кандидат на
      Чистку #3.
    - `PathSafety` остался в Core (там реально полезен и без DnD —
      в planned merge-path для папок).

- **Чистка #3 — после спринта 4** (приятное P2): когда A3 (Inline rename),
  A7/A8 (Thumbnails) и B2 (Folder preview) приедут. Кандидаты:
  - Разнести `MainWindow.xaml.cs` (~1400 строк): drag-source + drop-target +
    rubber-band + tree gestures + image-zoom + video — в behavior-классы.
  - Унифицировать sync- и async-API `FileOperationService` —
    sync single-item Copy/Move/Delete сейчас держится только ради тестов,
    можно убрать в `internal`.
  - Пересмотреть `TreeNodeViewModel` — он накопил bookmark-флаги,
    drives-флаги, lazy-load semantics. Возможно `BookmarkNodeViewModel`
    отдельно.
  - Тесты на `NavigationController` (сейчас покрытие идёт через
    `NavigationService` напрямую; обёрточный controller с командами и
    AddressText стоит покрыть, когда добавим `IDispatcher`-абстракцию).

При планировании следующих чисток — добавлять сюда новые подзаголовки.

---

## Что готово (фактически)

### Архитектура
- 4 проекта на .NET 10: `Wander.Core` (логика, abstractions), `Wander.Platform.Windows`
  (Win32/Shell/COM), `Wander.App` (WPF UI), `Wander.Core.Tests` (xUnit).
- Service Locator как точка композиции, без DI-контейнеров.
- File-scoped namespaces, 1TBS, `.editorconfig` соблюдается `dotnet format`-ом.
- Tests зелёные на все ключевые abstractions.

### Core-инфраструктура
- `IFileSystem` / `SystemIOFileSystem` — все базовые ops + `HasSubdirectories` + `GetEntry` + `ClearReadOnly`.
- `IShortcutService` / `ShellShortcutService` — создание и resolve `.lnk` через COM IShellLinkW.
- `IRecycleBin` / `ShellRecycleBin` — корзина через Shell. `RecycleHandle` для restore.
- `UndoService` — общий LIFO-стек, `IUndoableAction` (Move / Rename / Delete / Create / Composite).
  `BeginOperation()`-guard уже готов под будущие async-операции.
- `ILogger` / `NullLogger` — минимальный лог-контракт.
- `IConflictResolver` — стратегия для batch copy/move (Replace all / Skip all / Resolve each).
- `IFileLockInspector` / `RestartManagerLockInspector` — кто держит файл открытым.
- `IIconProvider` / `SystemIconProvider` — system icons + .lnk overlay, jumbo size.
- `IImageMetadataReader` / `MetadataExtractorImageReader` — EXIF включая RAW.
- `IAppStateStore` / `JsonAppStateStore` — `%LocalAppData%\Wander\state.json`.

### Файловые операции
- `FileOperationService` — единая точка входа, пушит `IUndoableAction` в `UndoService`,
  логирует через `ILogger`. `Delete` отправляет в корзину; `PermanentDelete` обходит
  её и затирает undo-стек.
- `CopyMany`/`MoveMany` с conflict resolver и agregated `BatchItemResult[]`.
- Cross-device move папок: fallback `CopyDirectory + DeleteDirectory`.
- Read-only delete: явный список + второй вопрос; снятие атрибута перед удалением.
- Self-drop защита с понятным текстом ("Cannot move 'photos' into its own subfolder '2024'").
- Locked files: RestartManager → текст вида "file is open in: Word (PID 1234)".

### UI / Навигация
- Дерево дисков и папок, lazy-load, листовые папки без треугольника, сохранение
  раскрытых путей, авто-раскрытие на текущую папку, никогда не сворачивается само.
- Alt+click по шеврону: на свёрнутом — раскрывает узел + прямых детей; на раскрытом —
  рекурсивно сворачивает потомков.
- Toolbar: borderless навигационные кнопки, адресная строка, View-меню с галочками
  (Details / Tiles / Large icons), главное меню (⋯) c Refresh / New folder / Quick preview /
  Options (stub) / Exit. Заголовок окна = имя текущей папки.

### Контекстное меню
- Строится заново на каждый правый клик: `ContextMenuBuilder` (Core, чистая
  функция) → `ContextMenuFactory` (App, WPF). Разметки меню в XAML нет —
  три вьюхи (Details / Tiles / LargeIcons) зовут один код.
- Правый клик по элементу вне выделения переносит выделение на него, внутри —
  сохраняет группу; по пустому месту снимает выделение и даёт меню папки.
  `Menu` / `Shift+F10` открывают то же меню с клавиатуры.
- Два разных меню: по выделению и по фону. Буфер обмена, `Copy path`,
  `Copy name`, `Create shortcut` — в подменю «File»; `Paste` и `New folder`
  первыми в фоновом меню.
- Пункты сторонних приложений (7-Zip, TortoiseGit, Notepad++, антивирусы,
  системное «Создать») — через классический `IContextMenu`, с иконками и
  вложенными подменю. Дубли режутся по каноническому глаголу.
- Настройки → «Контекстное меню»: мастер-выключатель расширений, чёрный
  список по одному, сворачивание в подменю «More options», галочка на каждый
  свой пункт. Разделители схлопываются сами.
- Новые команды: `Open with`, `Show in Explorer`, `Open in Terminal`,
  `Copy path` (`Ctrl+Shift+C`), `Copy name`, `Create shortcut`.
  `Alt+Enter` без выделения теперь открывает свойства текущей папки.

### Drag & drop
- Внутри окна и внешний (FileDrop). Effect: same-drive Move / cross-drive Copy /
  Shift=Move / Ctrl=Copy / Alt=Shortcut.
- Drag preview: иконка файла + бейдж `+N`, action-индикатор (↪/＋/↗/⊘), текст
  с именем и целью. DPI-correction.
- Drop target highlight (Adorner). Drop в `.lnk` папки → drop в реальную папку.
- Подтверждения с Cancel по-умолчанию для Delete и Move (включая DnD).

### Multi-select + clipboard
- Extended во всех трёх режимах. Deferred selection при click-and-drag сохраняет
  multi-selection. `SelectedEntries` обновляется из всех списков.
- Cut/Copy/Paste/Delete работают со множеством; одно общее подтверждение Move/Delete.
- Hotkeys: `Ctrl+A`, `Ctrl+C/X/V`, `Del`, `F2`, `Enter`, `Backspace`,
  `Ctrl+Shift+N`, `Ctrl+L`, `Esc`, `Alt+Enter`, `Alt+←/→/↑`, `F5`.

### Preview pane
- Toggle через главное меню, ширина настраивается splitter-ом, состояние сохраняется.
- Контент: Image, Text, Code (AvalonEdit с подсветкой), Web (WebView2 для PDF/HTML/Markdown).
- Все загрузки async с cancellation и спиннером.
- Footer summary: пустой выбор → текущая папка (рекурсивный count+size, async),
  файл → name/size/modified (+ EXIF для картинки), папка → count+size, multi → агрегат.
- EXIF включая RAW (CR2/CR3/NEF/ARW/DNG/...) через MetadataExtractor.

---

## Незавершённое из готового

Core готов, нужно дотянуть в UI / VM.

- ~~**[P0] `UndoService` → UI**~~ — **сделано**. `UndoCommand` в VM,
  `Ctrl+Z` в Window.InputBindings (видно из grep).
- ~~**[P0] `Shift+Delete` → `PermanentDelete`**~~ — **сделано**. VM
  использует `_ops.PermanentDelete` для permanent path, async версия
  `DeleteManyAsync(permanent: true)` для batch.
- ~~**[P0] Лог в файл**~~ — **сделано**. `FileLogger` пишет в
  `%LocalAppData%\Wander\logs\session-yyyymmdd-hhmmss.log`, регистрируется
  первым в `PlatformBootstrapper`. Заодно зарегистрирован как `ILogFile`
  (новый интерфейс).

---

## Багфиксы (P0)

### 1. ~~[P0] Ссылка не создаётся в ту же папку~~ — **сделано**
- В `OnDrop` стояла безусловная `PathSafety.DetectSelfDrop` → при Alt+drag
  в ту же папку early-return. В `OnDragOver` self-drop уже пропускался при Alt,
  но в `OnDrop` забыл сделать то же. Добавил `isLink` check симметрично.

### 2. ~~[P0] У ссылки не отображается значок-overlay (стрелочка)~~ — **частично сделано**
- Добавил `SHGFI_LINKOVERLAY` в `LoadShellIcon` для `.lnk` — Tree (Small),
  Details (Normal), Tiles (Normal) теперь рисуют overlay-стрелочку.
- **Остаётся** LargeIcons (jumbo через `SHGetImageList`) — system overlay
  там не накладывается. Запись в [TECHDEBT.md](TECHDEBT.md) про composite
  arrow поверх jumbo PNG.

### 3. ~~[P0] Drop highlight расширяется на всё раскрытое содержимое~~ — **сделано**
- В `FindHighlightElement` для TreeViewItem теперь возвращается `Bd`-part
  (Border в default Aero2 template) через `tvi.Template.FindName`. Подсветка
  накрывает только строку узла, не subtree. Fallback на сам TreeViewItem
  если шаблон без `Bd`.

### 4. ~~[P1] "Cannot drop here" пугает при drag внутри окна~~ — **сделано**
- Внутри окна при `Effects=None` без конкретной `SelfDropReason`
  `DragPreviewWindow` теперь `Visibility=Hidden` — пользователь видит
  только системный no-drop-курсор. Self-drop с причиной по-прежнему
  показывается громко (см. `UpdatePreviewForCurrentTarget`).

### 5. ~~[P0] "Двойное выделение" = focus rectangle + selection background~~ — **сделано**
- Добавил implicit `Style` для `DataGridRow`/`DataGridCell`/`ListBoxItem`
  с `FocusVisualStyle = {x:Null}`. Для `TreeViewItem` тот же setter
  добавлен в существующий ItemContainerStyle. Focus всё ещё трекается
  семантически (клавиатурная навигация работает), но пунктирная рамка
  не рисуется поверх selection-фона.

---

## Обязательные цели

### A. Вёрстка / UI

- **[P2] A1. Spacing и размеры иконок.** В Tiles 220×40 — иконка 32 px, в LargeIcons —
  плитки 120 px / иконка 96 px. Пересмотреть отступы между плитками, размеры
  самой плитки, дать побольше воздуха.
  **Частично сделано** для LargeIcons: значения вынесены в `AppSettings`
  (`LargeIconCellWidth`/`Image`/`Margin`/`LabelFontSize`) — пользователь
  может менять. Остаётся: применить эти binding-и в XAML LargeIcons, плюс
  то же самое для Tiles и Details, плюс перевыбрать defaults.
- ~~**[P1] A2. Чуть затемнить область дерева.**~~ — **сделано**.
  `Background="#F8F8F8"` на левой колонке (обёртка над bookmarks-панелью
  и деревом дисков).
- **[P2] A3. Inline rename "на иконке".** F2 / клик-задержка превращает имя
  в TextBox прямо в строке/плитке (как в Explorer). PromptDialog оставить
  fallback'ом, когда primary selection отсутствует.
- ~~**[P1] A4. Toggle "Show hidden" + opacity.**~~ — **сделано**.
  `Settings.ShowHidden` / `Settings.ShowSystem` фильтруют `Entries` в
  MainViewModel, по умолчанию обе скрыты (Explorer-parity). Когда тогглы
  включены — implicit-стили `DataGridRow` / `ListBoxItem` получают
  DataTrigger по `IsHidden` / `IsSystem` → `Opacity=0.55`. Заметно,
  но не отвлекает.
- **[P1] A5. Sort menu в View.** После view-modes (Details/Tiles/LargeIcons) —
  разделитель, потом группа "Sort by" → Name / Date modified / Size / Type,
  Asc/Desc как отдельные пункты, "Group folders first" чекбоксом. Применяется
  ко всем view-modes. Сохранять в `AppSettings.SortKey` + `AppSettings.SortAscending` +
  `AppSettings.GroupFoldersFirst`.

  **Сейчас**: sort захардкожен в `SystemIOFileSystem.Enumerate` —
  natural-sort (StrCmpLogicalW), папки и folder-shortcuts вверху, файлы внизу.
  Пользователь не может переключить. Меню остаётся последним P1 пунктом.
- ~~**[P1] A6. Сортировка .lnk-папок рядом с папками.**~~ — **сделано на
  уровне ФС**. `FileSystemEntry.LinksToDirectory` populates через
  `SystemIOFileSystem.IsFolderShortcut` (resolve .lnk + Directory.Exists),
  `IsFolderLike` computed property. В `Enumerate` folder-shortcuts уходят
  в группу `folderLikes` рядом с папками и сортируются вместе. Когда
  появится A5, эта же группировка ляжет под чекбокс "Group folders first".
- **[P2] A7. Thumbnails для изображений.** Вместо generic file icon — реальный
  превью самого изображения 32-48 px. Через `IShellItemImageFactory.GetImage`.
  Требует async (E3) + cache по path+mtime.
- **[P2] A8. Folder content preview.** Win10-style: за иконкой папки видны
  миниатюры файлов из неё. Через `IShellItemImageFactory.GetImage` для папки —
  shell сам соберёт миниатюру. Требует E3.
- ~~**[P1] A9. Клик в пустое пространство → снять выделение.**~~ —
  **сделано** (как side-effect A10): `StartRubberBand` без Ctrl
  вызывает `ClearListSelection` сразу на MouseDown, чистый клик в
  пустоту с немедленным MouseUp оставляет пустое выделение.
- ~~**[P1] A10. Rubber-band selection.**~~ — **сделано**. `Controls/RubberBandAdorner.cs`
  + hit-test в MainWindow.
- ~~**[P1] A11. Полупрозрачный системный курсор "невозможно" внутри окна.**~~ —
  **сделано** вместе с #4: `DragPreviewWindow` прячется в no-drop-зонах,
  остаётся только системный курсор.

### B. Preview pane

- ~~**[P0] B1. `Image.StretchDirection=DownOnly`.**~~ — **сделано**. Маленькие
  картинки больше не растягиваются на контейнер.
- **[P2] B2. Папка в preview.** Когда выбрана папка или ничего, content-area
  должна показывать что-то лучше чем placeholder. Варианты для обсуждения:
  список первых N файлов с превью / grid миниатюр / компактная статистика
  типов. Уточним при подходе.
- **[P1] B3. Под content общая инфа уже есть в summary footer** — проверить
  что для image/text она пишется корректно. Скорее всего обернётся в smoke-check.

### C. Выбор файлов / селект — см. A9, A10 + багфикс #5.

### D. Favorites (избранное) — [P1]

- ~~**D1.** Раздел "Bookmarks" над деревом дисков с раскрываемым узлом.~~
  — **сделано**. Свой Grid в левой колонке: заголовок-сворачиватель
  (`ToggleBookmarksCommand`, состояние `IsBookmarksExpanded` persist),
  `BookmarksTree` с `ItemsSource={Binding Bookmarks}`, тот же ItemTemplate
  что у дерева дисков, MaxHeight=280 чтобы при большом списке появлялся
  скролл и не выдавливалось дерево дисков ниже.
- ~~**D2.** Drag папки в раздел + context-menu remove.~~ — **сделано**.
  Дроп любых папок на `BookmarksPanel` (через `BookmarksPanel_Drop`)
  добавляет их через `Vm.AddBookmark`. Файлы игнорируются с понятным
  статусом. Контекст-меню «Убрать из закладок» работает только на
  пользовательских закладках (флаг `TreeNodeViewModel.IsRemovableBookmark`),
  для спец-папок IsEnabled=false.
- ~~**D3.** Дефолтные спец-папки — Загрузки + Документы + Изображения.~~ —
  **сделано**. Все три — реальные пути через `IKnownFolders` (новый
  интерфейс в Core, реализация в `Wander.Platform.Windows.FileSystem.WindowsKnownFolders`
  через `SHGetKnownFolderPath` для FOLDERID_Downloads / Documents / Pictures).
  Чекбоксы: `AppSettings.ShowBookmarkDownloads` / `ShowBookmarkDocuments`
  / `ShowBookmarkPictures`. «Этот компьютер» рассматривался, но отложили
  до D5 (через IShellNamespace) — нет смысла в синтетическом узле, который
  дублирует дерево дисков визуально.
- ~~**D4.** Сериализация.~~ — **сделано**. `AppState.Favorites`,
  `AppState.IsBookmarksExpanded`. Раскрытое состояние bookmark-папок
  включено в `ExpandedPaths` (CollectExpanded теперь итерирует и
  `Bookmarks`, и `Roots` с дедупом).
- ~~**D5. Корзина внутри Wander — базовое отображение.**~~ — **сделано
  частично (фаза 1: read-only)**. Введена абстракция `IShellNamespace`
  в `Wander.Core.Shell` (`IsShellPath` / `Enumerate` / `GetDisplayName`)
  и константа-сентинел `ShellPaths.RecycleBin = "shell:RecycleBinFolder"`.
  Реализация — `WindowsShellNamespace` в `Wander.Platform.Windows.Shell`,
  enumerate через `Shell.Application` COM с тем же dynamic-паттерном,
  что в `ShellRecycleBin.Restore`. Каждый item → `FileSystemEntry`
  с `FullPath` = реальным путём внутри `$Recycle.Bin\…` (иконка
  ловится system icon provider'ом, shell-launcher может его открыть),
  `Name` = оригинальное имя, `ModifiedUtc` = дата удаления (из
  GetDetailsOf column 2 с тем же locale-bound парсером).
  MainViewModel: `NavigateTo` пропускает `_fs.DirectoryExists` для
  shell-путей, `Refresh` ходит через `IShellNamespace`, `WindowTitle`
  берёт display name, `BuildBookmarks` добавляет «Корзину» как четвёртый
  дефолт. Деструктивные команды (Cut/Copy/Paste/Delete/PermanentDelete/
  Rename/NewFolder) гейтятся через `IsCurrentShellNamespace` — внутри
  корзины их CanExecute=false, чтобы пользователь случайно не оперировал
  по $Recycle.Bin путям в обход shell-восстановления. Чекбокс
  `AppSettings.ShowBookmarkRecycleBin` в категории «Закладки».
- **[P1] D5b. Корзина: операции (Restore / Empty / Delete-permanent).**
  Уже есть `IRecycleBin.Restore(RecycleHandle)` для отката собственных
  удалений Wander'а, но Restore произвольного item'а из корзины (когда
  пользователь не помнит handle) и Empty/Permanent-delete по одному
  файлу требуют отдельных операций. План:
  - Расширить `IRecycleBin`: `RestoreFromBin(string binBackingPath)`
    (матчинг по `FolderItem.Path` в `Items()`, invoke локализованного
    verb'а Restore — reuse `IsRestoreVerb` из `ShellRecycleBin`),
    `PermanentDelete(string binBackingPath)` (через `SHFileOperation`
    FO_DELETE без `FOF_ALLOWUNDO`), `EmptyAll()` (через
    `SHEmptyRecycleBin` с `SHERB_NOCONFIRMATION | NOPROGRESSUI | NOSOUND`,
    свой confirm-диалог поверх).
  - UI (решено: только context-menu, без header-бара): на entries в
    правой панели — «Восстановить» / «Удалить навсегда», `Visibility`
    через `BoolToVisibility` от `IsCurrentShellNamespace`. На самой
    закладке-«Корзина» в bookmarks-дереве — отдельный context-menu
    с «Очистить корзину» (показывать только для этого узла).
  - Дальше — drag-out из корзины как «Restore + Move» (Explorer-parity),
    отдельным шагом.
  - ~~Иконка корзины~~ — **сделано** в этой итерации (см. D5).
- **[P3] D6. Drag-reorder bookmarks.** Перетаскивание пользовательских
  закладок между собой для смены порядка. Сейчас порядок = порядок
  добавления, изменения через ручную правку state.json. Делать
  при первом запросе.
- ~~**D7. Source-aware авто-разворот дерева + persistence.**~~ — **сделано**.
  Каждая навигация несёт `NavigationSource` (Drives / Bookmark / Address /
  RightPane / Restore / External). `NavigationService` хранит
  `List<NavigationEntry>` + cursor вместо двух стеков; Back/Forward
  возвращают исходный source. `ExpandTreeToCurrent` для source=Bookmark
  разворачивает только панель закладок (с fallback на drives, если
  путь больше не в закладках — пример: пользователь убрал закладку
  между визитами). Для остальных source — только drives. Заодно
  чистим `IsSelected` на предыдущем узле, чтобы при прыжках между
  панелями не оставалось «двойное выделение».
  
  Persistence — отдельный record `NavigationStop(Path, Source)` (живёт в
  `Wander.Core.Navigation`): `AppState.LastPath` и `AppState.ExpandedPaths`
  переведены на него. `CollectExpanded` собирает раскрытое отдельно по
  панелям (Drives для Roots, Bookmark для Bookmarks) — один и тот же путь
  может быть раскрыт независимо в обеих панелях. `RestoreState`
  восстанавливает drives-side стопы сразу, bookmark-side — внутри
  `BuildBookmarks` (после того как закладки построены). `JsonAppStateStore`
  пишет enum как строку через `JsonStringEnumConverter`, чтобы state.json
  оставался человекочитаемым. Старый формат state.json теряется
  (`JsonSerializer.Deserialize` ловится try/catch → дефолтный `AppState`).
  Слом схемы зафиксирован в TECHDEBT.

### E. Async операции — [P1, главный фокус]

- ~~**E1.**~~ — **сделано**. `CopyManyAsync` / `MoveManyAsync` /
  `DeleteManyAsync(permanent)` в `FileOperationService`, докладывают прогресс
  в общий `OperationTracker` (Core/Operations). Sync `CopyMany`/`MoveMany`
  оставлены как тонкие обёртки. `DispatcherConflictResolver` маршалит conflict
  dialogs обратно на UI thread с фонового потока.
- ~~**[P1] E2. `ProgressDialog`.**~~ — **сделано (базовый)**.
  `Views/ProgressDialog.xaml`: заголовок ("Копирование" / "Перемещение" /
  "Удаление" / "В корзину"), текущий файл, ProgressBar 0..100, счётчик
  `done / total`, кнопка «Отмена». Подписан на `OperationTracker.Changed`,
  пересчитывает прогресс из первой in-flight операции снапшота. Кнопка
  «Отмена» (и крест в углу) дёргает `CancellationTokenSource`, batch
  раскладывает Cancelled-результаты. `MainViewModel.RunWithProgressDialogAsync`
  оборачивает Copy/Move/Delete batch — три точки вызова (HandleDrop /
  Paste / DeleteSelected). Hide-в-статус-бар отложено (P2-доработка).
- **[P2] E3. Async thumbnails.** A7 / A8 поверх async-инфраструктуры. Cache в памяти
  + опционально на диске (`%LocalAppData%\Wander\thumbs\`) с ключом по path+mtime.
- ~~**E4.**~~ — **сделано в Core**. `UndoService.BeginOperation()` ловит
  busy-period, `CanUndo` ложится при наличии активной операции. Покрытие
  тестом проверить можно отдельно.

### F. Settings — [P1 для расширения state.json, P2 для окна]

- ~~**F1. Всё в `state.json`**~~ — **сделано**, но в более чистом виде, чем
  планировалось: пользовательские настройки выделены в отдельный record
  `AppSettings` (RestoreLastFolder, ShowHidden, ShowSystem, LargeIcon* tuning,
  ShowDebugMenu) и встроены в `AppState.Settings`. Session-state (`LastPath`,
  expanded, preview, window geometry) живёт там же, но разнесён по другому
  под-полю.
- ~~**F2. SettingsDialog (основа)**~~ — **сделано базово**. `SettingsViewModel`
  с категориями (General / Safety / Layout / Debug), `SettingsCategoryViewModel`
  иерархия, `SettingsWindow.xaml`. Открытие из главного меню — проверить
  что пункт Options теперь зовёт реальный диалог, а не stub. Расширение
  категорий (Theme, Behavior, Advanced) — по мере роста скоупа.
- **[P2] F3. Reset to defaults.** Кнопка в SettingsDialog —
  `new AppSettings()` + save. Не подтверждено существование.

### G. Поиск — [P1 для базы, P3 для advanced]

- ~~**[P1] G1.** Search input в toolbar справа от адресной строки.~~ —
  **сделано**. Узкая TextBox 200px справа от `AddressBox` (Dock=Right
  после правых меню → визуально слева от них). Placeholder
  "Search in this folder" — TextBlock поверх с DataTrigger по
  `HasSearchQuery`. Фокус по `Ctrl+F` (пропускается, если фокус внутри
  AvalonEdit code-preview).
- ~~**[P1] G2.** Простой фильтр: `Name.Contains(query, IgnoreCase)`.~~ —
  **сделано**. `SearchQuery` setter триггерит `ApplyFilterAsync`:
  `Task.Run` фильтрует `_allEntries` через cancellation token. Каждый
  новый keystroke отменяет предыдущий проход — UI не фризим даже на
  больших папках. Esc чистит запрос (повторный — отдаёт фокус списку),
  навигация в другую папку тоже чистит. Status-bar показывает
  `X of Y items match "query"`. Дерево не трогается.
- **[P3] G3. Advanced.** Маленький значок ⋮ в search input → panel/dialog:
  recursive (по подпапкам), фильтр по типу/расширению, по дате/размеру,
  regex. Уточним scope отдельно.

### H. Спец-возможности — [P3]

- **H1. desktop.ini.** При смене view-mode в папке — опционально писать в
  desktop.ini. Уточнить с пользователем: "учитывать чужой" или "писать свой"
  или оба.
- ~~**H2. Unity .meta-партнёрство.**~~ — **поглощено секцией I** ниже.
  Оказалось частным случаем общего механизма companion-файлов, отдельной
  задачей больше не идёт.

---

### I. Companion-файлы: объединённое отображение и оценки фото

Общий механизм «файл + его спутник(и) = одна сущность». Unity `.meta`
(бывший H2) и RawTherapee `.pp3` — два частных случая одного и того же.

#### I1. Объединённое отображение — [P2, фундамент]

Отдельный флаг в настройках. Когда включён:

- В списке файлов показывается **только основной файл**, спутники скрыты.
- Признак наличия спутника — ненавязчивый маркер в строке (иконка/бейдж),
  чтобы было видно, что файл «с довеском».
- Выключенный флаг = текущее поведение, всё видно как есть.

**Ключевой архитектурный момент — два разных шаблона имён:**

| Шаблон | Пример | Кто так делает |
|---|---|---|
| `имя.расш.спутник` (дописывается) | `Sprite.png.meta`, `IMG_1234.CR2.pp3` | Unity, RawTherapee, Google Takeout |
| `имя.спутник` (заменяет расширение) | `IMG_1234.xmp`, `IMG_1234.AAE` | Adobe/darktable XMP, iPhone |

Правило сопоставления обязано поддерживать оба, иначе половина форматов
отвалится. Резолвер companion'ов — в Core, отдельной абстракцией; конкретные
форматы регистрируются в него как правила.

#### I2. Групповые операции — [P2, сразу за I1]

Все операции над основным файлом идут **вместе со спутниками**: Move, Copy,
Rename, Delete, отправка в корзину.

Требования, вытекающие из столпов проекта:

- **Атомарность по возможности.** Если перенос основного удался, а спутника
  нет — не оставлять расползшуюся пару молча. Либо докатывать, либо внятно
  сообщать.
- **Undo — одной записью.** Группа кладётся в `UndoService` как один
  composite: `Ctrl+Z` возвращает и файл, и спутника разом. Механика уже есть,
  `BatchExecutor` умеет composite'ы.
- **Конфликты считаются по группе**, а не по каждому файлу отдельно — иначе
  пользователь получит два диалога на одну логическую операцию.

#### I3. Инфо из спутника в быстром просмотре — [P2]

Footer/summary превью показывает важное из меты:

- **Unity `.meta`** — GUID (с возможностью скопировать: он нужен постоянно
  при работе с префабами и сценами).
- **RawTherapee `.pp3`** — текущие `Rank` и `ColorLabel`.

#### I4. Редактирование оценок в `.pp3` — [P2, требует особой аккуратности]

Менять `Rank` и `ColorLabel` прямо из Wander.

**Это первая фича, которая пишет в чужой формат — режим безопасности
максимальный:**

- **Атомарная запись:** temp-файл рядом → `File.Replace` (он делает
  atomic swap с backup'ом). Никакой записи «поверх» открытого оригинала.
- **Сохранять всё, чего не понимаем.** `.pp3` — это INI со множеством секций;
  трогаем ровно две строки, остальное переносим байт-в-байт. RawTherapee
  дописывает туда свои параметры проявки, потерять их = потерять работу
  пользователя.
- **Не создавать `.pp3` там, где его не было**, без явного согласия — пустой
  pp3 меняет поведение RawTherapee.
- **Проверить кодировку и переводы строк** исходного файла и сохранить их.
- Откат через `Ctrl+Z` (сохранять прежнее значение в undo-действии).

#### I5. Какие ещё форматы поддержать — [обсудить]

Предложения к разговору, от самого ценного:

- **XMP (`.xmp`)** — самый стандартный сайдкар вообще: Adobe Bridge/Lightroom,
  darktable, exiftool. Хранит рейтинг, цветовую метку, ключевые слова. Если
  делать «оценки фото» всерьёз, XMP важнее pp3 по охвату.
- **`.AAE`** — сайдкары правок с iPhone, лежат рядом с фото при импорте.
  Массово встречаются в пользовательских папках с фотками.
- **`.json` от Google Takeout** — `IMG_1234.jpg.json`, при выгрузке из Google
  Photos. Шаблон «дописывается», как у Unity.
- **Субтитры** (`.srt`, `.ass`, `.sub` + `.idx`) — другая предметная область,
  но механика ровно та же, а UX-выигрыш очень заметный: переименовал фильм —
  субтитры уехали следом.
- **Сайдкары других RAW-конвертеров** — `.dop` (DxO PhotoLab), `.on1`
  (ON1 Photo RAW), `.cos` (Capture One). Тот же слот, что pp3.
- **Контрольные суммы** (`.sha256`, `.md5`, `.sfv`) — проект сам такое
  публикует рядом с релизным exe, так что догфудинг.
- **`.pdb`** рядом с `.dll` / `.exe` — символы отладки, для разработчиков.

Отдельно решить: спутник у одного файла может быть **не один**
(`IMG.CR2` + `IMG.CR2.pp3` + `IMG.xmp`). Резолвер должен возвращать список,
а не единственный файл.

---

### J. Режим галереи — [P2]

#### J1. Автовключение

Если большая часть файлов в папке — графика, режим галереи включается сам.
Нужно определить: порог (доля? абсолютное число?), что считать графикой
(включая RAW), и — обязательно — **не перебивать явный выбор пользователя**.
Если он вручную переключил вид в этой папке, автоматика молчит.

#### J2. Переключатель день/ночь

Значок в тулбаре, меняет фон области просмотра на тёмный. Для разглядывания
фотографий светлый фон мешает.

Прицел на будущее: это фактически первый кусок тёмной темы (Roadmap).
Стоит сразу заводить как переключение палитры области контента, а не как
разовый хак с одним цветом — иначе потом переделывать.

#### J3. Фильтр по свойствам снимка

Фильтрация по **ISO, выдержке, рейтингу**. Вероятно, разворачивается со
стороны панели быстрого просмотра — там уже читается EXIF.

Технические зависимости, которые надо учесть заранее:

- Сейчас EXIF читается **лениво, для выбранного файла**. Фильтр по ISO
  требует прочитать метаданные **всей папки** — это I/O по сотням файлов.
  Нужен фоновый проход с прогрессом и кэшем, иначе UI встанет.
- Рейтинг живёт либо в EXIF/XMP, либо в сайдкаре (`.pp3` `Rank`) — то есть
  **J3 опирается на секцию I**. Порядок: сначала I, потом J3.
- Тумбнэйлы для галереи — это E3 (async thumbnails), который уже висит в
  плане. Без него галерея на большой папке будет фризить.

---

### K. Надёжное копирование больших файлов — [P2/P3, исследование]

Заменить/дополнить текущий `System.IO`-путь внешним движком для тяжёлых
случаев: копирование десятков гигабайт, нестабильная сеть, съёмные диски.

**Кандидаты:**

- **robocopy** — встроен в Windows, ставить нечего. Даёт retry
  (`/R` / `/W`), restartable mode (`/Z` — продолжает с места обрыва),
  многопоточность (`/MT`), длинные пути, внятные коды возврата.
- **FastCopy** (открытые исходники) — интересен не как зависимость, а как
  референс: свои буферы и обход системного кэша, чтобы копирование не
  вытесняло всё остальное из памяти. Посмотреть подход, не тащить код
  (лицензию проверить отдельно).
- **certutil** — копировать не умеет, это хешер (`certutil -hashfile`).
  Его место — **вторая половина задачи: verify-after-copy**. Скопировали →
  посчитали хеш источника и приёмника → сверили. Для «надёжно» это как раз
  недостающий кусок; для больших файлов считать хеш дорого, так что нужен
  флаг, а не всегда.

**Что придётся решить — это и есть основная работа, а не выбор утилиты:**

- **Слои.** Внешний процесс — платформенная деталь, значит реализация в
  `Wander.Platform.Windows` за существующей абстракцией. В Core не должно
  протечь ни слова про robocopy.
- **Прогресс.** `OperationTracker` ждёт per-item шаги, robocopy печатает
  свой прогресс в stdout. Либо парсить вывод, либо смириться на неопределённый
  прогресс для внешнего пути.
- **Отмена.** Сейчас `CancellationToken` проверяется между элементами. С
  внешним процессом отмена = убить процесс, и надо решить, что делать с
  недокопированным хвостом (robocopy `/Z` позволяет продолжить).
- **Undo.** Копирование само по себе откатывается удалением приёмника — но
  надо убедиться, что undo-запись формируется и для внешнего пути тоже.
  Операция без undo в UI не выпускается.
- **Когда включать.** Всегда? По порогу размера? Флагом в настройках?
  Гонять отдельный процесс ради копирования мелочи — лишние накладные
  расходы.
- **Ошибки.** Коды возврата robocopy нетривиальны: 0–7 это успех разной
  степени, 8+ — реальные сбои. Замапить в человеческие сообщения.

---

### L. Контекстное меню — [P2] — **сделано**

Меню как в Windows 10: весь список сразу, без «Показать дополнительные
параметры». Механизм и его цена описаны в
[ARCHITECTURE.md](ARCHITECTURE.md) — здесь только что именно закрыто и что
осталось.

#### L1. Модель и рендер — **сделано**
`ContextMenuBuilder` (Core) — чистая функция от `ContextMenuTarget` +
`ContextMenuSettings` + пунктов шелла; `ContextMenuFactory` (App) рисует
результат в WPF. Разметки меню в XAML больше нет — закрыт заодно пункт
TECHDEBT про тройное дублирование. 26 тестов на форму меню, включая
схлопывание разделителей.

#### L2. Семантика правого клика — **сделано**
Клик по элементу вне выделения переносит выделение, внутри — сохраняет
группу, по пустому месту снимает выделение и показывает меню папки.
`Menu` / `Shift+F10` — то же с клавиатуры.

#### L3. Подменю «File» — **сделано**
`Cut` / `Copy` / `Paste` / `Copy path` / `Copy name` / `Create shortcut`.
Логика: у буфера обмена есть хоткеи, верхний уровень тратится на то, у чего
их нет. В фоновом меню подменю нет — там `Paste` и `New folder` идут
первыми, это девять кликов из десяти.

#### L4. Пункты сторонних приложений — **сделано**
`IShellContextMenu` / `ShellContextMenu` поверх классического
`IContextMenu`. Проверено на живой машине: 7-Zip, TortoiseGit, Git GUI /
Bash, Notepad++, «Восстановить прежнюю версию» и системное подменю
«Создать» со всеми `ShellNew`-шаблонами — с иконками и вложенными подменю.
Дубли режутся по каноническому глаголу (`GCS_VERBW`).

#### L5. Кастомизация — **сделано**
Настройки → «Контекстное меню»: мастер-выключатель расширений (выключенный
= чужие DLL не грузятся вообще), чёрный список по одному, сворачивание
расширений в подменю «More options», галочка на каждый свой пункт.
Скрытое хранится как «что выключено», по строковым именам `MenuCommandId`.

#### L6. Что осталось — [P3]
- Асинхронный запрос чужого меню (сейчас синхронный на UI-потоке) —
  см. TECHDEBT.
- Owner-drawn пункты расширений не читаются — см. TECHDEBT.
- Инлайн-переименование (A3) сделало бы `Rename` из меню приятнее.
- `Select all` / `Invert selection` в фоновом меню — обсуждалось,
  сознательно не добавлено: `Ctrl+A` работает, а меню лучше держать
  коротким.

---

## Активные направления (P3 / обсуждается)

- Расширение preview: видео (MediaElement), zip-архивы, hex view.
- Symlinks / junctions / hard links — отдельная фича (сейчас только .lnk).
- Темы (светлая / темная) — формально внутри F1/F2.

---

## Связанные файлы

- [CLAUDE.md](../CLAUDE.md) — точка входа, навигация по всем докам, правила.
- [ARCHITECTURE.md](ARCHITECTURE.md) — как устроен код и механизмы.
- [TECHDEBT.md](TECHDEBT.md) — мелкие чистки.
- [BACKLOG.md](BACKLOG.md) — отложенные задачи.
- [README.md](../README.md) — описание для пользователя, сборка, лицензия.
- [.editorconfig](../.editorconfig) — codestyle.
