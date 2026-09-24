# Операции: надёжное копирование — спека блока (0.6)

Спека Фабле, 2026-09-22, для блока 4 версии 0.5; решением человека
2026-09-24 блок перенесён в 0.6. Сверена с кодом после блока 2 —
2026-09-24 (номера строк — по дереву на эту дату; финализация блока 2 и
работы 0.5 их сдвинут — перед запуском блока сверить шаги 1, 5 и 7).
Реализует Опус («делай блок „Надёжное копирование“»), ревью и
финализация — Фабле; шаги с COM / P/Invoke (5, 6) Фабле делает сам или
ревьюит первыми, шаг 7 трогает модель панелей блока 2 — её правило тоже
первым смотрит Фабле. Файл живёт до финализации блока, затем удаляется
(механизмы → ARCHITECTURE «Файловые операции», снапшот → DONE).

**Цель.** Копирование с флешки и карты фотоаппарата идёт минутами и должно
быть надёжным, прозрачным и ошибкоустойчивым: видно, что происходит и чем
кончилось, сбой не теряется, упавшее можно повторить. Решения человека
2026-09-22: [цель — съёмный том с буквой диска (Canon R8 виден как диск),
приоритет — надёжность, не скорость]; [MTP / PTP — 0.6]; [в 0.5 — самое
полезное, сложное — 0.6]; [завершённые операции живут в строке состояния,
пока пользователь не убрал; с ошибкой — ждут решения]; [журнал — сеанса].

## Границы

**Этот блок:** запись операции с итогом по элементам, строка
состояния держит завершённые, «повторить» и «убрать»; повтор при
временных ошибках ввода-вывода внутри операции; понятные сообщения по
кодам (место, FAT32 4 ГБ, устройство пропало); временное имя при
копировании; компьютер не засыпает; съёмные диски появляются и исчезают в
дереве, операция на пропавшем томе честно падает и повторяется после
возврата; журнал структурный с итогом и строкой в лог; `Ctrl+Z` ждёт
занятых; страховка корзины; имя занятого файла в папке.

**Вторая очередь (BACKLOG «Копирование — вторая очередь»):** MTP / PTP;
свой движок с хэшем на лету и проверкой после копии; продолжение после вылета (журнал
операции на диске, уборка `.wander-tmp`); пауза при пропаже устройства и
автопродолжение; robocopy, небуферизованный ввод-вывод; таймауты UNC;
«Извлечь» через `CM_Request_Device_Eject`, если шелл глагол не даёт;
`IRecycleBin.RestoreMany`.

## Сейчас

- `SystemIOFileSystem.CopyFile` (`:175-186`) → `CopyFileWithProgress`
  (`:304-344`): `CopyFileExW` без флагов, кроме `COPY_FILE_FAIL_IF_EXISTS`
  (`ApplyOne` всегда передаёт `overwrite: false`); без прогресса и отмены
  — `File.Copy`; прогресс дельтами, отмена — `PROGRESS_CANCEL` (1235 →
  `OperationCanceledException`), недописанное при отмене убирает Windows;
  любая другая ошибка — `IOException(Win32-сообщение и оба пути, HRESULT
  0x8007xxxx)`. `MoveEntry` (`:212-238`): между томами папка —
  `CopyDirectory` + `Directory.Delete`, файл — `CopyFileWithProgress` +
  `File.Delete`; уборки при сбое нет. Повторов, проверки, временного имени
  нет (временные имена — только `ReplaceAtomic` `:259-276` и
  `FileOperationService.ScratchName`).
- `BatchExecutor.ApplyOne` (`:722-773`) — единственное место вызовов
  `CopyFile` / `CopyDirectory` / `MoveEntry` в пакетном пути; занятых ждёт
  только перенос (`Gate.Retry`, `WaitForFile`), копия не ждёт. `ApplyEntry`
  (`:353-425`) ловит `OperationCanceledException` (статус `Cancelled`,
  `CreateAction` на любой существующий `dest` — файл или папку) и
  `Exception` (`Outcome(Failed, false, ex)`, ошибка доезжает до результата
  через `failure ??=`). Результат — `BatchItemResult(Source,
  FinalDestination, Status, Error, Busy)` (`:910`), **один на группу**:
  спутники свёрнуты в основной файл; `BatchItemStatus { Ok, Skipped,
  Replaced, Renamed, Merged, Cancelled, Failed }`. Удаление —
  `DeleteResult(Path, Status, Error, Busy)`, `DeleteStatus { Ok, Failed,
  Cancelled }` (`:915-916`).
