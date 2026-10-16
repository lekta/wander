# Wander — файловые операции

Конвейер операций и батчей, undo, корзина, занятые пути, прогресс и окно
операции, конфликты, защита системных путей; корзина и архивы как папки
(shell-namespace), извлечение; файлы-спутники. Слои и композиция —
ARCHITECTURE; поведение и пределы — DONE «Ход файловых операций»,
«Файловые операции», «Архивы как папки»; спека блока 0.6 «Надёжное
копирование» — OPERATIONS, после блока её механизмы приезжают сюда.

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
цикл конфликтов, composite-undo, recycle-vs-permanent; группы
`BatchGroup` (файл + спутники: один вопрос, один шаг прогресса).
Синхронные `CopyMany` / `MoveMany` — для тестов, продакшн — async на пуле
с прогрессом и `CancellationToken`. Отмена посреди элемента —
`BatchItemStatus.Cancelled`, не `Failed`: файл копируется под
`имя.wander-tmp` (`TransientFiles.TempNameFor`) и получает имя целым;
частично скопированная папка — в undo. Папка между томами —
`CopyDirectory` + `DeleteDirectory`, сбой копии убирает частичную;
read-only цели — список, второй вопрос, снятие атрибута. Ссылка на папку
внутри копируемой не копируется (петля) — `Skipped` с
`LinkNotCopiedException`; ссылка на файл копируется содержимым цели,
ссылка-источник верхнего уровня — насквозь.

- **Undo.** Один LIFO-стек: Move ↔ Move, Rename ↔ Rename, Delete →
  Restore из корзины, Create → Delete в корзину. `PermanentDelete`
  (`Shift+Delete`) не откатывается. `BeginOperation()` — busy-счётчик,
  `CanUndo == false` в полёте. Стек под одним локом; `Changed` — вне
  лока, может прийти с фона. Рестарт не переживает.
- **После безвозвратного удаления — `UndoService.Forget(удалённые пути)`**
  (решение человека 2026-09-29: история не очищается целиком). Шаг
  называет, что отмена должна застать на месте
  (`IUndoableAction.PathsBeforeUndo`: перенос и переименование — нынешний
  путь; создание, извлечение, выход действия — созданное; оценка —
  сайдкар; удаление в корзину — ничего, шаг остаётся всегда; связка — по
  `Steps`). Проход от свежего шага к старому с набором мёртвых путей: шаг,
  чьё «на месте» — мёртвый путь или под ним, выпадает, а то, что он вернул
  бы (`MovesOnUndo.To`, `PathsAfterUndo`), становится мёртвым — так
  выпадают зависевшие; уцелевший шаг уводит мёртвые пути назад через свой
  перенос. От связки остаются уцелевшие шаги (`WithSteps`). Сравнение
  путей — правило `PathRewrite` своей копией: `Undo/` папку `FileSystem/`
  не видит.
- **Откат — операция.** `UndoService.UndoAsync`: с пула, в
  `OperationTracker` (`OperationVerbs.Undo`), под тем же busy-счётчиком;
  связка разматывается по `Steps` с конца. Отмена возвращает несделанное
  в стек под тем же описанием. Шаг идёт через `IUndoStepGate`
  (`FileOperationService.UndoGate` над `BusyGate`): занятый файл ждётся из
  бюджета операции. Сбой шага — в `UndoOutcome.Failures`, остальные
  откатываются; в стек возвращается только сбой, который может пройти
  (занят, чужая операция, временная ошибка — `WorthAnotherTry`), иной
  запер бы всё под собой. По `UndoOutcome.Undone` `UndoLast` ведёт
  выделение и `FollowRelocated`.
