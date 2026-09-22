# Операции: надёжное копирование — спека блока 4 (0.5.0)

Спека Фабле, 2026-09-22. Реализует Опус («делай блок 4»), ревью и
финализация — Фабле; шаги с COM / P/Invoke (5, 6) Фабле делает сам или
ревьюит первыми. Файл живёт до финализации блока, затем удаляется
(механизмы → ARCHITECTURE «Файловые операции», снапшот → DONE).

**Цель.** Копирование с флешки и карты фотоаппарата идёт минутами и должно
быть надёжным, прозрачным и ошибкоустойчивым: видно, что происходит и чем
кончилось, сбой не теряется, упавшее можно повторить. Решения человека
2026-09-22: [цель — съёмный том с буквой диска (Canon R8 виден как диск),
приоритет — надёжность, не скорость]; [MTP / PTP — 0.6]; [в 0.5 — самое
полезное, сложное — 0.6]; [завершённые операции живут в строке состояния,
пока пользователь не убрал; с ошибкой — ждут решения]; [журнал — сеанса].

## Границы

**0.5 (этот блок):** запись операции с итогом по элементам, строка
состояния держит завершённые, «повторить» и «убрать»; повтор при
временных ошибках ввода-вывода внутри операции; понятные сообщения по
кодам (место, FAT32 4 ГБ, устройство пропало); временное имя при
копировании; компьютер не засыпает; съёмные диски появляются и исчезают в
дереве, операция на пропавшем томе честно падает и повторяется после
возврата; журнал структурный с итогом и строкой в лог; `Ctrl+Z` ждёт
занятых; страховка корзины; имя занятого файла в папке.

**0.6 (BACKLOG «Копирование — вторая очередь»):** MTP / PTP; свой движок с
хэшем на лету и проверкой после копии; продолжение после вылета (журнал
операции на диске, уборка `.wander-tmp`); пауза при пропаже устройства и
автопродолжение; robocopy, небуферизованный ввод-вывод; таймауты UNC;
«Извлечь» через `CM_Request_Device_Eject`, если шелл глагол не даёт;
`IRecycleBin.RestoreMany`.

## Сейчас

- `SystemIOFileSystem.CopyFile` (`:149-169`) → `CopyFileWithProgress`
  (`:283-327`): `CopyFileExW` без флагов, кроме `COPY_FILE_FAIL_IF_EXISTS`;
  прогресс дельтами, отмена — `PROGRESS_CANCEL`, недописанное при отмене
  убирает Windows; любая ошибка — `IOException(Win32-сообщение, HRESULT
  0x8007xxxx)`. `MoveEntry` (`:195-221`): между томами — копия +
  `File.Delete` / `Directory.Delete`. Повторов, проверки, временного имени
  нет.
- `BatchExecutor.ApplyOne` (`:722-763`) — единственное место вызовов
  `CopyFile` / `CopyDirectory` / `MoveEntry`; `ApplyEntry` (`:353`) ловит
  `OperationCanceledException` (статус `Cancelled`, undo на частичное) и
  `Exception` (`Outcome(Failed, false, ex)`, исключение доезжает до
  `BatchItemResult.Error`). Результат — `BatchItemResult(Source,
  FinalDestination, Status, Error, Busy)`, `BatchItemStatus { Ok, Skipped,
  Replaced, Renamed, Merged, Cancelled, Failed }`.
- `OperationTracker`: `Begin(verb, total, totalBytes, bytesAreWork, token)`
  → `IOperationHandle` (`Advance`, `AdvanceBytes`, `SetCurrentPath`,
  `SetTotalBytes`, `SetWeighing`, `Dispose`); `Dispose` удаляет операцию
  (`Remove` приватный) — завершённых записей нет. `OperationSnapshot` — только
  ход. `OperationViewModel` — только живая строка. Строка состояния:
  `MainWindow.xaml:624-705` (`HasActiveOperations`, `OperationsSummary`,
  `AggregateProgress`, попап `Operations`); значок важности у строки
  статуса — `:711-741` (`StatusSeverity`, ресурсы `StatusWarningGlyph` /
  `StatusErrorGlyph`).
- Итог: `MainViewModel.ReportBatchResults` (`:4989`) — счётчики и **первая**
  ошибка, `Say(text, OutcomeSeverity)` → `Journal.Note` + строка статуса.
  `ActionJournal` (`Core/Logging`): `Note(text, at, severity)`, `Render()`,
  предел 500; окна нет — `ShellCommandsController.OpenJournal` пишет
  `journal-<pid>.txt` и открывает редактором.