- `OperationTracker`: `Begin(verb, total, totalBytes, bytesAreWork, token)`
  (`:73-83`) → `IOperationHandle` (`Advance`, `AdvanceBytes`,
  `SetCurrentPath`, `SetTotalBytes`, `SetWeighing`, `Dispose`; `:321-348`);
  `Dispose` удаляет операцию (приватный `Remove`) — завершённых записей
  нет. `OperationSnapshot` (`:361`) — только ход; `WhenIdleAsync` ждёт
  пустого списка идущих. Все вызывающие только `Dispose`: `BatchExecutor`
  `:102` (удаление) и `:119` (пакет), `UndoService:139`,
  `ExtractionService:103`, `ExternalActionRunner:81`, `DebugOperation:91`.
  `OperationViewModel` — только живая строка.
- Строка состояния — `MainWindow.xaml:622-785`: блок операций `:658-748`
  (видимость — `HasActiveOperations`, кнопка `OperationsButton` с
  `OperationsSummary` и `AggregateProgress`, попап `:694-746` по
  `Operations`); значок важности у строки статуса — `:756-781`
  (`StatusSeverity`, ресурсы `StatusWarningGlyph` / `StatusErrorGlyph`).
- **`HasActiveOperations => Operations.Count > 0`** (`MainViewModel:773`)
  держит не только полосу: тик сторожа ждёт, пока он истинен (`busy`,
  `:2455-2457`), выход спрашивает про идущие (`MainWindow.xaml.cs:131-148`,
  `:245-262`). Поэтому список назначения и не мигает: сторож молчит до
  конца операции, вставка и drop перечитывают сами (`:5229-5232`,
  `:1799-1801`).
- Итог: `MainViewModel.ReportBatchResults` (`:5406-5436`) — счётчики и
  **первая** ошибка (`DescribeError` `:5497-5517`), `Say(text,
  StatusSeverity)` (`:3278`) → `Journal.Note` + строка статуса;
  `SetStatusQuietly` (`:3263`) — строка без журнала, всегда Info.
  `ActionJournal` (`Core/Logging`): `Note(text, at, severity)`, `Render()`,
  предел 500; окна нет — `ShellCommandsController.OpenJournal` (`:88-105`)
  пишет `journal-<pid>.txt` и открывает редактором.
- `TransientFiles.ReplaceSuffix = ".wander-tmp"`; сторож
  (`WindowsDirectoryWatcher.OnRenamed:118-121`) временные не сообщает, а
  переименование **из** временного имени считает нестроструктурным и без
  `OldPath`. Тик по пути, которого нет в листинге, уже перечитывает папку
  (`FolderSession.DecideWatchTick:425-430` через `FolderChanges.RowsFor`,
  тест `AChangedFileNobodyShows_Relists_WithoutBotheringTheTrees`).
- Устройства: строки дисков — `PanelRow` вида `Drive` на верхнем уровне
  панели «Компьютер» (ключ `""`; модель блока 2 — `PanelState`,
  `PanelRules`). Читаются один раз: `WorkspaceStarted` →
  `WorkspaceController.ReadLevel("")` из `GetRoots()` (неготовые диски
  пропускаются); перечитывание (`PanelRules.Reread:450`) верхний уровень
  пропускает. `WM_DEVICECHANGE` никто не слушает; хуков `HwndSource` в App
  нет (`MainWindow.OnSourceInitialized:120-129` — только геометрия);
  `SetThreadExecutionState` / power requests нет.
