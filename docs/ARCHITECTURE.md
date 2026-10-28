# Wander — архитектура

Слои, композиция, хранение и логи, правила настроек и строк, тесты,
границы. Механизмы — по областям, у каждой свой док («Области» ниже);
поведение и пределы — DONE; задачи — PLAN / BACKLOG / TECHDEBT; история —
CHANGELOG и гит.

## Слои

| Проект | TFM | Роль |
|---|---|---|
| `Wander.Core` | `net10.0` | Логика и абстракции. Не знает про Windows и UI. |
| `Wander.Platform.Windows` | `net10.0-windows10.0.19041.0` | Реализации интерфейсов Core: Win32, Shell COM, WinRT, `System.IO`. |
| `Wander.App` | `net10.0-windows10.0.19041.0` | WPF: окно, ViewModel'и, диалоги, конвертеры. |
| `Wander.Core.Tests` | `net10.0` | xUnit, **только** Core через фейки. |

TFM `10.0.19041.0` — ради WinRT-проекций без новых пакетов; цель — Windows
10 (19041 = 2004), вызовы WinRT обёрнуты глухим `catch`.

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
не спрашивает пользователя, живёт в Platform на не-WPF API (`RawThumbnail`,
`PdfPageImage`, `ImageConvertAction` — WinRT вместо WPF-декодера).
`Preview/ImageDecoder` в App правомерно: его выход — `BitmapImage`.

```
src/
├── Wander.Core/
│   ├── Actions/        свои действия: каталог и пресеты, применимость, командная строка, запуск
│   ├── Appearance/     UiTheme, арифметика тонов (контраст, подъём, читаемый цвет)
│   ├── Companions/     спутники: правила, группы, сайдкары оценок
│   ├── Diagnostics/    PerfLog, LongWait, BuildInfo, кто держит файл
│   ├── FileSystem/     IFileSystem, операции и батчи, конфликты, буфер, сторож, гарды,
│   │                   занятые файлы, временные ошибки, запись листинга и сортировка
│   ├── Folders/        вид и порядок папки, книга папок, desktop.ini
│   ├── Icons/          контракты значков и метаданных, форматы картинок, превью из RAW
│   ├── Imaging/        хелперы отсмотра, TGA, DIB, бюджет памяти картинок
│   ├── Layout/         геометрия: плитки, сетка, области окна, размеры панелей, drag у края
│   ├── Listing/        сессия папки: приход, сверка строк, преемник, место строки, фильтр
│   ├── Localization/   ITextSource, UiLanguage
│   ├── Logging/        ILogger, Log, маскирование путей, журнал действий, ротация
│   ├── Menu/           контекстное меню: правила, каталог, настройки, меню броска
│   ├── Navigation/     история, адрес, MRU, стартовая папка, следование за путём,
│   │                   названия закладок
│   ├── Operations/     OperationTracker, запись операции, повтор, заявки на пути, скорость,
│   │                   ожидание занятых
│   ├── Panels/         панели папок: состояние, строки, клавиши, сверка уровней
│   ├── Persistence/    AppState, AppSettings, AppPaths, временные файлы
│   ├── Preview/        маршруты панели просмотра и разбор форматов
│   ├── Rename/         групповое переименование
│   ├── Search/         маска, выражение, обход, экстракторы, кэш текста
│   ├── Shell/          контракты оболочки: namespace, меню, ярлыки; архивы, извлечение;
│   │                   устройства: путь, импорт
│   ├── Undo/           UndoService, IUndoableAction
│   ├── Workspace/      модель окна: состояние, события, правила, цель, эффекты
│   └── ServiceLocator.cs
│
├── Wander.Platform.Windows/
│   ├── Diagnostics/    Restart Manager
│   ├── FileSystem/     System.IO, свой цикл копирования, корзина, буфер, сторож, тома,
│   │                   приход и уход томов, известные папки
│   ├── Icons/          значки и миниатюры, дисковый кэш, EXIF, проба резкости
│   ├── Imaging/        кодировщик картинок (WinRT)
│   ├── Logging/        FileLogger, чистка логов
│   ├── Persistence/    state.json, folders.json, model-views.json, bookmark-titles.json, InstanceLock
│   ├── Power/          запрос «не спать» на время операции
│   ├── Preview/        карточка программы, проба кодеков
│   ├── Search/         IFilter
│   ├── Shell/          запуск, ярлыки, namespace (корзина, архивы, устройства WPD), меню оболочки,
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
    ├── Highlighting/   *.xshd, редактор кода с цветами по теме
    ├── Menu/           ContextMenuFactory, ShellMenuCache
    ├── Preview/        раскодирование и отрисовка для панели просмотра
    ├── Resources/      Strings*.resx и аксессоры, палитра (общая и две темы), StockControls, MenuStyles
    ├── Util/           форматы чисел и времени, ListVisuals, SelectionController, тема и язык
    ├── ViewModels/     вьюмодели настроек и строк, базовые типы
    ├── Views/          виды и окна
    ├── MainViewModel.cs — при окне, не в ViewModels/ (WINDOW, «Окно и его контролы»)
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
date   : 2026-10-27
commit : 5ad70fc

-- Wander.Core: levels --
  0: (root), Appearance, Imaging, Layout, Localization, Logging, Panels
  1: Diagnostics, Icons, Operations
  2: Undo
  3: FileSystem
  4: Companions, Folders, Navigation, Preview
  5: Actions, Rename
  6: Persistence, Search
  7: Listing, Shell
  8: Menu
  9: Workspace

-- Wander.Platform.Windows: levels --
  0: Diagnostics, Imaging, Logging, Persistence, Power, Preview, Search, Shell
  1: FileSystem, Icons
  2: (root)

-- Wander.App: levels --
  0: Menu, Resources
  1: Util
  2: Diagnostics, Highlighting, Preview, ViewModels
  3: Conflict, Converters
  4: Controls, Dialogs, DragPreview
  5: Controllers
  6: Views
  7: (root)

-- namespace <> folder mismatches --
  (none)
```
<!-- deps:generated:end -->

