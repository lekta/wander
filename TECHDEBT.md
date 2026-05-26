# Wander — технический долг

Сюда выписываются мелкие шероховатости, которые встретились по пути и
которые мы сознательно откладываем. Чистим позже целенаправленно, не вперемешку
с продуктовыми задачами.

Правило: когда замечаешь на ходу что-то "хорошо бы поправить", но это не
блокирует текущую задачу — добавь сюда строку и иди дальше. Не давай мелочёвке
тонуть в коде.

Формат пункта: одна-две строки с указанием места (файл/секция) и сути.

## Открыто

- **ShellRecycleBin.Restore не реализован** —
  `Wander.Platform.Windows/FileSystem/ShellRecycleBin.cs`. Send работает
  (SHFileOperation/FOF_ALLOWUNDO), Restore кидает NotImplementedException.
  Делать через `IFileOperation::CopyItem` (а не Shell32 dynamic InvokeVerb,
  чтобы не зависеть от локализованного имени verb'а "Restore"/"Восстановить").
  Плюс: обработка коллизий по дате удаления и кейс «target path уже занят».
  В коде в Restore лежит детальный TODO-комментарий с эскизом.
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
- **Ctrl+L** — фокус идёт в адресную строку, но текст не selectAll
  визуально подсвечивается синим, проверь на машине; если что — TextBox
  Focus + SelectAll иногда требуют BeginInvoke на Dispatcher.

## Закрыто

(пусто)
