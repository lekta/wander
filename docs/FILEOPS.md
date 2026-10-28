# Wander — файловые операции

Конвейер операций и батчей, undo, корзина, занятые пути, прогресс и окно
операции, конфликты, защита системных путей; надёжное копирование
(запись операции, повтор, свой цикл, проверка, съёмные диски,
извлечение); корзина, архивы, телефон и камера как папки
(shell-namespace), извлечение; файлы-спутники. Слои и композиция —
ARCHITECTURE; поведение и пределы — DONE «Ход файловых операций»,
«Файловые операции», «Архивы как папки», «Телефон и камера».

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
ссылка-источник верхнего уровня — насквозь. Перенос такой папки между
томами оставляет в источнике ссылку и папки над ней, остальное уходит
(`SystemIOFileSystem.DeleteAllButLinks`; рекурсивное удаление уносило
ссылку — её не было ни там, ни там); откат вливает перенесённое обратно
(стенд 2026-10-19).

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
  не видит. Папка, удалённая навсегда наполовину (сбой посреди), —
  `ForgetLost`: забываются шаги только про то, чего на диске уже нет.
- **Папки, куда возвращает откат, больше нет** (решение человека
  2026-09-29: спросить, куда вернуть, и объяснить почему).
  `UndoDestinations.Missing` до запуска отката: папки, куда перенос
  вернул бы элементы, которых нет, — без тех, что откат сам вернёт раньше
  (слитая папка из корзины), без папок на отключённом диске и без
  переименований. `UndoLast` → `AskWhereToPutBack`: на каждую — вопрос
  (`DialogKind.UndoFolderGone`, Cancel по умолчанию) и выбор папки с
  уцелевшей части пути; отказ — откат не идёт, шаг остаётся.
  `UndoAsync(…, redirect)` → `IUndoableAction.Redirected` (`MoveAction`,
  `CompositeAction`); остаток прерванного отката ответ помнит.
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
  - **Принято без следа** (`RecycleHandle.IsUntraceable`: ни id, ни пути
    в корзине) — удалено безвозвратно: у пакетного удаления
    `DeleteResult.Permanent`; у заменённой цели, опустевшей слитой папки и
    одиночного удаления — без шага отката, который не нашёл бы элемент
    (`RecycleUndo.Of`); старые шаги про него — `Forget`.
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
    повторяется, пока `FileInUse`, только в пределах тома. Перенос и
    переименование папки с занятым файлом внутри Windows отвергает «доступ
    запрещён» (0x80070005, стенд 2026-10-19), не «занято»: папка один раз
    обходится в поиске занятого (`FileInUse.MayBeHeldInside`, обходы — 5 с
    на операцию); нашёлся — ждётся и называется им, нет — отказ сразу;
    `RetryMany` —
    пачка корзины. Держатель называется один раз (`IFileLockInspector` —
    Restart Manager; своя заявка — «Wander: миниатюра»). Итог —
    `BusyReport` в `DeleteResult` / `BatchItemResult`. Ждут перенос,
    переименование, корзина, безвозвратное удаление (`DeleteForGood`) и
    откат (`IUndoStepGate`). Не дождалось удаление — «Файл занят» с
    «Повторить» (`DialogKind.DeleteInUse`).
  - Чтобы ждать редко: свои читатели — `SharedRead`
    (`FileShare.ReadWrite | Delete`); исключение — оценка в сайдкар
    (`IFileSystem.ReadAllBytesForUpdate`): `.pp3`, который RawTherapee
    как раз сохраняет, читался бы недописанным. Панель просмотра отпускает
    файл до операции и до отката (`ReleasePreview` →
    `PreviewController.Release` / `Restore`).
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
  токеном; в `SystemIOFileSystem` — свой цикл под временным именем
  («Надёжное копирование»). Извлечение байтов
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

## Надёжное копирование

