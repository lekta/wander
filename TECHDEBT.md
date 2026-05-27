# Wander — технический долг

Сюда выписываются мелкие шероховатости, которые встретились по пути и
которые мы сознательно откладываем. Чистим позже целенаправленно, не вперемешку
с продуктовыми задачами.

Правило: когда замечаешь на ходу что-то "хорошо бы поправить", но это не
блокирует текущую задачу — добавь сюда строку и иди дальше. Не давай мелочёвке
тонуть в коде.

Формат пункта: одна-две строки с указанием места (файл/секция) и сути.

## Открыто

- **ShellRecycleBin.Restore — проверить на разных Windows-локалях** —
  реализовано через `FolderItemVerb.DoIt()`, имя verb'а ищется в списке
  по совпадению с локализованной строкой. Сейчас прошиты `Restore` (en)
  и `Восстановить` (ru) в `ShellRecycleBin.IsRestoreVerb`. Для других
  локалей (de, fr, es, zh, ja, …) Restore кинет понятный IOException
  с подсказкой. Когда поедем за пределы en/ru — расширить список.
  Заодно проверить, что `GetDetailsOf(item, 1/2)` — это всё ещё
  "Original Location" / "Date deleted" на не-en системах (на Win10/11
  должно быть стабильно, но не на 100%).
- **ShellRecycleBin.Restore — кейс «target path занят»** —
  если между Delete и Undo пользователь создал файл с тем же именем,
  Shell сейчас молча допишет «(1)» к восстанавливаемому. Доработать
  обработку, когда понадобится (для v1 этого кейса не должно случаться часто).
- **ShellRecycleBin — STA + COM RCW lifetime** — Shell.Application
  COM-объекты создаются на UI-потоке (STA, всё ОК пока операции
  синхронные). При уходе в async для Restore нужен Dispatcher.Invoke
  или собственный STA-поток. И не освобождаем RCW через
  `Marshal.ReleaseComObject` — для коротких операций приемлемо, но
  для долгоживущей сессии стоит добавить.
- **FileOperationService — async overloads** — все методы синхронные. Когда
  начнём выносить долгие копирования с UI-потока, добавлять Task-вариант;
  UndoService.BeginOperation уже умеет держать guard на весь жизненный цикл
  async-операции (CanUndo вернёт false пока IsBusy).
- **Undo не переживает рестарт** — стек живёт в памяти. После закрытия
  Wander Ctrl+Z пуст, но файлы в системной корзине остаются —
  пользователь может восстановить через Explorer. Persisting undo log
  отдельной задачей, если понадобится.
- **MainWindow.xaml** — `ContextMenu` файлового списка продублирован в трёх
  местах (DataGrid Details, ListBox Tiles, ListBox LargeIcons). Вынести в
  `Window.Resources` и переиспользовать.
- **Wander.App/Assets/app.ico** — заглушечная иконка, пользователь подменит
  на финальную.
- **Drag&drop UX** — нет визуального drop-indicator'а на TreeView/списках
  (подсветка узла под курсором), используется только системный курсор
  Copy/Move. Драг-ghost тоже системный по умолчанию.
- **Долгие файловые операции** — recursive Copy/Move большой папки идёт
  синхронно на UI-потоке, окно зависает без прогресса/отмены. Пора будет
  выносить в Task с CancellationToken и прогресс-баром в статусе.
- **PromptDialog (Rename)** — не выделяет имя без расширения, нет валидации
  недопустимых символов в имени файла, нет немедленного rename-in-place
  в строке списка (как в Explorer по F2).
- **Conflict dialog: merge папок** — при конфликте папки выбор Replace
  делает delete+copy (теряем неконфликтующее содержимое target). Explorer
  делает рекурсивный merge, спрашивая для каждого вложенного файла.
- **Conflict dialog: Rename = auto** — кнопка "Keep both" подставляет
  суффикс "(1)", диалога с явным вводом имени нет. В Explorer есть.
- **In-use detection для папок** — RestartManager работает по файлам.
  Если папку нельзя переместить из-за залоченного вложенного файла,
  сейчас выводим базовое IOException. Можно сделать рекурсивный обход
  и попросить RM по каждому файлу — но это I/O-тяжело.
- **Symlinks / junctions** — Wander умеет создавать только .lnk
  (Explorer-shortcuts) через `IShortcutService`. NTFS symlinks /
  junctions / hard links не создаются и не «разрешаются»; их
  Reparse-флаг не отображается отдельной иконкой. Это отдельная
  фича (требует прав/Developer Mode для symlink, либо junction-only).
- **Preview pane: debounce** — async переключение происходит сразу на
  каждое изменение selection. При очень быстрой прокрутке списка стрелками
  будут лишние Task-старты (хоть и отменяемые). Стоит добавить 200-300 ms
  дебаунс.
- **Preview pane: WebView2 init cost** — WebView2 init происходит при
  первом обращении (PDF/HTML/MD). Это ~1-2 секунды на первом использовании.
  Можно прогревать в фоне через `EnsureCoreWebView2Async` на старте,
  если preview включён в state.
- **Preview pane: AvalonEdit для XAML** — AvalonEdit подсвечивает XAML
  отдельной грамматикой через extension; у нас xaml есть в списке code,
  но AvalonEdit'у не хватает XAML-definition в стандартной поставке (надо
  доустановить из его репозитория). Сейчас xaml открывается как plain XML.
- **.lnk overlay для jumbo (LargeIcons)** — `SHGFI_LINKOVERLAY` работает
  только с `SHGetFileInfo` (small/normal). Jumbo идёт через
  `SHGetImageList` SHIL_JUMBO → overlay system не накладывает. Решение —
  композитом наложить arrow-PNG из ресурсов после получения jumbo-иконки.
  Затрагивает только режим LargeIcons; Details/Tiles/Tree уже корректные.
- **Ctrl+L** — фокус идёт в адресную строку, но текст не selectAll
  визуально подсвечивается синим, проверь на машине; если что — TextBox
  Focus + SelectAll иногда требуют BeginInvoke на Dispatcher.

## Закрыто

(пусто)
