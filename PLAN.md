# Wander — план

Этот файл — список **обязательных целей** проекта и их детализация.

В отличие от соседей:

- [`TECHDEBT.md`](TECHDEBT.md) — мелкая шероховатость (дальние чистки).
- [`BACKLOG.md`](BACKLOG.md) — конкретные второстепенные задачи (полезное, но не сейчас).
- [`README.md`](README.md) Roadmap — стратегические направления продукта (куда движемся идейно).

**PLAN.md** — обязательное "что и как". То, что точно делаем, с детализацией.

---

## Столпы проекта

1. **UX** — убрать лишнее из Win11 Explorer, починить его баги, добавить удобств.
2. **Надёжность** — деструктивные операции всегда требуют подтверждения с Cancel по-умолчанию; явные ошибки вместо тихих сбоев.

См. [project memory](../.claude/projects/D--Dev-lekta-Wander/memory/).

---

## Что готово (фактически)

### Архитектура
- 4 проекта на .NET 10: `Wander.Core` (логика, abstractions), `Wander.Platform.Windows`
  (Win32/Shell/COM реализации), `Wander.App` (WPF UI), `Wander.Core.Tests` (xUnit).
- Service Locator как точка композиции, без DI-контейнеров.
- File-scoped namespaces, 1TBS, `.editorconfig` соблюдается `dotnet format`-ом.
- 24 unit-теста зелёные.

### Навигация и UI
- Дерево дисков и папок, lazy-load по раскрытию, листовые папки без треугольника.
- Сохранение раскрытых путей и текущей папки между запусками (`%LocalAppData%\Wander\state.json`).
- Дерево автоматически раскрывается на текущую папку, никогда не сворачивается само.
- Alt+click по шеврону: на свёрнутом — раскрывает узел + прямых детей; на раскрытом — рекурсивно сворачивает всех потомков.
- Заголовок окна = имя текущей папки.
- Toolbar: borderless навигационные кнопки, адресная строка, View-меню с галочками (Details / Tiles / Large icons), главное меню (⋯).
- Главное меню: Refresh, New folder, Quick preview (toggle), Options (stub), Exit.

### Файловые операции
- Open / Cut / Copy / Paste / Delete / Rename / New folder через `FileOperationService` (Core).
- Multi-select (Extended) во всех трёх режимах; deferred selection при drag.
- Drag & drop внутри окна и внешний (FileDrop). Эффект: same-drive Move / cross-drive Copy / Shift=Move / Ctrl=Copy / **Alt=Shortcut (.lnk)**.
- Drag preview: иконка файла + бейдж `+N`, action-индикатор (↪/＋/↗/⊘), текст с именем и целью. Follow cursor с DPI-correction.
- Drop target highlight (Adorner на TreeViewItem / ListBoxItem / DataGridRow).
- Drop в `.lnk` папки → drop в реальную папку (через `IShortcutService.Resolve`).
- Подтверждения с Cancel по-умолчанию для Delete и Move (включая DnD).
- Read-only delete: явный список + второй вопрос; снятие атрибута перед удалением.
- Self-drop защита с понятным текстом ("Cannot move 'photos' into its own subfolder '2024'").
- Cross-device move папок: fallback `CopyDirectory + DeleteDirectory`.
- Rename: блокировка недопустимых символов на ввод + tooltip.
- Conflict resolution: `IConflictResolver` + batch-dialog (Replace all / Skip all / Resolve each) + per-item dialog со сравнением source/target по размеру и дате.
- Locked files: RestartManager определяет процесс ("file is open in: Word (PID 1234)").
- Shortcuts (.lnk): создание через COM `IShellLinkW`, resolve target.
- System icons: SHGetFileInfo + SHIL_JUMBO; per-path кеш для `.lnk` (правильный overlay).

### Просмотр (preview pane)
- Toggle через главное меню, ширина настраивается splitter-ом, состояние сохраняется.
- Контент: Image (BitmapImage), Text, Code (AvalonEdit с подсветкой), Web (WebView2 для PDF/HTML/Markdown через Markdig), placeholder при невозможности.
- Все загрузки **асинхронные с cancellation** и спиннером во время загрузки.
- Footer summary: для пустого выбора — текущая папка (рекурсивный count + size), для файла — name/size/modified, для папки — name/files/size, для множества — агрегат. Считается асинхронно.
- EXIF для изображений: Camera Make/Model, ISO, F/, Shutter, FocalLength, DateTaken, размеры. Через NuGet `MetadataExtractor` — поддерживает RAW (CR2/CR3/NEF/ARW/DNG и т.д.).