- Харнесс: `fs` — create / mkdir / append / delete / lock / unlock, без
  `rename` (шаг `rename` — переименование в приложении); проверки итога
  операции нет; `wait-idle` операций не ждёт — только тишину лога и конец
  чтения листинга (`WaitIdleAsync:320-340`).

## Шаги

Порядок: сначала запись операции (1) — на неё опираются повтор (2), журнал
(3) и отладочная операция («Отладка» → «Операция», AI1 — стенд для всего
блока); затем то, что меняет поведение копирования (4, 5); затем окружение
(6, 7) и хвосты.

### 1. Запись операции: завершённые остаются

Core `Operations/`:

- `OperationItem(string Source, string? Destination, BatchItemStatus Status,
  Exception? Error, int Attempts = 1)`; `OperationOutcome { Done, Problems,
  Cancelled, Failed }` (`Problems` — есть `Failed` / `Skipped` с ошибкой при
  сделанном, `Failed` — ничего не сделано).
- `OperationRecord(long Id, string Verb, OperationOutcome Outcome, int
  Total, int Done, int Failed, int Skipped, int Cancelled, long Bytes,
  TimeSpan Elapsed, DateTime FinishedAtUtc, string? Target,
  IReadOnlyList<OperationItem> Items)` — `Items` обрезаны до 200 (первые и
  все проблемные), `ItemsTruncated` числом.
- `IOperationHandle.Finish(IReadOnlyList<OperationItem> items, string?
  target)` — считает итог, переводит операцию из `Running` в `Finished`;
  `Dispose` без `Finish` — как сейчас: запись `Done` без элементов, если
  токен не отменён, иначе `Cancelled` (извлечение, откат, свои действия
  переходят на `Finish` позже, не в этом шаге).
- `OperationTracker`: список завершённых (потолок 50, старые вытесняются),
  `IReadOnlyList<OperationRecord> Finished`, `Dismiss(long id)`,
  `DismissFinished(bool problemsToo)`, событие `Changed` — общее;
  `Snapshot()` и `WhenIdleAsync` — только идущие (выход из приложения
  завершённых не ждёт).
- `BatchExecutor.RunBatchAsync` (`:112-132`): после `ApplyBatch` —
  `op.Finish(items, destinationFolder)`; элемент — один на
  `BatchItemResult`, то есть на группу (спутники свёрнуты в основной).
  `DeleteManyAsync` (`:99-107`) — то же из `DeleteResult`.

App:

- `OperationViewModel`: `IsFinished`, `Outcome`, `OutcomeGlyph`
  (`Segoe MDL2`: `E73E` готово, `E7BA` с проблемами, `EA39` ошибка, `E711`
  отменено), `OutcomeText` («готово 12», «с ошибкой 1, пропущено 2»),
  `ProblemLines` (до 10: имя — причина через `DescribeError`), `RetryCommand`
  (`CanExecute` = есть `Failed` или `Cancelled`), `DismissCommand`.
- `MainViewModel.RebuildOperations` (`:5550-5592`): строки идущих как
  сейчас, затем завершённые из `Finished`. **`HasActiveOperations` —
  только идущие**, не `Operations.Count`: на нём тик сторожа и вопрос при
  выходе, завершённая строка не должна их держать. Новое `HasOperations` —
  видимость блока; `OperationsSummary` при отсутствии идущих — «✓ 2 · ⚠ 1».
- `MainWindow.xaml`, попап (`:694-746`): строка завершённой — глифы,
  глагол, итог, кнопки «Повторить» / «Убрать», раскрывающиеся
  `ProblemLines`; внизу «Убрать завершённые» (`DismissFinishedCommand`, без
  проблемных; `Shift` — все). Строки — ресурсы `Operation*` (есть `Items`,
  `Bytes`, `Speed`, `Remaining`, `Percent`, `One`, `Weighing`, `Many`).
- Выход: вопрос про идущие операции как есть (`OnClosing`,
  `ConfirmExitWithOperations`, `StopOperationsThenClose`; при сбое —
  `App.StopOperationsBeforeDying`); завершённые с проблемами выход не
  держат [решение].
