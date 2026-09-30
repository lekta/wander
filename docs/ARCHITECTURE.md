# Wander — архитектура и механизмы

Где что лежит и почему так. Задачи — в PLAN / BACKLOG / TECHDEBT.

## Слои

| Проект | TFM | Роль |
|---|---|---|
| `Wander.Core` | `net10.0` | Логика и абстракции. Не знает про Windows и UI. |
| `Wander.Platform.Windows` | `net10.0-windows10.0.19041.0` | Реализации интерфейсов Core: Win32, Shell COM, WinRT, `System.IO`. |
| `Wander.App` | `net10.0-windows10.0.19041.0` | WPF: окно, ViewModel'и, диалоги, конвертеры. |
| `Wander.Core.Tests` | `net10.0` | xUnit, **только** Core через фейки. |

TFM `10.0.19041.0` — ради WinRT-проекций (`Windows.Data.Pdf` для обложки
PDF), без новых пакетов. Цель — Windows 10 (19041 = Win10 2004;
`Windows.Data.Pdf` есть с 8.1), вызов обёрнут глухим `catch`.

**Жёсткое правило:** в Core нет `using System.Windows.*`, COM, PInvoke.
Нужно — интерфейс в Core, реализация в Platform.

**Platform без WPF.** Platform.Windows не ссылается на `PresentationCore` /
`PresentationFramework`: возможность, реализованная на WPF, отвинтилась бы
вместе с ним.

| Что делает код | Где живёт | На чём |
|---|---|---|
| Файл, процесс, реестр, шелл, кодирование в файл, метаданные | Platform | Win32, COM, **WinRT** (`Windows.Graphics.Imaging`, `Windows.Data.Pdf`), `System.Drawing`, MetadataExtractor |
| Картинка для экрана (`BitmapSource`), окно, диалог, буфер как объект WPF | App | WPF |
| Реализация интерфейса Core в App | только когда реализация про экран: `WpfDialogs`, `AppTextSource` | — |

Проверка при ревью: реализация интерфейса Core в App, которая не рисует и
не спрашивает пользователя, живёт в Platform — на не-WPF API, а не
переездом в App. Прецеденты: `RawThumbnail` и `PdfPageImage` (WinRT вместо
WPF-декодера), `ImageConvertAction` (`Windows.Graphics.Imaging` — тот же
WIC без `PresentationCore`). `Preview/ImageDecoder` в App правомерно: его
выход — `BitmapImage` для контрола.

```
src/
├── Wander.Core/
│   ├── Actions/        свои действия: каталог и пресеты, применимость, командная строка, запуск
│   ├── Companions/     спутники: правила, группы, сайдкары оценок
│   ├── Diagnostics/    PerfLog, LongWait, BuildInfo, кто держит файл
│   ├── FileSystem/     IFileSystem, операции и батчи, конфликты, буфер, сторож, гарды,
│   │                   занятые файлы, запись листинга и сортировка
│   ├── Folders/        вид и порядок папки, книга папок, desktop.ini
│   ├── Icons/          контракты значков и метаданных, форматы картинок, превью из RAW
│   ├── Imaging/        хелперы отсмотра, TGA, DIB, бюджет памяти картинок
│   ├── Layout/         геометрия: плитки, сетка, области окна, размеры панелей, drag у края
│   ├── Listing/        сессия папки: приход, сверка строк, преемник, место строки, фильтр
│   ├── Localization/   ITextSource
│   ├── Logging/        ILogger, Log, маскирование путей, журнал действий, ротация
│   ├── Menu/           контекстное меню: правила, каталог, настройки, меню броска
│   ├── Navigation/     история, адрес, MRU, стартовая папка, следование за путём
│   ├── Operations/     OperationTracker, заявки на пути, скорость, ожидание занятых
│   ├── Panels/         панели папок: состояние, строки, клавиши, сверка уровней
│   ├── Persistence/    AppState, AppSettings, AppPaths, временные файлы
│   ├── Preview/        маршруты панели просмотра и разбор форматов
│   ├── Rename/         групповое переименование
│   ├── Search/         маска, выражение, обход, экстракторы, кэш текста
│   ├── Shell/          контракты оболочки: namespace, меню, ярлыки; архивы, извлечение
│   ├── Undo/           UndoService, IUndoableAction
│   ├── Workspace/      модель окна: состояние, события, правила, цель, эффекты
│   └── ServiceLocator.cs
│
├── Wander.Platform.Windows/
│   ├── Diagnostics/    Restart Manager
│   ├── FileSystem/     System.IO, корзина, буфер, сторож, тома, известные папки
│   ├── Icons/          значки и миниатюры, дисковый кэш, EXIF, проба резкости
│   ├── Imaging/        кодировщик картинок (WinRT)
│   ├── Logging/        FileLogger, чистка логов
│   ├── Persistence/    state.json, folders.json, InstanceLock
│   ├── Preview/        карточка программы, проба кодеков
│   ├── Search/         IFilter
│   ├── Shell/          запуск, ярлыки, namespace (корзина, архивы), меню оболочки,
│   │                   процессы, поиск инструментов
│   └── PlatformBootstrapper.cs
│
└── Wander.App/
    ├── Conflict/       окно совпадений имён
    ├── Controllers/    контроллеры при MainViewModel и исполнитель модели окна
    ├── Controls/       свои контролы: значок, списки, плиточная панель, редактор имени, рамка
    ├── Converters/
    ├── Diagnostics/    CrashReporter, счётчики, UiStallWatch, SystemVitals, стенд операции
    ├── Dialogs/        IDialogs, WpfDialogs
    ├── DragPreview/    перетаскивание: приём, отдача, плашка
    ├── Highlighting/   *.xshd
    ├── Menu/           ContextMenuFactory, ShellMenuCache
    ├── Preview/        раскодирование и отрисовка для панели просмотра
    ├── Resources/      Strings*.resx и аксессоры, Palette, MenuStyles
    ├── Util/           форматы чисел и времени, ListVisuals, SelectionController
    ├── ViewModels/     вьюмодели настроек и строк, базовые типы
    ├── Views/          виды и окна
    ├── MainViewModel.cs — при окне, не в ViewModels/ (см. «Окно и его контролы»)
    ├── MainWindow.xaml(.cs)
    └── App.xaml(.cs)
```

### Граф зависимостей между папками

Снимает `tools\deps.ps1`: свод `using Wander.*` по папкам — рёбра, уровни,
циклы. `-UpdateDoc` пишет рёбра в `docs/deps.txt` и уровни в блок ниже
(руками не править). **Правило (O7, 2026-09-01): между папками внутри
проекта нет циклов, у каждой папки есть уровень** (0 — ни от кого не
зависит; N — самый длинный путь вниз). Ребро, замыкающее цикл, — повод
переложить файл или развернуть связь (событие вместо коллбэка вверх), не
исключение. Ребро `App -> Platform.Windows` — один файл, `App.xaml.cs`
(точка композиции).

<!-- deps:generated:begin -->
```
=== Wander dependency graph (using sweep) ===
date   : 2026-09-29
commit : 8116677

-- Wander.Core: levels --
  0: (root), Imaging, Layout, Localization, Logging, Operations, Panels
  1: Diagnostics, Icons, Undo
  2: FileSystem
  3: Companions, Folders, Navigation, Preview
  4: Actions, Rename, Search
  5: Listing, Persistence
  6: Shell
  7: Menu
  8: Workspace

-- Wander.Platform.Windows: levels --
  0: Diagnostics, Icons, Imaging, Logging, Persistence, Preview, Search, Shell
  1: FileSystem
  2: (root)

-- Wander.App: levels --
  0: Highlighting, Menu, Resources
  1: Diagnostics, Util
  2: Preview, ViewModels
  3: Conflict, Converters
  4: Controllers, Controls, Dialogs, DragPreview
  5: Views
  6: (root)

-- namespace <> folder mismatches --
  (none)
```
<!-- deps:generated:end -->

### Когда `IFileSystem`, а когда `System.IO`

**Через `IFileSystem`** — всё, что пользователь может отменить, что
обязан подменить тест, и всё, что перечисляет папки: операции, листинг,
сайдкары, перепись. **Напрямую `System.IO`** — байты одного выбранного
файла ради раскодирования, когда результат — картинка или текст на экране,
а не решение логики: так читает **только `Wander.Core/Preview/`** (обложки,
теги, меши, пробы текста), тесты туда не ходят. Новый `File.` /
`Directory.` в Core вне `Preview/` — кандидат в `IFileSystem` либо
расширение списка с записью здесь.

## Композиция: ServiceLocator

Статический `Dictionary<Type, object>`: `Register<T>`, `Get<T>`,
`TryGet<T>`, `IsRegistered<T>`, `Reset()` (тесты); все под `lock`.
Единственная регистрация — `App.OnStartup` →
`PlatformBootstrapper.RegisterDefaults()`, порядок значим:
(1) `ILogger` / `ILogFile` (`FileLogger`) — всё ниже логирует при
конструировании; пишет заголовок сессии (версия, ОС, рантайм, культура,
elevated); (2) платформенные абстракции (`IFileSystem`, `IKnownFolders`,
`IShellLauncher`, `IIconProvider`, `IAppStateStore`, `IFileLockInspector`,
`IShortcutService`, `IShellNamespace`, `IShellContextMenu`,
`IImageMetadataReader`); (3) общие синглтоны `UndoService`,
`OperationTracker`, `IRecycleBin`, `FileOperationService` — по одному на
приложение, иначе undo-стек и прогресс расползутся; (4)
`CompanionResolver`, `CompanionMetadataService` (зависит от `IFileSystem`
и `UndoService`). Тесты в локатор не ходят — фейки конструкторами.

### Обязательные и необязательные сервисы

Регистрация одна и безусловная: ветка «не зарегистрирован» либо описывает
реальный режим, либо недостижима. **Сервис, который где-то читается
`Get<T>()`, обязателен везде**: `IFileSystem`, `IShellLauncher`,
`IAppStateStore`, `IRecycleBin`, `IShortcutService`, `IIconProvider`,
`CompanionResolver`, `UndoService`, `OperationTracker`,
`FileOperationService`, `IDialogs` (App, регистрируется в `App.OnStartup`
рядом с `ITextSource`). Отсутствие — сломанный бутстраппер: падение на
старте честнее работы вполсилы; хост без Windows-слоя регистрирует свои
реализации сам. **Необязательные** читаются `TryGet<T>()`, у каждого
внятный ответ «нет»:

| Сервис | Чего не будет |
|---|---|
| `IShellNamespace` | закладки на Корзину, `shell:`-пути |
| `IKnownFolders` | закладки по умолчанию |
| `IShellContextMenu` | пункты сторонних приложений в меню |
| `IShellHandlerRegistry` | список обработчиков в настройках |
| `IFileLockInspector` | имя процесса в «файл занят» |
| `IVolumeInfoProvider` | блок тома над переписью корня |
| `ISystemClipboard` | буфер внутренний (конструктор `ClipboardController` по умолчанию) |
| `IDirectoryWatcher` | список не обновляется сам |
| `IImageMetadataReader` | нет EXIF |
| `CompanionMetadataService` | не читаются и не пишутся сайдкары |
| `ContentSearchService` | нет поиска по содержимому |
| `ILogFile` | нет пункта «Лог сеанса» и лога в отчёте о падении |
| `ILogger` | лога (`Core/Logging/Log` отдаёт `NullLogger`; так живут тесты Core) |
| `ITextSource` | Core отдаёт ключ вместо надписи |

Под тестом только последняя деградация (`TextFallbackTests`): `ITextSource`
в тестах не регистрируется специально.

### Конструирование в две фазы

Конструктор `MainViewModel`: (1) **зависимость строится раньше того, кто её
берёт** — nullable-ворнингов в сборке ноль, и это часть проверки: новый
CS8602 / CS8604 в конструкторе — сломан порядок; (2) **построить, потом
включить** — подписки с побочными эффектами (`Settings.PropertyChanged` →
перечитывания, `Workspace.StateChanged` со сменой раскрытого → запись
состояния) ставятся в конце, **после** `RestoreState()`; флага «идёт
восстановление» нет, запись от начальной навигации гасит
`_stateSaveTimer.Stop()` в конце `RestoreState`.

### Изменяемая статика

Под `lock` — только локатор: xUnit гонит классы параллельно,
`ServiceLocatorTests` пишет, пока соседи читают через `ITextSource.Text` →
`TryGet`, а чтение `Dictionary` под запись — неопределённое поведение.
Остальное оставлено сознательно:

| Место | Почему |
|---|---|
| `PerfLog._log`, `_windowStartMs` | диагностика, под `_lock`, тестами не читается |
| `Log.Details`, `Log.RevealPaths` | `volatile`, пишет только `MainViewModel` из настроек (Core настроек не знает — как `AppPaths.UseSystemTemp`); тесты Core их не включают, работают на умолчаниях |
| `IconImageCache` | под `_lock`, с потолком; второй владелец не нужен, пока провайдер — синглтон |
| `CrashReporter._offeredThisSession` | худшее у гонки нефатальных — второй диалог; fatal-путь флаг игнорирует |
| `UiStallWatch._worker`, `HighlightingCatalog._registered` | once-флаги (второй под `_lock`) |
| `MagnifierCursor._cached`, `ShellHandlerRegistry._searchPath` | ленивые неизменяемые: гонка строит то же значение дважды |
| `SystemPathGuard._userFolders` | `Lazy`, папки профиля из `IKnownFolders` (`TryGet`) при первом вызове гарда — после бутстраппера; в тестах — только `Environment`, без «Загрузок» |
| `PictureCache._frames` | доля кадров в бюджете картинок (`PictureMemory`), общая на все панели просмотра — только UI-поток; предел пишет `MainViewModel` из настроек |
| `StartFolder.Asked` | пишется один раз до окна (`App.OnStartup`, харнесс — `Override` до него), читает `MainViewModel` на старте; тесты трогают её в одном классе и возвращают |

`SystemIconProvider`: `_cache` / `_missing` / `_thumbnailOrder` — поля
**экземпляра** под `_lock`; статика там — только lock-объекты.

### Как потребляются сервисы

Регистрация до первого потребителя, дальше словарь заморожен; горячей
подмены и плагинов нет (появятся — пересмотреть). **Экземпляры разрешают
сервисы один раз в конструкторе в readonly-поля** — список зависимостей в
одном месте, будущие параметры конструктора. **Статические хелперы**
(`Text`, `Log`, `IconConverter.Load`, `SystemIconProvider.ResolveShortcut`)
ходят в локатор на каждый вызов: поиск по словарю не виден на фоне шелла,
декодера, диска. **Новых ленивых статических кэшей сервисов**
(`_x ??= Get<X>()`) без строки в таблице выше не заводить: ещё одна
статика и риск обращения до бутстраппера.

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
`BatchGroup` (файл + спутники: один вопрос, один шаг прогресса,
`Sprite (1).png` + `.meta`). Синхронные `CopyMany` / `MoveMany` — для
тестов, продакшн — async на пуле с прогрессом и `CancellationToken`.
Отмена посреди элемента — `BatchItemStatus.Cancelled`, не `Failed`,
частично скопированное — в undo. Результаты (`BatchItemResult`,
`DeleteResult`) — на уровне namespace. Папка между томами —
`CopyDirectory` + `DeleteDirectory`; read-only цели — список, второй
вопрос, снятие атрибута.

- **Undo.** Один LIFO-стек: Move ↔ Move, Rename ↔ Rename, Delete →
  Restore из корзины, Create → Delete в корзину. `PermanentDelete`
  (`Shift+Delete`) не откатывается. `BeginOperation()` —
  busy-счётчик, `CanUndo == false` в полёте. Стек под одним локом;
  `Changed` — вне лока, может прийти с фона. Рестарт не переживает.
- **После безвозвратного удаления — `UndoService.Forget(удалённые пути)`**
  (решение человека 2026-09-29: история не очищается целиком), зовут
  `BatchExecutor.DeleteManyCore` (пути со статусом `Ok`) и
  `FileOperationService.PermanentDelete`. Шаг называет, что отмена должна
  застать на месте, — `IUndoableAction.PathsBeforeUndo`: перенос и
  переименование — нынешний путь, создание, извлечение, выход действия —
  созданное, оценка — файл-спутник, удаление в корзину — ничего (шаг
  остаётся всегда), связка — по `Steps`. Проход от свежего шага к старому
  с набором мёртвых путей «на тот момент»: шаг, чьё «на месте» — мёртвый
  путь или под ним, выпадает, а то, что он вернул бы (`MovesOnUndo.To`,
  `PathsAfterUndo`), становится мёртвым — так выпадают шаги, зависевшие
  от него; уцелевший шаг уводит мёртвые пути назад через свой перенос и
  снимает те, что лежат там, куда он возвращает (занявшее это место
  пришло позже). От связки остаются уцелевшие шаги (`WithSteps`).
  Сравнение путей — правило `PathRewrite` (без регистра и хвостового
  разделителя), своей копией: `Undo/` папку `FileSystem/` не видит.
- **Откат — операция.** `UndoService.UndoAsync`: с пула, в
  `OperationTracker` (`OperationVerbs.Undo`), под тем же busy-счётчиком;
  связка разматывается по `IUndoableAction.Steps` с конца. Отмена
  возвращает несделанное в стек под тем же описанием (`WithSteps`). Сбой
  шага — в `UndoOutcome.Failures`, остальные откатываются, сбойный в стек
  не возвращается (иначе запер бы всё под собой). По
  `UndoOutcome.Undone` `UndoLast` ведёт выделение и `FollowRelocated`.
- **Окно операции — через 400 мс, без фокуса** (`RunWithProgressDialogAsync`,
  `ShowActivated = false`): короткая операция его не показывает, строка
  состояния показывает сразу. Модальный вопрос изнутри операции
  пережидается (`ComponentDispatcher.IsThreadModal`).
- **Корзина — на `IFileOperation`, в обе стороны** (`ShellRecycleBin`,
  стенд):
  - `SHFileOperation` с `FOF_ALLOWUNDO` удалял файл на пути длиннее
    `MAX_PATH` **безвозвратно и молча**. Движок говорит заранее: нет
    `TSF_DELETE_RECYCLE_IF_POSSIBLE` в `PreDeleteItem` — уничтожит;
    приёмник отвечает `E_FAIL`, наверх — `RecycleUnavailableException`.
    Короткая папка с длинным путём внутри эту проверку проходит, и
    оболочка спрашивает поверх `FOF_NO_UI` с «Да» по умолчанию — поэтому
    до движка свой обход (`TooLongForBin`, порог 259, документный). Итог —
    один вопрос после операции (`ChoiceRequest`: `CancelLabel` «Прервать»
    по умолчанию, `ArmDelay` 0,5 с до включения «Удалить безвозвратно»).
  - `PostDeleteItem` отдаёт элемент корзины, его id-list (base64) — в
    `RecycleHandle.BinItemId`; `Restore` — один `MoveItem`, 10–20 мс, без
    обхода корзины, локализованного глагола и разбора даты. Строка панели
    корзины несёт `BinFilePath` (`$R…`, обход до совпадения ~80 мс). Путь,
    удалённый дважды, возвращается правильной версией.
  - Занятое имя при возврате решается до движка (`UniqueNames`; под
    `FOF_NO_UI` он заменил бы сам), `FOF_RENAMEONCOLLISION` — сетка на
    гонку; пропавшая папка создаётся. `$I…` без пары убирает
    `RemoveIndexFile`.
  - Прогон — на своём STA-потоке (`OwnApartment`, без очереди
    сообщений), RCW освобождаются до выхода.
  - **Удаление пачкой, один прогон** (`IRecycleBin.SendMany`,
    `BatchExecutor.Recycle`; реализация по умолчанию — цикл `Send`, ею
    живут фейки): 3 мс на файл против 11. Результат, `BinItemId` и колбэк
    прогресса — на элемент, по порядку. Без `FOFX_EARLYFAILURE`: занятый
    файл (`0x80270027`) и папка с занятым внутри (`0x80270028`, остаётся
    **целой**) пропускаются за миллисекунды (с флагом — 1021 мс и стоп).
    Ошибка из `PreDeleteItem` останавливает прогон для остальных — это
    отмена (`E_ABORT`) и отказ корзины (`E_FAIL`), остаток прогоняется
    заново. Занятый элемент — отказ `FileInUse` без ожидания: ждёт
    операция (ниже). Возврат — прогон на элемент, ~15 мс (TECHDEBT).
  - Файл, открытый с `FileShare.Delete`, уходит в корзину и возвращается
    прямо под читателем — основание решения AF (а).
- **Занятые пути** (AF), три слоя снизу вверх:
  - `PathClaims` (Operations) — кто в Wander над чем работает. Заявка —
    **источник** операции; вид `UserOperation` (владелец — ключ
    `OperationVerbs`) или `Background` (`ClaimOwners`: shell-миниатюра,
    системный фильтр поиска — прервать нельзя). `Covering(path, except)` —
    сам путь, папка выше и всё внутри; `IsClaimed(path, kind)` — 0,5 мкс;
    `Changed` — только для операций пользователя. Запрос — 1,5 мкс при 50
    заявках, 130 при 50 000; контрольная строка —
    `Claims: N lookups, slowest X ms, table Y paths`, `WARN` от 5 мс.
  - `BusyWait` (Operations) — политика: взгляд каждые 0,2 с, бюджет 2 с
    **на операцию**, не на файл.
  - `HeldPaths` / `BusyGate` (FileSystem; один на операцию). `Check(path)`:
    заявка чужой операции пользователя — `ClaimedByOperationException`,
    фоновой — `Yield`. `WaitForFile` — через `IFileBusyProbe`
    (`WindowsFileBusyProbe`, `CreateFile(DELETE)`, пара мс); `Retry` —
    папка, пробы нет: операция повторяется, пока `FileInUse`, только в
    пределах тома (между томами удаление уже снесло бы остальное);
    `RetryMany` — пачка корзины, занятые уходят снова вместе. Держатель
    называется один раз (`IFileLockInspector` — Restart Manager, у папки
    по первым 500 файлам; своя заявка — «Wander: миниатюра»), в пачке —
    первые три. Итог — `BusyReport` в `DeleteResult` / `BatchItemResult`.
    Ждут перенос, переименование, корзина; безвозвратное удаление —
    только заявки (TECHDEBT). Не дождалось удаление — «Файл занят» с
    «Повторить» (`DialogKind.DeleteInUse`), повтор — по оставшимся.
  - Чтобы ждать редко: свои читатели — `SharedRead`
    (`FileShare.ReadWrite | Delete`; PDF-обложка — из своего потока);
    исключение — оценка в сайдкар (`IFileSystem.ReadAllBytesForUpdate`):
    `.pp3`, который RawTherapee как раз сохраняет, читался бы
    недописанным. Панель просмотра отпускает файл до операции
    (`PreviewController.Release` → `ContentReleased` → WebView2 на
    `about:blank`; `Restore` в `finally`); откат — не отпускает
    (TECHDEBT). Переименование — с пула.
  - Часы «в работе» рисует `AsyncIcon` (`ShowsWork`, `OnRender`) — не
    элемент в шаблоне каждой ячейки; один статический обработчик
    `PathClaims.Changed` — проход по живым иконкам, `Entries` не
    трогается. Часы — у заявки старше `PathClaims.BadgeDelayMs` (400 мс):
    `IsClaimed(путь, вид, olderThanMs)`, `DueInMs` и один статический
    `DispatcherTimer`; часы `PathClaims` подставляются в тесте.