### Хоткеи
- `Alt+←/→/↑` — back/forward/up.
- `Backspace` — up.
- `F5` — refresh.
- `Delete` — удалить.
- `Enter` — открыть.
- `F2` — переименовать.
- `Alt+Enter` — Properties (системный диалог через `ShellExecuteEx`).
- `Ctrl+A` — выделить всё.
- `Ctrl+C / X / V` — copy / cut / paste.
- `Ctrl+Shift+N` — новая папка.
- `Ctrl+L` — фокус адресной строки.
- `Esc` — снять выделение.

---

## Обязательные цели

### 1. Прогресс долгих файловых операций
- **Зачем**: Copy/Move/Delete крупной папки сейчас замораживает UI без обратной связи. Это блокер для надёжности.
- **Что**: async `FileOperationService.CopyManyAsync/MoveManyAsync` с `IProgress<BatchProgress>` и `CancellationToken`. UI-диалог с прогрессом (current file, %, кнопка Cancel) или прогрессбар в статусе.
- **Шаги**:
  1. Расширить `IFileSystem` async-операциями (или обернуть в FileOperationService через Task.Run).
  2. `BatchProgress` record (current, total, fileName).
  3. Прогресс-диалог `ProgressDialog.xaml` (modal или non-modal с возможностью продолжить работу).
  4. VM `HandleDrop`/`Paste`/`DeleteSelected` запускают через async с подпиской на progress.

### 2. Корзина по умолчанию + Shift+Delete
- **Зачем**: сейчас Delete permanent через стандартный диалог. Корзина даёт обратимость.
- **Что**: новый абстрактный `IRecycleBinService.Send(path)` в Core. Реализация через Shell `SHFileOperation` или `IFileOperation` в Platform.
- Default Delete отправляет в корзину; Shift+Delete — permanent с явным предупреждением о невозвратности.
- В TECHDEBT упоминание Shift+Del убрать после реализации.

### 3. Поиск по текущей папке
- **Зачем**: `Ctrl+F` обязателен для проводника.
- **Что**: Toolbar — search input справа от адресной строки. По мере ввода фильтрует `Entries` (текущая папка, не рекурсивно на MVP). Подсветка совпадений.
- Можно расширить рекурсивным режимом (toggle), но первая версия — non-recursive.

### 4. Темы (светлая / темная)
- **Зачем**: пользователь Win11 ожидает следования системной теме.
- **Что**: `ResourceDictionary` для светлой/темной палитры. Подписка на изменение `Windows.UI.ViewManagement.UISettings` или системного registry. Переключатель в Options.

### 5. Перенос conflict dialog / spinner на async-операции
- Когда #1 готов — `InteractiveConflictResolver` дёргает dialog на UI-потоке через `Dispatcher.Invoke`. Прогресс-диалог должен открываться/закрываться вокруг batch operation.

### 6. Доработка drop-индикации
- Drag-ghost preview-окно сейчас догоняет курсор с заметным лагом на быстром движении (subscription на GiveFeedback). Перейти на `WM_MOUSEMOVE` hook или `IDragSourceHelper` COM для более плавной картинки.
- Подсветка target — улучшить визуал (анимация появления, контрастность, индикатор для root-папок).

---

## Активные направления (обсуждается / на подходе)

- Расширение preview: видео (MediaElement), zip-архивы (System.IO.Compression), folder thumbnails (grid миниатюр), hex view.
- Symlinks / junctions / hard links — отдельная фича (требует прав/Developer Mode для symlinks). Сейчас только .lnk.

---

## Связанные файлы

- [TECHDEBT.md](TECHDEBT.md) — мелкие чистки.
- [BACKLOG.md](BACKLOG.md) — отложенные задачи.
- [README.md](README.md) — описание, сборка, roadmap.
- [.editorconfig](.editorconfig) — codestyle.