- Отладочная операция — на `Finish` в этом шаге (комментарий
  `DebugOperation:105-107` это обещает): «ошибка на 4-м» — элемент `Failed`
  с `IOException` (сейчас только счётчик и строка лога); «отмена посреди»
  — отменять токен, под которым операция заявлена (сейчас отменяется
  связанный, а операция стоит под токеном окна — запись вышла бы `Done`).

Тесты: `OperationTrackerTests` — `Finish` считает итог, `Dispose` без
`Finish`, потолок 50, `Dismiss`, `Changed` на каждое; `BatchExecutorTests`
— запись после копии с одним `Failed` (FakeFileSystem, шаг 4).

### 2. Повторить

- Core `Operations/OperationRetry.cs`: `RetryPlan? Plan(OperationRecord)` →
  `RetryPlan(string Verb, IReadOnlyList<(string Source, string
  DestinationFolder)> Items)` из `Failed` и `Cancelled` элементов (источник
  на месте — переносился ли он, неважно: не доехал); элемент — основной
  файл группы, спутники собираются заново, как при вставке; нет элементов
  — null. Тест: смешанная запись → только упавшие; переименованные и
  пропущенные не повторяются.
- `MainViewModel.RetryOperationAsync(long id)`: план → по папкам назначения
  → тот же путь, что вставка: группировка спутников (как `:5192`) и
  `CopyManyAsync` / `MoveManyAsync` (`FileOperationService:294-302`) с
  окном прогресса, конфликты спрашиваются заново; новая запись, старая
  убирается при старте повтора. Выделение после повтора приземляется, как
  после вставки, — только если папка назначения открыта
  (`FolderSession:217-225`). Копирование: источник изменился — конфликт
  решает окно, как обычно.
- Удаление: «Повторить» — `DeleteAsync` (`:4151`) по `Failed`, с
  подтверждением, как у обычного удаления (столп 2), → `RunDeleteAsync`
  (`:4233-4333`); `AskRetryInUse` (`:4340`) остаётся (спрашивает сразу).

### 3. Журнал структурный и строка итога в лог (AD5)

- Core `Logging/ActionJournal.cs`: `Record(JournalEntry)`;
  `JournalEntry(DateTime At, JournalKind Kind, string Headline,
  IReadOnlyList<string> Lines, StatusSeverity Severity)`; `JournalKind {
  FolderOpened, FileOpened, Copied, Moved, Renamed, Recycled, Deleted,
  Restored, Created, Extracted, Cancelled, ActionRun, Error, Note }`;
  `Render()` — заголовок как сейчас, строки с отступом два пробела, первые
  50 и «… ещё N»; предел 500 — по записям. `Note` остаётся для строк без
  структуры (поиск, буфер, ошибки, открытие папки — `MainViewModel:2711`,
  `:2828`).
- App: `ReportBatchResults` строит запись из `OperationRecord`
  («Скопировано в E:\Video — 12 файлов, 12,3 ГБ, 5 мин 27 с»; строка на
  элемент; у переименования «было → стало» из `plan`), зовёт
  `Journal.Record`, а строку статуса ставит без второй записи в журнал:
  `SetStatusQuietly(text, severity)` — сейчас он всегда Info, получает
  важность. Открытие и запуск — запись `FileOpened`: `OpenEntry`
  (`:1661-1698`, `_shell.Open` `:1690`), «Открыть с помощью» (`OpenWith`
  `:4061` → `ShellCommandsController.OpenWith` `:120-127`), временная
  копия из архива (`OpenArchiveEntryAsync` `:5389-5403`).
- Лог: `BatchExecutor` в конце `RunBatchAsync` — `Copy done: 12 items,
  12.3 GB, 327.4 s, 38.5 MB/s; failed 1, skipped 0` (INFO, английский —
  технический лог; сейчас только строка на элемент, `:397`).