- **Корзина — на `IFileOperation`, в обе стороны** (`ShellRecycleBin`):
  - `SHFileOperation` с `FOF_ALLOWUNDO` удалял файл на пути длиннее
    `MAX_PATH` **безвозвратно и молча**. Движок говорит заранее: нет
    `TSF_DELETE_RECYCLE_IF_POSSIBLE` в `PreDeleteItem` — уничтожит;
    приёмник отвечает `E_FAIL`, наверх — `RecycleUnavailableException`.
    Короткая папка с длинным путём внутри эту проверку проходит, и
    оболочка спрашивает поверх `FOF_NO_UI` с «Да» по умолчанию — поэтому
    до движка свой обход (`TooLongForBin`, порог 259). Итог — один вопрос
    после операции (`ChoiceRequest`: «Прервать» по умолчанию, `ArmDelay`
    0,5 с до включения «Удалить безвозвратно»).
  - `PostDeleteItem` отдаёт id-list элемента корзины —
    `RecycleHandle.BinItemId`; `Restore` — один `MoveItem` без обхода
    корзины (строка панели корзины несёт `BinFilePath`, `$R…`). Занятое
    имя при возврате решается до движка (`UniqueNames`; под `FOF_NO_UI` он
    заменил бы сам), `FOF_RENAMEONCOLLISION` — сетка на гонку; пропавшая
    папка создаётся; `$I…` без пары убирает `RemoveIndexFile`. Прогон — на
    своём STA-потоке (`OwnApartment`).
  - **Удаление пачкой, один прогон** (`IRecycleBin.SendMany`,
    `BatchExecutor.Recycle`; реализация по умолчанию — цикл `Send`, ею
    живут фейки). Без `FOFX_EARLYFAILURE`: занятый файл и папка с занятым
    внутри (остаётся **целой**) пропускаются без ожидания — `FileInUse`,
    ждёт операция (ниже). Ошибка из `PreDeleteItem` останавливает прогон
    — это отмена (`E_ABORT`) и отказ корзины (`E_FAIL`), остаток
    прогоняется заново. Возврат — прогон на элемент (TECHDEBT).
- **Занятые пути**, три слоя снизу вверх:
  - `PathClaims` (Operations) — кто в Wander над чем работает. Заявка —
    **источник** операции; вид `UserOperation` (владелец — ключ
    `OperationVerbs`) или `Background` (`ClaimOwners`: shell-миниатюра,
    системный фильтр поиска — прервать нельзя). `Covering(path, except)` —
    сам путь, папка выше и всё внутри; `Changed` — только для операций
    пользователя. Контрольная строка — `Claims: N lookups, slowest X ms,
    table Y paths`, `WARN` от 5 мс.
  - `BusyWait` (Operations) — политика: взгляд каждые 0,2 с, бюджет 2 с
    **на операцию**, не на файл.
  - `HeldPaths` / `BusyGate` (FileSystem; один на операцию). `Check(path)`:
    заявка чужой операции пользователя — `ClaimedByOperationException`,
    фоновой — `Yield`. `WaitForFile` — через `IFileBusyProbe`
    (`CreateFile(DELETE)`); `Retry` — папка, пробы нет: операция
    повторяется, пока `FileInUse`, только в пределах тома; `RetryMany` —
    пачка корзины. Держатель называется один раз (`IFileLockInspector` —
    Restart Manager; своя заявка — «Wander: миниатюра»). Итог —
    `BusyReport` в `DeleteResult` / `BatchItemResult`. Ждут перенос,
    переименование, корзина; безвозвратное удаление — только заявки
    (TECHDEBT). Не дождалось удаление — «Файл занят» с «Повторить»
    (`DialogKind.DeleteInUse`).
  - Чтобы ждать редко: свои читатели — `SharedRead`
    (`FileShare.ReadWrite | Delete`); исключение — оценка в сайдкар
    (`IFileSystem.ReadAllBytesForUpdate`): `.pp3`, который RawTherapee
    как раз сохраняет, читался бы недописанным. Панель просмотра отпускает
    файл до операции (`PreviewController.Release` / `Restore`); откат — не
    отпускает (TECHDEBT).
  - Часы «в работе» рисует `AsyncIcon` (`ShowsWork`, `OnRender`) — не
    элемент в шаблоне каждой ячейки; один статический обработчик
    `PathClaims.Changed` — проход по живым иконкам, `Entries` не
    трогается. Часы — у заявки старше `PathClaims.BadgeDelayMs`.