- **Прогресс — в двух счётчиках**: элементы (что выделил человек) и
  байты (что двигает диск; без них копия 5 ГБ держит бар на нуле).
  `OperationTracker.Begin(verb, total, totalBytes, bytesAreWork)` →
  `IOperationHandle` (диспозить всегда): `Advance` / `AdvanceBytes`
  (можно отрицательной дельтой) / `SetCurrentPath` / `SetTotalBytes` /
  `SetWeighing` («Подсчёт…»); `Snapshot()` — иммутабельный срез с `Id`,
  `Percent` (по байтам, иначе по элементам), `StartedAtUtc`. `verb` —
  **ключ ресурса** (`OperationVerbs`). `Changed` с фона — не чаще 10 раз в
  секунду, хвост довозит таймер; появление и завершение — без троттла.
  - Байты: `BatchExecutor` взвешивает источники до старта
    (`FolderStatistics.Collect`, глубина 64, вес по пути — один обход) —
    **после** вопроса о совпадениях, чтобы Cancel в нём ничего не стоил.
    Перенос — `IFileSystem.CopyFile/CopyDirectory/MoveEntry` с
    `IProgress<long>` и токеном; в `SystemIOFileSystem` — `CopyFileEx` с
    `LPPROGRESS_ROUTINE` (отмена внутри файла, недописанное убирает
    система). `ApplyOne` сводит оценку с отчитанным по элементу.
  - Извлечение байтов не знает: `IShellNamespace.CopyOut` отдаёт
    `IProgress<CopyOutWork>` от `IFileOperationProgressSink.UpdateProgress`
    — операция `BytesAreWork`, только процент.
- **Окно операции и статус-бар.** `RunWithProgressDialogAsync` открывает
  **немодальный** `ProgressDialog` и ждёт задачу, а не окно. Окно узнаёт
  свою операцию по токену в снимке (`OperationTracker.Begin`), не по `Id`:
  извлечение регистрирует операцию после диалога о совпадениях, и
  запущенная в этот промежуток досталась бы чужому окну. Пока операция
  идёт, `Closing` отменяется: `Alt+F4`, «Закрыть» и `Esc` = «Свернуть» в
  статус-бар, назад — «Показать». Заголовок свой (`WindowChrome`: у
  системного нет «свернуть» без крестика), одна кнопка-глиф `E921`.
  - Закрывает окно сам `RunWithProgressDialogAsync`
    (`ProgressDialog.Finish` в `finally`) **до** возврата к вызывающему:
    иначе вопрос об итоге брал открытое окно владельцем, умирал с ним, и
    приложение оставалось во вложенном цикле невидимого модального окна.
    Второй замок: `WpfDialogs.ActiveWindow` не отдаёт владельцем
    `ITransientWindow`.
  - Строку и панель статус-бара кормит `OperationViewModel` — на месте, не
    пересоздаётся (`TransferRate` — скользящее за 3 с; кнопки под
    курсором). Панель, а не тултип: тултип исчезает, стоит потянуться к
    кнопке. Важность — `StatusSeverity` (`Warn` / `Fail`; та же пометка в
    журнале действий).
- **Выход при идущих операциях.** `MainWindow.OnClosing`: вопрос
  (`DialogKind.ExitWithOperations`, Cancel по умолчанию); «да» —
  `e.Cancel`, `Vm.CancelAllOperations` (`RequestCancel` каждому окну),
  `await OperationTracker.WhenIdleAsync(10 с)` (Core, тест; продолжение не
  на контексте вызывающего — фатальный обработчик может на нём
  блокироваться), затем `Close()` вторым проходом. Таймаут —
  `IProcessRunner.KillAll` (`WindowsProcessRunner` помнит живые и убивает
  деревом). `Shutdown` (`SessionEnding`, smoke, харнесс) —
  `App.IsShuttingDown`, `ShutdownWithoutAsking`: отмена без вопроса. Вылет —
  `App.StopOperationsBeforeDying`: `ProgressDialog.CancelAll` (только
  `_cts.Cancel` — диспетчер может быть мёртв), `KillAll`,
  `WhenIdleAsync(3 с).Result`. «Выход» в меню — `MainWindow.Close`, не
  `Shutdown`. После `Hide()` — `IDirectoryWatcher.Watch(null)` и
  `PreviewPane.ReleaseWebView` обеих панелей.
- **Конфликты.** `IConflictResolver.ResolveAll(ConflictRequest)` — push:
  все коллизии до первого касания диска одним вызовом (плюс размер батча);
  ответ — `ConflictAnswer` на каждую показанную пару, вложенные
  включительно, по пути источника; null / `Cancel` — отмена батча;
  возникшая по ходу или не спрошенная — второй вызов из одного элемента.
  Каждый файл группы — своя коллизия; `BatchExecutor.ApplyGroup` уводит
  спутников за переименованным основным, кроме пропущенных.
  - `FileConflictInfo` — две записи + `IsMove` + `SourceReachable` (false
    у записи архива). `ConflictVerdict.Of` — вид, размер, кто новее (допуск
    2 с, FAT), «идентичны» (false без чтения при разных размерах, null —
    не сравнили). `FileContentComparer.AreIdentical` — блоками по 64 КБ
    через `IFileSystem.OpenRead` до первого отличия; `AutoCompareLimit`
    (64 МБ) делит очередь на два прохода.
  - `ConflictResolution.Merge`: `BatchExecutor.MergeFolder` обходит
    исходную папку, ответы по пути, вложенные — рекурсивно, опустевшую при
    перемещении — в корзину; `MergeScanner` (Core) даёт окну то же дерево
    заранее, с файлами без пары, глубина 64.
  - `ConflictBatch` — состояние окна в Core: дерево `ConflictPair`
    (вердикт, ответ, дети, `IsEffective`; `ConflictBatch.Effective` —
    ответы исполнителю), очередь сравнения `NextToCompare`, разовый ответ
    за нерешённые (`ConflictBulkAction`) и стоячая политика
    `SetSkipIdentical` (забирает ровно свои ответы; начальная —
    `AppSettings.SkipIdenticalOnConflict`). UI — `Conflict/ConflictWindow`
    (+ две вьюмодели), миниатюры `IconSize.Medium`, геометрия
    `AppState.ConflictWindow`; `DispatcherConflictResolver` маршалит на UI.
  - Почему окно такое: кнопка на строке читается как сделанное действие;
    отличия — весом, не фразой; «применить к нерешённым» не выбран —
    нетронутый список и так решается по каждому; «Заменить все» /
    «Пропустить все» — далеко от ОК, страховка — расстояние и `Ctrl+Z`.
  - Replace — цель **в корзину**, `DeleteAction` в composite перед
    основным шагом: `Ctrl+Z` возвращает обе стороны. Копия в свою же папку
    — не конфликт: `BatchExecutor` отвечает Rename (move — Skip) сам;
    сторож DnD пропускает (`PathSafety.IsAllowedDuplicate`), вырезание в
    ту же папку `PasteAsync` снимает (`PathSafety.AllAlreadyIn`).
- **Защита.** `SystemPathGuard` — функция от пути и окружения, без I/O,
  статически: корни дисков и шар (`\\server\share`), спец-папки (Windows,
  Program Files x86/x64, ProgramData, Users, корень профиля), папки
  профиля, которые ведёт Windows (Рабочий стол, Документы, Загрузки,
  Изображения, Музыка, Видео, AppData с Local / LocalLow / Roaming — сами,
  не содержимое; `IKnownFolders`, `Lazy` при первом вызове, иначе
  `Environment`), дерево `C:\Windows`. Содержимое Program Files и чужих
  профилей не блокируется — чистка остатков легальна. Причины — ключи
  `Guard*` через `Text.Format`. Запись внутрь — `MayWriteInto`: «нет»
  только у дерева Windows. `PathSafety` — self-drop с текстом через
  `ITextSource`. `IFileLockInspector` — «файл открыт в: Word (PID 1234)».
- **Отступления от «всё откатываемо»:** безвозвратное удаление
  (подтверждение всегда); восстановление из корзины в `UndoService` не
  кладётся, как в Explorer (откат был бы удалением), не деструктивно,
  логируется, `SystemPathGuard` не нужен — место решает шелл.

## Модель окна — `Core/Workspace/`, `Core/Panels/`

Панели, выделение списка, цель операций и клавиатура — одно состояние и
правила в Core под тестами; вью переводят ввод в события и рисуют
состояние. Спека блока 2 — `docs/REDESIGN.md` в коммитах 4845b8f и
622a674 (решения человека В1–В28); ссылки `REDESIGN 4.x` в коде и тестах
— на её разделы: 4.2 состояние, 4.3 цель, 4.4 события, 4.5 правила, 4.6
эффекты, 4.7 вью, 4.8 причуды WPF, 4.10 таблица, 4.12 следование за
путём, 4.13 перетаскивание.

```
вью / VM / пул ─событие─▶ WorkspaceController.Post ─▶ WorkspaceReducer.Apply(состояние, событие)
                                 ▲  очередь: событие от исполнения эффекта        │
                                 │  ждёт конца текущего                           ▼
                                 └─ эффекты ◀── StateChanged (проекция панелей, VM) ◀─ (состояние, эффекты)
     Navigate, ReadBranch, ProbeChevrons, ScheduleThrottle — сам;
     FocusZone, FocusRow, ApplyListSelection, OpenEditor — окну (ViewEffectRequested)
```

- **Состояние** — `WorkspaceState`, неизменяемые record'ы: `Folder` (путь,
  панель-источник), `List` (`Listing/ListState`: выделение путями,
  главная, каретка), `Bookmarks` / `Drives` (`PanelState`: уровни по пути
  с эпохой, раскрытое, `Location` — место открытой папки, `Caret`,
  `Editing`, `Revealing` — раскрытие вглубь), `Keyboard` (зона, последняя,
  окно активно, зона до диалога), `Menu` (снимок меню), настройки, часы
  троттла. Производное не хранится: цель (`TargetRules`), подсветка
  (`Highlight`), видимые строки (`PanelView.Rows`), предмет панели
  просмотра (`PreviewSubject`).
- **События** несут причину: ввод панели (`RowClicked`, `RowActivated`,
  `ChevronToggled` с `Alt`, `CaretMoveRequested`, `CaretMoved` — поиск по
  буквам), ввод списка (`ListSelectionChanged`, `ListCaretMoved`), окно
  (`ZoneEntered(зона, причина)`, `MenuOpened` / `Closed`,
  `WindowActivated` / `Deactivated`, `PaneHidden`, `DialogOpened` /
  `Closed`, `OptionsChanged`), факты (`Navigated`, `BranchRead` с эпохой,
  `ChevronsProbed`, `BookmarksChanged`, `Relocated`, `Removed`,
  `FolderChanged`, `ListingLanded`, `ViewModeChanged`, `ThrottleElapsed`).
  Неизвестная причина ничего не выделяет и не раскрывает.
- **Правила — модули в фиксированном порядке** (`WorkspaceReducer`):
  каждый владеет своим срезом, читает итог предыдущих, друг друга не
  зовёт; клавиатуру, мышь, часы и диск не читает.
  1. `NavigationRules` — что открывает папку: клик, `Enter`, «стрелки
     открывают» через `TreeNavThrottle` (сейчас / в момент T / никогда,
     срабатывание — `ThrottleElapsed`), `Ctrl+1` из соседней панели;
     открытая не открывается снова.
  2. `PanelRules` — строки, раскрытое, место, курсор. Уровень — эффект
     `ReadBranch` на пуле, устаревшая эпоха отбрасывается; раскрытие до
     пути — асинхронный спуск (`Revealing`); пересборка и перечитывание
     держат раскрытое, курсор и место по пути; ушедшая строка отдаёт
     курсор соседу; `WindowActivated` перечитывает раскрытое не чаще раза
     в 5 с. Листья без шеврона (проба фоном, перепроба на `FolderChanged`),
     раскрытое — в `AppState`. Сама панель не сворачивается никогда:
     строка без подпапок остаётся раскрытой (`PanelView` открывает любую
     строку из `Expanded`, кроме `IsLeaf`), `←` на ней — к родителю.
  3. `ListRules` + `Listing/ListingArrival` — выделение после приземления:
     намерение (`FolderSession.DecideArrival`), иначе по путям;
     переименованная — под новым именем (пара «было → стало» от своей
     операции и от сторожа); ушедшая выделенная — преемник
     (`CurrentRowFallback`, кроме ухода из выдачи поиска); в видимость —
     что попросили, главная при `ListingReason.Rearranged` и при возврате
     из выдачи; стоявшая на экране главная держит место
     (`ListLanding.Held`, `RowFollowing`). В список — эффект
     `ApplyListSelection`.
  4. `KeyboardRules` — куда клавиатуре идти, по состоянию до и после:
     упала из панели — на её курсор, из строки списка — на каретку без
     прокрутки; панель убрана — в список; диалог закрыт — где была; строки
     операции — на главную, если клавиатура в списке или (после диалога)
     нигде; ушла строка с кареткой — на преемника; смена вида — на
     каретку; в панели — на строке курсора.
- **Исполнитель** — `Controllers/WorkspaceController` (App): очередь
  (вложенного `Apply` нет, порядок трассы — порядок модели),
  `StateChanged` до эффектов (строка уже нарисована), чтение уровней и
  проба шевронов на пуле, таймер троттла. Трасса под `LogActions`:
  `WS <событие>; effects: …`, `WS target: …`.
- **Адаптеры** — тонкий код-бихайнд. `FolderTreesView`: ввод в события,
  `FocusRow` (строка не нарисована — когда нарисует проекция).
  `FileListView`: выделение пользователя — `ListSelectionChanged` (не во
  время `IsSyncingRows`), `ApplySelection` одним вызовом
  (`FileListBox.ReplaceSelection` = `SetSelectedItems`, у `FileDataGrid` —
  `BeginUpdateSelectedItems`), `FocusRow` по
  `ItemContainerGenerator.StatusChanged` без `UpdateLayout`, `OpenEditor`.
  `MainWindow`: причина прихода фокуса (`ReasonFor`: записанная окном;
  прежний элемент отсоединён — падение; кнопка мыши — клик; фокус был в
  меню — меню; `Activated` — активация) и `FocusZone` / `FocusRow`.
  Неактивное окно: перенос ждёт `Activated`; харнессу (`App.Headless`) —
  сразу.
- **Цель и меню** — `TargetRules` (строки списка, строка панели, фон
  папки; `TargetRules.Rename` — редактор в панели, в списке или групповое)
  и `MenuContext` (снимок предмета при открытии меню; команда из меню
  получает его параметром `MenuCall`, с хоткея — цель сейчас). Рамка «о
  чём меню» — флаг строки панели. Лог вставки — `Paste: … (how)`.
- **Панель — плоский список**: `Controls/FolderPanelList` — `ListBox` без
  выделения WPF, строки — `PanelView.Rows`, отступ —
  `TreeIndentConverter`; `TreeNodeViewModel` — проекция с четырьмя
  OneWay-флагами (курсор, активна, место — жирное имя, предмет меню);
  черта перед своими закладками — флаг `StartsUserSection`.
  `FolderTreesController` сверяет строки по ключам (`BranchReconcile`),
  больше 256 правок — одной заменой. Клавиши — `PanelKeyNavigation` (`↑`
  / `↓`, `←` / `→`, `Home`, `End`, `PgUp`, `PgDn`), буквы —
  `TypeAheadController`. UIA видит список, не дерево.
- **Следование за путём** — `Navigation/PathFollowing`: перенос или
  переименование Wander'ом (`FollowMoved` / `FollowRelocated`) →
  держатели по порядку: панели (и перечитать уровни), закладки, книга
  видов, MRU адреса, память выделения (`FolderSession.RewriteMemory`),
  буфер (`ClipboardController.Rewrite`, пока системный буфер наш),
  история последней. «Где теперь путь» — `PathRewrite.Under`; копия так
  же обновляет цель в панелях; `Ctrl+Z` — по
  `IUndoableAction.MovesOnUndo` (пары у `MoveAction`, `RenameAction`,
  `CompositeAction`). Листинг, перенаправленный без навигации, —
  `Listing follows: …`.

| WPF делает | Ответ |
|---|---|
| выкидывает заменённый объект из `SelectedItems` (`Replace`, `Reset`) | отчёты списка во время `IsSyncingRows` не шлются; выделение возвращает `ApplyListSelection` |
| присвоение `SelectedItem` схлопывает многовыделение | `SelectedItem` не привязан ни в одном виде |
| `SelectedItems.Add` линейный — массовое выделение квадратичное (5000 строк — 10,8 с, стенд `SelectionProbe`) | `ReplaceSelection` одним вызовом (0,36 с; `ui.selection-apply`) |
| удалённый элемент с фокусом отдаёт его предку (сам список) или окну | `ZoneEntered(…, FocusFell)` → `FocusRow` по правилу |
| исполняет пункт меню после закрытия и возврата фокуса | пункт получает снимок `MenuContext` |
| после модального диалога фокус — первому фокусируемому | `DialogOpened` / `DialogClosed` → `FocusZone` по правилу |
| свёрнутый элемент фокус не держит | `PaneHidden` до сворачивания → `FocusZone(список)` |
| прокручивает к строке с фокусом по обеим осям | `Line_RequestBringIntoView`: горизонталь — видимая |
| стрелки `DataGrid` идут от текущей ячейки | `FocusRow` ставит `CurrentCell` |
| двойной клик по строке дерева раскрывает | событие `ChevronToggled` |

Тесты — `WorkspaceScene` (модель на событиях, диск — дерево папок):
`PanelRulesTests`, `KeyboardRulesTests`, `ListRulesTests`,
`TargetRulesTests`, `MenuContextTests`, `PreviewSubjectTests`; рядом —
`PanelKeyNavigation`, `PanelView`, `TreeNavThrottle`, `PathFollowing`,
`DragHover`, `EdgeScroll`. Харнесс — `post`, `assert-state`,
`assert-focus` (QA.md).

## Навигация и дерево

- **`NavigationFallback`** (Core, тест) — куда идти, когда папки нет.
  `AfterDelete(удалённые, текущая)` — ближайший не задетый предок или
  null, вложенность как у `PathRewrite.Under`; строки удалённого уходят из
  обеих панелей (`Removed`), родители перечитываются
  (`RefreshTreesAbove`). `AfterRestore(путь, exists, kindOf)` — путь, пока
  есть, иначе `PathCrumbs.NearestExisting`, но только на
  `VolumeKind.Fixed` (букву флешки мог получить другой носитель); иначе
  `MainViewModel.OpenStartFolderAsync` берёт `AppSettings.WorkFolder` — он
  же старт, когда последней папки нет. Историю оба не трогают.
- **`StartFolder`** (Core, тест) — `--folder <путь>` / `--folder=<путь>`
  (`FromArguments`), у харнесса — `Override`; `OpenStartFolderAsync` берёт
  её первой; папки нет — `WARN` и старт как без ключа. Тестовые запуски
  передают свою (`check.bat run` — `tests\Fixtures`, харнесс — корень
  песочницы), иначе сеанс без `state.json` начнётся в «Документах».
- **`NavigationService`** — back / forward; запись несёт
  `NavigationSource`, чтобы дерево и панель просмотра реагировали
  по-разному. Панель наследуется в обе стороны: `GoUp` — источник
  текущей записи; шаг мимо панелей (папка списка, адрес, удержанный drag)
  остаётся в закладках, если папка открыта из них (`Inherit`); меняет
  панель только явный выбор или недостижимый путь (`PanelRules`). Клик по
  папке — `NavigateAndSelectFolder` (в её листинге ничего не выделено).
- **Пропавшая папка — состояние**: `MissingFolderPath` взводится в фоне
  по `DirectoryNotFoundException` / `DriveNotFoundException`, не
  проверкой перед навигацией (синхронный поход на шару = зависание).
  Закладке (`IsMissingBookmark`) — «Указать расположение…» (диалог — на
  `PathCrumbs.NearestExisting`) / «Убрать».
- **`NavigationController`** (App) — маршрутизация путей и
  shell-сентинелов (`shell:RecycleBinFolder` через `IShellNamespace`);
  адресная строка (`Breadcrumbs`, `RecentPaths`, `IsEditingAddress`), XAML
  — `Vm.Nav.X`. Крошки — `PathCrumbs.Split`, shell-сентинел одной
  крошкой, `ScrollViewer` в хвост; поле — `Ctrl+L` / клик по пустому,
  выход — `Esc`, потеря фокуса, удавшаяся навигация. `RecentPaths` (Core)
  — MRU 20 без дублей, `AppState.Session.RecentPaths`, `F4`.
- **Подсветка у каждой панели своя**: курсор — в модели, активная — у
  панели с клавиатурой; переход из одной панели курсор другой не трогает.