- Строки отката: `IUndoableAction` (`Undo/IUndoableAction.cs`, default-члены
  уже есть — `PathsAfterUndo`, `MetadataTargets`, `MovesOnUndo`, `Steps`)
  получает `string Verb => OperationVerbs.Undo` и `int ItemCount => 1`,
  реализации ставят своё: `MoveAction` (`UndoableActions.cs:28-34`),
  `CompositeAction` (`:81-126`), две в `CompanionMetadataService` (`:354`,
  `:378`), составные из `ExtractionService` (`:326-328`) и `RenameMany`
  (`:205`). Английский `Description` сейчас видят `StatusUndone`
  (`MainViewModel:4475`), подсказка «Отменить» (`NextDescription`, `:1566`)
  и `NamesOf` (`:4543`) — все три строятся из `Strings.Get(verb)` и числа;
  в лог (`Unwind:214`, `PushComposite:701-713`) `Description` остаётся.
- Окно журнала — как сейчас (файл в редакторе); формат текста — тот же
  `Render`.

Тесты: `ActionJournalTests` — `Record` + `Render` (отступы, «ещё N»,
предел по записям); `UndoableActionsTests` — `Verb` / `ItemCount` у
`MoveAction`, `CompositeAction`.

### 4. Повтор при временных ошибках и понятные сообщения

- Core `FileSystem/TransientIo.cs`: `bool IsTransient(Exception)` — по
  `HResult & 0xFFFF` ∈ {21 `NOT_READY`, 23 `CRC`, 31 `GEN_FAILURE`, 59
  `UNEXP_NET_ERR`, 64 `NETNAME_DELETED`, 121 `SEM_TIMEOUT`, 1117
  `IO_DEVICE`, 1167 `DEVICE_NOT_CONNECTED`, 1231 `NETWORK_UNREACHABLE`}
  (не путать с `TransientFiles.IsTransient(string)` — то про путь);
  `IoRetry.Run(Action, RetryPolicy, CancellationToken, Action<int>?
  onRetry)`, `RetryPolicy(int Attempts = 3, TimeSpan[] Delays = 1, 3, 5 с)`
  — сон отменяемый, последняя ошибка наружу.
- `BatchExecutor.ApplyOne` (`:722-773`): копирование файла и перенос между
  томами — в `IoRetry.Run`; перед повтором цель убирается (временное имя,
  шаг 5, делает это само); `onRetry` → номер попытки числом в
  `OperationSnapshot.Attempt` (текста в Core нет — `OperationViewModel`
  пишет «повтор 2 из 3»). **`CurrentPath` не трогать**: его как настоящий
  путь читают строка операции и панель просмотра (`OperationViewModel:134`,
  `PreviewController:1506-1507`). Число попыток — в
  `BatchItemResult.Attempts` → `OperationItem.Attempts`. `BusyGate.Retry`
  (занятость, `FileInUse` = только 0x80070020) остаётся отдельным слоем:
  занятость — не временная ошибка ввода-вывода.
- App `DescribeError` (`:5497-5517`; сейчас — отказ корзины, путь занят
  другой операцией, `IOException` с держателем, иначе `ex.Message`): коды →
  текст: 112 `DISK_FULL` («нет места на X:»), 223 `FILE_TOO_LARGE` («файл
  больше 4 ГБ, а диск — FAT32», если `IVolumeInfoProvider.Describe(цель)?
  .FileSystem == "FAT32"`; у неготового тома `FileSystem` пустой), 1167 /
  21 («устройство отключено или не готово»), 23 / 1117 («ошибка чтения с
  диска — носитель или кабель»); прочие как сейчас.
- FakeFileSystem: `CopyFailures` — `Dictionary<string, Queue<Exception>>`:
  очередь исключений на путь, каждый `CopyFile` снимает одно; пустая —
  успех (рядом уже есть `MoveInUseFor`, `RenameFailures`, `CopyChunk`;
  `CopyFile` пишет цель до прогресса).

Тесты: `TransientIoTests` (коды); `IoRetryTests` (две ошибки — третий
успех; три — наружу; отмена во сне); `BatchExecutorTests` — копия с двумя
временными ошибками = `Ok`, `Attempts = 3`; с постоянной = `Failed`,
`Attempts = 1`.

### 5. Временное имя при копировании