- **Прогресс — в двух счётчиках**: элементы (что выделил человек) и
  байты (что двигает диск; без них копия 5 ГБ держит бар на нуле).
  `OperationTracker.Begin(verb, total, totalBytes, bytesAreWork)` →
  `IOperationHandle` (диспозить всегда); `Snapshot()` — иммутабельный
  срез, `Percent` по байтам, иначе по элементам. `verb` — **ключ
  ресурса** (`OperationVerbs`). `Changed` с фона — не чаще 10 раз в
  секунду, хвост довозит таймер. Байты: `BatchExecutor` взвешивает
  источники до старта (`FolderStatistics.Collect`) — **после** вопроса о
  совпадениях, чтобы Cancel в нём ничего не стоил. Перенос —
  `IFileSystem.CopyFile/CopyDirectory/MoveEntry` с `IProgress<long>` и
  токеном; в `SystemIOFileSystem` — `CopyFileEx` с `LPPROGRESS_ROUTINE`
  (отмена внутри файла, недописанное убирает система). Извлечение байтов
  не знает: `IShellNamespace.CopyOut` отдаёт `IProgress<CopyOutWork>` —
  операция `BytesAreWork`, только процент.
- **Окно операции и статус-бар.** `RunWithProgressDialogAsync` открывает
  **немодальный** `ProgressDialog` через 400 мс и без фокуса
  (`ShowActivated = false`) и ждёт задачу, а не окно; модальный вопрос
  изнутри операции пережидается (`ComponentDispatcher.IsThreadModal`).
  Окно узнаёт свою операцию по токену в снимке, не по `Id`: извлечение
  регистрирует операцию после диалога о совпадениях, и запущенная в этот
  промежуток досталась бы чужому окну. Пока операция идёт, `Closing`
  отменяется: `Alt+F4` и `Esc` = «Свернуть». Закрывает окно сам
  `RunWithProgressDialogAsync` (`Finish` в `finally`) **до** возврата к
  вызывающему: иначе вопрос об итоге брал открытое окно владельцем,
  умирал с ним, и приложение оставалось во вложенном цикле невидимого
  модального окна; второй замок — `WpfDialogs.ActiveWindow` не отдаёт
  владельцем `ITransientWindow`. Строку и панель статус-бара кормит
  `OperationViewModel` — на месте, не пересоздаётся; панель, а не тултип:
  тултип исчезает, стоит потянуться к кнопке. Важность — `StatusSeverity`
  (та же пометка в журнале действий).
- **Выход при идущих операциях.** `MainWindow.OnClosing`: вопрос
  (`DialogKind.ExitWithOperations`, Cancel по умолчанию); «да» —
  `e.Cancel`, `Vm.CancelAllOperations`, `await
  OperationTracker.WhenIdleAsync(10 с)` (Core, тест; продолжение не на
  контексте вызывающего — фатальный обработчик может на нём
  блокироваться), затем `Close()` вторым проходом. Таймаут —
  `IProcessRunner.KillAll` (деревом). `Shutdown` (`SessionEnding`, smoke,
  харнесс) — `App.IsShuttingDown`: отмена без вопроса. Вылет —
  `App.StopOperationsBeforeDying`: `ProgressDialog.CancelAll` (только
  `_cts.Cancel` — диспетчер может быть мёртв), `KillAll`,
  `WhenIdleAsync(3 с).Result`. «Выход» в меню — `MainWindow.Close`, не
  `Shutdown`. После `Hide()` — `IDirectoryWatcher.Watch(null)` и
  `PreviewPane.ReleaseWebView` обеих панелей.