Копирование с флешки, карты и устройства — минутами: видно, что идёт и
чем кончилось, сбой не теряется, упавшее повторяется. Решения человека
2026-09-22: [приоритет — надёжность, не скорость]; [завершённые операции
живут в строке состояния, пока их не убрали]; [журнал — сеанса].
2026-10-15: [свой цикл копирования для всех копий; откат на системный —
правилом в коде]; [проверка — настройкой, по умолчанию со съёмных].

- **Запись операции** (`Core/Operations/OperationRecord.cs`).
  `IOperationHandle.Finish(items, target)` переводит операцию из идущих в
  `OperationTracker.Finished` (потолок `MaxFinished` = 50); `Dispose` без
  `Finish` — `Unitemised`. `OperationItem` — источник, назначение,
  `BatchItemStatus`, ошибка, `Attempts`, `Verified`, `Engine`; в записи —
  все проблемные и первые `MaxItems` = 200 остальных, строки и числа.
  `OperationOutcome` — Done / Problems / Cancelled / Failed; `Dismiss(id)`,
  `DismissFinished(problemsToo)`. App: `MainViewModel.HasActiveOperations` —
  только идущие (тик сторожа, вопрос при выходе), `HasOperations` —
  видимость блока; `OperationViewModel` — `IsFinished`, `Outcome`,
  `ProblemLines`, `RetryCommand`, `DismissCommand`; «Извлечь E:» —
  `OperationRecord.DriveRoots` плюс проба тома (`RefreshEjectable`).
- **«Повторить»** — `OperationRetry.Plan(record)`: только `Failed` и
  `Cancelled` (`IsRetriable`); копия и перенос — по папкам назначения тем
  же путём, что вставка (спутники и конфликты заново, своя запись и шаг
  отката; старая запись убирается при старте), удаление — `DeleteAsync` с
  подтверждением; источники устройства — импорт.
- **Временные ошибки** — `TransientIo.IsTransient`: Win32 21, 23, 31, 59, 64,
  121, 1117, 1167, 1231 и `CopyMismatchException`; `IoRetry.Run` с
  `RetryPolicy.Default` — 3 попытки, паузы 1 / 3 / 5 с, сон отменяемый; зовёт
  `BatchExecutor.ApplyOne` для копии файла и переноса между томами; номер
  попытки — `OperationSnapshot.Attempt` («повтор 2 из 3»), итог —
  `BatchItemResult.Attempts`. Занятость (0x80070020) — не временная
  ошибка: её ждёт `BusyGate`.
- **Свой цикл копирования** — `SystemIOFileSystem.CopyThroughTemp` →
  `CopyBytes`: цель под `имя.wander-tmp` (`TransientFiles.TempNameFor`;
  сирота с таким именем — наша, удаляется первой), `OwnCopy` —
  `StreamCopy.Copy` (Core, тест: буфер 1 МБ, `SetLength` заранее, SHA-256
  на лету), `Flush(flushToDisk)`, атрибуты и время изменения — после;
  `File.Move` в конечное имя — только целым; сбой и отмена — `TryDelete`.
  Без прогресса, отмены и проверки — `File.Copy`. `MoveEntry` папки между
  томами: `CopyDirectory` + удаление источника; сбой — частичная копия
  убирается, если папки не было до нас; ссылка на папку внутри не
  копируется (`CopyReport.SkippedLinks` → `Skipped` с
  `LinkNotCopiedException`), источник — `DeleteAllButLinks`.
  `CopyReport(Engine, Verified, SkippedLinks)` → итог элемента и строка
  лога; `IFileSystem.WriteFromStream` — тот же цикл для потока устройства.
- **Откат на `CopyFileEx`** (решение человека 2026-10-15: правило в коде, не
  настройка). `SystemCopyReason`: `Encrypted`, `SparseFile`, дополнительные
  потоки (`FindFirstStreamW`; `Zone.Identifier`), оба конца на одном
  SMB-сервере, один том ReFS → `CopyFileWithProgress` (`CopyFileExW`,
  прогресс дельтами, отмена `PROGRESS_CANCEL`), проверка — хэши обеих
  сторон. Свой цикл упал незнакомой ошибкой (`IsUnfamiliar`) — элемент тем
  же заходом через системный, прогресс откатывается. «Отладка» →
  «Копировать средствами Windows» (`AppSettings.CopyWithWindows` →
  `CopyOptions.SystemOnly`) — целиком, строкой в лог.