### Когда `IFileSystem`, а когда `System.IO`

**Через `IFileSystem`** — всё, что пользователь может отменить, что
обязан подменить тест, и всё, что перечисляет папки: операции, листинг,
сайдкары, перепись. **Напрямую `System.IO`** — байты одного выбранного
файла ради раскодирования, когда результат — картинка или текст на экране,
а не решение логики: так читает **только `Wander.Core/Preview/`**, тесты
туда не ходят. Новый `File.` / `Directory.` в Core вне `Preview/` —
кандидат в `IFileSystem` либо расширение списка с записью здесь.

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
`CompanionResolver`, `CompanionMetadataService`. Тесты в локатор не ходят —
фейки конструкторами.

### Обязательные и необязательные сервисы

Регистрация одна и безусловная: ветка «не зарегистрирован» либо описывает
реальный режим, либо недостижима. **Сервис, который где-то читается
`Get<T>()`, обязателен везде**: `IFileSystem`, `IShellLauncher`,
`IAppStateStore`, `IRecycleBin`, `IShortcutService`, `IIconProvider`,
`CompanionResolver`, `UndoService`, `OperationTracker`,
`FileOperationService`, `IDialogs` (App, регистрируется в `App.OnStartup`
рядом с `ITextSource`). Отсутствие — сломанный бутстраппер: падение на
старте честнее работы вполсилы. **Необязательные** читаются `TryGet<T>()`,
у каждого внятный ответ «нет»:

| Сервис | Чего не будет |
|---|---|
| `IShellNamespace` | закладки на Корзину, `shell:`-пути |
| `IDeviceContent` | телефонов и камер в «Компьютере» и импорта с них |
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
включить** — подписки с побочными эффектами (`Settings.PropertyChanged`,
`Workspace.StateChanged` со сменой раскрытого → запись состояния) ставятся
в конце, **после** `RestoreState()`; флага «идёт восстановление» нет,
запись от начальной навигации гасит `_stateSaveTimer.Stop()` в конце
`RestoreState`.

### Изменяемая статика

Под `lock` — только локатор: xUnit гонит классы параллельно,
`ServiceLocatorTests` пишет, пока соседи читают через `ITextSource.Text` →
`TryGet`. Остальное оставлено сознательно:

| Место | Почему |
|---|---|
| `PerfLog._log`, `_windowStartMs` | диагностика, под `_lock`, тестами не читается |
| `Log.Details`, `Log.RevealPaths` | `volatile`, пишет только `MainViewModel` из настроек (Core настроек не знает — как `AppPaths.UseSystemTemp`); тесты Core работают на умолчаниях |
| `IconImageCache` | под `_lock`, с потолком; второй владелец не нужен, пока провайдер — синглтон |
| `CrashReporter._offeredThisSession` | худшее у гонки нефатальных — второй диалог; fatal-путь флаг игнорирует |
| `UiStallWatch._worker`, `HighlightingCatalog._registered` | once-флаги (второй под `_lock`) |
| `MagnifierCursor._cached`, `ShellHandlerRegistry._searchPath` | ленивые неизменяемые: гонка строит то же значение дважды |
| `SystemPathGuard._userFolders` | `Lazy`, папки профиля из `IKnownFolders` (`TryGet`) при первом вызове гарда — после бутстраппера; в тестах — только `Environment` |
| `PictureCache._frames` | доля кадров в бюджете картинок (`PictureMemory`), общая на все панели просмотра — только UI-поток; предел пишет `MainViewModel` из настроек |
| `StartFolder.Asked` | пишется один раз до окна (`App.OnStartup`, харнесс — `Override` до него); тесты трогают её в одном классе и возвращают |

`SystemIconProvider`: `_cache` / `_missing` / `_thumbnailOrder` — поля
**экземпляра** под `_lock`; статика там — только lock-объекты.

### Как потребляются сервисы

Регистрация до первого потребителя, дальше словарь заморожен; горячей
подмены и плагинов нет. **Экземпляры разрешают сервисы один раз в
конструкторе в readonly-поля** — список зависимостей в одном месте,
будущие параметры конструктора. **Статические хелперы** (`Text`, `Log`,
`IconConverter.Load`, `SystemIconProvider.ResolveShortcut`) ходят в
локатор на каждый вызов: поиск по словарю не виден на фоне шелла и диска.
**Новых ленивых статических кэшей сервисов** (`_x ??= Get<X>()`) без строки
в таблице выше не заводить: ещё одна статика и риск обращения до
бутстраппера.

## Области

Док на область; новая сессия читает ARCHITECTURE и док задетой области.

| Док | Что внутри |
|---|---|
| FILEOPS | операции и батчи, undo, корзина, занятые пути, прогресс и окно операции, конфликты, защита; надёжное копирование: запись операции, повтор, свой цикл, проверка, съёмные диски, извлечение; корзина, архивы, телефон и камера как папки, извлечение; спутники |
| WINDOW | модель окна (`Core/Workspace`, `Core/Panels`) и исполнитель, навигация и панели, контролы и границы вью, диалоги, клавиатурные области, цвета и тема, плитки и `VirtualizingWrapPanel`, значки и миниатюры, отзывчивость, замеры |
| LISTING | сессия папки и посадка строк, выделение и каретка, буфер, drag & drop, сторож, поиск, галерея и оценки |
| MENU | контекстное меню и шелл, меню «Действия», свои действия, групповое переименование |
| PREVIEW | панель просмотра, маршруты и форматы, зум и полный экран, медиа, миниатюры мимо шелла, хелперы отсмотра |

Где лежат механизмы правил CLAUDE.md: «список не дёргается» — LISTING,
«Запись оценки не пересобирает папку»; «выделенный файл не уходит с
экрана» — LISTING, «Сессия папки»; «новая операция обязана» — FILEOPS,
«Файловые операции»; каждый модальный вопрос — WINDOW, «Диалоги — один
шов»; таймер — только троттл — WINDOW, «Таймеры»; отступления от «всё
откатываемо» — FILEOPS (безвозвратное удаление, временная копия записи
архива) и MENU (процесс действия не откатывается).

## Состояние и логи

Корень — `AppPaths.DataRoot` (Core, `Persistence/`): `--data-dir <путь>` →
`--portable` (`data` рядом с exe) → `WANDER_DATA_DIR` →
`%LOCALAPPDATA%\Wander`; `AppPaths.Resolve(args)` в `App.OnStartup` до
логгера, `Override` — харнессу и тестам. `LOCALAPPDATA` из среды не
читается — папку даёт оболочка. Потребители: `FileLogger`,
`JsonAppStateStore`, `ThumbnailDiskCache`, `CrashReporter`, `PreviewPane`
(WebView2). Источник корня — в заголовке сессии (`Data root: …
(arg|portable|env|override|default)`).