- **Конфликты.** `IConflictResolver.ResolveAll(ConflictRequest)` — push:
  все коллизии до первого касания диска одним вызовом; ответ —
  `ConflictAnswer` на каждую показанную пару, вложенные включительно, по
  пути источника; null / `Cancel` — отмена батча; возникшая по ходу или не
  спрошенная — второй вызов из одного элемента. Каждый файл группы — своя
  коллизия; `BatchExecutor.ApplyGroup` уводит спутников за
  переименованным основным, кроме пропущенных.
  - `FileConflictInfo` — две записи + `IsMove` + `SourceReachable` (false
    у записи архива). `ConflictVerdict.Of` — вид, размер, кто новее (допуск
    2 с, FAT), «идентичны» (null — не сравнили).
    `FileContentComparer.AreIdentical` — блоками через
    `IFileSystem.OpenRead` до первого отличия; `AutoCompareLimit` (64 МБ)
    делит очередь на два прохода.
  - `ConflictResolution.Merge`: `BatchExecutor.MergeFolder` обходит
    исходную папку, ответы по пути, вложенные — рекурсивно, опустевшую при
    перемещении — в корзину; `MergeScanner` (Core) даёт окну то же дерево
    заранее, глубина 64.
  - `ConflictBatch` — состояние окна в Core: дерево `ConflictPair`
    (вердикт, ответ, дети; `Effective` — ответы исполнителю), очередь
    сравнения `NextToCompare`, разовый ответ за нерешённые
    (`ConflictBulkAction`) и политика `SetSkipIdentical` (начальная —
    `AppSettings.SkipIdenticalOnConflict`). UI — `Conflict/ConflictWindow`,
    `DispatcherConflictResolver` маршалит на UI. Почему окно такое: кнопка
    на строке читается как сделанное действие; отличия — весом, не
    фразой; «Заменить все» / «Пропустить все» — далеко от ОК.
  - Replace — цель **в корзину**, `DeleteAction` в composite перед
    основным шагом: `Ctrl+Z` возвращает обе стороны. Копия в свою же папку
    — не конфликт: `BatchExecutor` отвечает Rename (move — Skip) сам;
    сторож DnD пропускает (`PathSafety.IsAllowedDuplicate`), вырезание в
    ту же папку `PasteAsync` снимает (`PathSafety.AllAlreadyIn`).
- **Защита.** `SystemPathGuard` — функция от пути и окружения, без I/O,
  статически: корни дисков и шар, спец-папки (Windows, Program Files,
  ProgramData, Users, корень профиля), папки профиля, которые ведёт
  Windows (Рабочий стол, Документы, Загрузки…, AppData — сами, не
  содержимое; `IKnownFolders`, `Lazy`), дерево `C:\Windows`. Содержимое
  Program Files и чужих профилей не блокируется — чистка остатков
  легальна. Причины — ключи `Guard*` через `Text.Format`. Запись внутрь —
  `MayWriteInto`: «нет» только у дерева Windows. `PathSafety` — self-drop
  с текстом через `ITextSource`.
- **Отступления от «всё откатываемо»:** безвозвратное удаление
  (подтверждение всегда); восстановление из корзины в `UndoService` не
  кладётся, как в Explorer (откат был бы удалением), не деструктивно,
  логируется, `SystemPathGuard` не нужен — место решает шелл.

## Shell-namespace: корзина и архивы

Вещи, которые выглядят папками, а папками не являются, — за контрактом
`IShellNamespace` (Core); `WindowsShellNamespace` диспетчеризует: корзина
— `ShellRecycleBinFolder`, архив — `ShellArchiveFolder` (обе на
`IShellItem`), «Компьютер» (`shell:MyComputerFolder`, `ComputerDrives`) —
готовые диски по `DriveInfo`, не `IShellItem`: шелл добавил бы телефоны и
сеть, которых Wander ещё не открывает (Y3, Y4). Строка диска — корень
папкой, `FileSystemEntry.Volume` для полосы (`VolumeInfo.Fill`, Core,
тест). Сентинелы — только чтение: запись Wander'а ушла бы в открытую
«папку»; меню строки диска всё же спрашивает оболочку
(`MainWindow.QueryShellMenu`). `Backspace` и «вверх» из корня диска в
«Компьютер» не ведут (решение человека 2026-10-15). Листинг — ветка
`RefreshShellAsync` (эпоха, спиннер, отмена, `PublishRows`); `Enumerate`
смотрит на `CancellationToken` между элементами.