- **Панель не уезжает вбок**: `FolderTreesView.Line_RequestBringIntoView`
  гасит запрос WPF и выпускает заново с горизонталью видимой части
  `ScrollContentPresenter`. Выключается `AppSettings.TreeScrollsSideways`.
- **Переименование папки — путём переноса**: `F2` в панели —
  `MainViewModel.RenameFolderAsync` → `RenameMany`, затем
  `FollowRelocated`; строки переписываются на месте (`Relocated`), уровни
  родителей перечитываются. Перенесённая строка уходит из прежнего уровня
  (`PanelRules.Follow`): иначе пропала бы из его перечитывания, и
  `SettleGone` увёл бы курсор и свернул ветку.
- **Листинг вне UI-потока** — `RefreshFolderAsync` / `RefreshShellAsync`
  в `Task.Run` с отменой: побеждает последняя навигация; спиннер после
  150 мс.
- **Иконки и миниатюры** — `Controls/AsyncIcon` (наследник `Image`):
  кэшированную отдаёт синхронно (`TryGetCachedIcon`), остальное —
  `Task.Run` под шлюзом (4 слота), результат отбрасывается, если контейнер
  переехал на другой файл; после очереди актуальность перепроверяется.
  - Синхронный ответ диск не трогает: «папка или файл» говорит строка
    (`AsyncIcon.RowIsFolder`), иначе общий ключ типа (`file|noext`,
    `ext|.txt`) рисовал бы новую папку значком файла. Декодированный тир
    помнит, из какого `byte[]` картинка.
  - Тиры: `SystemIconProvider` — `byte[]` (память FIFO 512 + диск);
    **`IconImageCache`** — декодированные замороженные `BitmapImage` (до
    256 и не больше четверти `PictureMemory`; иначе декод оставался на
    UI-потоке — 338 декодов и 141 мс за секунду прокрутки).
  - Ступени: `Small` / `Normal` — значок по расширению (`SHGetFileInfo`;
    типы со значком в файле — `.exe`, `.ico`, `.url`…, `_ownIconExtensions`
    — по пути, как `.lnk`); `Medium` (96) / `Large` (256; 384 / 512 на
    крупном масштабе системы — `ThumbnailCacheOptions.SideFor`) —
    `IShellItemImageFactory` только для `IsThumbnailable` (иначе шелл
    пишет значок в общий `thumbcache`). Прочее на `Large` — `SHIL_JUMBO`:
    слот 256, значок не масштабируется; `TrimJumboSlot` режет слот до
    ступени над нарисованным. Шелл не увеличивает (`SIIGBF_RESIZETOFIT`
    только ужимает) — `StretchDirection=DownOnly`, ячейка — потолок.
  - Мимо шелла: RAW — `RawThumbnail` (`RawPreviewExtractor` + WinRT
    `Windows.Graphics.Imaging` с масштабом на разжатии; не
    `System.Drawing` — GDI+ сериализуется; 75 → 3 мс; оверлей ярлыка на
    RAW потерян осознанно). `Medium` / `Large` сначала спрашивают
    `BookCover` (`.fb2`, `.epub`; обложка «страницей»; 16 / 32 не
    получают). PDF — `PdfPageImage` (`Windows.Data.Pdf`, 14 + 13 мс),
    **всегда** (читалки вроде SumatraPDF provider не регистрируют);
    синхронно `.AsTask().GetAwaiter().GetResult()` — вызывающие на фоне.
    Аудио — `AudioCover` (тег, иначе картинка рядом; кэш на папку по
    mtime). `.lnk` — `ExistingLinkTarget` подменяет на цель, стрелку
    накладывает `DrawLinkOverlay` (шелл запекает её в значок, не в
    миниатюру); ключ по `.lnk` (TECHDEBT).
  - Ключ кэша: по пути, где картинка своя, по расширению, где общая;
    бюджет считает первые. На диск — только крупная; размер в ключе,
    когда не 256; поколение (`ThumbnailDiskCache.Generation`) — правка
    того, как рисуется миниатюра, инвалидирует диск.
- **Закладки** — drag-add, сворачиваемые, `AppState.Favorites` /
  `IsBookmarksExpanded`; спец-папки через `IKnownFolders` →
  `SHGetKnownFolderPath` плюс Корзина. `Delete` на своей — `ChoiceDialog`
  «закладку или папку» (`MainViewModel.DeleteFromBookmark`), на встроенной
  — выключение (`BookmarksController.HideSpecial`).

## Shell-namespace: корзина и архивы

Две вещи, которые выглядят папками, а папками не являются: корзина
(`shell:RecycleBinFolder`) и архив, открытый как папка. Обе за контрактом
`IShellNamespace` (Core); `WindowsShellNamespace` диспетчеризует:
корзина — `ShellRecycleBinFolder`, архив — `ShellArchiveFolder`, обе на
`IShellItem`. Листинг — ветка `RefreshShellAsync` (эпоха, спиннер,
отмена, `PublishRows`), адрес и крошки обычные (`GetDisplayName` →
null); `Enumerate` принимает `CancellationToken` и смотрит на него между
элементами.

- **Путь внутри архива — обычный parsing name** (`D:\pack.7z\sub\b.txt`).
  Надвое режет `ArchivePath.Parse(path, extensions)` (Core): первый
  сегмент с архивным расширением — контейнер, хвост — путь внутри.
  Расширения Platform читает из реестра лениво, по классу
  папки-обработчика (`FolderHandlerOf`: `HKCR\<ProgID>\CLSID` с учётом
  `UserChoice`, иначе `SystemFileAssociations\<ext>\CLSID`; ∈
  `CompressedFolder` / `ArchiveFolder` / `CABFolder`; fallback `.zip`).
  Решение 2026-09-24: 7-Zip и WinRAR своей папки не регистрируют —
  отданная им ассоциация меняет запуск, а не просмотр папкой.
- **Один предикат** — `Archives.Of(path)` → `IShellNamespace.ParseArchive`,
  никаких `EndsWith(".zip")`; `File.Exists` контейнера — настоящая папка
  `backup.zip` открывается папкой. `MainViewModel` кэширует ответ на смену
  пути (`NoteCurrentLocation`): `CanExecute` спрашивает десятки раз в
  секунду.
- **`IShellItem`, не `Shell.Application`**: у того `FolderItem.Size` и
  `ModifyDate` для `ArchiveFolder` врут (0 и 1899), а `Items()` корзины —
  один непрерываемый вызов (~5 с холодной). Перечисление —
  `BHID_EnumItems`; папка — `SFGAO_FOLDER`; размер и дата — `IShellItem2`
  (`PKEY_Size`, `PKEY_DateModified`), у папок в архиве размера нет.
- **Корзина.** Имя — `System.FileName` (как на диске, с расширением:
  display name прячет `.lnk` всегда, остальное — по настройке
  Проводника; запасное — хвост `SIGDN_NORMALDISPLAY`). `FullPath` —
  `SIGDN_FILESYSPATH` (`$R…`); время удаления —
  `PKEY_Recycle_DateDeleted`, точный FILETIME в `ModifiedUtc` (сортировка и
  сопоставление в `Restore`); «откуда» — `PKEY_Recycle_DeletedFrom`; у
  папки настоящий размер. «Восстановить» ищет по `$R`-файлу
  (`RecycleHandle.BinFilePath`), `Ctrl+Z` — по `BinItemId`. ~0,4 с на 1400.
- **Корзина — на своём STA-потоке** (`OwnApartment.Run`): её
  apartment-threaded папка с потока пула попала бы в общий host-STA, и
  долгий вызов держал бы overlay-запросы значков (`SHGetFileInfo`) —
  четыре слота `AsyncIcon` и следующую папку. Холодный заход размазан по
  строкам (6,4 с на 1469, первая через 12 мс) — листинг порциями (AD2):
  `PortionClock` (Core, тест) — первая на 300 мс, дальше раз в секунду,
  копии через `IProgress` в `Enumerate`; `MainViewModel.Land` кладёт первую
  как приход (вуаль снимается), следующие — как перечитывание с
  выделением, порция после целого отбрасывается; архив порций не даёт.
  Лог: `Recycle bin: opened … first row after … rows in …, N portion(s) on the way`,
  `Recycle bin: listing abandoned at …`.
- **Байты: zip — потоком, остальное — движком шелла.** `CompressedFolder`
  отдаёт `BHID_Stream` (`IShellNamespace.ReadEntry` →
  `ShellArchiveFolder.ReadEntry`, в память; 430 КБ за 18 мс); у
  `ArchiveFolder` — `E_NOINTERFACE`, а `IDataObject` без `FileContents`;
  `IFileOperation::CopyItem` с `FOF_NO_UI` извлекает всё. Поток берут
  миниатюры, панель и «Открыть» — копию. `Shell/ExtractionService` (Core)
  вокруг `IShellNamespace.CopyOut`: один вызов движка на батч, гард на
  цель, `IConflictResolver` (Replace → старое в корзину), лог,
  `OperationTracker`, отмена через `PreCopyItem`, undo — `ExtractAction`
  в композите. Не `BatchExecutor`: тот требует `stat` обоих концов. Входы:
  `Ctrl+C` внутри кладёт пути внутрь, `PasteAsync` узнаёт их по
  `ParseArchive`; «Извлечь…» (`MenuCommandId.Extract`) — плюс
  `PickFolder`. Причина отказа движка — `ShellArchiveFolder.Failure`
  (первый отказ элемента, иначе код прогона); стоп без причины (zip с
  паролем без окна) — `ArchiveLockedException`.
- **Внутри архива выключено решением**: удаление, переименование,
  вырезать, вставка, создание, drop внутрь, сторож, оценки, спутники,
  поиск по содержимому и подпапкам, статистика папки. Фильтр по имени
  работает. Меню внутри — пять строк, не серые: серая строка обещает
  «потом». «Извлечь рядом» — `FixedConflictResolver(Rename)`: никого не
  спросили — ничего не заменяется. Миниатюры — только картинки (ниже):
  `IsThumbnailable` → false (шелл отвечает значком типа и писал бы его в
  общий `thumbcache`); папке внутри `SHGetFileInfo` нужен
  `FILE_ATTRIBUTE_DIRECTORY`, иначе пустой лист.
- **Пусто или защищено**: zip с паролем перечисляется, но байт не даёт;
  7z с `-mhe` — ноль записей, как пустой. Оба — текст в статусе;
  нечитаемый контейнер — «архив повреждён или недоступен».
- **Отступление от «всё откатываемо»** — временная копия записи
  (`Core/Shell/TempExtraction.CopyOutAsync`) без гарда, диалогов и undo:
  копия чужого файла — не данные пользователя. Папка — `AppPaths.Tmp` по
  хешу пути записи (`TempFiles.FolderFor`): `DataTmp` (`<DataRoot>\tmp`)
  либо `SystemTmp` (`%TEMP%\Wander`) по `AppSettings.UseSystemTemp`
  (`bool?`: до выбора — системная Temp в портативном;
  `AppPaths.UseSystemTemp` ставит вьюмодель). `TempFiles.Sweep` на старте
  чистит **обе** папки старше суток. Одну копию делят «Открыть», панель и
  окно конфликтов.
- **Конфликты: «извлёк — сравнил».** `ExtractionService` перед
  `ResolveAll` распаковывает пары «файл против файла равного размера» от
  мелких к крупным в пределах `AutoCompareLimit`; байты читаются по
  `FileConflictInfo.ReadablePath` (`SourceReadPath`), ключ ответов —
  `Source.FullPath`. Папка внутри не распаковывается
  (`SourceReachable` = false): обойти может только шелл; её ответы —
  заменить / оставить / под новым именем.
- **Дерево и закладки.** Архив — строка в обеих панелях:
  `WorkspaceController.ReadLevel` добавляет к папкам файлы с
  `Archives.Of(path) is { IsRoot: true }`; уровень под
  `Archives.Contains(path)` — через `IShellNamespace.Enumerate`. Шеврон
  архива не пробуется (`PanelRow.IsProbed`) — иначе каждая папка открывала
  бы все свои архивы; пустой теряет шеврон при раскрытии. Меню и цели drop
  нет (`FolderTreesView.HasMenu`); источник drag — да (`CanDrag`): папка
  архива уходит объектом оболочки (`OutgoingDrag.ShellPayload`). Закладка
  на путь внутри — лист, не «пропавшая».
- **Наружу — шелловским объектом данных**: `CF_HDROP` с путём внутри
  архива читается как несуществующий файл. `IShellNamespace.CreateDataObject`
  собирает то же, что Проводник: `SHCreateItemFromParsingName` →
  `SHGetIDListFromObject` → `SHCreateShellItemArrayFromIDLists` →
  `BindToHandler(BHID_DataObject)` (`ShellDataObject`); не `…FromShellItems`
  — shell32 её по имени не экспортирует. Внутри `CFSTR_SHELLIDLIST`, у zip
  ещё `FileGroupDescriptor`.
  - `OutgoingDrag.Run` — `DataObject(comObject)`, **только** `Copy`;
    `Ctrl+C` — `ISystemClipboard.SetShellObject` (`OleSetClipboard`, OLE
    поднимается по `CO_E_NOTINITIALIZED`).
  - Свои приёмники берут пути у самого жеста —
    `OutgoingDrag.InFlightPaths` на время `DoDragDrop` (дописать формат в
    обёрнутый объект WPF не даёт); drop в папку —
    `MainViewModel.ExtractAsync`; папка внутри архива и листинг архива —
    отказ, у листинга нейтральный.
  - Объект выбирает `MainViewModel` и передаёт в
    `ClipboardController.Copy` — обратная зависимость `Core/FileSystem` →
    `Core/Shell` дала бы цикл. Свой список путей остаётся для вставки
    внутри Wander; `SyncFromSystem` его не стирает (флаг
    `_sharedShellObject`).
- **Панель просмотра.** Архив в папке — `PreviewRoute.Archive`: первый
  уровень на пуле, папки сверху, до 200 строк и «и ещё N»; решает не
  расширение — `PreviewRouter.Route(path, isArchive)` берёт факт от
  вызывающего (`Archives.Of` + `CanNavigate`). Тот же список — для папки
  архива без выделения. Файл внутри до 32 МБ — временная копия (с размером
  записи или без него, как у RAR, — переиспользуется); распаковка **одна
  за раз** (`SemaphoreSlim` в `PreviewController`): движок не
  останавливается внутри записи, и стрелки по RAR набирали 85 потоков
  пула.
- **Миниатюры картинок внутри** (AL; шелл их не даёт — стенд по всем
  флагам `IShellItemImageFactory`): `ArchiveThumbnail` — `SizeOf`
  (`PKEY_Size`, до 32 МБ); zip — `ReadEntry` в память и
  `RawThumbnail.RenderPicture(bytes)` (ориентация —
  `MetadataExtractorImageReader.Read(Stream)`), остальное — `CopyOut` в
  `TempFiles.FolderFor("thumbnail|" + path)` под `SemaphoreSlim(1)` (у
  solid-архива запись стоит распаковки блока) и удаление сразу; RAW и TGA
  — всегда копией. Готовая копия панели (`TempExtraction.CopyPathFor`)
  читается. Ключ дискового кэша — путь записи + mtime и размер архива
  (`ThumbnailDiskCache.TryBuildFileName`). Хвосты — TECHDEBT.

## Выделение, буфер, фильтр

- **`SelectionController`** (App) — отложенный клик: оба смысла нажатия
  на строку (схлопывание мультивыбора и `Ctrl`-переключение) применяются
  на отпускании, начавшееся перетаскивание их отменяет — drag везёт всё
  выделение. Исключение — `Ctrl` по невыделенной строке при старте
  перетаскивания: она добавляется.
- **`RubberBandController`** (App) — адорнер, захват мыши, пересечение с
  контейнерами; только с пустого места (`ListVisuals.IsChrome` — полосы
  прокрутки, заголовки, разделители). Нажатие взводит (`Arm`),
  прямоугольник — на системном пороге перетаскивания (`Begin`), иначе клик
  в зазор между плитками ловил обоих соседей. У края прокрутка по
  `EdgeScroll`, угол привязан к содержимому; строка, ушедшая за экран
  внутри рамки, остаётся выделенной, пока не вернётся вне её.
- **Каретка** — `ListState.Caret` модели (`MainViewModel.CaretPath` —
  проекция): от неё пойдёт следующая стрелка; рамка —
  `CaretRowConverter`, кисть `RowCaretBorder`, триггер последним. Путь, не
  строка: строки заменяются на каждом перечитывании. Сообщает список:
  нажатие, клавиатура, положенная жестом (`ListCaretMoved`), отчёт о
  выделении (строка с фокусом, иначе последняя выделенная). Читают
  `TryEnterList` и `CaretIndex`.
- **Вырезанные строки** гаснут по `MainViewModel.CutPaths` +
  `CutRowConverter` — без флага на записи, иначе `Ctrl+X` пересобирал бы
  строки.
- **`EntryVisibility`** (Core) — `ShowHidden` / `ShowSystem` /
  `HideSystemRootFolders` одним значением для списка и дерева, в фон —
  снимком. `SystemRootFolders` (`$RECYCLE.BIN`,
  `System Volume Information`) — отдельно от `SystemPathGuard`: тот запрещает менять,
  этот решает показывать.
- **`ClipboardController`** (Core) — пути + copy / move; хранит пути, не
  содержимое.
- **`ListVisuals.Ancestors`** (App) — единственный путь вверх от
  `e.OriginalSource`: у `Run` `VisualTreeHelper.GetParent` **бросает**;
  текстовые элементы шагают по логическому дереву.
- **`TypeAheadController`** (Core) — префикс, таймаут 1 с, «та же буква
  перебирает»; часы подставляются.
- **`SearchController`** (Core) — живой фильтр: `SetSource` — снимок после
  hidden / system, проекция на фоне с отменой на каждое нажатие,
  `FilteredChanged`; в Core — гонки «печатаю + Refresh» в VM были
  источником багов.

### Системный буфер обмена

```
Ctrl+C/X → ClipboardController ─┬→ модель в памяти (Paste читает её)
                                └→ ISystemClipboard.SetFiles → Explorer видит
Window.Activated → SyncFromSystem → ISystemClipboard.GetFiles → модель ← Explorer
```

Модель в памяти: буфер эксклюзивен и межпроцессен, а
`CommandManager.RequerySuggested` дёргает `CanExecute` десятки раз в
секунду. Чтение по `Activated`, не `WM_CLIPBOARDUPDATE`: чтобы вставить,
окно всё равно активируют; «поменялся, пока активны» самоисправляется;
`AddClipboardFormatListener` — отдельным классом, если понадобится.
Прочитанное побеждает; запись при занятом буфере остаётся внутри.
`WindowsClipboard` — свой Win32, не `System.Windows.Clipboard` (Platform
не тянет WPF); формат —
`CF_HDROP` + `Preferred DropEffect` (DWORD копировать / вырезать).
Неприятности API: память `SetClipboardData` принадлежит системе;
«вырезано» — **бит** `DROPEFFECT_MOVE` (пишут `COPY|LINK`); ретрай на
каждом вызове; `OpenClipboard(NULL)` + `EmptyClipboard` обнуляет
владельца и ломает `SetClipboardData` — владелец `GetActiveWindow()`.
Вырезал у нас, вставил в Проводнике — перемещение не наше, в undo нет.

Текст и картинка (X): `GetFiles` только отмечает флаги (`ClipboardFiles`:
`HasText` / `HasImage` / `HasAnything`), байты — в момент вставки:
`GetText` (`CF_UNICODETEXT`), `GetImagePng` (`PNG` как есть, иначе
`CF_DIB` → `DibFile.ToBmp` (Core, тест: заголовок 14 байт, смещение
пикселей с масками и палитрой) → PNG WinRT-кодером на пуле),
`GetFormatNames` для «не вставлено». `ClipboardPaste.Choose` (Core) —
один вид: файлы → текст → картинка; файлы не с диска — ничего. Пишет
`FileOperationService.CreateFile`: гард, лог, undo в корзину, ничего не
заменяет (`IFileSystem.WriteNew`, `CreateNew`, имя — `UniqueNames`).
`SyncFromSystem` перед вставкой — текст из самого Wander виден без
переактивации.

### Drag & drop

Приём — `DropTargetController` (список и панели), отдача —
`DragPreview/OutgoingDrag` (список и дерево; `FileDrop`). Плашка: иконка
+ `+N`, действие, цель, DPI; при `Effects=None` без причины скрыта,
причина — только когда целились в папку
(`DropTargetController.TargetIsFallback`). Садится справа-снизу от курсора
и переворачивается у края (`WindowPlacement.BesideCursor`). Вне своих окон
(`WindowFromPoint` → наш ли процесс) — только «что в руках»: разрешает
цель своим курсором. Подсветка цели — adorner, у строки панели — строка.

- **Вглубь, не отпуская** (U1 / U2) — `Core/Layout/DragHover`,
  `EdgeScroll` (тесты, часы подставлены), таймер удерживаемого drag
  (40 мс) — в `DropTargetController`. У края — прокрутка (зона 24 px, до
  1500 px/с, квадратично); над папкой после задержки (панель —
  2 × `MouseHoverTime`, список — 3 ×) — `HoverOpened`: раскрыть строку
  панели (`ChevronToggled`) или войти в папку списка; не для архива,
  корзины и перетаскиваемой папки с потомками; отката нет. Сброс — уход
  за окно, бросок, полоса закладок.
- **Правая кнопка**: драг тем же порогом из `FileListView` и
  `FolderTreesView`; `OutgoingDrag` несёт `InFlightRightButton`,
  `DropTargetController` запоминает его и
  `DragDropKeyStates.RightMouseButton` из `DragOver` (к `Drop` кнопка
  отпущена); `Execute` отдаёт план в `offerMenu`. Меню —
  `Core/Menu/DropMenuBuilder` (тест): действия, применимые ко всем
  брошенным (`ActionApplicability`), выход — в папку броска
  (`MainViewModel.RunActionOnDropped` →
  `ExternalActionRunner.RunAsync(..., outputFolder)`), скрытые подменю
  скрыты (`Normalize`). Действия — над первичными файлами
  (`DescribeDropAsync` → `GroupPathsWithCompanions`, `Primary`): иначе
  один `.xmp` среди брошенного выключал бы действия над картинками. Меню —
  после возврата `DoDragDrop` (`ShowDropMenu`).