**Две копии на одних данных**: установленная (`C:\Programs\Wander`) и
Debug из Rider делят `%LOCALAPPDATA%\Wander` — так задумано. Кто пишет
`state.json`, решает `InstanceLock` (Platform): экземпляр без ключа держит
именованный мьютекс `Local\Wander.state.<хэш корня>`; экземпляр с
`--yield` (`AppPaths.Yields`, профиль `launchSettings.json`), увидев
владельца раз, до конца сеанса не пишет (`IAppStateStore.IsReadOnly`,
«— настройки не сохраняются» в заголовке). Хэш корня — харнесс и
установленная копия друг друга не видят. Кэш миниатюр и профиль WebView2
общие. Профиль — `AppPaths.WebView2`: `<tmp>\WebView2` при
`UseSystemTemp`, иначе `<DataRoot>\WebView2`, выбор раз на запуск;
неиспользуемый удаляется после первого кадра, если нет другого экземпляра
(`App.SweepUnusedWebViewProfile`); опции `--disable-component-update
--disable-background-networking`, tracking prevention выключен — браузер
ничего не качает.

**`state.json`** (`JsonAppStateStore`, record `AppState`): `Session`
(последняя папка и место, раскрытое, вид, панели, MRU), `Favorites`
(закладки в порядке пользователя; пропавший путь остаётся, `IsMissing`),
`Window` (`WindowGeometry`; обратно через `WindowPlacement`, Core/Layout:
меньше 320×240 — отбрасывается, позиция прижимается к виртуальному
экрану), `Settings` (`AppSettings`). Ловушки:
- `ExpandedPaths` — только **видимо** раскрытые
  (`FolderTreesController.CollectExpanded` по `PanelView.Rows`): раскрытое
  под свёрнутой строкой не пишется, иначе восстановление раскроет
  свёрнутого родителя.
- Размеры панелей — доли окна `LayoutWindowWidth` / `Height`:
  `PaneSizes.Restore` (Core/Layout, тест) — окно того же размера — те же
  пиксели, другого — та же доля, списку не меньше 240 px; зовётся при
  `Loaded` и ещё раз при `ContentRendered` (в `Loaded` развёрнутое окно
  ещё в обычных границах). В файл уходит **пара, выставленная
  пользователем** (`_saved*`), не размеры с экрана; перебазирование —
  только перетаскиванием разделителя, иначе круг монитор → ноутбук →
  монитор возвращал панели не туда. Контроль: `State loaded`,
  `Pane sizes: …`, `State written`.
- `AppState.Version` (`CurrentVersion`, сейчас 1) поднимается, когда
  изменение потерялось бы или прочиталось бы неверно старой сборкой. Файл
  новее сборки **не перезаписывается**, читается как обычно; без поля —
  текущая форма. Правило «кто пишет, когда версий несколько» — BACKLOG,
  «Сборка и поставка». Миграционного слоя **нет**: `Load` ловит исключение
  → `new AppState()` (до 1.0).

**`folders.json`** — база параметров папок: `Folders/FolderSettingsBook`
(Core, тест), записи `FolderRecord` (путь, дата создания UTC, день
последнего захода, закреплённые вид и `Sort`; поля необязательные, новое —
без смены версии), ключ — путь без регистра и хвостового разделителя.
Хранятся только папки, которым есть что помнить; `DefaultCapacity` = 3000,
вытеснение по дню захода. `IFolderSettingsStore` → `JsonFolderSettingsStore`
(Platform, тот же `InstanceLock`, своя `Version`, запись через `.tmp` +
`Move`, `--yield` не пишет). Тем же образцом — `model-views.json`
(`IModelViews` → `JsonModelViewStore`): ракурс каждой повёрнутой модели,
до 4000; `bookmark-titles.json` (`IBookmarkTitleStore` →
`JsonBookmarkTitleStore`): свои названия закладок, путь → название —
отдельным файлом, потому что старая сборка пишет `state.json` целиком и
стёрла бы незнакомый блок (BACKLOG AD11). Читается синхронно в `RestoreState`, пишется из `WriteStateNow`
тем же дебаунсом. На приходе дата создания читается на пуле с листингом;
папка **без** записи проверяет `AdoptCandidates` (та же дата и том,
другой путь), и ровно один пропавший усыновляется (`Adopt`) — закрепление
находит папку, переименованную снаружи; две копии с одной датой
(robocopy `/DCOPY:T`) — ничего. Свои переносы — `Follow` по
`PathFollowing`. Миграция: `SessionState.ManualViewModes` читаются один
раз в книгу; глобальный `SessionState.ViewMode` не переносится —
умолчание стало настройкой (решение 2026-09-23).