- **Путь внутри архива — обычный parsing name** (`D:\pack.7z\sub\b.txt`).
  Надвое режет `ArchivePath.Parse(path, extensions)` (Core): первый
  сегмент с архивным расширением — контейнер, хвост — путь внутри.
  Расширения Platform читает из реестра лениво, по классу
  папки-обработчика (`FolderHandlerOf`: `HKCR\<ProgID>\CLSID` с учётом
  `UserChoice`, иначе `SystemFileAssociations\<ext>\CLSID`; ∈
  `CompressedFolder` / `ArchiveFolder` / `CABFolder`; fallback `.zip`).
  7-Zip и WinRAR своей папки не регистрируют — отданная им ассоциация
  меняет запуск, а не просмотр папкой.
- **Один предикат** — `Archives.Of(path)` → `IShellNamespace.ParseArchive`,
  никаких `EndsWith(".zip")`; `File.Exists` контейнера — настоящая папка
  `backup.zip` открывается папкой. `MainViewModel` кэширует ответ на смену
  пути (`NoteCurrentLocation`): `CanExecute` спрашивает десятки раз в
  секунду.
- **`IShellItem`, не `Shell.Application`**: у того `FolderItem.Size` и
  `ModifyDate` для `ArchiveFolder` врут, а `Items()` корзины — один
  непрерываемый вызов. Перечисление — `BHID_EnumItems`; папка —
  `SFGAO_FOLDER`; размер и дата — `IShellItem2` (`PKEY_Size`,
  `PKEY_DateModified`), у папок в архиве размера нет.
- **Корзина.** Имя — `System.FileName` (display name прячет `.lnk` всегда,
  остальное — по настройке Проводника). `FullPath` — `SIGDN_FILESYSPATH`
  (`$R…`); время удаления — `PKEY_Recycle_DateDeleted` в `ModifiedUtc`;
  «откуда» — `PKEY_Recycle_DeletedFrom`. «Восстановить» ищет по
  `$R`-файлу (`RecycleHandle.BinFilePath`), `Ctrl+Z` — по `BinItemId`.
- **Корзина — на своём STA-потоке** (`OwnApartment.Run`): её
  apartment-threaded папка с потока пула попала бы в общий host-STA, и
  долгий вызов держал бы overlay-запросы значков (`SHGetFileInfo`) —
  четыре слота `AsyncIcon` и следующую папку. Холодный заход долгий —
  листинг порциями: `PortionClock` (Core, тест) — первая на 300 мс,
  дальше раз в секунду, копии через `IProgress` в `Enumerate`;
  `MainViewModel.Land` кладёт первую как приход (вуаль снимается),
  следующие — как перечитывание с выделением, порция после целого
  отбрасывается; архив порций не даёт. Лог: `Recycle bin: opened … first
  row after … rows in …, N portion(s) on the way`, `… listing abandoned`.