### Слежение за папкой

`IDirectoryWatcher` / `WindowsDirectoryWatcher` (`FileSystemWatcher`) за
открытой папкой; shell-namespace — нет. Пачки с фона → повторяющийся
`DispatcherTimer` 500 мс (перезапускаемый при непрерывном потоке не
сработал бы) → `FolderSession.DecideWatchTick`; холостой тик гасит
таймер. Правится имя, своё переименование не приземлилось или идёт своя
операция — `Hold`. Ошибка вотчера (переполнение) = изменение, вотчер
переподнимается. Переименование — парой (`DirectoryChange.OldPath` из
`OnRenamed`) → `FolderChanges.Renames`, выделение идёт за новым именем.

**Миниатюра не переживает свой файл** (кэш ключуется путём, а файл под
путём могли заменить):
- решение тика несёт `Stale` (`WatchTickDecision.Stale` ←
  `FolderChanges.ChangedPaths`) — все названные пути, **включая**
  структурные; `MainViewModel` чистит `IIconProvider.Forget` +
  `IconImageCache`, `AsyncIcon` перерисовывается (статическое событие,
  подписка `Loaded`–`Unloaded`), дисковая запись — на пуле;
- публикация листинга сверяет `ForgetIfChanged(path, FileStamp)` — mtime
  и размер против закэшированной картинки, словарём, без диска: ловит
  правку, пока папка не была открыта. `FileStamp` (`Core/Icons`) одинаково
  читают листинг и провайдер.

Дисковый ключ и так несёт mtime и размер; точечное удаление закрывает
перезапись с сохранением обоих.

## Сессия папки — `Wander.Core/Listing/`

Машина **решений** без ввода-вывода (O11): факты на входе («навигация в
X», «листинг эпохи N долетел», «сторож заметил»), решения на выходе
(«опубликовать», «выделить», «перечитать»); диск, потоки, `Dispatcher`,
таймеры и коллекции — в `MainViewModel`. Перечисление —
`DirectoryInfo.EnumerateFileSystemInfos()`; новая папка заезжает одним
уведомлением (`BulkObservableCollection.ReplaceAll`: 4 вместо 7681 на
5000 файлов); старые строки убираются ниже ввода (`Background`), и не
убираются, если листинг уже пришёл.

- **`FolderSession`** — `BeginListing` выдаёт эпоху и «прибытие /
  перечитывание»; `IsCurrent(epoch)` — единственный вопрос «мой ли ответ»
  (листинг, проход оценок через `RatingsController.isCurrent`,
  публикация). `OnNavigating` запоминает выделение покидаемой папки, гасит
  обогнанное намерение, планирует умолчание (подъём — покинутая, иначе
  память LRU 64). `DecideArrival` — единственное потребление намерения.
  `SetArrivalHere` — намерение операции (вставка — вставленное, с
  клавиатурой) только для папки на экране.
  `RewriteMemory` — папка перенесена Wander'ом. `DecideWatchTick` поверх
  `FolderChanges`: стоп / подождать / перечитать (с парами
  переименований) / перечитать строки; идемпотентен.
- **`ListingDiff`** — «текущие строки + свежий листинг → план»
  (`RemoveAt` / `Insert` / `Move` / `Replace` / пересобрать).
  Неизменённая строка не теряет контейнер. Двигаются только строки вне
  наибольшей возрастающей подпоследовательности (n log n): уехавший по
  дате снимок — один `Move`. Пересборка (`Reset`, прокрутка в начало) —
  когда общего нет, в своём порядке меньше половины общих или правок
  больше 256.
- **`CurrentRowFallback`** — выделенный файл ушёл (`Del`, удалён снаружи,
  перенесён, спрятан): текущим становится следующий уцелевший, иначе
  предыдущий — **выделен**, каретка и клавиатура на нём, без прокрутки
  [решения 2026-09-21 и 2026-09-22: одно правило на все причины; рамка
  без выделения, как в Проводнике, оставила бы панель просмотра пустой].
  Спрашивает `ListingArrival`; `Del` задаёт его заранее
  (`NextAfterRemoval`).
- **`ArrivalIntent`** — одно отложенное намерение «что выделить, когда
  долетит»: установка заменяет, применение одно.
- **Выделенный файл не уходит с экрана сам** (решение человека
  2026-09-28, `Listing/RowFollowing`, тест): главная строка, стоявшая на
  экране, после посадки — **на том же месте**, что бы ни пришло вокруг:
  сторож, `F5`, операция, фильтр, порядок, переименование снаружи,
  проход оценок, выдача поиска и возврат из неё. Строку, от которой
  отмотали, посадка не возвращает — кроме тех, что свою строку показывают
  (`ListLanding.Scroll`: намерение, `Rearranged`, `ResultsLeft`). Список в
  самом начале там и остаётся, пока строка на экране; перед сменой,
  сделанной пользователем, это отступает. Три звена:
  - `ListingArrival` отдаёт `ListLanding.Held` — чьё место держит главная,
    путём **до** посадки (своё, прежнее имя при переименовании, или
    ушедшей строки, чьим преемником стала); другая по намерению, другой
    папки, передача соседке — `null`;
  - `FileListView` до посадки (`MainViewModel.RowsLanding`) снимает
    `RowStand` (путь, расстояние от верха, в начале ли), после —
    `RowFollowing.Decide` → `Hold` / `Reveal` / ничего; посадка до
    раскладки прошлой берёт место из `_asked`;
  - без промежуточного кадра: плитки — в следующем измерении
    (`VirtualizingWrapPanel.ShowOnNextMeasure`, `TileLayout.Hold`),
    таблица — `ScrollToVerticalOffset`.

  `Rearranged` — фильтр по имени, звёздам, меткам (сеттеры
  `SearchController`) или порядок (`SetSource(…, rearranged)` из
  `Refresh(rearranged)`; просьба живёт до первой посадки —
  `MainViewModel._reordering`); первая проекция приходит с флагом в
  `FilteredChanged`. Выдача поиска садится через модель (`LandResults`,
  `ListingReason.Results`: выделенная, попавшая в выдачу, остаётся, не
  попавшая — без преемника; подгрузки и пересортировка — `ListingDiff`,
  «папка ↔ выдача» — целиком). Вид, выходящий на экран, приводится к
  выделению (`ApplyViewAttachment`).
- **Место прошлого сеанса** — `ArrivalIntent.Place`: файл, строки вокруг
  (`StoodAmong`) и строка для верха (`Top`); ставится в
  `OpenStartFolderAsync`, если открылась та же папка. `DecideArrival`:
  файл есть — он; нет — `CurrentRowFallback` по `StoodAmong`; нет и
  соседей — ничего, `Top` не применяется. `Top` идёт `ArrivalDecision` →
  `ListLanding` → `ApplyListSelection`; вид ставит строку первой на
  приоритете `Loaded` (до раскладки смещение прижалось бы к пустому
  списку) — таблице индекс, плиткам `VirtualizingWrapPanel.ShowFromTop`, —
  затем выделенную в видимость. Клавиатуру место берёт с собой
  (`TakeFocus`). Пишется в `WriteStateNow` (`CurrentPlace`: не для выдачи
  и не пока листинг другой папки); верх отдаёт
  `MainViewModel.ListTopRow` ← `FileListView.FirstRowOnScreen`;
  `FlushState` при закрытии пишет, если место сдвинулось
  (`ListPlace.SameAs`).

Инварианты — `FolderSessionTests` / `ListingDiffTests` / `ListRulesTests`.
У VM осознанно: правило спиннера (тайминг вокруг `Task.WhenAny`),
восстановление статуса операции поверх «N элементов».

## Поиск

```
Маска ─┬ shallow → SearchController (Core): проекция снапшота, на каждую букву
       └ deep ───→ ContentSearchController (App), по таймеру
Текст ───────────→   └ ContentSearchService (Core): обход + IContentExtractor
```

Граница — `ContentSearchController.IsDeep`: есть текст или включены
подпапки; тогда `Query` очищается и поле становится запросом.
**Навигация сбрасывает поиск целиком** (`Reset`): забытая галка подпапок
превращала каждую букву в обход диска. Поэтому же область и галка бинарей
не в `state.json`.

- **Быстрый фильтр не кончается на текущей папке**: `SearchController`
  сужает листинг в памяти; `ContentSearchController` через 400 мс и от
  2 символов (`MinAutoRunLength` — только здесь) запускает
  `ContentSearchService` со `SearchScope.Subfolders` (`IsFilterPass`),
  **засеянный** найденным (не мигает), повторы — по
  `SearchResultsController._seen`, `HereFirst` держит найденное здесь
  выше. Окно поиска этим путём не ходит (`_fromFilterBox`).
- **Два критерия через «И»**: маска — ворота, отвергнутый файл не
  открывается (17 файлов / 43 мс против 5074 / 217). Галки «в содержимом»
  нет — её роль играет наличие текста.
- **`NameFilter`** — подстрока; `*` / `?` → шаблон на всё имя; части через
  `;` (как в Everything). Сопоставление руками, без регулярок (на каждую
  букву; `*a*a*a*a*b` — бэктрекинг; тут одна точка возврата), разбор один
  раз на запрос.
- **`IContentExtractor`** — три несовместимых ответа: байты для декода,
  zip с XML, COM-фильтр Windows (в Core жить не может). Порядок в
  `PlatformBootstrapper` — часть контракта: `ZipDocumentExtractor`
  (`.docx .xlsx .pptx .epub .odt .ods .odp`, zip + `XmlReader`),
  `FilterTextExtractor` (Platform, `.doc .rtf .pdf .chm .msg .mht …`),
  `PlainTextExtractor` последним (`TextProbe` по 8 КБ + `EncodingProbe` —
  расширения врут: `.asset` бывает YAML и бинарём). **Провал специфичного
  заканчивает файл** («не удалось прочитать»; иначе `.pdf` без обработчика
  находил слово в `%PDF-1.4`). Экстракторы не бросают — `null`;
  `IsExpensive` — что кэшировать и считать непрочитанным.
- **Почему `IFilter`**: `.doc` — OLE с piece table, свой читатель — хвост
  ошибок; `OffFilt.dll` есть в Windows с 7. `IFilter::Init` без
  `APPLY_INDEX_ATTRIBUTES` отдаёт **пустой документ** (`1|2` → 0 символов,
  `1|2|8|16` → текст); value-чанки отбрасываются. Список форматов
  именованный: реестровый фильтр текста декодирует системной кодовой
  страницей. «Фильтра нет» на расширение (`_withoutFilter`) — только по
  ответам `LoadIFilter` `E_FAIL` и `REGDB_E_CLASSNOTREG`; `FILTER_E_UNKNOWNFORMAT` (`~$….doc`),
  `STG_E_SHAREVIOLATION`, `STG_E_DOCFILECORRUPT` — про один файл (стенд).
- **`BinaryTextSearch`** — отдельный режим: бинари по умолчанию вне (как
  `grep`, `ripgrep`, VS Code); по галке побайтово, **только ASCII**
  (`Supports`); ответ «да / нет».
- **`ExtractedTextCache`** — LRU «путь + размер + mtime», 32 МБ в
  символах, только дорогие форматы (25 мс против 129). Индекс — REJECTED.
  Пределы: 32 МБ на файл, глубина 64 + посещённые, 5000 результатов.
- **Запуск сам.** `ContentSearchController` получает корень и видимость
  колбэками. Пауза 400 мс, порога длины **здесь** нет (три символа давали
  необъяснимую тишину на `:no`); переключатель и `Enter` — сразу.
  `SearchState` (не запускался / ждёт / идёт / готово / остановлен) ведёт
  «Остановить», индикатор, статус. Отмена **забирает владение**: `Cancel`
  поднимает поколение, отменённый проход молчит. «Остановить» = `Stop()`.
- **Окно, а не панель**: критериев четыре, попап закрывался от клика
  мимо. `SearchWindow` с `Owner`, не `Topmost`; скрывается, не
  уничтожается; «свернуть / развернуть» сняты `SetWindowLong` в
  `SourceInitialized` (`ToolWindow` уродует крестик, `NoResize` мешает
  растягивать). `Dismissed` возвращает клавиатуру в список.
- **`SearchExpression`** — `маска:текст` (двоеточие запрещено в именах;
  первое делит) — для **вывода**: поиск из окна виден в поле. Флаги в
  строку не попали — `HasNonDefaultOptions` подсвечивает `⋮` (BACKLOG).
- **Результаты** — `SearchResultsController._rows` → `Entries` пачками не
  чаще 200 мс.
  `FileSystemEntry.MatchSnippet` и `ParentFolder` — как
  `OriginalLocation`: одна строка, без параллельной таблицы. Пока
  результаты на экране, `Refresh()` не пересобирает — только
  `PruneMissingAsync` на пуле; повторить — `F5`.

## Контекстное меню

Что показать — Core, чем нарисовать — App, откуда чужие пункты —
Platform. Строится на каждый правый клик, разметки в XAML нет.

```
правый клик
  ├→ MainWindow: чинит выделение (вне — переносит, пусто — снимает → меню папки)
  ├→ ShellMenuCache.Acquire(paths, folder)            App
  │     └→ IShellContextMenu.Open                     Platform
  │           SHParseDisplayName → IShellFolder → GetUIObjectOf / CreateViewObject
  │           → IContextMenu → QueryContextMenu(HMENU) → обход → ShellMenuEntry[]
  ├→ ContextMenuBuilder.Build(target, settings, shellItems)   Core, чистая → MenuEntry[]
  └→ ContextMenuFactory.Build(model, session)                 App → WPF ContextMenu
```

- **`ContextMenuBuilder`** — правила («Rename на одном», «в корзине
  ничего деструктивного», «у папки нет Open with») чистой функцией от
  `ContextMenuTarget` и `ContextMenuSettings`, под тестами; разделители
  схлопывает `Normalize`. **По выделению** — `Открыть`, «Открыть с
  помощью», расширения, подменю «Файл», `Свойства`; **по фону** —
  `Создать`, «Вид» и «Сортировка», `Открыть в терминале`,
  `Копировать путь`, расширения, `Свойства`. «Вид» и «Сортировка» (решение
  человека 2026-09-28: нужны, дубль выключается в настройках) — блоки меню
  «Вид» строка в строку: подпись «Эта папка · …», виды с хоткеями,
  «Автоматически», «Сделать видом по умолчанию»; ключи, «По
  возрастанию», «Папки сверху», «По умолчанию», «Сделать сортировкой по
  умолчанию» (без закрепления серый). Выбор — строка с `Argument`
  (`SetView` / `SetSortKey`) и галочкой из `ContextMenuTarget` (`View`,
  `ViewReason`, `Sort`, `SortPinned`). В корзине — только «Вид».
- Системное «Создать» **вливается** в наше (по глаголу `NewFolder`
  дочерней строки, не по подписи): своя «Папка» первой (откат и rename на
  месте), `Ярлык` и шаблоны от шелла. «Открыть с помощью» тоже вливается;
  своя «Выбрать приложение…» — при выключенных расширениях.
- **Порядок по частоте**: сверху то, ради чего открыли; свои операции — в
  «Файл» (у половины хоткеи).
- **`SplitShell`** — по каноническому глаголу, не по подписи: подменю с
  ребёнком `openas` → в «Открыть с помощью»; глагол из списка
  (`PreviousVersions`) **или** динамическое подменю («Отправить»,
  «Передать на устройство» — ни один пункт не несёт глагола) → в конец
  «Файл»; остальное — верх. Эвристика (TECHDEBT).
- **`ShellEntryKey.For(verb, header)`** — глагол, иначе нормализованная
  подпись: TortoiseGit пишет в подпись ветку (выключение по подписи
  отваливалось на `git switch`), 7-Zip верхнему пункту глагол не
  публикует, но подпись там стабильна. `IsBlocked` проверяет обе формы.
- **«Программа» и «Для чего»** — из реестра (`IShellHandlerRegistry`):
  `<scope>\shellex\ContextMenuHandlers\<имя>` → CLSID → `InprocServer32` →
  версия DLL; `<scope>\shell\<verb>` — имя ключа = глагол; то же под
  `SystemFileAssociations\<scope>`. `HKLM\SOFTWARE\Classes` и `HKCU\…`
  по отдельности, не `HKCR` (склеенное — минуты против сотни мс). Базовые
  области 40–50 мс холодно, все 848 — ~150 мс.
  - `ShellExtensionCatalog` (Core) сливает реестр и встреченное по
    `ShellEntryKey`. **Таблица — встреченное**: строка реестра без встречи
    — только не Windows и тип добавлен руками (`TrackedShellScopes`);
    выключенная — всегда; строки с ключом-CLSID без имени, приложения и
    описания — нет; одинаковые складываются (`Fold` → `Aliases`, BitLocker)
    при равных подписи, приложении и областях; строка с именем приложения
    — весь его раздел.
  - Программа Windows — «ОС» (`ShellHandler.IsOsComponent`: `ProductName`
    как у `shell32.dll` или «Microsoft® Windows® Operating System»; Core —
    `ShellAppOs`).
  - Поле над таблицей — `ShellExtensionFilter` (Core, тест): подпись,
    программа, типы; расширение с точкой находит и «все файлы». Фильтр и
    сортировка — `ListCollectionView` страницы
    (`ContextMenuSettingsCategory.ShellRows`).
- **Тип меню у ярлыка — тип цели** (`ShellScopes.MenuScopeOf`, стенд):
  шелл строит меню `.lnk` из обработчиков цели, своя у ярлыка одна —
  «Расположение файла». Встреченное и «последние типы» пикера пишутся
  типом цели (папка — `Directory`); `.lnk`, записанный раньше, стирается
  при чтении (`SettingsViewModel.ApplyFrom`).
- **Не в меню намеренно:** «Удалить безвозвратно» (только `Shift+Del`),
  закладки, «Показать в Проводнике», `pintohomefile`. «Открыть в
  терминале» — только папка и фон; значок из встроенных — у него одного
  (глиф `Segoe MDL2 Assets`), он стоит среди сторонних.
- **`ShellContextMenu`** читает **классическое** меню (Win11 прячет его
  под «дополнительные параметры»; оттуда же «Создать» с `ShellNew`).
  `HMENU` не отдаётся в `TrackPopupMenu`, а обходится и перерисовывается
  WPF-строками. Цена: ленивые подменю будит `IContextMenu2::HandleMenuMsg`
  с `WM_INITMENUPOPUP`; owner-drawn (`dwItemData`) пропускаются с логом;
  иконки `hbmpItem` → PNG (`ShellMenuIcons`). Дубли по глаголу
  (`GetCommandString`, `GCS_VERBW`: `cut` / `copy` / `paste` / `delete` /
  `rename` / `properties` / `link` / `openas` / `copyaspath`) рисуем сами.
  «Открыть в Терминале» самого Windows Terminal (глагол — CLSID; релиз,
  Preview, Canary) заменён своим безвозвратно — список `ShellVerbs`.
- **`ShellMenuCache`** — последняя сессия жива: повтор по тому же
  выделению в шелл не ходит (0,4–1,1 с первый раз, 80–260 мс дальше).
  `Acquire` про *другую* цель освобождает прошлую; «открыто ли меню» не
  считается (`ContextMenu.Closed` приходит **после** следующего клика);
  `Invalidate` только отвязывает от ключа.
- **Расширения в нашем процессе** (как в Explorer): `try/catch` с логом;
  `ShellExtensionsEnabled = false` — чужие DLL не грузятся; команда — **после**
  закрытия меню (обработчики открывают модальные диалоги).
- **Кастомизация** (`ContextMenuSettings`): мастер-выключатель, чёрный
  список, скрытые свои — как «что выключено» по строковым именам
  `MenuCommandId` (новый пункт появится сам). `KnownShellEntries`
  копится и подрезается при сохранении (`TrimKnownEntries`).
- **Меню «Действия» в шапке — третья форма тех же правил**
  (`MenuOperations` / `OperationsMenu`):
  `ContextMenuTarget.Place = MenuPlace.Header` →
  `ContextMenuBuilder.BuildHeader`, постоянный порядок
  (подпись · Переименовать группой · Частные ▸ · Конвертировать ▸ ·
  Извлечь рядом · Извлечь… · Ярлык · Копировать путь · Терминал), но
  **неприменимое не показывается**. Серое с тултипом — только действие без
  программы; `ActionApplicability` проверяет инструмент последним.
  Подпись — «4 изображения» через `Text.Plural` (формы через `|`). Один
  каталог: `HideableTree` и галочки действуют на оба места. Шелл в шапке не
  опрашивается; перестройка — на `SubmenuOpened` (пункт без детей WPF
  считает кнопкой — в XAML заглушка). Пустое подменю билдер не создаёт
  (`AddSubmenu`): `Sub` без детей — лист.

## Свои действия и групповое переименование

Один каталог `CustomAction` (`Core/Actions/`) — строка «название · для
каких файлов · программа или встроенный обработчик · шаблон аргументов ·
режим · где показывать · объявленный выход»; хранится в
`AppSettings.CustomActions`, пресеты — из кода, сливаются по `Id`. Решает
Core, UI исполняет:

- **Применимость** — `ActionApplicability.For`: все выделенные подходят
  под `FileTypeSelector` (группа `FileTypeGroups` — списки панели
  просмотра — или маска `*.psd;*.ai`); пустое выделение — действия для
  папок, на текущую; `RequiredTool` без инструмента — `ToolMissing`.
  Частичное совпадение не считается (BACKLOG).