- `TransientFiles.TempNameFor(string destination)` → `destination +
  ".wander-tmp"` (тот же суффикс).
- `SystemIOFileSystem.CopyFile` в ветке с прогрессом / отменой: `temp =
  TempNameFor(dest)`; сирота с таким именем — удалить (наша);
  `CopyFileWithProgress(src, temp)`; `File.Move(temp, dest, overwrite)`; при
  исключении и отмене — `TryDelete(temp)`, дальше как было. `File.Copy`-ветка
  без прогресса (тесты, мелочь) — без изменений. `MoveEntry` файла между
  томами — тем же путём.
- `BatchExecutor.ApplyEntry`, ветка отмены (`:400-419`): частичного файла
  больше нет — undo-шаг `CreateAction` для файла не создаётся (для папки
  остаётся). **Меняет поведение под тестом:** `BatchExecutorTests:1081-1109`
  (файл, отменённый посреди, откатывается) правится этим шагом.
- Сторож — уже готово (блок 2): тик по пути, которого нет в листинге,
  перечитывает папку, и во время операции тики и так ждут
  (`HasActiveOperations`). Остаётся шаг харнесса ниже.
- `MoveEntry` папки между томами при исключении (`:218-223`): удалить
  частичную копию, если её создали мы (закрывает TECHDEBT «cross-volume без
  уборки»).

Глазами: во время копирования большого файла в открытую папку назначения
файла не видно до конца, потом появляется готовым; отмена — ни файла, ни
`.wander-tmp`; вылет процесса (убить из диспетчера) — остаётся
`имя.wander-tmp`, виден в списке, удаляется руками (уборка — 0.6).

### 6. Компьютер не засыпает

- Core `Operations/IPowerGuard.cs`: `IDisposable KeepAwake(string reason)`.
- Platform `Power/PowerRequestGuard.cs`: `PowerCreateRequest`
  (`REASON_CONTEXT` с текстом) → `PowerSetRequest(PowerRequestSystemRequired)`
  → `PowerClearRequest` + `CloseHandle` в `Dispose`; без
  `PowerRequestDisplayRequired` — экран гаснет, система нет. Причина —
  `ITextSource` ключ `PowerRequestReason` («Wander: файловая операция»);
  видна в `powercfg /requests`.
- `OperationTracker`: `IPowerGuard?` через конструктор (сейчас `(TimeSpan?
  minInterval, Func<DateTime>? now)`, создаётся в `PlatformBootstrapper:107`);
  первая идущая операция берёт, последняя `Dispose` отпускает. Харнесс
  берёт настоящие сервисы платформы (подменяет только лог и диалоги) —
  машина не спит и во время его операций; счётчик — `FakePowerGuard` в
  тестах Core. Тест: `OperationTrackerTests` — счётчик 1 при двух
  операциях, 0 после обеих.

### 7. Съёмные диски (Y2)

- Core `FileSystem/IVolumeChanges.cs`: `event EventHandler<VolumeChange>?
  Changed`; `VolumeChange(string Root, bool Arrived)`.
- Platform `FileSystem/VolumeChangeHub.cs : IVolumeChanges`: `bool
  HandleMessage(int msg, nint wParam, nint lParam)` — `WM_DEVICECHANGE`
  (0x219), `DBT_DEVICEARRIVAL` (0x8000) / `DBT_DEVICEREMOVECOMPLETE`
  (0x8004), `DEV_BROADCAST_VOLUME` (`dbcv_devicetype == 2`), буквы из маски
  `dbcv_unitmask`; событие на каждую букву. Platform без WPF — хук ставит
  App: `MainWindow.OnSourceInitialized` (`:120-129`) → `HwndSource.AddHook`,
  вызов `hub.HandleMessage`.