- **Байты: zip — потоком, остальное — движком шелла.** `CompressedFolder`
  отдаёт `BHID_Stream` (`IShellNamespace.ReadEntry`, в память); у
  `ArchiveFolder` — `E_NOINTERFACE`, а `IDataObject` без `FileContents`;
  `IFileOperation::CopyItem` с `FOF_NO_UI` извлекает всё. Поток берут
  миниатюры, панель и «Открыть» — копию. `Shell/ExtractionService` (Core)
  вокруг `IShellNamespace.CopyOut`: один вызов движка на батч, гард на
  цель, `IConflictResolver` (Replace → старое в корзину), лог,
  `OperationTracker`, отмена через `PreCopyItem`, undo — `ExtractAction`
  в композите. Не `BatchExecutor`: тот требует `stat` обоих концов. Входы:
  `Ctrl+C` внутри кладёт пути внутрь, `PasteAsync` узнаёт их по
  `ParseArchive`; «Извлечь…» (`MenuCommandId.Extract`) — плюс
  `PickFolder`. Причина отказа движка — `ShellArchiveFolder.Failure`;
  стоп без причины (zip с паролем без окна) — `ArchiveLockedException`.
- **Внутри архива выключено решением**: удаление, переименование,
  вырезать, вставка, создание, drop внутрь, сторож, оценки, спутники,
  поиск по содержимому и подпапкам, статистика папки. Меню внутри — пять
  строк, не серые: серая строка обещает «потом». «Извлечь рядом» —
  `FixedConflictResolver(Rename)`: никого не спросили — ничего не
  заменяется. Миниатюры — только картинки: `IsThumbnailable` → false (шелл
  ответил бы значком типа и писал бы его в общий `thumbcache`); папке
  внутри `SHGetFileInfo` нужен `FILE_ATTRIBUTE_DIRECTORY`.
- **Пусто или защищено**: zip с паролем перечисляется, но байт не даёт;
  7z с `-mhe` — ноль записей, как пустой. Оба — текст в статусе;
  нечитаемый контейнер — «архив повреждён или недоступен».
- **Отступление от «всё откатываемо»** — временная копия записи
  (`Core/Shell/TempExtraction.CopyOutAsync`) без гарда, диалогов и undo:
  копия чужого файла — не данные пользователя. Папка — `AppPaths.Tmp` по
  хешу пути записи (`TempFiles.FolderFor`): `DataTmp` (`<DataRoot>\tmp`)
  либо `SystemTmp` (`%TEMP%\Wander`) по `AppSettings.UseSystemTemp`
  (`bool?`: до выбора — системная Temp в портативном). `TempFiles.Sweep`
  на старте чистит **обе** папки старше суток. Одну копию делят
  «Открыть», панель и окно конфликтов.
- **Конфликты: «извлёк — сравнил».** `ExtractionService` перед
  `ResolveAll` распаковывает пары «файл против файла равного размера» от
  мелких к крупным в пределах `AutoCompareLimit`; байты читаются по
  `FileConflictInfo.ReadablePath`, ключ ответов — `Source.FullPath`. Папка
  внутри не распаковывается (`SourceReachable` = false): её ответы —
  заменить / оставить / под новым именем.
- **Дерево и закладки.** Архив — строка в обеих панелях:
  `WorkspaceController.ReadLevel` добавляет к папкам файлы с
  `Archives.Of(path) is { IsRoot: true }`; уровень под
  `Archives.Contains(path)` — через `IShellNamespace.Enumerate`. Шеврон
  архива не пробуется (`PanelRow.IsProbed`) — иначе каждая папка открывала
  бы все свои архивы. Меню и цели drop нет (`FolderTreesView.HasMenu`);
  источник drag — да: папка архива уходит объектом оболочки. Закладка на
  путь внутри — лист, не «пропавшая».
- **Наружу — шелловским объектом данных**: `CF_HDROP` с путём внутри
  архива читается как несуществующий файл. `IShellNamespace.CreateDataObject`
  собирает то же, что Проводник (`SHCreateShellItemArrayFromIDLists` →
  `BindToHandler(BHID_DataObject)`, `ShellDataObject`). Файл в обычной
  папке — PIDL папки один раз, имена в ней — `IShellFolder.ParseDisplayName`
  + `ILCombine` (`IdListParser`): разбор по имени на два порядка дешевле
  разбора полного пути. `OutgoingDrag.Run` — `DataObject(comObject)`, из
  архива **только** `Copy`; `Ctrl+C` — `ISystemClipboard.SetShellObject`
  (`OleSetClipboard`). Свои приёмники берут пути у самого жеста —
  `OutgoingDrag.InFlightPaths` на время `DoDragDrop` (дописать формат в
  обёрнутый объект WPF не даёт); drop в папку — `MainViewModel.ExtractAsync`.
  Объект выбирает `MainViewModel` и передаёт в `ClipboardController.Copy`
  — обратная зависимость `Core/FileSystem` → `Core/Shell` дала бы цикл;
  свой список путей остаётся для вставки внутри Wander (`_sharedShellObject`).