- **Командная строка** — `CommandLine.Expand`:
  `{path} {name} {ext} {dir} {paths} {list} {out} {outdir}`, кавычки ставит
  подстановка, хвостовой `\` корня удваивается; `ValidationKey` ловит
  режим «на каждый файл» с `{paths}` и наоборот, `{outdir}` без
  объявленного выхода или в режиме «все одной командой». **Выход** —
  `OutputNames.Resolve`: рядом с источником или в выбранной папке
  (`RunAsync(..., outputFolder)`), источник считается занятым; занятое имя
  — `FileSystem/UniqueNames`, **номер после наибольшего** («clip (3)»,
  «clip (4)» → «clip (5)»): одно правило на «оставить обе», извлечение и
  выходы действий (решение 2026-09-16) — новое последним в списке, номер
  не переезжает на другой файл.
- **Хранение пресетов** — `ActionCatalog.ToStored` / `Merge`: у пресета в
  `state.json` только `Id`, `Enabled` и свой `Program`
  (`ActionCatalog.Override`), остальное из кода — улучшенная команда
  доезжает и до выключившего. Свои строки — целиком.
- **Программа строки** выбирается: `ActionCatalog.ProgramChoices` —
  инструменты каталога (порядок «Программ»), программы строк, встроенные
  обработчики по названию (`ActionPresets.BuiltinTitle`); новая — файлом.
  `WithProgram` ставит программу с инструментом и видом строки: инструмент
  — по голому имени и в `RequiredTool` (его находит `WithLocatedProgram`,
  а `ActionApplicability` гасит, пока нет); встроенный —
  `ActionKind.Builtin`, аргументы — его настройки (`BuiltinProblem` через
  `ImageConvertOptions.Parse`, подсказка — `ArgumentsHint`). Список один на
  все строки и заменяется, только когда меняются его строки (иначе
  `ComboBox` выбирает заново; `null` при замене — не выбор). Проверка —
  `ProgramValidationKey`: не выбрана / не найдена — ключ текста, голое имя
  инструмента — дело `NeedsTool`. Поиск своей — `IToolLocator.Locate`, как
  у `CreateProcess`: путь — сам файл, имя — `System32`, `Windows` и `PATH`
  процесса, без расширения — `.exe`; не `Find` с папками установщиков.
- **Кодировщик картинок** — `Platform/Imaging/ImageConvertAction` (WinRT,
  «Platform без WPF»): метаданные `ImageMetadata` (MetadataExtractor, и с
  RAW) пишутся номерами тегов (`Imaging/ExifTags`, GPS), слова — из свойств
  декодера; JPEG без изменения пикселей — transcoding без пережатия.
- **Исполнение** — `ExternalActionRunner`: по одному;
  `OperationTracker` (`OperationVerbs.RunAction`); гард на папку выхода;
  `{list}` — временный файл в `AppPaths.Tmp`; код ≠ 0 — `Failed` с хвостом
  stderr; отмена убивает процесс (`IProcessRunner.WasKilled`), недописанный
  выход — в корзину, остальные `Cancelled`. **Undo — только объявленный
  выход** (`CreateAction`); процесс не откатывается — осознанно.
  **`{outdir}`** (решение человека 2026-09-29) — программе, которая
  называет выход сама и пишет поверх готового (`soffice --convert-to`):
  пустая папка `out-<guid>` в `AppPaths.Tmp` на запуск; после кода 0
  `TakeOutput` переносит оттуда файл с именем шаблона (`OutputNames.Name`)
  под имя из `OutputNames.Resolve`, нет такого файла — `Failed` с
  `ActionsOutdirMissing`; папка удаляется в `finally`, после вылета — под
  чисткой `TempFiles.Sweep`. Без объявленного выхода или одной командой на
  всех запуск отказывает до процесса: из папки нечего было бы забрать.
  `IProcessRunner` в Core, `WindowsProcessRunner` в Platform:
  `UseShellExecute = false`, оба потока читаются на ходу (иначе полный пайп
  вешает программу), stderr — только при скрытой консоли,
  `Kill(entireProcessTree)`. Встроенные — `IBuiltinAction` по имени в
  `Program`, выход необязателен.
- **Отладочные действия** (AI2) — `CustomAction.DebugOnly`: меню
  показывают их при меню отладки (`ContextMenuTarget.ShowDebug` из
  `Settings.ShowDebugMenu`), таблица настроек — никогда (`_debugActions`,
  в `state.json` не попадают). `HoldFileAction` (Core) держит выделенный
  файл `FileShare.None` 5 или 30 с (маска `*` — только файлы): занятость
  настоящая — `PathClaims`, часы, отказ другой операции, `IFileBusyProbe`,
  Restart Manager с Wander в держателях.
- **Групповое переименование** — `Core/Rename/`: `RenameRules` (найти /
  заменить, шаблон, регистр — фиксированный порядок; расширение — только
  регистр; счётчик до `RenameRules.MaxCounterWidth` цифр) →
  `RenamePlanner.Preview` — чистая функция от правил, элементов и
  `RenameContext` (`exists`, спутники, дата съёмки — `[X]` читает EXIF на
  пуле): «было → станет», дубликаты в пачке, совпадения снаружи; имя,
  которое пачка сама освобождает, — не совпадение. `BatchRenameGate` —
  два и более **одного вида**. Применение —
  `FileOperationService.RenameMany`, **двухфазный**: член, чьё имя занято
  поздним членом, паркуется на `…<8 hex>.wander-tmp` (сторож не видит);
  один composite, откат в обратном порядке. Память окна —
  `AppState.RenameRules` и пять шаблонов `AppState.RenameTemplates`
  (`RenameTemplateHistory`, `[N]` не запоминается).

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
   CreateRatingSidecar (по согласию)
Listing/RatedListing     WithRatings() листинг → тот же с Rating (читалка делегатом)
```

| Шаблон | Пример | Кто |
|---|---|---|
| `Appended` — к полному имени | `Sprite.png.meta`, `IMG.CR2.pp3` | Unity, RawTherapee, Takeout |
| `Replaced` — заменяет расширение | `IMG_1234.xmp` | Adobe / darktable, `.AAE` |

`Appended` — по точному имени, `Replaced` — по stem'у. Два претендента на
stem: сайдкар — RAW, если RAW среди них ровно один (`IMG.CR2` +
`IMG.jpg` при `IMG.xmp` — XMP у RAW, иначе после «Превью из RAW» сайдкар и
оценка осиротеют); иначе — ни к кому. То же в `Group` и `FindCompanions`
(`CompanionResolver.Owner`).

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
- **Оценки** — `SidecarRating` (`Rank` / `ColorLabel`), формат — за
  `CompanionMetadataService` по расширению; `ColorLabels` нумерованы
  одинаково (XMP хранит имя `Red`, pp3 — номер); `SidecarText` — BOM,
  переводы строк.
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
  только без сайдкара, `.pp3` с `Rank=3` меняет проявку; `.xmp` не влияет,
  читается с 5.7, синхронизируется с 5.11. `AppSettings.RawRatingFormat`,
  при `.pp3` — предупреждение.
- **`.meta` только читается** — Unity владеет им, перезапись отвяжет ассет.

## Галерея и оценки

### Запись оценки не пересобирает папку

Правило (CLAUDE.md): клик по звезде не зовёт `Refresh()`.

```
RatingsController.Apply(строки, поле, значение)
   ├ делит на «сайдкар есть / нет», спрашивает про вторую группу один раз
   ├ CompanionMetadataService.ApplyRatingToMany → один CompositeAction
   └ ApplyResults
       ├ SearchController.Replace: состав видимого тот же → ItemsChanged (эти строки);
       │                            строка выпала из фильтра → полный проход
       └ MainViewModel.ReplaceRows: Entries[i] = новая, выделение назад
```

- **Выделение.** `record` заменяется, и список выкидывает объект из
  `SelectedItems`. Любая пересборка `Entries` (точечная и `SyncEntries`)
  идёт под `IsSyncingRows` — отчёты списка не шлются (иначе замены водили
  панель просмотра по чужим фото), затем приземление (`ListingLanded`,
  `RowsReplaced`): выделение по путям одним вызовом, без прокрутки и
  фокуса.
- **Оценка во всех видах** (J4): «Таблица» — столбец; «Галерея» и
  «Значки» — бейдж: пустой `ContentControl`, шаблон подкладывает триггер
  на `Rating` (у значков — плашка `IconsRatingBadge`, +2 визуала без
  оценки); «Плитка» — звёзды второй строкой вместо типа
  (`TileSecondLineConverter`).
- **Сторож** — `DirectoryChange` + `FolderChanges`: изменился состав →
  `Refresh()`; содержимое известных строк → перечитать их; неизвестный
  файл → `Refresh()`. Глушение по времени теряет настоящие изменения.
- **Панель просмотра** — `SetPrimary` сравнивает путь + размер + mtime:
  та же строка — перечитать спутников, не декодировать RAW.
- **Клик по звезде или свотчу** уходит хозяину неразрешённым
  (`RatingRequestedEventArgs`: `Clicked`, `Current`); «поставить или
  снять» решает `RatingToggle.Resolve` (Core, тест) против **всех** целей
  — выделение, если показанный файл в нём, иначе один файл; половина
  сплита — про свой. `Shift`+цифры в галерее — `SetColorForSelection` тем
  же правилом. Неудавшаяся запись — `RatingsController.StatusReported`
  (`StatusLine` с уровнем), даже если не записалось ничего.
- **Служебные файлы.** `ReplaceAtomic` пишет `<файл>.wander-tmp`,
  `File.Replace` — **свой** бэкап `<файл>~RF<hex>.TMP` (не описан у API;
  найден логом сторожа) — оба в `TransientFiles`. Переименование **из**
  нашего служебного — запись содержимого (`WindowsDirectoryWatcher.OnRenamed`).
- Порядок при сортировке по оценке не меняется до следующего листинга.
- **`Ctrl+Z`** — `IUndoableAction.MetadataTargets`: непустой = состав не
  изменился, `UndoLast` → `RatingsController.RefreshRowsAsync`;
  `CompositeAction` отдаёт объединение, только если **все** члены —
  метаданные. Отмена переноса — `MovesOnUndo` через `FollowRelocated`
  («Модель окна»): открытая папка, вернувшаяся на место, не оставляет
  список на пустом пути.

### Проход по оценкам — второй

```
RefreshFolderAsync (листинг + свёртка, пул)
   ├→ ChooseView() — только при входе, не на F5
   ├→ _search.SetSource() — строки на экране
   └→ RatingsController.StartPass() → RatedListing.WithRatings() (пул, отмена) → SetSource() с Rating
```

Пятьсот RAW — пятьсот чтений, папка должна появиться раньше. Трогает
только строки с `Companions`; без сайдкаров возвращает **тот же список по
ссылке** — UI-проход пропускается. Как читать — делегат `ReadRatingFor`;
отмена по эпохе; `ListingDiff` сверяет строки
`FileSystemEntry.SaysTheSameAs` (с оценкой). **Сортировка по оценке** —
`SortKey.Rating` в `EntryComparers`: первый проход при пустых оценках (=
по имени), второй пересортировывает той же `EntryComparers.Sort`, что
`SystemIOFileSystem.Enumerate` (компаратор имён ординальный — TECHDEBT).
Неоценённое = 0 — папка не переставляется, пока null'ы становятся нулями.
**Перечитывание той же папки** (`F5`, сторож, порядок) несёт оценки с
экрана (`RatedListing.CarryRatings`, по пути, строкам со спутником) до
нового прохода, при сортировке по оценке — сразу в её порядке: иначе
фильтр по звёздам на миг прятал все снимки.

### Фильтр — внутри `SearchController`

`RatingFilter` там же, где фильтр по имени: проекция одна, два фильтра —
гонка. `Reset()` снимает оба. **Набор, а не порог** — два битовых набора
(оценки, метки): клик — элемент и выше, `Ctrl` + клик — один; ранг 0 —
«без оценки», клик берёт его в одиночку. Горит выбранное
(`RatingFilter.HasRank`, `FilterStarConverter`). Клик разложен
(`ReadFilterGesture` / `ClickRankFilter`, `ClickColorFilter`): харнесс
клавиатуру не трогает. Папки не отбрасываются. Первая проекция после
смены фильтра — «перестройка» (флаг в `FilteredChanged`).

### Папка со снимками и автовыбор вида

`ImageFolderProbe.IsImageFolder` — чистая функция от листинга и правил
спутников: знаменатель — содержательные файлы (не спутники, не бэкапы, не
подпапки), иначе папка с `.pp3` у каждого RAW набирает ровно 50 %;
минимума нет. Расширения — `Icons/ImageFormats`, один список с панелью
просмотра и миниатюрами (`_thumbnailableExtensions` берёт `All`).

**Вид — у папки** (AG + Z1). `Folders/ViewChoice.Decide` (Core, тест) —
одно правило на приходе в любую папку: закрепление → оно; автогалерея
включена, не корзина и папка со снимками (`ImageFolderProbe`, лениво) →
«Галерея»; иначе `AppSettings.DefaultViewMode` (из коробки «Значки»,
`ViewMode.LargeIcons`). Ответ — вид и `ViewReason` (закреплён / авто:
снимки / по умолчанию) для подписи «Эта папка · …». `F5` вид не трогает
(`arriving`). Выбор в меню и `Ctrl+Shift+1/2/6/7` — **закрепление за
папкой**; «Автоматически» снимает; «Сделать видом по умолчанию» пишет
настройку и снимает закрепление с этой папки. `ViewMode` — в
`Core/Folders`; хранение — `FolderSettingsBook` («`folders.json`»).

**Сортировка — тоже у папки** (решение человека 2026-09-28): любой выбор
порядка (меню «Вид», меню фона, заголовок таблицы) закрепляет **весь**
`SortOptions` за папкой (`FolderRecord.Sort`, `FolderSettingsBook.SetSort`);
остальные — по `AppSettings.SortKey` / `SortAscending` /
`GroupFoldersFirst`. `MainViewModel.CurrentSort` — закрепление, иначе
умолчание: по нему листинг, пересортировка выдачи
(`SearchResultsController`), галочки меню, стрелка заголовка (`RaiseSort`).
«По умолчанию» снимает, «Сделать сортировкой по умолчанию» пишет
настройку и снимает; смена умолчания перечитывает только папку без
своего порядка; запись, усыновлённая на приходе, со своим порядком
перечитывает папку ещё раз. Дерево — по имени, папки сверху.

Чужой `desktop.ini` (H1): перечисление видит его до фильтра видимости;
на приходе, на пуле, читается `[ViewState] FolderType=`
(`Folders/DesktopIni`, тест): `Pictures` / `Photos` — ещё один факт
`ViewChoice.Decide` («Галерея», «авто: снимки»). Не пишется.

### Фон галереи — палитра

`GalleryBackground` (Light / Grey / Dark) в Core, яркость тёмных —
`GalleryGreyLevel` / `GalleryDarkLevel`. `GalleryPalette` (App) из трёх
чисел собирает **весь** набор: фон, подпись, приглушённый, ховер,
выделение активное / неактивное, рамки — роли двигаются вместе (тёмный
фон со светлой подписью нечитаем, с голубым Проводника — лайтбоксы ярче
фото); на тёмном подсветка — `Lift` фона, на светлом — `#CCE8FF` /
`#E8E8E8`. `Light` = `SystemColors.WindowColor`, по умолчанию. Панель
просмотра берёт фон только под картинкой (`Image`, `Gif`, лупа).

## Окно и его контролы

| Кто | За что |
|---|---|
| `MainWindow` | тулбар, адрес, статус-бар, глобальные хоткеи, сборка меню, причины прихода фокуса и исполнение эффектов вью (модель окна), геометрия (решения — `Core/Layout/`) |
| `Controllers/WorkspaceController` | исполнитель модели окна: очередь событий, эффекты, трасса |
| `Views/FolderTreesView` | обе панели папок как адаптер модели: ввод в события, фокус на строку курсора, drag строки, редактор имени, `Shift` + колесо, полоса «+» |
| `Views/FileListView` | все виды и общие жесты: выделение (отчёт модели, применение одним вызовом), рамка, взведение drag, двойной клик, rename на месте, type-ahead, `Ctrl` + колесо, меню |
| `Views/PreviewPane` | панель просмотра, зум, транспорт, WebView2 |
| `DragPreview/DropTargetController` | приём drop: папка под курсором, разрешён ли, что сделает, подсветка; удержание над папкой и прокрутка у края |
| `DragPreview/OutgoingDrag` | перетаскивание наружу: плашка, курсор, формулировка |

**`MainViewModel` живёт при окне** (корень и namespace `Wander.App`), не
в `ViewModels/`: она хостит контроллеры, а те берут базовые типы из
`ViewModels/` — иначе цикл.

**`DropTargetController` решает, но не действует**: отвечает `DropPlan`,
выполняет VM (с логом, гардом, undo); `Execute` держит обвязку (отказ,
`Handled`, снятие подсветки в `finally`). Один на все поверхности.
Проверки повторяются на самом drop'е — модификаторы меняются между
движением и отпусканием. Удержание над папкой и прокрутка у края —
«Drag & drop».

Граница окно ↔ список: `DragStartRequested`, `ContextMenuRequested`;
вниз `FocusList()`, `ClearSelection()`, `StartRename()` и эффекты модели
`ApplySelection()`, `FocusRow()`, `OpenEditor()`. Окно ↔ панели:
`ContextMenuRequested`, `FocusListRequested` (`Esc`); вниз
`FocusBookmarks()` / `FocusDrives()` / `HasBookmarks` / `PaneOf()` /
`ShowFocusOutline()` / `RevealAndFocus()` / `FocusRow()`;
`Connect(drops, drag)` — общие `DropTargetController` и `OutgoingDrag`.

### Диалоги — один шов

Каждый модальный вопрос — через `Wander.App/Dialogs/IDialogs`:
`Ask(DialogRequest)` (`DialogKind`, заголовок, текст, кнопки, значок;
кнопка по умолчанию всегда отменяющая, поэтому не поле),
`Choose(ChoiceRequest)` (ответы на кнопках, «Отмена» по умолчанию; индекс
или -1), `Prompt`, `PickFolder`, `CreateConflictResolver(skipIdentical)`.
Продакшн — `WpfDialogs` (`MessageBox` поверх активного окна, но не окна операции,
`ITransientWindow`; `ChoiceDialog`, `PromptDialog`, `OpenFolderDialog`,
`DispatcherConflictResolver(InteractiveConflictResolver)`); харнесс
подставляет `ScriptedDialogs` до постройки вьюмодели. Голый
`MessageBox.Show` — только аварийный в `CrashReporter`.

### Клавиатурные области

`Tab` переключает **области**: тулбар → адрес → закладки → дерево → поле
поиска (G6) → список. Порядок и обход — `Core/Layout/WindowZones`
(`WindowZone`, `Order`, `Ring`, `FolderPane`): кольцевая арифметика и
лестница умолчаний. Окну — `ZoneOf` по визуальному дереву, `CycleZone`,
`FocusZone` (`false` — обход дальше: свёрнутые закладки, выключенные
кнопки). Не средствами WPF: родной `Tab` идёт внутрь каждого контрола.
`Tab` **всегда** «следующая область», и из текстового поля. Панель
просмотра не в кольце (`Tab` в текстовое поле запер бы клавиатуру):
`Ctrl+3` — `PreviewPane.TakeKeyboard` (контрол по `Kind`; грузящаяся
панель берёт клавиатуру по приходу контента), повтор — во вторую
половину, `Esc` — `FileList.FocusList`.

- **`Alt`-сочетания не `KeyBinding`**: `Alt` переводит окно в режим меню
  раньше маршрутизации. `Alt+←/→/↑`, `Alt+Enter`, `Alt+D`, `Esc` адресной
  области — в `MainWindow.OnPreviewKeyDown` (туннель впереди режима меню и
  `InputBindings`); при открытом редакторе имени хоткеи окна молчат —
  иначе `Esc` снимал бы выделение раньше отмены правки.
- **Стрелки на границах сетки** — `Core/Layout/GridNavigation`: сетка =
  один список, свёрнутый в строки; `→` в конце строки — следующая, `↑` в
  верхней — первый, `↓` в короткую последнюю — её последний; `Shift`
  тянет через границу, якорь — конец выделения без каретки.
  Перехватываются только нажатия, на которые WPF отвечает «некуда».
- **Фокус на самом списке — тупик**: `TryEnterList` — первая стрелка
  входит сверху или снизу, при выделении — на каретку; `FocusVisualStyle`
  у `ListGestures` снят; `TakeKeyboardOnClick` — ветки, помечающие нажатие
  обработанным (лассо, мультивыделение), забирают клавиатуру сами.
- **Рамка области** — `BorderBrush` контролов (толщина 1, меняется цвет),
  `OnZoneFocusChanged` на `GotKeyboardFocus` **окна**. `GridSplitter` из
  обхода убран — размер панелей только мышью.
- **`Ctrl+1`** — `WindowZones.FolderPane`: раскрыть до открытой папки ту
  панель, из которой открыли (правило модели на `ZoneEntered` с причиной
  «хоткей»), повтор переключает; не из панели — `_lastFolderPane`. Из
  соседней панели в ту, что не держит открытую папку, — на её курсор (со
  «стрелки открывают» — и переход), курсора нет — раскрытие открытой
  папки: `WorkspaceState.HeldRow` читает зону до события — модуль
  клавиатуры последний. `Ctrl+Shift+E` — без переключения; `Ctrl+2` —
  список.
- **В панели стрелки двигают курсор** (`CaretMoveRequested`); открывают
  клик (`RowClicked`), `Enter` (`RowActivated`) и, с «стрелки
  открывают», стрелка через троттл. Клавиши ловит `List_PreviewKeyDown`
  раньше `KeyBinding` окна.
- **Зачем контролы**: режим = контейнер и триггер видимости в
  `FileListView`, жесты — стиль `ListGestures`; галерея добавилась
  контейнером, окно узнало двумя строками. Полоса фильтра оценок — там же,
  `Dock="Top"`.

### Редактор имени

Один `TextBox` на контрол в adorner-слое `ScrollContentPresenter`
(`RenameAdorner`); подпись — `x:Name="NameLabel"` (контракт шаблонов); в
`DataGrid` синхронизируется текущая ячейка. Текст переносится, редактор
растёт вниз. Клик по пустому месту той же папки применяет правку
(`CommitRenameOnClickAway`): список помечает такое нажатие обработанным
ради лассо, и без этого редактор висел бы над снятым выделением. Строка
вне виртуализации — запасной `PromptDialog`.

### Цвета — один словарь