- Панель «Компьютер» — модель блока 2, узлов дерева больше нет: событие
  `VolumesChanged` в `WorkspaceEvents` → `PanelRules` перечитывает верхний
  уровень панели дисков (`ReadBranch("")`; `Reread` его сейчас пропускает —
  `PanelRules:450-455`, а `FolderChanged("")` лишь пометит уровень
  непрочитанным) → `BranchRead` сливает строки по порядку букв и уводит
  курсор с ушедших (`OnBranchRead`, `PanelRules:161-183`), шевроны —
  эффектом `ProbeChevrons`. **Не** `Removed(paths)`: он снимает совпавшие
  строки в обеих панелях, включая закладки верхнего уровня
  (`PanelRules:235-240`, `:624-643`), а закладка на флешку должна сереть.
  Закладки — `PostBookmarks()` (`MainViewModel:1969`): пересчитает
  `PanelRow.IsMissing`. Тест `PanelRulesTests`: диск пришёл — строка на
  своём месте; ушёл — курсор на соседе, закладка осталась.
- Открытая папка на ушедшем томе —
  `NavigationFallback.AfterVolumeRemoved(current, roots, recent)` (новый
  рядом с `AfterDelete`, `AfterRestore`; тест: последний недавний путь на
  живом томе, иначе первый фиксированный корень); сторож текущей папки
  уходит без исключения (`WindowsDirectoryWatcher.Stop` уже ловит и пишет
  в лог). Событие приходит с UI-потока (хук) — маршалинг не нужен.
- Операция с пропавшего тома: временные ошибки (шаг 4) → три попытки →
  `Failed` → «Повторить» после возврата тома. Пауза и автопродолжение —
  0.6.
