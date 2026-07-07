# Wander — технический долг

Сюда выписываются мелкие шероховатости, которые встретились по пути и
которые мы сознательно откладываем. Чистим позже целенаправленно, не вперемешку
с продуктовыми задачами.

Правило: когда замечаешь на ходу что-то "хорошо бы поправить", но это не
блокирует текущую задачу — добавь сюда строку и иди дальше. Не давай мелочёвке
тонуть в коде.

Формат пункта: одна-две строки с указанием места (файл/секция) и сути.

## Открыто

- **state.json schema break — bookmarks panel-awareness** — `AppState.LastPath`
  стал `NavigationStop?` (вместо `string?`), `AppState.ExpandedPaths` стал
  `IReadOnlyList<NavigationStop>` (вместо `IReadOnlyList<string>`). Поле
  `LastPathSource` удалено (его роль перешла в `NavigationStop.Source`).
  Старые state.json от предыдущих билдов не загрузятся — `JsonAppStateStore.Load`
  ловит исключение и возвращает `new AppState()`. Pre-1.0 ОК; миграционный
  слой можно добавить позже, если понадобится.
- **Корзина: статичная иконка (нет empty/full overlay)** — в `SystemIconProvider`
  есть `LoadShellNamespaceIcon` через PIDL `FOLDERID_RecycleBinFolder`,
  но мы получаем одну иконку и кэшируем её на сессию. Shell возвращает
  «полную» если в корзине что-то есть на момент первого запроса, иначе
  «пустую» — после очистки/наполнения иконка не обновляется до рестарта.
  Чтобы переключать — нужно слушать `SHCNE_UPDATEIMAGE`/`SHCNE_RENAMEFOLDER`
  через `SHChangeNotifyRegister` или просто инвалидировать кэш при
  Restore/Empty операциях. Сделать вместе с D5b.
- **Корзина: WindowsShellNamespace кэширует RCW для всей сессии** —
  каждый `Refresh` пересоздаёт `Shell.Application` через
  `Activator.CreateInstance`, не освобождая через
  `Marshal.ReleaseComObject`. Для коротких сессий приемлемо, тот же
  компромисс что в `ShellRecycleBin`. Пересмотреть если корзина
  начнёт «утекать».
- **Корзина: enumerate синхронно на UI-потоке** — `Refresh` зовёт
  `IShellNamespace.Enumerate` синхронно. Для бакета с тысячами
  recycled items может фризить UI. Async-обёртка пойдёт в одной
  пачке с E3 (async thumbnails).
- **Bookmarks: drop INTO bookmark folder = no-op** — сейчас любой drop
  на `BookmarksPanel` (включая прямо на узел существующей закладки)
  трактуется как «добавить в закладки». Drop в Explorer-стиле «скопировать
  файл в эту папку» через панель закладок не работает, нужно перетащить
  на ту же папку в нижнем дереве дисков. Развести логику, когда понадобится.
- **state.json — миграция WindowGeometry** — после чистки #1 геометрия
  окна живёт в подrecord `Window` вместо top-level `WindowLeft/Top/...`
  Старые state.json теряют позицию окна один раз. Pre-1.0 ОК, но если
  когда-то появится миграционный слой — обработать backfill из старых
  полей.
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
- **ShellRecycleBin — STA + COM RCW lifetime** — `Send` (SHFileOperation)
  уже зовётся с thread-pool потока через DeleteManyAsync — на практике
  работает, но shell-API формально предпочитает STA; следить. `Restore`
  (Shell.Application COM) пока зовётся только с UI-потока (STA, ОК) —
  при уходе undo в async нужен Dispatcher.Invoke или собственный
  STA-поток. И не освобождаем RCW через `Marshal.ReleaseComObject` —
  для коротких операций приемлемо, но для долгоживущей сессии стоит
  добавить.