`Resources/Palette.xaml` — кисти по тому, **что красят** (поверхности,
линии, текст, строки, контролы, акцент, метки, меню); влит в `App.xaml`
первым; `MenuStyles.xaml` вливает сам. Code-behind — `Resources/Palette.cs`,
`static readonly` на одном классе: опечатка падает на первой отрисовке.
Тёмная тема — второй набор тех же значений, если больше ничего нет
(`Foreground="#888"` во вьюхе — светлый угол). Не в словаре намеренно
(шапка файла): `GalleryPalette` (вычисляется), `*.xshd`,
`DefaultBackgroundColor` WebView2, обложка книги в `SystemIconProvider`;
свет 3D-сцены — раздел «Not chrome». Семь градаций серого текста — TECHDEBT.

### Подсветка плитки и что шаблону нельзя

Подсветку рисует **контейнер** (`ListBoxItem`) своим `ControlTemplate` по
property-триггерам: `TileChrome` — форма, `TileItem` — цвета плиток
(`#E5F3FB` ховер, `#CCE8FF` выделение, `#E8E8E8` без фокуса),
`GalleryItem` — из палитры (лениво, в сеттерах). Рамка `TileChrome` в 1 px
входит в высоту ячейки (`ChromeBorder`, и в «Плитке») — иначе подпись
теряет нижние выносные; у строки «Таблицы» рамки ячейки нет. Отступ —
`Margin` контейнера (`TileMetrics` ресурсом от `ApplyTileMetrics`):
контейнер = плитка, выделенные не сливаются; `Padding` 0.

Шаблон оплачивается на каждой навигации × видимые плитки. Запреты
(измерены):
- **Никакого `TextBox`** — редактор один (`RenameAdorner`), подпись
  `TextBlock x:Name="NameLabel"`.
- **Никаких `Style.Triggers`** — состояние строки у контейнера, данные —
  конвертер (`TileSecondLineConverter`).
- **Никаких `RelativeSource`** — размеры `DynamicResource` или
  наследование (`FontSize` на `ListBox`).
- **Видное у меньшинства — не безусловно**: бейдж оценки — пустой
  `ContentControl`, `DataTemplate.Trigger` подкладывает `Content`.

Нижняя планка — картинка + подпись, 5 визуалов; продуктовые 8–9.
`LAYOUT <вид> container: N visuals` в логе — регресс виден.

### TileLayout + VirtualizingWrapPanel

WPF не даёт виртуализирующий wrap. Разделено: **`Core/Layout/TileLayout`**
— вся арифметика (колонки, позиция N, высота, диапазон реализации, куда
доскроллить), неизменяемое значение с нуля на проход, `TileLayoutTests`;
**`TileMetrics`** — размер ячейки и содержимого из настроек (`ForTiles` /
`ForLargeIcons`, производные — второй кегль, колонка значка),
`TileMetricsTests`; **`App/Controls/VirtualizingWrapPanel`** — спросить
генератор, померить, расставить.

Ловушки арифметики (отладчиком не ловятся): колонки и расстановка — от
одного прохода; `ArrangeOverride` не просит measure; `BringIndexIntoView`
не зовёт `UpdateLayout()` изнутри раскладки; размер ячейки не зависит от
прокрутки; **размер ячейки — вход, а не выход**: `TileMetrics` из настроек
одним значением (`Settings.IconsMetrics` / `TilesMetrics`), дети меряются
**ровно ячейкой** — мерить контейнер значит замкнуть кольцо «контент →
геометрия → контейнеры».

**Якорь при переливе.** Смещение в пикселях, и при другом числе колонок
или высоте ячейки ряды ниже первого уезжают. `TileLayout.Reflows(прежняя)`
— та же длина, другие колонки или высота (одна высота вьюпорта — не
перелив); `AnchorAt(смещение, клавиатура, выделенные)` — ячейка с
клавиатурой, если видна, иначе первая видимая выделенная, иначе первая
видимая; `Hold(якорь)` — смещение, при котором она на прежней высоте.
Исполнитель — `MeasureOverride` между старым и новым `TileLayout`, пока
дети старого диапазона (не `SizeChanged`: поздно и не видит `Ctrl` +
колесо). `_anchor` держится весь перелив подряд (туда-обратно — то же
смещение, тест) и отпускается `SetVerticalOffset`, `OnItemsChanged`,
сменой выделения, видимости и фокусом на ячейке. Срабатывает только на
геометрию.

Исключение из «ровно ячейкой»: **единственная выделенная** меряется с
бесконечной высотой (`MeasureChild` / `IsExpanding`) — подпись показывает
имя целиком, ячейка растёт вниз поверх соседей (`ZIndex` из
`ArrangeOverride`), сетка не переливается; при двух и больше не растёт
никто. **Незакрытое:** на стенде `ZIndex` кладёт ячейку поверх
(растеризатор и `PrintWindow`), в живом окне сосед сверху (BACKLOG,
«Интерфейс»). Две неочевидности: потолок подписи вешает **триггер «не
выделен»** — `DynamicResource` в шаблоне ложится значением на элемент и
обходит триггер по приоритету; панель **сама подписана на
`SelectionChanged` владельца** и инвалидирует себя — контейнер с тем же
constraint отдаёт прежний `DesiredSize`, и WPF не помечает панель
грязной (стенд, QA.md «что можно проверить без окна»: 159 px до, 173,84
после).

Инвариант тестом: `ExtentWidth` не шире вьюпорта при колонках > 1.
Производного состояния панель не хранит. Харнесс: настоящие
`FileListView` и `MainViewModel` за экраном, сторожевой поток считает
проходы (300 файлов: 0 в простое, 8–14 на щелчок колеса).

**Скроллер свой у каждого вида**: плиточные — горизонтальная `Disabled`,
вертикальная `Auto`; `Details` — `Auto` / `Auto`. Автополоса — ловушка
(`ColumnWidth`): `ScrollViewer` меряет с полосой и без, wrap хочет её при
одной ширине и не хочет при другой — вечная гонка; колонки считаются по
ширине с вычтенной полосой, если содержимое её потребует.

**Recycling** (`VirtualizationMode.Recycling` + `generator.Recycle`):
`layout.realise` 260–400 мс/с → 5–10 мс на пачку. Рамка выделения видит
только реализованные.

### Вид, которого не видно, не строит ничего

Четыре вида на одной `Entries`; `Reset` пачкает измерение всех панелей, а
`Collapsed` предка менеджеру раскладки не указ — навигация реализовывала
папку трижды (`COUNT layout.new: 96 in 3 passes`). Свои панели: при
`owner.IsVisible == false` `MeasureOverride` только перемеряет детей
(немеренный грязный ребёнок держит очередь вечно) и сбрасывает маркеры;
`IsVisibleChanged` инвалидирует. `DataGrid`:
`FileListView.ApplyViewAttachment` отвязывает `ItemsSource`, пока не на
экране; сначала отвязать, потом привязать (уходящий вид сообщает пустое
выделение); `SelectedItem` гасится **до** строк (таблица —
`ClearBinding`, плиточные — локальный `null` поверх стиля `TilePanel`);
многовыделение снимается и ставится обратно. Цена — одна заминка на
смену вида; прокрутка таблицы не переживает.

### Что на экране — читается первым

Шлюз `AsyncIcon._gate` на четыре загрузки отдаёт в порядке создания
контейнеров, не видимости. С `AppSettings.VisibleFirstLoading` (зеркало
`AsyncIcon.VisibleFirst`) — `IconLoadGate` с двумя очередями: значок в
окне своего `ScrollViewer` обгоняет; место известно после раскладки —
запрос ждёт `DispatcherPriority.Loaded`. `FirstScreenWatch`: часы с
`RefreshFolderAsync`, вью отдаёт значки реализованных строк в окне, ждёт
`AsyncIcon.Painted`; `abandoned` при уходе.

## Замеры производительности

`PerfLog.Measure("имя")` суммирует в секундные окна, в лог — > 100 мс
суммарно или > 33 мс за вызов; цена — два таймстампа и словарь под
локом: `PERF layout.realise: 202 ms in 9 calls, worst 38,4 ms`.

| Имя | Что |
|---|---|
| `layout.measure` | `MeasureOverride` плиточной панели, **включает** `layout.realise` |
| `layout.realise` | создание контейнеров: шаблон, привязки, измерение |
| `layout.arrange` | `ArrangeOverride` |
| `icon.decode-ui` | декод миниатюры, только если нет в `IconImageCache` |
| `list.apply` | листинг заезжает в `Entries` |
| `ui.selection-apply` | выделение модели ложится в список |
| `ui.stall` | UI не отвечал (снаружи) |
| `bg.*` | фон: `bg.icon-load`, внутри `bg.thumb-disk` / `-shell` / `-disk-write` |

`bg.` — не UI-поток, законно > 1 с/с. Всегда включены `PerfCounters`
(`COUNT layout.new / reused / kept / discard`,
`LAYOUT <вид> container: N visuals`), `FirstScreenWatch`
(`First screen painted in N ms: K icons, M awaited` / `abandoned`),
`Folder listed in N ms` от 300 мс. `Startup: first frame N ms` —
`MainWindow.OnFirstFrame` на `ContentRendered` (`Loaded` на ~900 мс
раньше). `UiStallWatch` — поток раз в 200 мс просит диспетчер
(`DispatcherPriority.Input`), ждёт > 150 мс → `ui.stall`; тот же heartbeat
закрывает окно `PerfLog`, флашит `PerfCounters` и дёргает `SystemVitals`.
Точки замеров — PERFORMANCE.md.

`SystemVitals` (`App/Diagnostics/`) — раз в 5 с и на каждый `ui.stall`:

```
SYS ws=431 private=360 gen=167/155/134 alloc=+45 loh=6 handles=1060 threads=40 cpu=8,0
```

МБ; `GC.CollectionCount` нарастающим итогом; `alloc` — прирост
(`GC.GetTotalAllocatedBytes`); `loh` — `GetGCMemoryInfo().GenerationInfo[3]`;
`cpu` — доля ядра (`TotalProcessorTime` / стена / `ProcessorCount`);
`gcpause` — мс остановки сборщиком с прошлой строки (сборки почти все
полные — подвисание видно только по паузе). `Process` берётся один раз,
`Refresh()` перед чтением. Смысл — форма за сессию (растущий `ws`,
невозвращающиеся `handles`, `gen2` на каждую папку): ради неё сценарий
`soak`; в отчёте харнесса — секция `SYS` (последние пять).

## Отзывчивость: приоритеты

«Всё асинхронно» ≠ «не мешает»: континуации `await` и `BeginInvoke` — на
`Normal`, ввод — на `Input`, **ниже**; поток результатов заслоняет клики.

- **Результат фона — ниже ввода, в два яруса**: листинг
  (`RefreshFolderAsync`: `Dispatcher.Yield(Background)` перед очисткой и
  `PublishRows`) на `Background`, миниатюры (Medium / Large) на
  `ContextIdle` — в одном ярусе старые строки стояли, пока долетят сотни
  миниатюр. Устаревшее отбрасывают токен и эпоха. Лёгкие иконки (Small /
  Normal) — на `Normal`, кешированные синхронно
  (`AsyncIcon.IsLightweight`).
- **Синхронный путь навигации не трогает диск**: `NavigateTo` путь не
  проверяет, `NavigationSource.Address` проверяется в фоне с гардом «уже
  ушёл»; ретаргет вотчера — на пуле по поколению; `state.json` — дебаунс
  `_stateSaveTimer` 500 мс с флашем из `OnClosing`.
- **Очистка при входе — не внутри клика**: демонтаж на `Background` после
  отрисовки перехода; листинг уже пришёл — один своп; медленная папка —
  очистка сразу и спиннер.
- **Остальной диск на пуле** (O10): уровни панелей (`ReadBranch`),
  `PruneMissingAsync`, `OpenStartFolderAsync`, открытие файла для панели,
  размер кэша. Чтение папки обрывается по токену между элементами
  (`IFileSystem.Enumerate`, `IShellNamespace.Enumerate`; корзина — 2 мс
  после отмены); дерево и панель просмотра токен не передают.
- **Клавиатура панели коалесируется** (`TreeNavThrottle`: одиночное —
  сразу, серия — один переход после покоя); навигация в открытую папку
  гасится правилом. **Шевроны оптимистичные** до пробы `ProbeChevrons`.
- **Иконки**: `SHGetFileInfo` сериализован (`_shellIconLock` — под
  конкуренцией отдавал пусто для handler-иконок); негативный кэш
  `_missing` — только у миниатюр; `Unloaded` поднимает поколение —
  загрузка снесённого контейнера отступает, вернувшийся (recycling)
  переспрашивает по `Loaded`; ретрай через секунду; провал —
  `[icon-diag]`, > 1 с — `slow shell load`.
- **Закрытие**: `Hide()` до освобождения COM — ~1,4 с доумирает невидимо.

### Таймеры: троттл решения, а не место решения

Таймер только прореживает события. Обязательно: **решение отделимо**
(зовётся напрямую: `RunNow()`, `FlushState()`, `Finish` флашит); **тик
идемпотентен** (`Flush` при чистом `_dirty` выходит, `DecideWatchTick`
без изменений — `Idle`), поэтому таймер повторяющийся; **гасит себя и не
теряет накопленное под занятостью** (`Idle` / `Hold`). Инвентарь: сторож
500 мс, флаш результатов 200, автозапуск поиска 400, `state.json` 500,
клавиатура дерева 90; часы воспроизведения (`GifImage`, `_videoTimer`) —
пока показ, гасятся на `Unloaded` / `ResetVideoTransport`. Дебаунса
панели просмотра нет — поколение через отмену. Абстракции часов нет:
таймеры в App, тесты не достают.

## Preview pane

`PreviewController` (App) — конвейер с отменой и спиннером. `PreviewPane`
берёт контроллер **своим `DataContext`**: всё, что панели нужно от
хозяина, — свойства контроллера (`ContentPalette` кладёт
`MainViewModel.PushPalette`), наружу — события `RatingRequested`,
`RevealRequested`. Поэтому панель живёт и вторым экземпляром: половина
сплита, «Сравнить», полный экран.

- **Сплит.** `MainViewModel.PreviewSecond` (`ShowFooter = false`,
  `ShowPictureBar = true`; `ShowRawDecode` зеркалится) + второй
  `PreviewPane`, созданный при первой паре (`MainWindow.ApplyPreviewSplit`)
  и вложенный в первый (`PreviewPane.ShowSecond`). Футер первой — под
  обеими и про выделение; звёзды, балл и гистограмма — на полосе снимка
  каждой половины: `IsPreviewSplit` включает `ShowPictureBar` и первой, её
  футер свои прячет (`FooterRating`, `FooterHistogram`, `FooterSharpness`),
  звезда половины — про её файл
  (`ApplyRatingFromPane(wholeSelection: false)`).
- **Стопкой или рядом** — `SplitOrientation.Stacked` (Core, тест): сумма
  площадей двух вписанных кадров в обоих раскладах по месту над футером
  (`PreviewPane.PairArea`); стопкой ⇔ примерно a₁·a₂ > r². Формы — из
  заголовков (`PictureLoader.ShapeOf`: EXIF или WIC, встроенный JPEG у RAW,
  поворот 5–8), на пуле до показа (`MainViewModel.ShowPair`, до 150 мс);
  неизвестная — квадрат. Переворот — только когда другой расклад крупнее в
  `TurnAbove` = 1,1 раза. То же правило у `CompareWindow` (`Arrange`).
- **Зум синхронный**: `PreviewPane.Link(a, b)` через `ZoomLink` (Core,
  тест) — ведущая отдаёт точку долями области, вторая повторяет
  (`FollowZoom`, без захвата мыши); с зажатой правой вторая стоит, сдвиг
  запоминается; `Reset` — новая пара (`PreviewPairShapes` в сплите, новый
  правый снимок на полном экране).
- **Что показывать.** Пара — `PreviewPair.Of(выделение, листинг)` (Core,
  тест): ровно два файла с маршрутом, порядок по списку. Один из
  нескольких — `PreviewSubject.Of(цель, каретка, листинг)` (Core, тест):
  `Primary` — каретка, если она в выделении (`SelectedItem` WPF стоит на
  первом выделенном), в паре — верхний. `RefreshPreviewPrimary` зовётся
  на каждую смену строк, главной и каретки — список сообщает их в любом
  порядке. Сводка по нескольким — `ShotSummary` (Core): одно
  значение, два-три через запятую, больше — поле не пишется; EXIF у первых
  ста; `ShotSummary.CameraName` убирает двойную марку.

| Kind | Чем |
|---|---|
| `Image` | `BitmapImage`, `DownOnly`; RAW — встроенное превью |
| `Gif` | `Controls/GifImage` |
| `Video` | `MediaElement` |
| `Audio` | тот же транспорт, карточка трека; играет `MediaPlayer` |
| `Text` | `TextBox`; документ текстом — с переносом (`TextWrap`) |
| `Code` | AvalonEdit |
| `Document` | `RichTextBox` (RTF) |
| `Web` | WebView2 — PDF / HTML / MHTML / Markdown / FB2 / SVG |
| `Model` | `Viewport3D` — STL / OBJ / glTF / GLB |
| `Folder` | перепись + блок тома на корне |
| `Executable` | карточка программы: значок, строки `ExecutableCard` |

- **`PreviewRouter`** (Core, `PreviewRouterTests`) — «расширение →
  `PreviewRoute`», без диска; `Route` (каким загрузчиком) ≠ `Kind` (каким
  контролом). Таблица — **порядок правил**, побеждает первое: `.webp` —
  картинка и многокадровый контейнер, `.mtl` — текст, `.svg` рисуется, а
  для действий остаётся текстом (`TextLike`).
- **Медиа.** `Audio` и `Video` делят `MediaUri` и транспорт; проигрыватель
  разный обязательно: `MediaElement` без площади открывает файл и стоит на
  нуле (200×120 играет, 1×1 молчит). Контроллер ставит `Kind` **до**
  `MediaUri`. `RestartMedia` (повтор и второе нажатие кнопки): файл
  объявил длительность — `Position = 0; Play()`, нет — только `Close()` +
  `Open()` (стенд `MediaPlayer`); пустой кадр переоткрытия закрывает снимок
  последнего кадра (`HoldLastFrame`, `VideoHold`, элемент держит размер
  через `MinWidth` / `MinHeight`) до первого тика со сдвигом. «Доиграл» —
  флаг, не позиция: движок сам отматывает на ноль. `PlaybackClock`: повтор
  по умолчанию для роликов короче 3 с и без длительности
  (`LoopsByDefault`, не для звука), «позиция стоит секунду» = доиграли;
  `Timecode` — длительность вверх, без объявленной — самая дальняя позиция.
- **Фон** — `MainViewModel.ContentPalette` (палитра области файлов), не
  `Settings.GalleryPalette`. `Foreground` / `Dim` **считаются от фона по
  контрасту** (фиксированная пара на сером давала 2.2:1). Ловушка: путь
  `DataContext.ContentPalette.…` — без `DataContext.` биндинг через
  `RelativeSource` молча не находит (ловится
  `PresentationTraceSources.DataBindingSource`). Текст, код, документы —
  светлые.
- **Оценка**: строка вне блока спутников — `OfferRating` предлагает и
  файлу без сайдкара; полоса — у всего, у чего `HasRating`.
- **Полоса снимка** (`PreviewController.ShowPictureBar`) — в
  `ContentArea`, левый нижний угол; шаблоны футера (`RatingControls`,
  `HelperSwitches`, `HistogramChart`). Приглушение — `ReachBar` /
  `UpdateBar`: 30 %, пока мышь не в нижних `BarReach` = 96 px и не пришёл
  `RatingChanged` (`NoteRatingShown`, 1,5 с); переход 60 мс; на зуме
  прозрачна. Кнопки хелперов, **RAW** и `</>` — на строке звёзд в
  `WrapPanel`.
- **Полный экран** (`Views/FullscreenWindow`): план —
  `FullscreenPlan.Of(выделение, листинг, каретка)`, шаг —
  `PictureWalk.Step` (оба Core, тест; `stood` — место снимка, скрытого
  фильтром, `skip` — левый для правого). У каждой панели свой контроллер
  (`MainViewModel.NewPictureViewer`; хелперы, фон и RAW общие); `Detach`
  при закрытии отписывает от `ReviewHelpers` — иначе жил бы с кэшем
  кадров.
  - `←` / `→` в паре меняют роли панелей (`KeepSide`), а не грузят правый
    снимок в левую — иначе на кадр виден левый во весь экран.
  - Панель прозрачна, пока ничего не показывает (`Kind` None, не
    `IsLoading`); `IsPlaceholderVisible` молчит на первой загрузке. Полоса
    — `BarAtRest` = 0, будит мышь у края или `FlashBar`; у пары ходят
    вместе (`BarReached` ↔ `ReachBarWith`).
  - `Delete` — `MainViewModel.DeletePictureAsync` → тот же `DeleteAsync`,
    про снимок `KeyTarget`; автоповтор не удаляет подряд. Картинки
    декодируются в память (`OnLoad`), файл не держат.
  - Курсор: `CursorIdleMs` = 1,5 с → `ForceCursor` + `Cursors.None`;
    синтетический `MouseMove` без сдвига не будит.
  - `Ctrl` + `Z` — `UndoCommand` из `OnPreviewKeyDown`; `Alt` + `Space` и
    `F10` (`Key.System`) гасятся — системное меню ломало окно без рамки;
    `Z` — `PreviewPane.ToggleZoom` (`_zoomPinned`, `ZoomMove.Pinned`).
  - `MainViewModel.Say` → `StatusSaid` → плашка 4 с. Строки после оценки
    — заново (`RatingsController.FindInSource`, и скрытые фильтром) по
    `Entries.CollectionChanged` и `Ratings.CompanionsChanged`.
  - Окно закрывается на KeyDown, и автоповтор уходит в главное: `Enter` /
    `Space` в галерее и `Esc` в списке с `IsRepeat` ничего не делают.
- **Отступ** — `PreviewPane.PictureMargin` (4 px, на полном экране 0): на
  него смотрят `ImgFit`, `ImgOverlay`, `GifPreview`, размер декода
  (`ReportViewport`) и зум (`UpdateZoomPosition`). Картинка посередине по
  обеим осям; лупа на непрокручиваемой оси ставит кадр так же,
  `(hh - srcH) / 2`.