- «Извлечь» [стенд Фабле]: даёт ли `ShellContextMenu` для `E:\` глагол
  «Eject»; да — пункт уже приходит чужим, ничего не делать; нет — 0.6.
- Глазами: вставить / вынуть флешку при открытой папке на ней, при
  копировании с неё и на неё; закладка на флешку сереет и оживает.

### 8. `Ctrl+Z` ждёт занятых

`UndoService.Unwind` (`Undo/UndoService.cs:191-226`, сейчас зовёт
`step.Undo()` напрямую): каждый шаг через `BusyGate.Retry` / `RetryMany`
того же ворот, что у операций. `UndoAsync(tracker, ct, PathClaims?
claims)` (`:127-153`) получает только заявки, а `HeldPaths` создаётся
внутри `FileOperationService()` (`:66-69`) и в локаторе не зарегистрирован
— отдать ворота наружу (свойство сервиса) и передать в `UndoAsync`.
Занятость → ожидание с бюджетом, отказ называет держателя. **Меняет
поведение под тестом:** сейчас упавший шаг выбрасывается из стека
(doc-комментарий `UndoService:108-112`, `UndoServiceAsyncTests:131`,
`:147`) — становится «остаётся, следующий `Ctrl+Z` пробует снова»; тесты
правятся шагом. App: `ReleasePreview` перед `UndoAsync` (единственный
вызов — `UndoLast`, `MainViewModel:4440-4441`), как перед удалением. Тест
`UndoServiceTests`: `MoveInUseFor` на один раз — откат прошёл со второй
попытки, шаг не выброшен из стека при отказе.

### 9. Корзина: страховка

`ShellRecycleBin` (`:144-148`): оболочка ответила успехом без
идентификатора элемента корзины — сейчас `WARN` («Ctrl+Z will not find
it») и хэндл с путём файла в корзине; откат ищет по id, затем по этому
пути (`MoveOut:294-299`), иначе падает. Страховка — когда нет **ни id, ни
пути**: элемент `DeleteStatus.Ok` с флагом `Permanent = true` (новое поле
`DeleteResult`, `BatchExecutor:915`), undo-шаг не создаётся
(`BatchExecutor:686-689`), итог и журнал говорят «удалён безвозвратно
(корзина не приняла)». Id нет, путь есть — откат живой, строку `WARN`
поправить. Запасной поиск по времени (`IRecycleBin.cs:10-18`) описан, но
не сделан — не в этом шаге. Стенды границ (а), (б) — PLAN T.

### 10. Занятая папка: назвать файл и программу (AF)

Отказ переноса / переименования папки с «что-то внутри занято»: отчёт о
занятости папки собирает `BusyGate.Retry` (`:202-227`), держателя называет
`NameHolder` (`:304-318`); `RestartManagerLockInspector` уже раскрывает
папку в первые 500 файлов одной сессией (`:68-90`) — называет программы,
но не файл; `IFileBusyProbe.IsBusy` для папки всегда `false`. Добавить:
обход `IFileBusyProbe.IsBusy` по файлам папки до первого занятого (предел
5 с, отменяемый), Restart Manager — про него одного; текст «занят `имя` —
`программа`». Проверять — «Отладка: занять на 30 с» в «Действиях» (AI2).

### 11. Хвосты

- I2: спутник упал после переезда основного — откат уже переехавших
  членов группы в `ApplyGroup` (`:301-345`; как `RenameMany`, `:160-206`,
  `:253-263`), статус группы `Failed`.
- `DeleteForGood` (`:598-629`, сейчас только `gate.Check`) ждёт занятый
  через `BusyGate` (TECHDEBT).
- Проверка длинного пути для корзины — один раз на операцию:
  `TooLongForBin` (`ShellRecycleBin:222-244`) идёт на каждый путь в каждом
  `SendMany` (`:95`), а `RetryMany` шлёт всё заново (`BatchExecutor:666-678`)
  (TECHDEBT).
- BACKLOG «reparse points»: `CopyDirectory` (`:188-210`) пропускает
  junction-подпапки молча (`:199-205`) — в итог элемент `Skipped` с
  причиной «ссылка не копируется». Символические ссылки на файлы внутри
  копируются содержимым цели, источник-junction верхнего уровня —
  насквозь: так и оставить, записать в ARCHITECTURE.

## Харнесс и стенды

- `ScenarioRunner`: шаг `assert-operations` (`{"step":
  "assert-operations", "finished": 1, "failed": 1, "outcome":
  "Problems"}`) с ожиданием конца идущих (`wait-idle` операций не ждёт),
  команды `retry-last-operation`, `dismiss-operations`; `fs` получает
  `rename` — переименование снаружи.
- `file-ops.json` (сейчас: копия и вставка, три ответа на конфликт,
  перенос, цепочка откатов, занятый файл, длинный путь, read-only; упавшей
  копии и записи операции нет): `fs lock` источника → `copy` + `paste` →
  `assert-operations failed 1` → `fs unlock` → `retry-last-operation` →
  `assert-operations failed 0` → `assert-entries`. Шаг с `.wander-tmp`: `fs
  create x.wander-tmp` → `fs rename` в `x` → `assert-entries contains x`
  (сторож перечитывает путь, которого не было в листинге; логика уже есть —
  шаг проверяет её вместе с фильтром временных).
- Отладочная операция (AI1) — глаза на строку состояния без диска:
  сценарии «ошибка на 4-м» и «отмена посреди» дают запись с проблемами
  (после правки `DebugOperation` в шаге 1).
- Стенд Фабле: `powercfg /requests` при идущей операции; `Eject` в меню
  тома.
- Живой прогон человека: копия 2–4 ГБ с карты R8 на SSD и обратно; вынуть
  карту посреди, вернуть, «Повторить»; строка итога в логе и журнале.

## Критерии ревью

- Core без Windows: коды ошибок — числа с именами в комментарии, power и
  устройства — за интерфейсами; Platform без WPF (хук в App).
- Ни одна ветка не теряет исключение: `Failed` всегда с `Error`.
- Повтор не удваивает undo: старая запись убирается, новая операция — свой
  шаг отката.
- Запись операции не держит `FileSystemEntry` и битмапы — только строки и
  числа (50 записей × 200 элементов).
- Завершённые строки не держат тик сторожа, полосу хода и вопрос при
  выходе: `HasActiveOperations` — только идущие.
- Список назначения не мигает: `.wander-tmp` не появляется в листинге при
  копировании, готовый файл появляется один раз.
- Тесты, правленные по существу (`BatchExecutorTests:1081-1109`,
  `UndoServiceAsyncTests:131`, `:147`), — только те, что названы в шагах 5
  и 8.
