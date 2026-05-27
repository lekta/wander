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

### Спринт 1 — багфиксы + связать готовое (P0)
1. **Закрыть готовый Core wiring**: Ctrl+Z в UI (UndoService готов), Shift+Delete →
   `PermanentDelete` с явным предупреждением (см. "Незавершённое" ниже).
2. **Багфиксы DnD**: #1 (ссылка в ту же папку), #2 (overlay стрелочки на .lnk),
   #3 (drop highlight на всё дерево).
3. **B1**: `Image.StretchDirection=DownOnly` (5 минут).
4. **Двойное выделение** (#5): убрать FocusVisualStyle на row-контейнерах.

### Спринт 2 — async операций + первая техническая чистка (P1)
5. **E. Async copy/move/delete** + `ProgressDialog`. Самая важная инфраструктурная
   фича — она же разблокирует A7/A8 (thumbnails) и подтягивает надёжность.
6. **Технические чистки #1** (см. секцию ниже) — сразу после E, поскольку async
   меняет signature и публичный API `FileOperationService`.

### Спринт 3 — proper file-manager UX (P1)
7. #4 ("Cannot drop here" не пугает внутри окна).
8. **A5 Sort menu** + **A6 .lnk-папки рядом с папками**.
9. **A4 Show hidden toggle + opacity**.
10. **A9 Click-empty=unselect**, **A10 Rubber-band selection**.
11. **A2 Затемнить дерево** (мелочь, можно вместе с A4/A5).
12. **G1+G2 Search basic** (input в toolbar + фильтр текущей папки).
13. **D Favorites**.

### Спринт 4 — приятное (P2)
14. **A3 Inline rename**.
15. **A7 Thumbnails картинок** + **A8 Folder content preview** (требуют E3).
16. **F2 SettingsDialog** + **F3 Reset**.
17. **B2** — content для папки в preview (после согласования вида).
18. **A1 Spacing/sizes** — итеративный полишинг.

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

- **Чистка #1 — после E (async операций).** `FileOperationService` получит
  Task-overload-ы, появится `BatchProgress`, ProgressDialog. Возможные кандидаты:
  - Разделить `FileOperationService` на читающую и пишущую части, или вынести
    batch-логику (`ApplyBatch`/`PushComposite`/conflict-handling) в отдельный класс.
  - Покрыть `UndoService` busy-guard полноценным тестом под async (race-conditions).
  - Унифицировать sync- и async-public API — выбрать одну форму как primary.
- **Чистка #2 — после D + G + Settings**. Когда добавится Favorites, Search,
  SettingsDialog — VM раздуется. Кандидаты:
  - Разделить `MainViewModel` на specialized VM-ы (NavigationViewModel,
    SelectionViewModel, PreviewViewModel, ...) с фасадом сверху.
  - Пересмотреть `AppState` — выделить под-records (PreviewState, ViewState, ...).

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

- **[P0] `UndoService` → UI**. KeyBinding `Ctrl+Z` на Window; `UndoCommand` в
  VM (`CanExecute = _undo.CanUndo`). В Status текст последнего отменённого action.
  Подписка на `UndoService.Changed` для refresh `CommandManager`.
- **[P0] `Shift+Delete` → `PermanentDelete`**. KeyBinding в Window. В диалоге
  явное предупреждение "This will be deleted permanently and **cannot be undone**"
  + иконка Warning + Cancel по-умолчанию. После — Status: "Permanently deleted N items".
- ~~**[P0] Лог в файл**~~ — **сделано**. `FileLogger` пишет в
  `%LocalAppData%\Wander\logs\session-yyyymmdd-hhmmss.log`, регистрируется
  первым в `PlatformBootstrapper`. Smoke-тест подтвердил запись session-start
  и операций навигации.

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

### 4. [P1] "Cannot drop here" пугает при drag внутри окна
- **Симптом**: пока курсор не над валидной папкой, плашка preview агрессивно
  пишет "Cannot drop here".
- **Действие**: внутри окна, если `Effects=None`, скрывать preview-плашку
  целиком (или показывать только нейтральный мини-индикатор без красного знака).
  Снаружи окна — оставить как есть, системный курсор всё равно справится.

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
  самой плитки, дать побольше воздуха. Конкретные значения подобрать визуально.
- **[P1] A2. Чуть затемнить область дерева.** Background `#F8F8F8` или
  `SystemColors.ControlBrush`. Минут 10 работы.
- **[P2] A3. Inline rename "на иконке".** F2 / клик-задержка превращает имя
  в TextBox прямо в строке/плитке (как в Explorer). PromptDialog оставить
  fallback'ом, когда primary selection отсутствует.
- **[P1] A4. Toggle "Show hidden" + opacity.** Скрытые/системные по умолчанию
  не показываем. В View-меню чекбокс "Show hidden" — при включении рисуются
  с `Opacity=0.5`. Сохранять в `AppState.ShowHidden`.
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
- **[P1] A9. Клик в пустое пространство → снять выделение.** В DataGrid/ListBox
  при клике на пустую область — `UnselectAll()`. Сейчас селект сохраняется.
- **[P1] A10. Rubber-band selection.** Drag по пустому месту → прямоугольник
  выделения. ListBox с `SelectionMode=Extended` сам этого не делает —
  нужен `MouseDown` + временный adorner + `IsInRubberBand` hit-test.
- **[P1] A11. Полупрозрачный системный курсор "невозможно" внутри окна.**
  Продолжение багфикса #4 — silent default cursor (стрелка), preview прячется.

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

- **D1.** Раздел "Favorites" над деревом дисков с раскрываемым узлом.
- **D2.** Context-menu "Add to Favorites" + drag папки в раздел Favorites.
  Удаление — context-menu "Remove from Favorites".
- **D3.** По умолчанию: Downloads, Documents (через `SpecialFolder`).
- **D4.** Сериализация: `AppState.Favorites: IReadOnlyList<string>`.

### E. Async операции — [P1, главный фокус]

- **E1.** `FileOperationService.CopyManyAsync` / `MoveManyAsync` /
  `DeleteAsync` / `PermanentDeleteAsync` с `IProgress<BatchProgress>` и
  `CancellationToken`. `BatchProgress` = current/total + currentFileName +
  bytesDone/bytesTotal. Sync-методы оставить тонкими обёртками `.GetAwaiter().GetResult()`
  для текущего кода (потом смигрируем).
- **E2. `ProgressDialog`.** Modal с прогрессом текущего файла + общим,
  кнопкой Cancel, кнопкой "Hide" (свернуть в статус-бар). Спинер видим пока
  не пошёл реальный прогресс (для маленьких файлов он и не появится).
- **E3. Async thumbnails.** A7 / A8 поверх async-инфраструктуры. Cache в памяти
  + опционально на диске (`%LocalAppData%\Wander\thumbs\`) с ключом по path+mtime.
- **E4. UndoService busy-guard.** Убедиться что `CanUndo=false` пока batch
  в работе, и Ctrl+Z во время прогресса молча игнорируется. Тест на это.

### F. Settings — [P1 для расширения state.json, P2 для окна]

- **[P1] F1. Всё в `state.json`** (решено): добавляем `ShowHidden`, `SortKey`,
  `SortAscending`, `GroupFoldersFirst`, `Favorites`, `Theme`. По мере появления
  фич — дополняем `AppState`.
- **[P2] F2. SettingsDialog.** Group: View (Show hidden, theme, ...); Behavior
  (default action на double-click, auto-load thumbnails); Advanced (clear cache,
  reset state). Открывается из главного меню Options (сейчас stub).
- **[P2] F3. Reset to defaults.** Кнопка в SettingsDialog — `new AppState()` + save.

### G. Поиск — [P1 для базы, P3 для advanced]

- **[P1] G1.** Search input в toolbar справа от адресной строки. Узкая TextBox
  + placeholder "Search in this folder". Фокус по `Ctrl+F`.
- **[P1] G2.** Простой фильтр: `Name.Contains(query, IgnoreCase)`. Дерево
  не трогается. Esc / пустой query — снимает фильтр.
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