- **Проверка копии** (V). `CopyOptions.Verify`: цель перечитывается мимо
  кэша (`FILE_FLAG_NO_BUFFERING`, по секторам) и сравнивается с хэшем
  источника; расхождение — `CopyMismatchException`, копия не остаётся,
  повтор как у временной ошибки; итог — `OperationItem.Verified`,
  «проверено N». Настройка «Проверять скопированное» —
  `AppSettings.VerifyCopies` (`VerifyPolicy`: `Removable` по умолчанию —
  источник на `VolumeKind.Removable` или устройство; `Always`; `Never`) →
  `CopyVerification.ShouldVerify` в `FileOperationService.CopyOptionsFor`.
  Стенд V1 (`measurements/0.6-dev.md`): на SSD свой цикл вровень с
  `CopyFileEx`, проверка +10–20 %; флешка, кардридер, HDD, SMB — PLAN 0.7.
  Внешний USB-диск у Windows `Fixed` — по умолчанию не проверяется (TECHDEBT).
- **Ошибки словами** — `MainViewModel.DescribeError`: 112 / 39 — «нет места»
  (FAT с местом под файл, но без записей в папке — `VolumeInfo.OutOfEntries`:
  «в папке кончились записи»); 223 на FAT32 — «файл больше 4 ГБ»; 21 / 1167
  (и 31 у устройства) — «устройство отключено или не готово»; 23 / 1117 —
  «ошибка чтения с носителя»; `COPYENGINE_E_*` — у извлечения;
  `DeviceErrors.IsAsleep` — «камера выключилась или уснула»; «объект не
  найден» у устройства — «телефон обновил список файлов»;
  `CopyMismatchException`, `LinkNotCopiedException`,
  `RecycleUnavailableException`, `ClaimedByOperationException`; держатель —
  `IFileLockInspector`.
- **Компьютер не засыпает** — `IPowerGuard.KeepAwake(reason)` ←
  `Platform/Power/PowerRequestGuard` (`PowerCreateRequest` +
  `PowerSetRequest(SystemRequired)`, без `DisplayRequired`; `PowerClearRequest`
  + `CloseHandle` в `Dispose`); `OperationTracker` берёт на первой идущей
  операции и отпускает на последней; причина — ключ `PowerRequestReason`
  (`powercfg /requests`); не удалось — `WARN`, операция идёт.
- **Журнал структурный** (AD5) — `ActionJournal.Record(JournalEntry)`:
  `JournalKind` (FolderOpened, FileOpened, Copied, Moved, Renamed, Recycled,
  Deleted, Restored, Created, Extracted, Cancelled, ActionRun, Error, Note),
  `Headline`, `Lines` (строка на файл, «было → стало»), `Severity`; `Render`
  — первые 50 строк и «… ещё N», предел 500 записей; `Note` — строки без
  структуры. `ReportBatchResults` строит запись из `OperationRecord`, статус
  — `SetStatusQuietly`; открытие и запуск — `FileOpened`. Лог:
  `BatchExecutor` — `Copy done: 12 items, 12.3 GB, 327.4 s, 38.5 MB/s;
  failed 1, skipped 0`. Строки отката — `IUndoableAction.Verb` / `ItemCount`,
  `Description` — только в лог.