**Номер сборки** — четвёртое число `FileVersion`, `BuildInfo.BuildNumber`;
строка версии — `v0.4.1-beta.137 D, 96e5e, 22.09.26`. Счётчик —
`src/Wander.App/build-number.txt`, вне гита, на машину; цель
`StampBuildNumber` в `Wander.App.csproj` крутит его на обычной сборке
(кроме `-p:WanderRelease=true`, дизайн-сборок IDE и `*_wpftmp`),
`version.ps1` сбрасывает в 0. У релиза и CI номера нет — `BuildInfo.Line`
в три числа. `LastRunVersion` = `BuildInfo.Version`: кэш миниатюр
сбрасывает смена версии, не пересборка.

**`logs\session-*.log`** — `FileLogger`; в тестах `NullLogger`. Ротация —
`LogFolders.Sweep` при старте на пуле (правило — `Core/Logging/LogRetention`,
тест; текущий лог и чужие файлы не трогаются). Повторы `WARN` / `ERROR`
схлопывает `Core/Logging/RepeatCollapser` (подпись — уровень, сообщение,
тип и первый кадр исключения): та же подпись в течение 5 с считается,
итог «`ERROR repeated N times over M s: …`»; `INFO` не схлопывается —
хронология. Долгие фоновые ожидания называет
`Core/Diagnostics/LongWait.WatchAsync` (тест): `SLOW wait: …` / `SLOW
done: …` — листинг архива, уровень панели, панель просмотра, распаковка.

**Журнал действий** — `Core/Logging/ActionJournal`: что видел
пользователь, словами статус-бара; кнопка в статус-баре открывает
`journal-<pid>.txt`. Открытые папки и операции; описание самого списка
(«элементов: 27») — через `SetStatusQuietly`, в журнал не идёт.

**Что лог говорит о файлах.** Без своего логгера пишут через
`Core/Logging/Log`. Строка `$"..."` через `ILogger` или `Log` идёт
обработчиком `LogMessage`: каждое значение маскируется отдельно
(`LogMask.Scrub`) — граница пути точная; `FileLogger` маскирует строку
целиком ещё раз и текст исключения. Метка — `<C:\~3fa91c\~0b2e4d.jpg>`:
диск, глубина, расширение, «та же папка — та же метка» в пределах сеанса
(хэш со случайной солью на запуск); корень диска, `shell:` и путь
исходника в стеке — как есть; в кавычках — имя. Голое имя оборачивают
`Log.Path`; кавычек вокруг не имени не ставить. Отключает
`AppSettings.LogPaths` (`Log.RevealPaths`); до `RestoreState` —
маскировано всегда. Харнесс включает `LogPaths` сразу: `assert-log` ищет
пути.

**Трасса действий** — `AppSettings.LogActions` (`Log.Details`;
`Log.Detail` через `DetailMessage` не собирает строку, пока выключено):
`Key:`, `Click:`, `Menu:`, `Focus:`, `Selection:` и трасса модели окна —
`WS <событие>; effects: …`, `WS target: …`.

**`thumbs\*.png`** — `ThumbnailDiskCache` (Platform): имя SHA-256 от
«путь + mtime + размер» (изменился — другое имя); запись во временный +
`File.Move(overwrite)`; ошибки диска глотаются; подрезка по времени
обращения раз в 64 записи и при уменьшении лимита, до 80 % бюджета, в
фоне; лимиты — `IIconProvider.ConfigureCache(ThumbnailCacheOptions)`.

**`crashes\*.zip`** — `CrashReporter`; `App.HookCrashLogging`:
`DispatcherUnhandledException` (лог + репорт, `Handled = true`),
`AppDomain.UnhandledException` (флаш), `TaskScheduler.UnobservedTaskException`.
Репорт — заготовка GitHub issue + локальный zip; **ничего не уходит без
действия пользователя**; на ту же ошибку — не чаще раза в минуту
(`App.ShouldOffer`); под `--smoke` выключен.