- `TransientFiles.ReplaceSuffix = ".wander-tmp"`; сторож
  (`WindowsDirectoryWatcher:118,126`) временные не сообщает, а переименование
  **из** временного имени считает нестроструктурным — рассчитано на
  `ReplaceAtomic`, где цель уже была в листинге.
- Устройства: `FolderTreesController.LoadRoots` один раз при старте,
  `WM_DEVICECHANGE` никто не слушает; хуков `HwndSource` / `WndProc` в App
  нет (`MainWindow.OnSourceInitialized` — только геометрия);
  `SetThreadExecutionState` / power requests нет.
- Харнесс: `fs lock` / `unlock` есть; проверки итога операции нет
  (`assert-entries`, `assert-log`).

## Шаги

Порядок: сначала запись операции (1) — на неё опираются повтор (2), журнал
(3) и отладочная операция (PLAN AI1, стенд для всего блока); затем то, что
меняет поведение копирования (4, 5); затем окружение (6, 7) и хвосты.

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
- `BatchExecutor.RunBatchAsync`: после `ApplyBatch` — `op.Finish(items,
  destinationFolder)`; элементы из `BatchItemResult` один в один.
  `DeleteManyAsync` — то же из `DeleteResult`.

App:

- `OperationViewModel`: `IsFinished`, `Outcome`, `OutcomeGlyph`
  (`Segoe MDL2`: `E73E` готово, `E7BA` с проблемами, `EA39` ошибка, `E711`
  отменено), `OutcomeText` («готово 12», «с ошибкой 1, пропущено 2»),
  `ProblemLines` (до 10: имя — причина через `DescribeError`), `RetryCommand`
  (`CanExecute` = есть `Failed` или `Cancelled`), `DismissCommand`.
- `MainViewModel.RebuildOperations` (`:5133`): строки идущих как сейчас,
  затем завершённые из `Finished`; `HasActiveOperations` — идущие (бар),
  новое `HasOperations` — видимость блока; `OperationsSummary` при
  отсутствии идущих — «✓ 2 · ⚠ 1».
- `MainWindow.xaml` попап: строка завершённой — глифы, глагол, итог,
  кнопки «Повторить» / «Убрать», раскрывающиеся `ProblemLines`; внизу
  «Убрать завершённые» (`DismissFinishedCommand`, без проблемных;
  `Shift` — все). Строки — ресурсы `Operation*`.
- Выход: вопрос про идущие операции как есть; завершённые с проблемами
  выход не держат [решение].

Тесты: `OperationTrackerTests` — `Finish` считает итог, `Dispose` без
`Finish`, потолок 50, `Dismiss`, `Changed` на каждое; `BatchExecutorTests`
— запись после копии с одним `Failed` (FakeFileSystem, шаг 4).

### 2. Повторить

- Core `Operations/OperationRetry.cs`: `RetryPlan? Plan(OperationRecord)` →
  `RetryPlan(string Verb, IReadOnlyList<(string Source, string
  DestinationFolder)> Items)` из `Failed` и `Cancelled` элементов (источник
  на месте — переносился ли он, неважно: не доехал); нет элементов — null.
  Тест: смешанная запись → только упавшие; переименованные и пропущенные не
  повторяются.
- `MainViewModel.RetryOperationAsync(long id)`: план → по папкам назначения
  → тот же путь, что вставка (`_ops.CopyAsync` / `MoveAsync` с окном
  прогресса, конфликты спрашиваются заново); новая запись, старая убирается
  при старте повтора. Копирование: источник изменился — конфликт решает
  окно, как обычно.
- Удаление (`DeleteManyAsync`): «Повторить» — тот же `DeleteSelectedAsync`
  по `Failed`; `AskRetryInUse` остаётся (спрашивает сразу).

### 3. Журнал структурный и строка итога в лог (AD5)

- Core `Logging/ActionJournal.cs`: `Record(JournalEntry)`;
  `JournalEntry(DateTime At, JournalKind Kind, string Headline,
  IReadOnlyList<string> Lines, StatusSeverity Severity)`; `JournalKind {
  FolderOpened, FileOpened, Copied, Moved, Renamed, Recycled, Deleted,
  Restored, Created, Extracted, Cancelled, ActionRun, Error, Note }`;
  `Render()` — заголовок как сейчас, строки с отступом два пробела, первые
  50 и «… ещё N»; предел 500 — по записям. `Note` остаётся для строк без
  структуры (поиск, буфер, ошибки).