- **Съёмные диски** (Y2) — `IVolumeChanges` (Core): `Changed(VolumeChange(Root,
  Arrived, IsDevice))`, `Releasing(root)`. `VolumeChangeHub` (Platform):
  `WM_DEVICECHANGE` через хук `HwndSource` в `MainWindow` (`HandleMessage`;
  `DEV_BROADCAST_VOLUME`, событие на букву маски); устройства —
  `RegisterDeviceNotification(GUID_DEVINTERFACE_WPD)`. Широковещательное об
  уходе приходит не всегда (прогон 2026-10-19) — том открытой папки
  сторожится своим хэндлом корня (`Guard`, `DBT_DEVTYP_HANDLE`):
  `DBT_DEVICEQUERYREMOVE` — хэндл закрыт, `Releasing` (сторож закрывается,
  панель просмотра отпускает файл); `QUERYREMOVEFAILED` — снова;
  `REMOVECOMPLETE` / `REMOVEPENDING` — «том ушёл», с широковещательным — один
  раз (окно 10 с). Лог: `Volume guarded`, `Volume removal asked`, `refused`,
  `Volume removed: F:\ (broadcast | notice)`. App: `VolumesChanged`
  (`WorkspaceEvents`) → `PanelRules` читает верхний уровень панели дисков
  (`Read(Pane.Drives, TopKey)`), курсор уходит с ушедших; закладка на ушедший
  том сереет (`PostBookmarks`), не снимается; `F5` читает и диски; открытый
  «Компьютер» в списке перечитывается. Папка на ушедшем томе —
  `NavigationFallback.AfterVolumeRemoved` (WINDOW).
- **Безопасное извлечение** (E) — один путь `MainViewModel.EjectAsync(root,
  operationId?)`: кнопка «Извлечь E:» завершённой операции и строка `Eject`
  меню диска (MENU). Открытая папка на томе уходит, `ReleasePreview`, глагол
  оболочки `eject` корня свежей сессией меню (`InvokeEject`; нет глагола —
  `StatusEjectNoVerb`); панели после глагола не перечитываются. Ушёл ли том
  — по букве (`WaitForVolumeGone`: `GetLogicalDrives` раз в 2 с, до 5 с);
  ушёл — `Ejected`: статус и перечитывание дисков; не ушёл — `WhoHoldsDrive`
  (файлы операции → `IFileLockInspector`), «не извлечён: …» и ещё 2 мин
  ожидания. WebView2 уходит на `about:blank` при уходе с веб-вида
  (`PreviewPane.LeaveWebPage`). Устройствам WPD извлечение не нужно;
  `CM_Request_Device_Eject` не понадобился (BACKLOG). Остаётся: миниатюры и
  поиск хэндлы не отдают (TECHDEBT).

## Shell-namespace: корзина и архивы

