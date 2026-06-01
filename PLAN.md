# Wander — план

Список **обязательных целей** проекта и их детализация. Снапшот сделанного
здесь же — поэтому при старте новой сессии этот файл читается первым.

В отличие от соседей:

- [`TECHDEBT.md`](TECHDEBT.md) — мелкая шероховатость (дальние чистки).
- [`BACKLOG.md`](BACKLOG.md) — конкретные второстепенные задачи (полезное, но не сейчас).
- [`README.md`](README.md) Roadmap — стратегические направления продукта.

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

### Спринт 3 — техническая чистка #1 + добить P1 (текущий фокус)
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
3. **A5 Sort menu** + **A6 .lnk-папки рядом с папками**.
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
10. **D5. Корзина внутри Wander (shell-namespace)** — нужно ввести
    `IShellNamespace` в Core: enumerate shell items под `FOLDERID_RecycleBinFolder`,
    маппинг shell-item → `FileSystemEntry`-аналог, операции Restore /
    Empty / Delete-permanent через `IFileOperation` (Vista shell). После —
    добавить «Корзину» как третий дефолт в bookmarks + третий чекбокс в
    настройках. Влечёт shell-интеграцию, отложили намеренно.

### Спринт 4 — приятное (P2)
10. **A3 Inline rename**.
11. **A7 Thumbnails картинок** + **A8 Folder content preview** (требуют E3).
12. **F2 SettingsDialog: расширить** (Theme, Behavior, Advanced) + **F3 Reset**.
13. **B2** — content для папки в preview (после согласования вида).
14. **A1** — применить settings-binding к шаблонам Tiles/Details, перебрать
    defaults.

### Дальше (P3)
- **G3** Advanced search.
- **H1** desktop.ini.
- **H2** Unity .meta-партнёрство.

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
- **Чистка #2 — после D + G + Settings**. Когда добавится Favorites, Search,
  SettingsDialog — VM раздуется. Кандидаты:
  - Разделить `MainViewModel` на specialized VM-ы (NavigationViewModel,
    ClipboardViewModel, ...) поверх уже выделенного `PreviewController`.
  - Пересмотреть `AppState` дальше — выделить session-сектор (LastPath,
    ViewMode, ExpandedPaths, IsPreviewVisible, PreviewWidth) в отдельный
    подrecord по аналогии с `WindowGeometry`.

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
  ко всем view-modes. Сохранять в `AppState.SortKey` + `AppState.SortAscending` +
  `AppState.GroupFoldersFirst`.
- **[P1] A6. Сортировка .lnk-папок рядом с папками.** Сначала Directory + .lnk-на-папку,
  потом File + .lnk-на-файл. Требует resolve каждого .lnk: добавить
  `IsFolderShortcut` в `FileSystemEntry`, populate в `SystemIOFileSystem.Enumerate`
  через `IShortcutService.Resolve`. Делать sync на enumeration (cache не нужен —
  один Read.lnk быстрый).
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
- **[P1] D5. Корзина внутри Wander.** Нужна абстракция
  `IShellNamespace` в Core: enumerate shell-items под
  `FOLDERID_RecycleBinFolder` через `IShellFolder.EnumObjects`,
  доменный тип `ShellItem` (display name, parsing name, иконка),
  операции Restore/Delete через `IFileOperation`. После —
  добавляем `BookmarksSettingsCategory` чекбокс «Показывать
  «Корзину»» и кнопку-узел в bookmarks. Текущая фаза 1 без неё:
  чекбокса нет, в тексте категории сказано «добавим отдельным
  шагом».
- **[P3] D6. Drag-reorder bookmarks.** Перетаскивание пользовательских
  закладок между собой для смены порядка. Сейчас порядок = порядок
  добавления, изменения через ручную правку state.json. Делать
  при первом запросе.
- ~~**D7. Source-aware авто-разворот дерева.**~~ — **сделано**. Каждая
  навигация теперь несёт `NavigationSource` (Drives / Bookmark /
  Address / RightPane / Restore / External). `NavigationService` хранит
  `List<NavigationEntry>` + cursor вместо двух стеков; Back/Forward
  возвращают исходный source. `ExpandTreeToCurrent` для source=Bookmark
  разворачивает только панель закладок (с fallback на drives, если
  путь больше не в закладках — пример: пользователь убрал закладку
  между визитами). Для остальных source — только drives. Заодно
  чистим `IsSelected` на предыдущем узле, чтобы при прыжках между
  панелями не оставалось «двойное выделение».

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
- **H2. Unity .meta-партнёрство.** Опция в Settings ("Treat Unity .meta as
  companion file"). При операциях с файлом — выполнять ту же операцию с
  одноимённым `.meta` рядом (если есть). Кейсы: Move, Copy, Rename, Delete.

---

## Активные направления (P3 / обсуждается)

- Расширение preview: видео (MediaElement), zip-архивы, hex view.
- Symlinks / junctions / hard links — отдельная фича (сейчас только .lnk).
- Темы (светлая / темная) — формально внутри F1/F2.

---

## Связанные файлы

- [TECHDEBT.md](TECHDEBT.md) — мелкие чистки.
- [BACKLOG.md](BACKLOG.md) — отложенные задачи.
- [README.md](README.md) — описание, сборка, roadmap.
- [.editorconfig](.editorconfig) — codestyle.