- App: `ReportBatchResults` строит запись из `OperationRecord`
  («Скопировано в E:\Video — 12 файлов, 12,3 ГБ, 5 мин 27 с»; строка на
  элемент; у переименования «было → стало» из `plan`), зовёт
  `Journal.Record`, а строку статуса — через `Say(text, severity,
  journal: false)` (новый параметр): дубля нет. Открытие и запуск
  (`ShellCommandsController.Open` / `OpenWith`, временная копия из архива)
  — `Journal.Note` с `FileOpened`.
- Лог: `BatchExecutor` в конце `RunBatchAsync` — `Copy done: 12 items,
  12.3 GB, 327.4 s, 38.5 MB/s; failed 1, skipped 0` (INFO, английский —
  технический лог).
- Строки отката: `IUndoableAction` получает `string Verb =>
  OperationVerbs.Undo` и `int ItemCount => 1` (default-члены интерфейса),
  реализации ставят своё; `StatusUndone` формирует текст из
  `Strings.Get(verb)` и числа, `Description` из строк уходит.
- Окно журнала — как сейчас (файл в редакторе); формат текста — тот же
  `Render`.

Тесты: `ActionJournalTests` — `Record` + `Render` (отступы, «ещё N»,
предел по записям); `UndoableActionsTests` — `Verb` / `ItemCount` у
`MoveAction`, `CompositeAction`.

### 4. Повтор при временных ошибках и понятные сообщения

- Core `FileSystem/TransientIo.cs`: `bool IsTransient(Exception)` — по
  `HResult & 0xFFFF` ∈ {21 `NOT_READY`, 23 `CRC`, 31 `GEN_FAILURE`, 59
  `UNEXP_NET_ERR`, 64 `NETNAME_DELETED`, 121 `SEM_TIMEOUT`, 1117
  `IO_DEVICE`, 1167 `DEVICE_NOT_CONNECTED`, 1231 `NETWORK_UNREACHABLE`};
  `IoRetry.Run(Action, RetryPolicy, CancellationToken, Action<int>?
  onRetry)`, `RetryPolicy(int Attempts = 3, TimeSpan[] Delays = 1, 3, 5 с)`
  — сон отменяемый, последняя ошибка наружу.
- `BatchExecutor.ApplyOne`: копирование файла и перенос между томами — в
  `IoRetry.Run`; перед повтором цель убирается (временное имя, шаг 5,
  делает это само); `onRetry` → `progress.SetCurrentPath` с пометкой
  (текста в Core нет — `OperationSnapshot.Attempt` числом,
  `OperationViewModel` пишет «повтор 2 из 3»); число попыток — в
  `BatchItemResult.Attempts` → `OperationItem.Attempts`. `BusyGate.Retry`
  (занятость) остаётся отдельным слоем: занятость — не временная ошибка
  ввода-вывода.
- App `DescribeError`: коды → текст: 112 `DISK_FULL` («нет места на X:»),
  223 `FILE_TOO_LARGE` («файл больше 4 ГБ, а диск — FAT32», если
  `IVolumeInfoProvider.Describe(цель).FileSystem == "FAT32"`), 1167 / 21
  («устройство отключено или не готово»), 23 / 1117 («ошибка чтения с
  диска — носитель или кабель»); прочие как сейчас.
- FakeFileSystem: `CopyFailures` — `Dictionary<string, Queue<Exception>>`:
  очередь исключений на путь, каждый `CopyFile` снимает одно; пустая — успех.

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
- `BatchExecutor.ApplyEntry`, ветка отмены: частичного файла больше нет —
  undo-шаг `CreateAction` для файла не создаётся (для папки остаётся).
- Сторож: переименование из временного имени в имя, которого **нет в
  листинге**, должно перечитать состав. `FolderChanges` / `FolderSession.
  DecideWatchTick`: нестроструктурный тик по пути, не известному листингу,
  → `Refresh`. Тест в `FolderSessionTests`.
- `MoveEntry` папки между томами при исключении: удалить частичную копию,
  если её создали мы (закрывает TECHDEBT «cross-volume без уборки»).

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
- `OperationTracker`: `IPowerGuard?` через конструктор (регистрация в
  `PlatformBootstrapper`, харнесс — `FakePowerGuard` со счётчиком); первая
  идущая операция берёт, последняя `Dispose` отпускает. Тест:
  `OperationTrackerTests` — счётчик 1 при двух операциях, 0 после обеих.

### 7. Съёмные диски (Y2)

- Core `FileSystem/IVolumeChanges.cs`: `event EventHandler<VolumeChange>?
  Changed`; `VolumeChange(string Root, bool Arrived)`.