- **Декод под поле и соседи** (AK). Поле — `SetViewport`: место под
  картинку в пикселях устройства с шагом `BoxStep` = 64.
  `PictureLoader.Decode` (пул): JPEG и встроенный JPEG — под поле
  (`PictureFit.DecodeWidth`, Core, тест: от 95 % поля — целиком; масштаб в
  DCT), остальное целиком → `DecodedPicture`. Поле выросло — перерез через
  `BoxSettleMs` = 300 (`PictureFit.TooSmall`). Первый размер панели
  ждётся до `BoxWaitMs` = 500 (`_boxKnown`).
  - `PictureCache` (UI-поток) — три кадра и байты, `SizedCache` над
    `MemoryShare`; ключ путь + время + размер + поле; спрятанная панель и
    `Detach` отдают кадры. Соседи — `PreviewNeighbors.Of` (Core, тест: выше
    и ниже, только картинки, первым — по направлению движения), по одному
    после показа (`DecodeNeighborsAsync`; законченный декод кладётся в кэш
    и при отмене), только для кадра своей строки (цель ярлыка и запись
    архива — каждый раз).
  - **Память под картинки** — `PictureMemory` (Core, тест): МБ из
    настройки либо 1/16 `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`;
    четверть — `IconImageCache` (байты `W × H × bpp`), остальное — кадрам
    всех панелей; показанный и греющиеся — `Keep`; `Fits` решает соседа по
    `PictureLoader.DecodedBytes`. Предел ставит
    `MainViewModel.ApplyThumbnailCacheSettings`, сразу.
  - Серия — смена чаще `BurstMs` = 150: промах кэша ждёт `BurstDelayMs` =
    90 и переспрашивает. `preview.shown` — раз на смену выделения
    (`_shownMeasured`).
  - **Между картинками панель не гаснет**: прежняя стоит до готовности
    следующей (`ClearPreviewContent(keepImage)`), вуаль — после
    `VeilDelayMs` = 250. Футер ждёт картинку (`FooterWaitsForPicture`,
    спутники очищаются и заполняются в один такт) — иначе он дважды менял
    высоту и вертикальный снимок дёргался; высоту держит содержание:
    спутники — `CompanionLabel` → `SummaryNote` (`SummaryHead` /
    `SummaryRest`), факты через `SummaryText.Gap`.
- **DPI** (AM): `BitmapPixelSizeConverter` — пиксели ÷
  `PreviewPane.DpiScale` (`Loaded`, `OnDpiChanged`) для `ImgFit`,
  `GifPreview`, обложки аудио; вписанный вид — `HighQuality` по
  пиксельному размеру; `MagnifierCursor` — `scaleWithDpi`; 1:1 — на целых
  пикселях. Крупная миниатюра по масштабу — «Навигация и дерево».
- **RAW не декодируется**: WIC на `.CR3` — ~1150 мс на 33 МБ
  (`DecodePixelWidth` и `Thumbnail` не помогают).
  `RawPreviewExtractor` (Core) достаёт JPEG из контейнера за 8–13 мс:
  ISO-BMFF (`uuid` Canon с `PRVW`); TIFF (IFD → JPEG: CR2, NEF, ARW, DNG,
  PEF, 3FR; RW2 / RWL — тег `0x002E`, длина — счётчик; ORF — Exif IFD →
  MakerNote `OLYMPUS` / `OM SYSTEM` → `0x2020` → `0x0101` / `0x0102`,
  смещения от начала заметки; старый `OLYMP` — нет); RAF — указатель в
  заголовке (байт 84, big-endian); CIFF (CRW) — запись `0x2007` корневой
  кучи. SHORT в слоте значения — первые два байта (`MM`-файл иначе даёт
  старшую половину: iPhone DNG терял превью). Кандидаты от большего к
  меньшему с проверкой маркера (в DNG / NEF самый большой поток —
  raw-данные, SOF3). У CR3 `PRVW` — 1620×1080, полноразмерный — первая
  дорожка `moov` (`Extract(fullSize: true)`, 60–110 мс против ~10).
  - Не прочитан (FFF — превью несжатое) — **превью кодека**:
    `ImageDecoder.CodecPreview` (`BitmapDecoder.Preview`, без
    `Frames`; Raw Image Extension — 3–100 мс против 0,7–2 с матрицы),
    вписанное; `DecodedPicture.CodecPreview` — для кнопки RAW и зума 1:1
    как встроенное. Нет и его (DNG-декодер, IIQ от WIC как TIFF) — декод
    файла целиком.
  - Панель берёт оба (`LoadImageAsync` → `LoadFullSizeAsync`): быстрый —
    сразу и вписанным (`Image`), большой — после 150 мс на файле и
    **только в зум** (`ZoomSource`; по нему меряют `ImgZoom`,
    `IsImageDownscaled`, `UpdateZoomPosition`), иначе пересэмплировал бы
    картинку после каждой стрелки. Одинаковые байты не декодируются
    дважды; `PreviewPane.RefreshImageZoom` — зум под сменой картинки.
  - Предел вписанного (`ImageCapWidth` / `Height`) у RAW — кадр из EXIF
    (`DecodedPicture.FrameWidth`, `PictureLoader.WholeFrame`: больше превью
    и той же формы в пределах 2 %), пока такой кадр придёт — `ShowPicture`
    (`bigger` у CR3, `decode` с матрицы): превью сразу растянуто и только
    дорезчивается. RAW с одним маленьким превью — предел по превью.
    `RefitWhenSettledAsync` переспрашивает `TooSmall` при срабатывании.
    Миниатюрам — только быстрый.
  - JPEG лежит **неповёрнутым**: ориентация IFD0 контейнера —
    `ImageMetadata.Orientation` → `ApplyOrientation`
    (`TransformedBitmap`). Тег применяется к **каждой** картинке:
    `BitmapImage` не поворачивает и JPEG с камеры.
- **`IgnoreImageCache` — только для файлов**: у картинки из `MemoryStream`
  URI нет, и `FinalizeCreation` на .NET 10 падает на `null` — быстрый путь
  RAW молча уходил в полный декод.
- **Карточка программы** (B7): `PreviewRoute.Executable` →
  `LoadExecutableAsync` → `IExecutableInfoReader` (Core) ←
  `WindowsExecutableInfo`: `FileVersionInfo`, `PeHeader.Parse` (Core, тест:
  первые 4 КБ — машина, подсистема, DLL, CLR), `CreateFromSignedFile`,
  `WinVerifyTrust` без UI и сети (`WTD_REVOKE_NONE`,
  `CACHE_ONLY_URL_RETRIEVAL`; `TRUST_E_NOSIGNATURE` — «нет подписи»,
  иное — «не подтверждается»; каталожные не смотрятся) →
  `ExecutableCard.Facts`. Подпись хэширует весь файл — отдельный
  `ReadSignature` после `FullSizeDwellMs`, по одной через
  `_signatureGate`, без отмены (ответ про ушедший файл отбрасывается по
  ссылке); больше `SignatureByItselfBytes` = 200 МБ — «не проверялась» и
  `CheckSignatureCommand`.
- **Документ текстом** (B5): `PreviewRoute.DocumentText` →
  `ContentSearchService.DocumentText` — только форматные экстракторы (общий
  показал бы бинарник буквами), кэш общий с поиском; `Kind = Text`,
  `TextWrap = Wrap`, пометка первой строкой; до 64 МБ и 200 000 знаков.
  `ZipDocumentExtractor` ставит `\n` после `_lineEnds` (p, h, h1–h6, li,
  tr, table-row, row, si, br — один список на все форматы): абзац —
  строка и в панели, и в сниппете.
- **Поиск в тексте** (B6): `TextFind` (Core, тест) — смещения, первое от
  каретки, по кругу, без регистра, от 10 000 — «N+». `FindBar`: смещения в
  тексте и коде, диапазоны в RTF (по run'ам — через смену формата не
  находится); `ForgetMatches` на смене `Text` / `CodeText` /
  `DocumentPath`; дочитанный RTF ищется заново в `LoadDocumentAsync`.
  - Подхват — `PreviewController.FindRequest` в конце загрузки, запрос —
    `FindTextFor` (`MatchSnippet` и `ContentSearch.TextQuery`; у второй
    половины и «Сравнить» — `MainViewModel.FoundText`).
  - `Ctrl+F` — сначала `OpenFind` панелей (вторая половина отвечает сама,
    `SecondSlot`), иначе поле над списком. `F3` / `Shift+F3` —
    `PreviewPane.FindAgain`; `PastLast` над выдачей по содержимому →
    `MainViewModel.NextFoundRow` (`FindWalk`, Core: по порядку списка,
    мимо найденных только по имени) и `FileList.SelectRow`.
  - За показанным началом: `PreviewController.NoteRest` помнит остаток
    (путь и пропуск либо хвост строки), `CountPastShownAsync` считает на
    пуле (`TextFind.Count` блоками по 64K с переносом хвоста;
    `EncodingProbe.Reader` — та же кодировка, тест на совпадение с
    `Decode`; остаток в памяти — `StringReader`); `ShownTextLength` —
    слова заметки не ищутся.
- **`Ctrl+C` в панели** — выделенный текст, не файл; решает окно
  (`PreviewPane.TryCopySelectedText`), не каждое поле.
- **TGA** (B8): `TgaDecoder` (Core, тест) → `BgraImage`: несжатый и RLE,
  палитра (индекс 8 / 16, записи 15–32 бит), цвет 15–32 бит, серый 8,
  угол из дескриптора, нулевая во всём кадре альфа — непрозрачно; до
  16384²; палитра, поле id и объём данных проверяются до буфера. Панель —
  `ImageDecoder.Tga` (`BitmapSource.Create`, Bgra32), миниатюры —
  `TgaThumbnail` (WinRT-кодер PNG, до 64 МБ); `.tga` в `ImageFormats.All`.
- **HEIF** (B10): `ImageFormats.Heif` (`.hif` с камер — тот же
  контейнер), декод под панель (`PictureLoader._scaled`: 12 Мп — ~250 мс
  вместо 600–1000); поворот
  контейнера — `UprightHeif` в `MetadataExtractorImageReader` (WIC
  EXIF-тег игнорирует); 1×1 без пикселей — `DecodeFailed` в
  `ImageDecoder.Decode`. После провала — `ICodecProbe` /
  `WindowsCodecProbe` (`MFTEnumEx`: `CLSID_WICHeifDecoder` — HEIF Image
  Extensions, `MFVideoFormat_HEVC` — HEVC Video Extensions; список WIC не
  годится — заглушка в `windowscodecs.dll` есть всегда) →
  `PreviewController.ExplainMissingCodecAsync`, плашка и
  `OpenStoreCommand` (`ms-windows-store://pdp/?ProductId=…`).
- **SVG** (B8): `PreviewRoute.Svg` перед кодом; `PreviewText.SvgPage` —
  `<img>` с `data:` base64 (скрипт не исполняется) в WebView2;
  `ShowSvgSource` — разметка, на обе половины; больше 1 МБ — разметкой
  (строка WebView2 до 2 МБ). Снимок из панели в кэш — TECHDEBT.
- **Не открылось и занято** — `ExplainUnreadableAsync`: держатель через
  Restart Manager на пуле.
- **Разбор в Core (`Preview/`), отрисовка в App.**
  - `AudioTags`: ID3v2.2–2.4 (синх-безопасные размеры), ID3v1, Vorbis,
    MP4-атомы, RIFF-INFO; WMA / Ogg — нет; AAC — только ID3 (кадр ADTS
    проходит проверки MP3 и врёт). Длительность FLAC из `STREAMINFO`, MP3
    по первому кадру, VBR — `Xing` / `Info` / `VBRI`; кодировка 0 — по
    всем однобайтовым полям сразу. Обложка: `APIC` / `PICTURE`, иначе
    `Cover.jpg`, `folder.jpg`, `front.*`, одноимённая, единственная
    картинка в папке. Тесты на байтах из билдеров, не на фикстурах.
  - `MeshFile` + `Stl` / `Obj` / `GltfReader` → `MeshData` (позиции +
    `MeshPart` с индексами и цветом `Kd` / `baseColorFactor`; без текстур
    и нормалей — WPF считает пофасеточные, обратная сторона
    притемнена). STL двоичный — по `84 + 50 × n`; OBJ — отрицательные
    индексы от конца, четырёхугольники веером; glTF — обход узлов с
    матрицами и памятью посещённых, буфер `BIN` / `data:` / `.bin` рядом
    по простому имени. Камера — из радиуса описанной сферы.
  - `Fb2Document` (HTML-фрагмент, потоковый `ReadCover`, namespace по
    локальному имени, бюджет 400 000 по ходу обхода с закрытием тегов,
    картинки `data:` до 6 МБ); `BookCover` (`.fb2`, `.epub`:
    `container.xml` → OPF → манифест, EPUB 3 `cover-image` / EPUB 2
    `<meta name="cover">` / по имени; DjVu, CHM, `.doc` — нет).
  - Markdown — свой `MarkdownPipeline` (`UsePipeTables`, `UseGridTables`,
    `UseEmphasisExtras`, `UseTaskLists`, `UseAutoLinks`, `UseFootnotes`; не
    `UseAdvancedExtensions` — тянет iframe и `{#id .class}`).
  - `EncodingProbe`: BOM → строгий UTF-8 → счёт 1251 / 866 по регистру
    (строчные ×3; порог 8 кириллических букв — `ä ö ü` это кириллица в
    1251; таблицы в Core). `TextProbe`: 8 КБ, BOM, нулевой байт —
    приговор, доля управляющих. Текст — первый мегабайт, оборванный
    `U+FFFD` срезается.
- **`App/Preview/`**: `ImageDecoder`, `ModelBuilder` + `ModelScene` (Core →
  `MeshGeometry3D`), `PreviewText` (бюджет, кодировка, Markdown,
  HTML-обёртка), `SummaryText`, `PictureLoader`, `PictureCache`,
  `ExecutableCard`. Контроллеру — конвейер.
- **Ярлык прозрачен**: `.lnk` резолвит `IShortcutService`
  (`ShellShortcutService`, `IShellLinkW`), рисуется цель; `LinkTarget` в
  футере и «Перейти к оригиналу» → `MainViewModel.RevealPath`
  (намерение `ArrivalIntent.Rows`; папка уже открыта — посадка заново).
- **Футер**: пусто — папка (рекурсивно, async); файл — имя, размер, дата
  и EXIF (`MetadataExtractor`, RAW включая CR2 / CR3 / NEF / ARW / DNG);
  несколько — агрегат; под ним спутники (GUID из `.meta` с копированием).
- **Том** — `IVolumeInfoProvider` / `WindowsVolumeInfo` над `DriveInfo`,
  только на корне; неготовое свойство бросает — «не готово» = том с
  нулевой ёмкостью.