Вещи, которые выглядят папками, а папками не являются, — за контрактом
`IShellNamespace` (Core); `WindowsShellNamespace` диспетчеризует: корзина
— `ShellRecycleBinFolder`, архив — `ShellArchiveFolder` (обе на
`IShellItem`), «Компьютер» (`shell:MyComputerFolder`, `ComputerDrives`) —
готовые диски по `DriveInfo` (шелл добавил бы сеть, Y3), за ними
устройства; телефон и камера — `DeviceFolders` (ниже). Строка диска — корень
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
  стоп без причины (zip с паролем без окна) — `ArchiveLockedException`;
  словами оба — `MainViewModel.DescribeError` (частые `COPYENGINE_E_*`:
  диск полон, FAT32, корень FAT, занято). Архив в обычной папке —
  листинг верхнего уровня на пуле (`ExtractionSourcesAsync`); сведения о
  совпадениях — листинг раз на папку архива за извлечение.
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
  бы все свои архивы; папка внутри архива пробуется через шелл с
  бюджетом 300 мс на пачку (`WorkspaceController.ProbeAsync`: zip — ~15 мс
  на папку, tar.gz распаковывается целиком), остальные — шеврон на веру.
  Меню и цели drop нет (`FolderTreesView.HasMenu`);
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
- **Телефон и камера** (Y4). Путь —
  `DevicePath`: `device:<имя>\<хранилище>\<папки>`, имя — шелла (двойник
  — `(2)`), не parsing name: `\\?\` внутри ломает `GetDirectoryName`, PnP
  ID от порта. Список устройств — элементы «Этого компьютера» с интерфейсом
  WPD (`OwnApartment`); содержимое — WPD напрямую (`WpdDevice`: FTM-класс,
  свойства пачкой по списку, у PTP — по одному; поток и миниатюра —
  ресурсы), фолбэк — `ShellDeviceReader` (`IDeviceContent.ThroughShell`,
  «Отладка»). ID объектов живут сессией: `DeviceFolders` держит сессию и
  кэш «путь → объект» по имени устройства, `VolumeChange.IsDevice` из
  `VolumeChangeHub` (`GUID_DEVINTERFACE_WPD`) закрывает её. Мусор корня —
  `Hidden` + `System` по `DeviceClutter` (решение человека 2026-10-19:
  имена с точки, `LOST.DIR`, в `Android` — всё, кроме `media`, пустые
  стандартные `Alarms`, `Ringtones`…; пустые нестандартные не прячутся —
  по имени не отличить от папки человека), прячет `EntryVisibility`. Общие
  с архивом развилки App — `Containers` (`Contains` — запись внутрь
  запрещена, `Inside` — выносится своей копией). Копирование с устройства
  — `DeviceImportService` (Core, тест на `FakeDeviceContent`):
  `IFileSystem.WriteFromStream` (временное имя, хэш, проверка, дата
  устройства), совпадения — «идентичны» по размеру и дате
  (`SameBySizeAndDate`), пропажа устройства — пауза
  (`IOperationHandle.SetPaused`, опрос 2 с, 10 мин), вернулось — тот же
  файл заново. Временная копия просмотра и «Открыть» — `CopyOut` ветки
  устройства, у RAW для просмотра — только голова файла (PREVIEW, «RAW
  с устройства»); миниатюры — `DeviceThumbnail`: у RAW — своя камеры
  (160 px, без поворота — поворот только в заголовке файла), после
  просмотра — из прочитанной головы, с поворотом (`AsyncIcon.Invalidate`
  после `CopyHead`); брошенный до конца поток R8 отпускает ~0,42 с
  (дочитывает файл; `Cancel` не помогает, 2026-10-22) — строка `Device
  stream let go` в логе. Ошибка `IEnumPortableDeviceObjectIDs.Next` — конец
  списка, как в 0.5, со строкой `WARN` (правильный вариант — PLAN 0.7);
  пока камера читает
  что-то рядом (голову соседнего снимка), миниатюру она не отдаёт
  (0x800700AA, R8): у RAW — ещё три попытки через 100 мс, потом бросок
  без запоминания значка типа, повтор `AsyncIcon` через секунду и строка
  `[icon]` в логе; значок устройства и
  хранилища — шелла по parsing name, малый при пустом `SHGFI_ICON` (R8) —
  по индексу из системного списка (`LoadViaParse`). Только чтение: `Delete` и
  вставка на устройство — отказ строкой статуса, меню — «Открыть»,
  «Копировать», «Скопировать в…». Закладка на папку устройства — строка,
  пока оно подключено (WINDOW, «Компьютер» и закладки). Решения человека
  2026-10-19: [WPD напрямую, шелл — строка устройства и фолбэк
  настройкой]; [10 минут ожидания]; [закладки на папки устройства — видны
  только при подключённом; своё название — у любой закладки].
  - **Почему WPD напрямую** (развилка 6, 2026-10-19). Холодный листинг
    большой папки телефона шеллом — минуты (Pixel 6, 1743 файла — 33 с;
    OnePlus 6T, 7644 — 3 мин) против 1,2 и 10 с у WPD (`EnumObjects` плюс
    свойства пачкой `IPortableDevicePropertiesBulk`; у PTP / R8 пачки нет —
    по одному, 6 мс на объект, как шелл). Шелл — строка устройства в
    «Компьютере» (имя, значок; parsing name = PnP ID) и фолбэк «Отладка» →
    «Читать устройства через оболочку Windows»
    (`AppSettings.DevicesThroughShell`). Пакет `MediaDevices` не взят:
    чужой код ради одной ветки.
  - **Факты стенда Y4a** (2026-10-19; Pixel 6, OnePlus 6T, Canon R8; стенд
    `artifacts\stands\DeviceStand`, вне гита). PnP ID у OnePlus и R8 — от
    порта (другой порт — другой ID), у Pixel — серийный; PUID объекта
    переживает переподключение, ID объектов WPD — сеансовые; parsing name
    с PUID обратно не открывается (`E_INVALIDARG`) — в шелл только по
    display names. `BHID_EnumItems` даёт имя, размер, дату изменения
    (создания — только камера); атрибуты Android `copy, move, ren, del`,
    R8 — `ro, copy, del`; `SFGAO_HASSUBFOLDER` у всех папок. Поток — 33–35
    МБ/с у телефонов (USB 2), R8 — 38 / 63 по USB 2 / 3, буфер 256 КБ;
    миниатюра шелла — 17–23 мс, WPD-миниатюра R8 — ~14 КБ. Устройство ушло
    посреди чтения — `IStream::Read` 0x8007001F и `REMOVAL` в ту же 0,1 с;
    кабель и выключатель не различить. Запертый телефон — устройство есть,
    хранилищ нет, кода нет; разблокировали и выбрали «Передача файлов» —
    переподключение; заперли после — доступ остаётся. Новое на телефоне —
    в листинге через ≤ 4 с, дата папки не меняется. Время изменения копии
    = устройства, создания — момент копии.
  - **Ошибки устройств** — `DeviceErrors`: `IsAsleep`
    (`E_WPD_DEVICE_NOT_OPEN`, `E_WPD_DEVICE_IS_HUNG`), `IsGone` (те же
    плюс 31, 21, 1167) — пауза импорта; «объект не найден» — «телефон
    обновил список файлов, обновите папку»; запертый телефон — `StateOf ==
    Locked`, подсказка в статусе.
  - **Как источники видит Windows.** Флешка, внешний диск, кардридер — том
    с буквой (FAT32: файл до 4 ГБ, 65 534 записей в папке, mtime с шагом
    2 с; плохая карта — 23 / 1117 посреди файла; вынули — 21). Фотоаппарат
    по USB — PTP / MTP, узел WPD без буквы (Fujifilm, Panasonic, Sony в
    режиме накопителя — том): чтение, одна передача за раз, свои
    миниатюры, выключение рвёт сессию. Телефон Android — MTP, тот же узел:
    доступ при разблокированном экране, листинг из кэша медиасканера.
    iPhone — PTP драйвером Apple, только `Internal Storage\DCIM`,
    «Доверять этому компьютеру». Сверх копирования (папка по дате, вторая
    копия, удаление после проверки, «только новые») — BACKLOG «Импорт с
    камеры».
  - **Не в блоке** — TECHDEBT «Устройства: хвосты Y4»; запись на
    устройство и удаление с него — BACKLOG.

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
  `.pp3`) раньше `Replaced` (`IMG.xmp`); каждое поле — из первого, где оно
  есть (`ReadRating(sidecars)`: `.pp3` без `Rank` не прячет звёзды `.xmp`
  darktable), — поверх оценки из снимка (`EmbeddedRating.Merge`: поле
  сайдкара, и 0 тоже, перекрывает камеру). **Запись** (`WriteTargets`) —
  во все сайдкары, где поле есть, и в `.pp3` всегда (2026-10-16:
  RawTherapee пишет туда `Rank` на каждую свою оценку, без него — «в RT не
  оценивали», и RT показывал бы камеру); ни в одном — в первый; ключа не
  было — `Ctrl+Z` его убирает, а не пишет 0. Создаваемый несёт второе
  поле снимка, чтобы метка не сняла звёзды камеры.
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