**Стенды в меню «Отладка»**: «Операция» (`Diagnostics/DebugOperation`) —
поддельная операция на восемь файлов без диска, через настоящие
`OperationTracker` и окно прогресса: «Ровно», «Ошибка на 4-м», «Отмена
посреди», «Три сразу». Занять файл — отладочные действия (MENU, «Свои действия
и групповое переименование»).

## Настройки — окно и правила

`Views/SettingsWindow` — тонкий вид над `SettingsViewModel`: страница —
подкласс `SettingsCategoryViewModel` и `DataTemplate` по его типу;
изменения применяются сразу, «Отмена» откатывает к снимку при открытии;
окно по умолчанию 880×640. Правила раскладки и подписей (2026-09-25, по
гайдлайнам Microsoft, GNOME HIG и Win UX Guide «Check boxes»):

1. **Страница — часть Wander, как её встречает человек**, не механизм и
   не «разное»: «Основного» и «Дополнительно» нет. Три группы слева
   отбивкой (`StartsCluster`): что на экране (Папки и закладки, Список
   файлов, Вид — под ним Размеры и Галерея, Интерфейс), что делается с
   файлами (Файловые операции — под ними Контекстное меню, Действия,
   Программы, Оценки), служебное (Кэш и память, Клавиатура, Отладка и
   сброс). Страница о части другой — под ней с отступом (`IsNested`).
   Пункт — на страницу той части, о которой он. Не нашлось места — повод
   обсудить, не «Прочее».
2. **Группа — заголовок-контекст** («При запуске», «Подтверждения»):
   пункт его не повторяет; заголовок говорит, когда пункт действует. Один
   из нескольких — переключатели по строке на вариант, не флажок с
   угадываемым «выключено»; что относится к варианту — в его строке.
   Поле, уточняющее флажок, — внутри фразы: «Включать галерею, если
   снимков в папке больше [50] %».
3. **Флажок** — фраза про включённое состояние, без отрицания; в группе
   — один оборот. Отмечено — включено везде, в таблицах колонка «Вкл»;
   хранится как угодно (`IsShown` над `IsHidden`).
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
   (`ResetGroupTitle`), не у кнопок таблицы; справка и отладка — в конце
   списка.
8. **Ширина**: страница умещается в 660 px без прокрутки вбок. Таблица —
   строка в одну линию, до четырёх колонок, длинная ячейка — многоточие
   (`CellText`), целиком — в подсказке; заголовок сортирует, кроме
   таблицы с порядком меню; остальное о строке — в форме под таблицей;
   превью — рядом с полями или под ними (`WrapPanel`).
9. **Таблица и форма** (`SettingsGrid`): выделенная строка — `RowSelected`
   при своём цвете текста, не гаснет с клавиатурой в форме; форма —
   карточка, шапка которой — сама строка, строка прокручивается в вид
   (`ActionsGrid_ShowSelected`). Поиск — над таблицей, добавление —
   кнопкой под ней. Программа — список используемых и выбор файла; чего
   нет — красная строка, сохранение не запрещается.
10. **Поиск и фильтр** — `Controls/FilterBox` (и вне настроек): подсказка
    в пустом, крестик в непустом, `Esc` очищает непустое и до окна не
    доходит (в диалоге `Esc` — «Отмена», фильтр не должен уносить правки);
    пустое отдаёт `Esc` окну.

У `ComboBox` `DisplayMemberPath` / `SelectedValuePath` — атрибутами на нём,
раньше `SelectedValue`: заданные стилем, приходят позже значения, и список
стоит пустым. Вёрстку страниц без экрана проверяет стенд: страница в
`HwndSource` без `WS_VISIBLE` за экраном → PNG (QA.md).

- **«Клавиатура»** — таблицы GUIDE (`HotkeyCatalog`, `SharedSizeGroup` —
  одна колонка жестов на все группы), только чтение; жесты литералами,
  описания из ресурсов. `HotkeyCatalog.Filter` ищет по жесту и описанию,
  пробелы в жесте снимаются.
- **«Память под картинки»** — `AppSettings.PictureMemoryMb` (0 — 1/16
  памяти машины; 128…65536); лимиты кэша — сразу (`OnSettingsChanged` →
  `ApplyThumbnailCacheSettings`); диск миниатюр — 256 МБ по умолчанию.