- **Панель просмотра и миниатюры внутри архива** — PREVIEW («Маршруты»,
  «Миниатюры мимо шелла»).

## Companion-файлы

Служебный файл рядом с основным (`.meta`, `.pp3`, `.xmp`) — довесок,
одной строкой, едет вместе. `AppSettings.IntegrateCompanions`, по
умолчанию вкл.

```
CompanionRule            суффикс + шаблон имени; формат — данные, не код
CompanionResolver        Collapse() список → свёрнутый; FindCompanions() путь → рядом;
   │                     RenamePlan() путь + имя → план группы
   ├→ RefreshFolderAsync (свёртка на пуле) ├→ WithCompanions() (перед батчами)
   └→ FileOperationService.RenameMany() (группа = один undo-шаг)
CompanionMetadataService чтение/запись содержимого: UnityMetaSidecar.Read (GUID,
   импортёр), Pp3Sidecar.Read (Rank, ColorLabel), WithRank → IFileSystem.ReplaceAtomic,
   CreateRatingSidecar (по согласию); RatingSidecars() порядок чтения; PlanWrite()
   Edit / Create / None; PhotoRating() → EmbeddedRating.Read (заголовок снимка) + EmbeddedRatingCache
Listing/RatedListing     WithRatings() листинг → тот же с Rating (читалка делегатом)
```

| Шаблон | Пример | Кто |
|---|---|---|
| `Appended` — к полному имени | `Sprite.png.meta`, `IMG.CR2.pp3`, `IMG.CR2.xmp` | Unity, RawTherapee, darktable, Takeout |
| `Replaced` — заменяет расширение | `IMG_1234.xmp` | Adobe, Bridge, exiftool, `.AAE` |

`Appended` — по точному имени, `Replaced` — по stem'у. Два претендента на
stem: сайдкар — RAW, если RAW среди них ровно один (`IMG.CR2` +
`IMG.jpg` при `IMG.xmp` — XMP у RAW, иначе после «Превью из RAW» сайдкар и
оценка осиротеют); иначе — ни к кому (`CompanionResolver.Owner`).

Правило darktable `.xmp` стоит раньше `Replaced` (по stem'у `IMG.CR2.xmp`
ушло бы к `IMG.CR2.pp3`) и с `Versions`: дубликаты `IMG_1234_01.CR2.xmp`
(две цифры и больше после `_`, `CompanionRule.TryVersion`) — спутники
`IMG_1234.CR2`, пока нет файла `IMG_1234_01.CR2` (решение 2026-10-01): в
листинге — `FindMain`, с диска — `IFileSystem.FileNamesLike`,
переименование — `VersionNameFor`; в `RatingSidecars` они не входят.

- Свёртка — в воркере `RefreshFolderAsync` **после** Hidden / System:
  спутник отфильтрованного файла и сирота видны.
- `FileSystemEntry.Companions` — пути (пусто у обычного файла и при
  выключенном флаге); в футере — серым «(+.xmp)» после имени
  (`CompanionLabel`). Значок в списке — REJECTED. Меню про спутников не
  знает.
- Групповые операции — `BatchGroup`: один шаг прогресса и результат на
  группу, composite-undo; коллизия — у каждого файла своя, переименованный
  основной уводит спутников. Из выделения — бесплатно (`Companions` уже в
  записи); из плоского списка (буфер, drop из Explorer) —
  `CompanionResolver.Group()` с диском, в `Task.Run`.