- **FileOperationService — async overloads (частично сделано)** —
  батч-операции (CopyManyAsync / MoveManyAsync / DeleteManyAsync) с
  репортингом в OperationTracker уже есть, VM использует их.
  Single-item Copy / Move / Delete / PermanentDelete остались синхронными —
  их зовут только тесты, но если из app-кода понадобится прямой одиночный
  вызов, его тоже нужно перевести в Task.
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
- **Долгие файловые операции — per-file/per-byte прогресс** — Cancel в
  ProgressDialog теперь реально останавливает copy/move/delete между
  элементами, но прогресс по-прежнему считается по элементам верхнего
  уровня, не по байтам. Для перекидывания одной 5-Гб папки бар надолго
  встанет на 0% — рендерить per-file/per-byte прогресс отдельной задачей.
  Отмена тоже гранулярна по top-level элементам: начатое копирование
  большой папки доработает до конца, прервать «внутри» нельзя.
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
- **SystemIconProvider — кэш без ограничений** — после переезда на
  `IShellItemImageFactory` LargeIcons кэшируется per-path (нужно для
  тумбнэйлов изображений/видео). В папке с 10К+ файлов это N×~30 KB
  PNG в памяти сессии. На v1 терпимо, но стоит добавить LRU bound
  (например, 500 записей) когда станет заметно.
- **Replace при copy/move уничтожает целевой файл безвозвратно** —
  `BatchExecutor.ApplyOne` при выборе Replace делает `DeleteFile`/
  `DeleteDirectory(recursive)` мимо корзины, затем move. Противоречит
  столпу «всё откатываемо»: Ctrl+Z вернёт source на место, но затёртый
  target потерян навсегда; при падении move после delete теряем target,
  не получив source. Вариант: отправлять target в корзину (`IRecycleBin.Send`)
  и класть `DeleteAction` в composite перед `MoveAction` — тогда undo
  восстанавливает обе стороны. Решение продуктовое (Explorer в корзину
  НЕ отправляет) — см. обсуждение в сессии 2026-07-06.
- **MoveEntry cross-volume fallback — нет уборки при частичном сбое** —
  copy+delete: если рекурсивная копия упала на середине, частичная копия
  остаётся в destination; если копия прошла, а delete source не удался
  (залоченный файл), получаем дубликат данных. Рассмотреть транзакционную
  уборку/докат и внятное сообщение пользователю.
- **Превью HTML: JS включён** — WebView2 теперь не пускает превью в сеть
  (NavigationStarting фильтрует не-file/about/data) и режет попапы, но
  скрипты внутри локального .html всё ещё исполняются (IsScriptEnabled
  глобальный, а встроенный PDF-viewer без JS не работает). Вариант:
  переключать `IsScriptEnabled` per-navigation (выкл для .html/.htm,
  вкл для .pdf) или рендерить HTML через NavigateToString без скриптов.
- **Логи не ротируются** — `%LOCALAPPDATA%\Wander\logs` растёт бесконечно
  (файл на каждую сессию). Добавить retention: чистить старше N дней или
  оставлять последние N файлов при старте FileLogger.
- **PreviewController.CountAndSum — идёт по junctions** —
  `Directory.EnumerateFiles(p, "*", AllDirectories)` с legacy-настройками
  следует reparse-points: цикл из junction = бесконечный подсчёт до смены
  выделения, а размеры задваиваются. Перейти на `EnumerationOptions`
  с `AttributesToSkip = ReparsePoint` + `IgnoreInaccessible = true`.
- **Recycle длинных путей (>MAX_PATH)** — SHFileOperation не понимает
  long paths, longPathAware-манифест помогает только System.IO. Удаление
  в корзину для очень глубоких путей упадёт с IOException. Мигрировать
  на COM `IFileOperation` (он же снимет locale-зависимость Restore).
- **SystemIconProvider — синхронный IShellItemImageFactory.GetImage** —
  вызывается из `IconConverter` на UI-потоке при биндинге каждой
  ячейки WrapPanel. Для уже закэшированных файлов мгновенно, но первое
  открытие большой папки с видео может фризить UI (shell декодирует
  первый кадр). Сделать async-загрузку с временным placeholder-иконкой
  и обновлением через DispatcherTimer/PriorityBinding.

## Закрыто

- **.lnk overlay для jumbo (LargeIcons)** — для `.lnk` теперь запрашиваем
  индекс overlay через `SHGFI_OVERLAYINDEX`, получаем jumbo-overlay-иконку
  через `IImageList.GetOverlayImage` + `GetIcon` и композитим её поверх
  target-иконки в `SystemIconProvider.ComposeIconWithOverlay`. Бадж стрелки
  теперь виден в режиме Large icons.