- Служебные файлы корня тома (`pagefile.sys`, `hiberfil.sys`,
  `swapfile.sys`, `DumpStack.log.tmp`) — той же настройкой, что служебные
  папки (`SystemRootFolders`).
- «Помощь» — `CrashReporter.GuideUrl` (`lekta.github.io/wander/guide/`,
  в английском интерфейсе — `…/en/guide/`).
- **«Интерфейс»** — цветовая схема (сразу), фон галереи, язык (после
  перезапуска). Последняя в группе «что на экране».

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
     `UpdateSourceTrigger=Explicit` (`NumericField`), поиск — `FilterBox`,
     длинный текст — `TrimmedToolTip`;
   - текст — `Strings.resx` и `Strings.Settings.cs`.
5. **Новая страница** — подкласс `SettingsCategoryViewModel` с `guide:`,
   `DataTemplate`, место в `SettingsViewModel.Categories`
   (`startsCluster`, `nested`). Выбранная строка и текст поиска — свойства
   страницы, не владельца (каждое изменение владельца сохраняется);
   страница со строкой владельца следит за его коллекцией
   (`ActionsSettingsCategory`).
6. **Доки**: руководство — строка в очередь PLAN (при встраивании: пункт
   с подписью из окна в «Справочник» → «Настройки», ссылка из текста о
   поведении, раздел для `F1`; до него `F1` — на существующий раздел);
   CHANGELOG; DONE, «Настройки и справка»; чек-лист QA, «Настройки»; сюда
   — только новое правило или механизм.
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
попадает. **Языки**: русский — нейтральный `Strings.resx`
(`NeutralLanguage=ru` в `Directory.Build.props`: «en» там заставлял
`ResourceManager` отвечать на `en` русским), английский — `Strings.en.resx`
с теми же ключами, сборка-спутник `en\Wander.resources.dll` (в single-file
— внутри exe). Язык сеанса — `AppSettings.Language` (`UiLanguage`),
правило — `UiLanguages.Resolve` (Core, тест): «как в Windows» — русский
только в русской Windows, иначе английский. `InterfaceLanguage.Apply` в
`App.OnStartup` ставит `CurrentUICulture` и `DefaultThreadCurrentUICulture`
до первой строки; `CurrentCulture` (даты, числа) — региональный формат
человека. Окна берут текст при создании (`{x:Static}`), поэтому смена —
после перезапуска. Множественное число — `Text.PluralForm`: три формы
через `|` — русское правило, две — английское one / other, одна —
неизменное слово. Ссылки на сайт — по языку (`CrashReporter.GuideUrl`, у
страниц настроек `guide` / `guideEn`). Клавиши со словами в
`HotkeyCatalog` («Esc в дереве») — тоже ресурсы (`HotkeyGesture*`).
**Граница слоёв**: Core отдаёт пользователю подписи меню
(`ContextMenuCatalog`) и причину отказа drop'а (`PathSafety.FormatReason`)
через `ITextSource` (`AppTextSource` в App); Core хранит ключи. Без
источника `Text.Get` возвращает ключ — режим тестов.

Проверки — шаги `check.bat`: `check-strings.ps1` — ключи в коде и XAML
против `Strings.resx` в обе стороны (и константы `OperationVerbs`), у
каждого перевода — те же ключи, плейсхолдеры, переводы строк и формы
множественного числа; `check-resources.ps1` — каждый `{StaticResource}` /
`{DynamicResource}` и `FindResource` в App определён каким-то `x:Key`
(неиспользуемые печатаются, не роняют).

**Лог и журнал — разные вещи** (2026-09-25), в строках и в GUIDE не
путаются: **лог** («лог сеанса», «логи») — технический файл
`logs\session-*.log`, словами кода; **журнал** («журнал действий») —
`ActionJournal`, что видел пользователь, словами строки состояния. Слово
«журнал» про лог не пишется.

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
сломан); новая абстракция в Core → фейк рядом. Разбор форматов — на
байтах из билдеров, не на фикстурах.

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
5. Новый формат — PREVIEW, «Новый формат — по шагам».
6. Настройка или страница настроек — «Настройки — окно и правила»,
   «Новая настройка — по шагам».
7. Новый механизм — в док области («Области»); сюда — слой, композиция,
   правило или отступление от них.
8. Перед коммитом — `tools\check.bat` (`run` — со smoke, `format` — пишет).