- Авто-переименование тянет спутников (`Sprite (1).png.meta`)
  подстановкой общей части. Мимо батча — `RenamePlan` + `RenameMany`.
- Наружу перетаскиванием — в списке id, не в `CF_HDROP`: LISTING, «Drag & drop»,
  «Спутники наружу».
- **Оценки** — `SidecarRating` (`Rank` / `ColorLabel`, `InPhoto`), формат —
  за `CompanionMetadataService` по расширению; `ColorLabels` нумерованы
  одинаково (XMP хранит имя `Red`, pp3 — номер); `SidecarText` — BOM,
  переводы строк. **Порядок чтения** (решение 2026-10-01) —
  `RatingSidecars`: `Appended` (darktable `IMG.CR2.xmp`, RawTherapee
  `.pp3`) раньше `Replaced` (`IMG.xmp`); первый с оценкой — поверх оценки
  из снимка (`EmbeddedRating.Merge`: поле сайдкара, и 0 тоже, перекрывает
  камеру). **Запись** — во все сайдкары, где поле есть (`Holding`), ни в
  одном — в первый; создаваемый несёт второе поле снимка, чтобы метка не
  сняла звёзды камеры.
- **Запись в чужой формат — узкий путь**: только поля оценки, в
  существующем — одна строка, остальные байты как есть (в `.pp3` вся
  проявка); XMP — строковая хирургия, не `XDocument` (round-trip переписал
  бы атрибуты, префиксы, `<?xpacket?>`); нет свойства — атрибутом в
  `rdf:Description` **только** при объявленном `xmp:`, иначе
  `NotSupportedException`; только `ReplaceAtomic` (temp → `File.Replace`);
  BOM и `\r\n` / `\n` сохраняются; прежнее — в `SidecarRatingAction`.
- **`CreateRatingSidecar`** — единственное создание неназванного файла:
  подтверждение (спрашивает `MainViewModel`), лог, `SystemPathGuard`,
  `SidecarCreatedAction` — undo **удаляет**; существующий —
  `InvalidOperationException`; снятие оценки не создаёт. Созданный `.xmp`
  несёт `xmp:Rating` и пустой `xmp:Label` — дальше правка на месте.
- **`.xmp` по умолчанию**: RawTherapee применяет профиль по умолчанию
  только без сайдкара, `.pp3` с `Rank=3` меняет проявку; `.xmp` не влияет.
  RawTherapee 5.11+ читает и пишет оценку в `IMG.xmp` только с включённым у
  себя `ThumbnailRankColorMode`, по умолчанию — `.pp3` и снимок.
  `AppSettings.RawRatingFormat`, при `.pp3` — предупреждение.
- **`.meta` только читается** — Unity владеет им, перезапись отвяжет ассет.
- **Оценка из снимка** (`EmbeddedRating`) — читается, не пишется: только
  заголовок, по контейнеру (ISO box — XMP в uuid-боксе и Canon `CMT1`;
  JPEG — APP1; TIFF-образные RAW — IFD0, `0x02BC` XMP и `0x4746` EXIF
  `Rating`; RAF — вшитый JPEG). XMP поверх EXIF; 0 без метки — «ничего».
  `Reads` — RAW и то, что пишет камера; HEIF `meta` не разбирается
  (TECHDEBT). `PlanWrite`: сайдкар есть — `Edit`; нет — `Create` при
  значении > 0 или при 0 поверх собственной оценки снимка (решение
  2026-10-01: в RAW не пишем, звёзды камеры снимает сайдкар с 0); иначе
  `None`. Кэш `EmbeddedRatingCache` — ключ путь + размер + mtime; сторож —
  `ForgetPhotoRating` по изменённому пути, `F5` — `ForgetPhotoRatings`
  папки (exiftool `-P` сохраняет mtime).