- **Перепись папки** — `FolderStatistics` (Core, итеративно; глубина 64 —
  защита от junction'ов, «числа неполные»); `IsCensusLoading` отдельно от
  `IsLoading`; числа раз в 150 мс через `IProgress<FolderProgress>`, типы
  — в конце.
- **Подсветка кода**: AvalonEdit по расширению (и `.diff` / `.patch`);
  свои `Highlighting/*.xshd` (`Batch`, `ShaderLab`, `YAML`) через
  `HighlightingCatalog.EnsureRegistered()`; битый пропускается.
- **Строка панели** в панели просмотра — `PreviewSubject`: запись папки
  читается на пуле (`ShowPanelFolder`), показывается, если цель ещё она.
- **WebView2 изолирован**: `NavigationStarting` — только `file:` /
  `about:` / `data:`, попапы режутся; `WebResourceRequested` режет
  `http` / `https` / `ws` / `wss` / `ftp` (deny-list: рендерер раздаёт
  обвязку по внутренним схемам) — внешние картинки в Markdown не
  грузятся. Скрипты локального `.html` исполняются (TECHDEBT).

## Хелперы отсмотра — `Wander.Core/Imaging/`

Папка уровня 0: чистые функции над `BgraImage`
(`byte[] Pixels, Width, Height, Stride`) — факты на входе, числа и маски
на выходе, тесты на синтетике. App подаёт пиксели и забирает маски
(`Preview/ReviewOverlay`), Platform меряет файл (`ISharpnessProbe` /
`Icons/SharpnessProbe`: WinRT, декодируется только нужная область).
Набор включённых — `ReviewHelpers` (App), один на окно: обе половины
сплита, полный экран, галерея; между запусками не хранится.

**Мера одна — крутизна края** (`Sharpness.Measure` → `CrispMap`):
градиент Собеля, делённый на местный контраст (окно 7×7): у ступеньки
около 4, у края шириной w — около 4/w, от контраста сцены не зависит.
«Верхние 3 % градиентов» дают резкому кадру и его размытой паре 3,04 % и
3,17 % отметок, крутизна — 1,11 % и 0,06 % (стенд). Порог пикинга — 2,0.
Мерить — только в родном разрешении: на уменьшенном промах фокуса
читается как попадание.

**Что на чём.** Пикинг и балл — полный кадр (у RAW — вшитый JPEG, у CR3
большой); в панели маска ужимается максимумом, в лупе — 1:1. Клиппинг,
гистограмма и кривые — рабочая копия ≤ 2560 px. Миниатюрам маска — с
шагом 2 (`Measure(step)`). Кривые теней и светов двигают середину не
больше чем на 10 уровней из 255.

**Точки против линий** (`FocusPeaking.Continuous`): пятно меньше шести
точек выбрасывается; вытянутое (в 2,5 раза длиннее ширины) или крупное
(от 200 точек) — грань в полный вес; мелкое круглое (снег, блик, зерно) —
вчетверо меньше. Балл (`Sharpness.Score`) — 98-й перцентиль крутизны по
области × доля «настоящих» граней среди всех краёв (полный вес с 3 %).

**Балл — по середине кадра, не по точке AF**: Canon пишет зону AF в
makernote (`CanonAfInfo`, поворот — `AfGeometry`), но бывает в углу над
потолком — данные камеры; по такой зоне промах получал 100, попадание
95, по середине — 27 и 91. Рамка рисуется — видно, когда камера пишет
ерунду.

**Режим RAW**: декод сенсора не шарпится, зерно в нём чётче граней
(4,6 % отметок против 0,85 % у JPEG) — перед замером `Luma.Denoise`,
медиана 3×3. Балл и в этом режиме — по вшитому JPEG.

**Что живёт между кадрами**: балл — `SharpnessProbe` по пути и штампу (до
4000); маски пикинга — `ReviewOverlay`, бит на пиксель (24 Мп = 3 МБ),
бюджет 32 МБ; рендеры ячеек — `ReviewThumbs`, 120.

**Наложения**: метки клиппинга и пикинга — одна `Indexed4` (на 24 Мп
12 МБ вместо 96), рамки AF — геометрия поверх, всё — `DrawingImage` с
клипом по кадру (без клипа перо у края раздувало картинку, и лупа
уезжала). Панель — `ImgOverlay` размером с `ImgFit`; лупа — своё, в
разрешении кадра, место ставит `UpdateZoomPosition`.

**Кто когда считает.** Панель — `PreviewController.ScheduleHelpers`:
токен привязан к загрузке, публикация — только если картинки те же
(`ReferenceEquals`); пока у CR3 не приехал большой JPEG, пикинг и балл
ждут (`Request.FullReady`). В галерее считают ячейки: `ReviewThumb`
заказывает при появлении и отменяет при уходе (`ReviewThumbs.Ask`:
пикинг — по полному кадру, клиппинг и кривые — по самой миниатюре, метки
— той же `ReviewOverlay.Marks`, что в панели), `SharpnessController`
складывает баллы в строки пачками. Очередь — `RankedGate` (Core, два
потока): ранг спрашивается, когда освобождается место, — файлы в панели
(`ShownPath`), остальное выделенное, прочее на экране
(`MainViewModel.HelperRank`). Сама панель — мимо очереди.

## Состояние и логи

Корень — `AppPaths.DataRoot` (Core, `Persistence/`): `--data-dir <путь>` →
`--portable` (`data` рядом с exe, `Environment.ProcessPath`) →
`WANDER_DATA_DIR` → `%LOCALAPPDATA%\Wander`; `AppPaths.Resolve(args)` в
`App.OnStartup` до логгера, `Override` — харнессу и тестам.
`LOCALAPPDATA` из среды не читается — папку даёт оболочка. Потребители:
`FileLogger`, `JsonAppStateStore`, `ThumbnailDiskCache`, `CrashReporter`,
`PreviewPane` (WebView2). Источник корня — в заголовке сессии
(`Data root: … (arg|portable|env|override|default)`).

**Две копии на одних данных**: установленная (`C:\Programs\Wander`,
`publish.ps1 -Install`) и Debug из Rider делят `%LOCALAPPDATA%\Wander` —
так задумано. Кто пишет `state.json`, решает `InstanceLock` (Platform):
экземпляр без ключа держит именованный мьютекс
`Local\Wander.state.<хэш корня>` (только существует, пока жив процесс);
экземпляр с `--yield` (`AppPaths.Yields`, профиль `launchSettings.json`)
перед каждой записью смотрит, есть ли владелец, и, увидев его раз, до
конца сеанса не пишет (`IAppStateStore.IsReadOnly`, «— настройки не
сохраняются» в заголовке, строка в логе). Хэш корня — харнесс и
установленная копия друг друга не видят. Кэш миниатюр и профиль WebView2
общие. Профиль — `AppPaths.WebView2` (AD1): `<tmp>\WebView2` при
`UseSystemTemp`, иначе `<DataRoot>\WebView2`, выбор раз на запуск;
неиспользуемый удаляется после первого кадра, если нет другого
экземпляра (`App.SweepUnusedWebViewProfile`); опции
`--disable-component-update --disable-background-networking`, tracking
prevention выключен — браузер ничего не качает.

**`state.json`** (`JsonAppStateStore`, record `AppState`):
- `Session` — `LastPath` (`NavigationStop?`), `LastPlace` (`ListPlace?`:
  главная строка, по `ListPlace.Neighbors` = 8 строк с каждой стороны и
  первая на экране), `ExpandedPaths` (только **видимо** раскрытые —
  `FolderTreesController.CollectExpanded` по `PanelView.Rows`: раскрытое
  под свёрнутой строкой живёт в сеансе, но не пишется, иначе
  восстановление раскроет свёрнутого родителя),
  `ViewMode`, `IsPreviewVisible`, `PreviewWidth`, `IsFoldersVisible`
  (убрана — колонка и сплиттер в 0, `FoldersWidth` ждёт),
  `IsBookmarksExpanded`, `RecentPaths`, `ManualViewModes` (легаси, для
  миграции в `folders.json`), `BookmarksHeight`, `FoldersWidth`,
  `LayoutWindowWidth` / `LayoutWindowHeight` — окно, долей которого были
  три размера панелей.
  - `PaneSizes.Restore` (Core/Layout, тест): окно того же размера — те же
    пиксели, другого — та же доля, списку не меньше 240 px; зовётся при
    `Loaded` и ещё раз при `ContentRendered` (в `Loaded` развёрнутое окно
    ещё в обычных границах). Потолок перетаскивания — окно минус резерв
    соседа (`MainViewModel.PaneCeiling`).
  - В файл уходит **пара, выставленная пользователем** (`_saved*`), не
    размеры с экрана; перебазирование (`RebasePaneSizes`) — только
    перетаскиванием разделителя, иначе круг монитор → ноутбук → монитор
    возвращал панели не туда. Легаси без размера окна — как было, но не
    шире «окно минус резерв». Контроль: `State loaded`,
    `Pane sizes: window WxH, set at WxH; …`, `State written` (только
    когда пара изменилась).
- `Favorites` — закладки в порядке пользователя (`MoveBookmark`);
  стандартные не здесь; пропавший путь остаётся (`IsMissing`).
- `Window` — `WindowGeometry`; обратно через `WindowPlacement`
  (`Core/Layout/`): меньше 320×240 — отбрасывается, позиция прижимается к
  виртуальному экрану с полосой заголовка.
- `Settings` — `AppSettings`: `RestoreLastFolder`, `ShowHidden`,
  `ShowSystem`, `ConfirmRecycle`, сортировка, метрики видов, чекбоксы
  закладок, `TreeKeyboardNavigates`, `TreeScrollsSideways`,
  `DefaultViewMode`, `ShowDebugMenu`, `LogActions`, `LogPaths`,
  `VisibleFirstLoading`, галерея, контекстное меню
  (`ShellExtensionsEnabled`, `BlockedShellExtensions`,
  `KnownShellEntries`, `HiddenContextMenuItems`).

`AppState.Version` (`AppState.CurrentVersion`, сейчас 1) поднимается,
когда изменение потерялось бы или прочиталось бы неверно старой сборкой.
Файл новее сборки **не перезаписывается** (`JsonAppStateStore.Save`),
читается как обычно; без поля — текущая форма. Правило «кто пишет, когда
версий несколько» — BACKLOG, «Сборка и поставка» (AD11). Миграционного
слоя **нет**: `Load` ловит исключение → `new AppState()` (до 1.0).

**`folders.json`** — база параметров папок (Z1):
`Folders/FolderSettingsBook` (Core, тест), записи `FolderRecord` (путь,
дата создания UTC, день последнего захода, закреплённые вид и `Sort`;
поля необязательные, новое — без смены версии), ключ — путь без регистра
и хвостового разделителя. Хранятся только папки, которым есть что помнить;
`DefaultCapacity` = 3000, вытеснение по дню захода.
`IFolderSettingsStore` → `JsonFolderSettingsStore` (Platform, тот же
`InstanceLock`): своя `Version` = 1, файл новее сборки не перезаписывается,
запись через `.tmp` + `Move`, `--yield` не пишет. Читается синхронно в
`RestoreState` (тысячи строк — миллисекунды), пишется из `WriteStateNow`
по `_foldersDirty` тем же дебаунсом. На приходе дата создания читается на
пуле с листингом (`IFileSystem.GetCreationTimeUtc`), `Touch` двигает день
захода; папка **без** записи проверяет `AdoptCandidates` (та же дата и
том, другой путь) через `DirectoryExists`, и ровно один пропавший
усыновляется (`Adopt`) — закрепление находит папку, переименованную
снаружи; две копии с одной датой (robocopy `/DCOPY:T`) — ничего. Свои
переносы — `Follow` по `PathFollowing`. Миграция:
`SessionState.ManualViewModes` (до 128 закреплений 0.4.x) читаются один
раз в книгу; глобальный `SessionState.ViewMode` не переносится —
умолчание стало настройкой (решение 2026-09-23).

**Номер сборки** (AH) — четвёртое число `FileVersion`,
`BuildInfo.BuildNumber`; строка версии —
`v0.4.1-beta.137 D, 96e5e, 22.09.26`. Счётчик —
`src/Wander.App/build-number.txt`, вне гита, на машину; цель
`StampBuildNumber` в `Wander.App.csproj` крутит его на обычной сборке
(кроме `-p:WanderRelease=true`, дизайн-сборок IDE и `*_wpftmp`),
`version.ps1` сбрасывает в 0. У релиза (`release.yml`, `publish.ps1` без
`-Install`) и CI номера нет — `BuildInfo.Line` в три числа.
`LastRunVersion` = `BuildInfo.Version`: кэш миниатюр сбрасывает смена
версии, не пересборка.

**`logs\session-*.log`** — `FileLogger`; в тестах `NullLogger`. Ротация —
`LogFolders.Sweep` при старте на пуле: 200 последних `session-*` /
`journal-*`, 20 `crashes\crash-*.zip`, правило — `Core/Logging/LogRetention`
(тест); текущий лог и чужие файлы не трогаются. Повторы `WARN` / `ERROR`
схлопывает `Core/Logging/RepeatCollapser` (подпись — уровень, сообщение,
тип и первый кадр исключения): первое пишется, та же подпись в течение
5 с считается, итог «`ERROR repeated N times over M s: сообщение`» — при
смене строки, раз в минуту и при закрытии. `INFO` не схлопывается —
хронология. `Written` поднимается на каждый вызов. Smoke `check.bat run`
пишет в `artifacts\smoke\data`. Долгие фоновые ожидания называет
`Core/Diagnostics/LongWait.WatchAsync` (тест):
`SLOW wait: что - still running after 5 s` и `SLOW done: что - took N s`
— на листинге архива (`RefreshShellAsync`), на уровне панели папок
(`WorkspaceController.ReadAsync`), в панели просмотра и на распаковке
записи.

**Журнал действий** — `Core/Logging/ActionJournal`: что видел
пользователь, словами статус-бара, с датой и временем; кнопка (`F0E3`) в
статус-баре открывает `journal-<pid>.txt` системным просмотрщиком.
Открытые папки (только приход) и операции; описание самого списка
(«элементов: 27» на каждую букву фильтра) — через `SetStatusQuietly`, в
журнал не идёт. Без повторов и пустых, до 500 строк.

**Что лог говорит о файлах.** Без своего логгера пишут через
`Core/Logging/Log` (`Info` / `Warn` / `Error`, `Log.Current`). Строка
`$"..."` через `ILogger` или `Log` идёт обработчиком `LogMessage`: каждое
значение маскируется отдельно (`LogMask.Scrub`) — граница пути точная;
`FileLogger` маскирует строку целиком ещё раз и текст исключения. Метка —
`<C:\~3fa91c\~0b2e4d.jpg>`: диск, глубина, расширение, «та же папка — та
же метка» в пределах сеанса (хэш со случайной солью на запуск); корень
диска, `shell:` и путь исходника в стеке — как есть; в кавычках (`'…'`,
`"…"`, «…») — имя. Голое имя оборачивают `Log.Path`; кавычек вокруг не
имени не ставить (версия в `Version changed`). Отключает
`AppSettings.LogPaths` (`Log.RevealPaths`). Отчёт о падении маскирует
`crash.txt` и заготовку issue тем же правилом. До `RestoreState` —
маскировано всегда. Харнесс включает `LogPaths` сразу: `assert-log` ищет
пути.

**Трасса действий** — `AppSettings.LogActions` (`Log.Details`;
`Log.Detail` через `DetailMessage` не собирает строку, пока выключено):
`Key:` (аккорд и зона; набранное — `(typed)`, пока пути маскируются),
`Click:`, `Menu:` (класс-обработчик `MenuItem.Click`), `Focus:`,
`Selection:` и трасса модели окна — `WS <событие>; effects: …`
(`WorkspaceController`) и `WS target: …`.

**`thumbs\*.png`** — `ThumbnailDiskCache` (Platform): имя SHA-256 от
«путь + mtime + размер» (изменился — другое имя); запись во временный +
`File.Move(overwrite)`; ошибки диска глотаются; подрезка по времени
обращения раз в 64 записи и при уменьшении лимита, до 80 % бюджета, в
фоне; лимиты — `IIconProvider.ConfigureCache(ThumbnailCacheOptions)`.

**`crashes\*.zip`** — `CrashReporter`; `App.HookCrashLogging`:
`DispatcherUnhandledException` (лог + репорт, `Handled = true`),
`AppDomain.UnhandledException` (флаш),
`TaskScheduler.UnobservedTaskException`. Репорт — заготовка GitHub issue +
локальный zip; **ничего не уходит без действия пользователя**; на ту же
ошибку — не чаще раза в минуту (`App.ShouldOffer`); под `--smoke` выключен.

**Стенды в меню «Отладка»**: «Операция» (AI1, `Diagnostics/DebugOperation`)
— поддельная операция на восемь файлов по 20–80 МБ, 2–5 с каждый, без
диска, через настоящие `OperationTracker` и окно прогресса: «Ровно»,
«Ошибка на 4-м», «Отмена посреди», «Три сразу». Занять файл — отладочные
действия (AI2, «Свои действия и групповое переименование»).

## Настройки — окно и правила

`Views/SettingsWindow` — тонкий вид над `SettingsViewModel`: страница —
подкласс `SettingsCategoryViewModel` и `DataTemplate` по его типу;
изменения применяются сразу, «Отмена» откатывает к снимку при открытии;
окно по умолчанию 880×640. Правила раскладки и подписей (2026-09-25, по
гайдлайнам Microsoft, GNOME HIG и Win UX Guide «Check boxes»):

1. **Страница — часть Wander, как её встречает человек**, не механизм и
   не «разное»: «Основного» и «Дополнительно» нет. Три группы слева
   отбивкой (`StartsCluster`): что на экране (Папки и закладки, Список
   файлов, Вид — под ним Размеры и Галерея), что делается с файлами
   (Файловые операции — под ними Контекстное меню, Действия, Программы,
   Оценки), служебное (Кэш и память, Клавиатура, Отладка и сброс).
   Страница о части другой — под ней с отступом (`IsNested`). Пункт — на
   страницу той части, о которой он (оценки ставятся в любом виде — не на
   странице галереи). Не нашлось места — повод обсудить, не «Прочее».
2. **Группа — заголовок-контекст** («При запуске», «Подтверждения»,
   «Совпадения имён при копировании и перемещении»): пункт его не
   повторяет; заголовок говорит, когда пункт действует («Изменения от
   других программ», не «Обновление»); у страницы из одной группы —
   название страницы. Один из нескольких — переключатели по строке на
   вариант, не флажок с угадываемым «выключено»; что относится к варианту
   — в его строке. Поле, уточняющее флажок, — внутри фразы: «Включать
   галерею, если снимков в папке больше [50] %».
3. **Флажок** — фраза про включённое состояние, без отрицания; в группе
   — один оборот. Отмечено — включено везде, в таблицах колонка «Вкл»;
   хранится как угодно (`IsShown` над `IsHidden`, `ShowConsole` над
   `HideConsole`).
4. **Поле** — существительное, единица через запятую («Высота строки,
   px»); диапазон и стандартное — в подсказке.
5. **Страница понятна без подсказок** (2026-09-28): что пункт делает,
   говорят подпись и группа. Строка серым под пунктом (`Note`, `CheckNote`)
   — только цена изменения, которую надо увидеть до действия (спутники,
   `.pp3`, кэш, память и Temp, пути в логе). Подсказка — только то, чего
   на экране нет (обрезанный текст — `TrimmedToolTip`). Остальное — GUIDE:
   F1 и «?» открывают раздел страницы на сайте (`GuidePage`, шаг «сайт»).
6. **Зависимый пункт** — под родителем с отступом (`Sub`), выключен с
   ним; подсказка видна и у выключенного.
7. **Порядок**: частое выше; сброс страницы — своей группой внизу
   (`ResetGroupTitle`, отбивка шире), не у кнопок таблицы; справка и
   отладка — в конце списка.
8. **Ширина**: страница умещается в 660 px без прокрутки вбок. Таблица —
   строка в одну линию, до четырёх колонок, длинная ячейка — многоточие
   (`CellText`), целиком — в подсказке; заголовок сортирует, кроме
   таблицы с порядком меню; остальное о строке — в форме под таблицей;
   превью — рядом с полями или под ними (`WrapPanel`). Две строки в ячейке
   вдвое урезают видимое и убирают сортировку.
9. **Таблица и форма** (`SettingsGrid`): выделенная строка — `RowSelected`
   при своём цвете текста, не гаснет с клавиатурой в форме; форма —
   карточка, шапка которой — сама строка (тот же голубой, название,
   кнопки строки), строка прокручивается в вид
   (`ActionsGrid_ShowSelected`). Поиск — над таблицей, добавление — кнопкой
   под ней. Программа — список используемых и выбор файла; чего нет —
   красная строка, сохранение не запрещается.
10. **Поиск и фильтр** — `Controls/FilterBox` (и вне настроек): подсказка
    в пустом, крестик в непустом, `Esc` очищает непустое и до окна не
    доходит (в диалоге `Esc` — «Отмена», фильтр не должен уносить правки;
    так в Rider); пустое отдаёт `Esc` окну. Окно, которое берёт `Esc`
    раньше детей (окно поиска), так и делает.

У `ComboBox` `DisplayMemberPath` / `SelectedValuePath` — атрибутами на нём,
раньше `SelectedValue`: заданные стилем, приходят позже значения, и список
стоит пустым. Вёрстку страниц без экрана проверяет стенд: страница в
`HwndSource` без `WS_VISIBLE` за экраном (без источника `DataGrid` не
раскладывает колонки; ширина — в физических пикселях) → PNG.

- **«Клавиатура»** — таблицы GUIDE (`HotkeyCatalog`, `SharedSizeGroup` —
  одна колонка жестов на все группы), только чтение; жесты литералами,
  описания из ресурсов. `HotkeyCatalog.Filter` ищет по жесту и описанию,
  пробелы в жесте снимаются («ctrl+q» → «Ctrl + Q»).
- **«Память под картинки»** — `AppSettings.PictureMemoryMb` (0 — 1/16
  памяти машины; 128…65536); лимиты кэша — сразу (`OnSettingsChanged` →
  `ApplyThumbnailCacheSettings`); диск миниатюр — 256 МБ по умолчанию.
- Служебные файлы корня тома (`pagefile.sys`, `hiberfil.sys`,
  `swapfile.sys`, `DumpStack.log.tmp`) — той же настройкой, что служебные
  папки (`SystemRootFolders`).
- «Помощь» — `CrashReporter.GuideUrl` (`lekta.github.io/wander/guide/`).

### Новая настройка — по шагам

Порядок один для пункта и для новой страницы; правила — выше.

1. **Нужна ли.** Сначала поведение без настройки; настройка — когда
   привычек две и обе настоящие (`TreeKeyboardNavigates`) или у выбора
   есть цена для человека (кэш, `.pp3`).
2. **Страница и группа** — правила 1, 2, 7; не нашлось — вопрос человеку.
3. **Контрол и подпись** — правила 2–6. Хранится как удобнее коду,
   показывается включённым: `HideSystemRootFolders` в `AppSettings`,
   `ShowSystemRootFolders` во вьюмодели.
4. **Код, по цепочке**:
   - `AppSettings` — свойство с умолчанием, в комментарии — почему такое;
   - `SettingsViewModel` — поле и свойство (число — `ClampInt`), строка в
     `ApplyFrom` и `ToRecord`; производное — `Raise` из сеттера;
   - потребитель — ветка в `MainViewModel.OnSettingsChanged`: сразу и в
     обе стороны («Отмена» и сброс — тем же `ApplyFrom`); производное — в
     ранний выход, иначе второе сохранение;
   - правило — чистая функция Core с тестом, значение параметром: Core
     настроек не читает (`Log.Details`, `AppPaths.UseSystemTemp`,
     `ThumbnailCacheOptions`);
   - разметка — строка в шаблоне страницы `SettingsWindow.xaml` стилями
     `Row`, `Sub`, `GroupTitle`, `Note` / `CheckNote`, `FieldRow`; число —
     `UpdateSourceTrigger=Explicit` (`NumericField`), текст —
     `NumericField.Enabled="False"`, поиск — `FilterBox`, длинный текст —
     `TrimmedToolTip`;
   - текст — `Strings.resx` и `Strings.Settings.cs`.
5. **Новая страница** — подкласс `SettingsCategoryViewModel` с `guide:`,
   `DataTemplate`, место в `SettingsViewModel.Categories`
   (`startsCluster`, `nested`). Выбранная строка и текст поиска — свойства
   страницы, не владельца (каждое изменение владельца сохраняется);
   страница со строкой владельца следит за его коллекцией
   (`ActionsSettingsCategory`).
6. **Доки**: руководство — строка в очередь PLAN (при встраивании: пункт
   с подписью из окна в «Справочник» → «Настройки», ссылка из текста о
   поведении, раздел для `F1`; до него `F1` — на существующий раздел); CHANGELOG;
   DONE, «Настройки и справка»; чек-лист QA, «Настройки»; сюда — только
   новое правило или механизм.
7. **Проверка**: `check.bat run` — строки, ключи XAML, цели `F1`, smoke;
   страница в 660 px — стендом или глазами; «Отмена» и «Сбросить все
   настройки» возвращают значение. Имя свойства вьюмодели — контракт шага
   `settings` харнесса.

## Строки интерфейса

`Resources/Strings.resx` (встроенный ресурс), `Resources/Strings.cs` —
одна строка на ключ поверх `ResourceManager`, аксессор разложен по
областям partial-классами (`Strings.Settings.cs` …); XAML —
`{x:Static res:Strings.Key}`; ненайденный ключ возвращает себя. Класс
руками, не `MSBuild:Compile`: markup-компилятор WPF собирает XAML во
временном проекте (`*_wpftmp.csproj`), куда designer-файл из `obj/` не
попадает. Второй язык — `Strings.<culture>.resx` (BACKLOG). **Граница
слоёв**: Core отдаёт пользователю подписи меню (`ContextMenuCatalog`) и
причину отказа drop'а (`PathSafety.FormatReason`) через `ITextSource`
(`AppTextSource` в App); Core хранит ключи. Без источника `Text.Get`
возвращает ключ — режим тестов (`ContextMenuCatalogTests`);
`FormatReason` принимает `ITextSource?` параметром.

Проверки — шаги `check.bat`: `check-strings.ps1` — ключи в коде и XAML
против `Strings.resx` в обе стороны (и константы `OperationVerbs`);
`check-resources.ps1` — каждый `{StaticResource}` / `{DynamicResource}` и
`FindResource` в App определён каким-то `x:Key` (без областей
видимости; неиспользуемые печатаются, не роняют).

**Лог и журнал — разные вещи** (2026-09-25), в строках и в GUIDE не
путаются: **лог** («лог сеанса», «логи») — технический файл
`logs\session-*.log` (`FileLogger`), для разбора ошибок, словами кода;
**журнал** («журнал действий») — `ActionJournal`, что видел пользователь,
словами строки состояния. Слово «журнал» про лог не пишется.

## Тесты

xUnit, `tests/Wander.Core.Tests`, **только Core**; UI и Platform — smoke.
Если тест не пишется — логика не в том слое.

| Фейк | Что |
|---|---|
| `FakeFileSystem` | `Directories` (`HashSet`), `Files` (`Dictionary<string, byte[]>`), `CallLog`; `RenameFailures` роняет путь — для откатов |
| `FakeConflictResolver` | `batchOverride` на всё, `perItem`-очередь; `ResolveAllCalls` (размер каждого вызова) / `Conflicts` (что показали) |
| `FakeRecycleBin` | поверх `FakeFileSystem`, `Send` / `Restore`, `CallLog` |

`CallLog` — «сходили ровно туда и ровно столько раз».

Правила: **локатор — не канал доставки фейков** (конструктором; xUnit
параллелен, локатор один на процесс; регистрирует и `Reset()` только
`ServiceLocatorTests`, и только `IFileSystem`; `ITextSource` не
регистрирует никто — `TextFallbackTests`); пути case-insensitive; никакого
реального I/O и времени (`NullLogger`); никаких гонок как утверждения
(детерминированная синхронизация; тест, проходящий под нагрузкой, —
сломан); новая абстракция в Core → фейк рядом.

## Осознанные границы

Только Windows (Core платформонезависим по дисциплине). Нет DI и
MVVM-фреймворка. Нет телеметрии, аналитики, сети. `PublishTrimmed` не
включать. Только `.lnk` (symlinks / junctions не создаются и не
разрешаются; обход может зациклиться). Long paths, UNC-таймауты, FAT32 —
сырые исключения (BACKLOG). Undo не персистится. Тесты только Core.

## Как добавлять новое

1. Платформенная возможность — интерфейс в Core → реализация в Platform →
   регистрация в `PlatformBootstrapper`.
2. Операция меняет файлы — `SystemPathGuard`, лог, `IUndoableAction`,
   подтверждение с Cancel по умолчанию, если деструктивна.
3. Логика распухла в `MainViewModel` — контроллер; без WPF — **в Core** под
   тесты (так появились `BatchExecutor`, `ClipboardController`,
   `SearchController`, `SelectionController`, `PreviewController`,
   `NavigationController`, `FolderSession`).
4. Новый сайдкар — строка в `CompanionResolver.Default`; разбор содержимого
   (как `Pp3Sidecar`) — только если есть что показать в футере.
5. Настройка или страница настроек — «Настройки — окно и правила»,
   «Новая настройка — по шагам».
6. Перед коммитом — `tools\check.bat` (`run` — со smoke, `format` — пишет).