- Platform `FileSystem/VolumeChangeHub.cs : IVolumeChanges`: `bool
  HandleMessage(int msg, nint wParam, nint lParam)` — `WM_DEVICECHANGE`
  (0x219), `DBT_DEVICEARRIVAL` (0x8000) / `DBT_DEVICEREMOVECOMPLETE`
  (0x8004), `DEV_BROADCAST_VOLUME` (`dbcv_devicetype == 2`), буквы из маски
  `dbcv_unitmask`; событие на каждую букву. Platform без WPF — хук ставит
  App: `MainWindow.OnSourceInitialized` → `HwndSource.AddHook`, вызов
  `hub.HandleMessage`.
- `FolderTreesController.OnVolumeChanged`: пришёл — `_fs.GetRoots()`,
  вставить узел по порядку букв, `ProbeForChevrons` для него; ушёл — снять
  узел (выделение снять до `Remove`, как в `RefreshChildrenAsync`),
  открытая папка на нём — `NavigationFallback.AfterVolumeRemoved(current,
  roots, recent)` (Core, тест: последний недавний путь на живом томе, иначе
  первый фиксированный корень); закладки — `BookmarksController` переспросить
  `IsMissing`; сторож текущей папки — `Dispose` без исключения (ошибку в
  лог). Событие приходит с UI-потока (хук) — маршалинг не нужен.
- Операция с пропавшего тома: временные ошибки (шаг 4) → три попытки →
  `Failed` → «Повторить» после возврата тома. Пауза и автопродолжение —
  0.6.
- «Извлечь» [стенд Фабле]: даёт ли `ShellContextMenu` для `E:\` глагол
  «Eject»; да — пункт уже приходит чужим, ничего не делать; нет — 0.6.
- Глазами: вставить / вынуть флешку при открытой папке на ней, при
  копировании с неё и на неё; закладка на флешку сереет и оживает.

### 8. `Ctrl+Z` ждёт занятых

`UndoService.Unwind` (`Undo/UndoService.cs:191`): каждый шаг через
`BusyGate.Retry` / `RetryMany` того же ворот, что у операций (`HeldPaths`
передать в `UndoAsync`); занятость → ожидание с бюджетом, отказ называет
держателя. App: `ReleasePreview` перед `UndoAsync` (как перед удалением).
Тест `UndoServiceTests`: `MoveInUseFor` на один раз — откат прошёл со
второй попытки, шаг не выброшен из стека при отказе.

### 9. Корзина: страховка

`ShellRecycleBin`: оболочка ответила успехом без идентификатора элемента
корзины (сейчас `WARN` и нерабочий откат) → элемент помечается
`DeleteStatus.Ok` с флагом `Permanent = true` (новое поле `DeleteResult`),
undo-шаг не создаётся, итог и журнал говорят «удалён безвозвратно (корзина
не приняла)». Стенды границ (а), (б) — PLAN T.

### 10. Занятая папка: назвать файл и программу (AF)

Отказ переноса / переименования папки с «что-то внутри занято» → обход
`IFileBusyProbe.IsBusy` по файлам папки до первого занятого (предел 5 с,
отменяемый), Restart Manager про него одного; текст «занят `имя` —
`программа`». Место — там, где сейчас собирается `BusyReport` для папки
(`BusyGate.WaitForFile` / `HeldPaths`). Проверять — PLAN AI2.

### 11. Хвосты

- I2: спутник упал после переезда основного — откат уже переехавших
  членов группы в `ApplyGroup` (как `RenameMany`), статус группы `Failed`.
- `DeleteForGood` ждёт занятый через `BusyGate` (TECHDEBT).
- Проверка длинного пути для корзины — один раз на операцию (TECHDEBT).
- BACKLOG «reparse points»: `CopyDirectory` пропускает молча — в итог
  элемент `Skipped` с причиной «ссылка не копируется».

## Харнесс и стенды

- `ScenarioRunner`: шаг `assert-operations` (`{"step":
  "assert-operations", "finished": 1, "failed": 1, "outcome":
  "Problems"}`), команды `retry-last-operation`, `dismiss-operations`; `fs`
  получает `rename`.
- `file-ops.json`: `fs lock` источника → `copy` + `paste` → ожидание →
  `assert-operations failed 1` → `fs unlock` → `retry-last-operation` →
  `assert-operations failed 0` → `assert-entries`. Шаг с `.wander-tmp`: `fs
  create x.wander-tmp` → `fs rename` в `x` → `assert-entries contains x`
  (сторож, шаг 5).
- Отладочная операция (PLAN AI1) — глаза на строку состояния без диска:
  сценарии «ошибка на 4-м» и «отмена посреди» дают запись с проблемами.
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
- Список назначения не мигает: `.wander-tmp` не появляется в листинге при
  копировании, готовый файл появляется один раз.
