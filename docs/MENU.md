# Wander — меню и действия

Контекстное меню (Core решает, App рисует, Platform приносит чужие
пункты), меню «Действия» в шапке, каталог своих действий и их запуск,
групповое переименование. Поведение — DONE «Контекстное меню»,
«Действия: переименование группой, свои команды, конвертация»; окно
операции, undo и занятые файлы — FILEOPS.

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
  человека 2026-09-28: нужны, дубль выключается в настройках) — блоки
  меню «Вид» строка в строку; выбор — строка с `Argument` (`SetView` /
  `SetSortKey`) и галочкой из `ContextMenuTarget` (`View`, `ViewReason`,
  `Sort`, `SortPinned`). В корзине — только «Вид».
- Системное «Создать» **вливается** в наше (по глаголу `NewFolder`
  дочерней строки, не по подписи): своя «Папка» первой, `Ярлык` и шаблоны
  от шелла. «Открыть с помощью» тоже вливается; своя «Выбрать
  приложение…» — при выключенных расширениях.
- **Порядок по частоте**: сверху то, ради чего открыли; свои операции — в
  «Файл».
- **`SplitShell`** — по каноническому глаголу, не по подписи: подменю с
  ребёнком `openas` → в «Открыть с помощью»; глагол из списка
  (`PreviousVersions`) **или** динамическое подменю («Отправить»,
  «Передать на устройство» — ни один пункт не несёт глагола) → в конец
  «Файл»; остальное — верх. Эвристика (TECHDEBT). `eject` у диска —
  своя строка `Eject` второй, под «Открыть» (2026-10-19): Wander сначала
  отпускает диск (`MainViewModel.EjectAsync`, OPERATIONS «Безопасное
  извлечение»).
- **`ShellEntryKey.For(verb, header)`** — глагол, иначе нормализованная
  подпись: TortoiseGit пишет в подпись ветку, 7-Zip верхнему пункту глагол
  не публикует, но подпись там стабильна. `IsBlocked` проверяет обе формы.
- **«Программа» и «Для чего»** — из реестра (`IShellHandlerRegistry`):
  `<scope>\shellex\ContextMenuHandlers\<имя>` → CLSID → `InprocServer32` →
  версия DLL; `<scope>\shell\<verb>` — имя ключа = глагол. `HKLM` и `HKCU`
  по отдельности, не `HKCR` (склеенное — минуты против сотни мс).
  `ShellExtensionCatalog` (Core) сливает реестр и встреченное по
  `ShellEntryKey`. **Таблица — встреченное**: строка реестра без встречи
  — только не Windows и тип добавлен руками (`TrackedShellScopes`);
  одинаковые складываются (`Fold` → `Aliases`); программа Windows — «ОС»
  (`ShellHandler.IsOsComponent`). Поле над таблицей —
  `ShellExtensionFilter` (Core, тест).
- **Тип меню у ярлыка — тип цели** (`ShellScopes.MenuScopeOf`): шелл
  строит меню `.lnk` из обработчиков цели, своя у ярлыка одна —
  «Расположение файла». Встреченное пишется типом цели (папка —
  `Directory`); `.lnk`, записанный раньше, стирается при чтении
  (`SettingsViewModel.ApplyFrom`).
- **Не в меню намеренно:** «Удалить безвозвратно» (только `Shift+Del`),
  закладки, «Показать в Проводнике», `pintohomefile`. «Открыть в
  терминале» — только папка и фон.
- **`ShellContextMenu`** читает **классическое** меню (Win11 прячет его
  под «дополнительные параметры»; оттуда же «Создать» с `ShellNew`).
  `HMENU` не отдаётся в `TrackPopupMenu`, а обходится и перерисовывается
  WPF-строками. Цена: ленивые подменю будит `IContextMenu2::HandleMenuMsg`
  с `WM_INITMENUPOPUP`; owner-drawn (`dwItemData`) пропускаются с логом;
  иконки `hbmpItem` → PNG (`ShellMenuIcons`). Дубли по глаголу
  (`GetCommandString`, `GCS_VERBW`: `cut` / `copy` / `paste` / `delete` /
  `rename` / `properties` / `link` / `openas` / `copyaspath`) рисуем сами.
  «Открыть в Терминале» самого Windows Terminal (глагол — CLSID) заменён
  своим — список `ShellVerbs`.
- **`ShellMenuCache`** — последняя сессия жива: повтор по тому же
  выделению в шелл не ходит. `Acquire` про *другую* цель освобождает
  прошлую; «открыто ли меню» не считается (`ContextMenu.Closed` приходит
  **после** следующего клика); `Invalidate` только отвязывает от ключа.
- **Расширения в нашем процессе** (как в Explorer): `try/catch` с логом;
  `ShellExtensionsEnabled = false` — чужие DLL не грузятся; команда —
  **после** закрытия меню (обработчики открывают модальные диалоги).
- **Кастомизация** (`ContextMenuSettings`): мастер-выключатель, чёрный
  список, скрытые свои — как «что выключено» по строковым именам
  `MenuCommandId` (новый пункт появится сам). `KnownShellEntries` копится
  и подрезается при сохранении (`TrimKnownEntries`).
- **Меню «Действия» в шапке — третья форма тех же правил**
  (`MenuOperations` / `OperationsMenu`): `ContextMenuTarget.Place =
  MenuPlace.Header` → `ContextMenuBuilder.BuildHeader`, постоянный
  порядок, но **неприменимое не показывается**. Серое с тултипом — только
  действие без программы; `ActionApplicability` проверяет инструмент
  последним. Подпись — `CountCaption` (Core, тест): «3 CR3», «4
  изображения», «5 файлов», «7 элементов» — по расширению, группе
  `FileTypeGroups` и виду; один элемент — имя. Тот же подсчёт — у меню
  броска, плашки перетаскивания и вопроса перед удалением; где есть
  только пути — `CountCaption.Items`. Один каталог: `HideableTree` и
  галочки действуют на оба места. Шелл в шапке не опрашивается;
  перестройка — на `SubmenuOpened` (пункт без детей WPF считает кнопкой —
  в XAML заглушка). Пустое подменю билдер не создаёт.

## Свои действия и групповое переименование

Один каталог `CustomAction` (`Core/Actions/`) — строка «название · для
каких файлов · программа или встроенный обработчик · шаблон аргументов ·
режим · где показывать · объявленный выход»; хранится в
`AppSettings.CustomActions`, пресеты — из кода, сливаются по `Id`. Решает
Core, UI исполняет:

- **Применимость** — `ActionApplicability.For`: все выделенные подходят
  под `FileTypeSelector` (группа `FileTypeGroups` или маска `*.psd;*.ai`);
  пустое выделение — действия для папок, на текущую; `RequiredTool` без
  инструмента — `ToolMissing`. Частичное совпадение не считается
  (BACKLOG).
- **Командная строка** — `CommandLine.Expand`:
  `{path} {name} {ext} {dir} {paths} {list} {out} {outdir}`, кавычки ставит
  подстановка, хвостовой `\` корня удваивается; `ValidationKey` ловит
  режим «на каждый файл» с `{paths}` и наоборот, `{outdir}` без
  объявленного выхода. **Выход** — `OutputNames.Resolve`: рядом с
  источником или в выбранной папке (`RunAsync(..., outputFolder)`);
  занятое имя — `FileSystem/UniqueNames`, **номер после наибольшего**
  («clip (3)», «clip (4)» → «clip (5)»): одно правило на «оставить обе»,
  извлечение и выходы действий (решение 2026-09-16) — новое последним в
  списке, номер не переезжает на другой файл.
- **Хранение пресетов** — `ActionCatalog.ToStored` / `Merge`: у пресета в
  `state.json` только `Id`, `Enabled` и свой `Program`
  (`ActionCatalog.Override`), остальное из кода — улучшенная команда
  доезжает и до выключившего. Свои строки — целиком; своя с программой
  каталога по имени файла (`ffmpeg.exe`) при слиянии привязывается к
  инструменту (`BoundToTool`).
- **Программа строки** — `ActionCatalog.ProgramChoices`: инструменты
  каталога, программы строк, встроенные обработчики по названию
  (`ActionPresets.BuiltinTitle`); новая — файлом. `WithProgram` ставит
  программу с инструментом и видом строки: инструмент — по голому имени и
  в `RequiredTool`; встроенный — `ActionKind.Builtin`, аргументы — его
  настройки (`BuiltinProblem` через `ImageConvertOptions.Parse`). Список
  один на все строки и заменяется, только когда меняются его строки
  (иначе `ComboBox` выбирает заново). Проверка — `ProgramValidationKey`.
  Поиск своей — `IToolLocator.Locate`, как у `CreateProcess`: путь — сам
  файл, имя — `System32`, `Windows` и `PATH` процесса, без расширения —
  `.exe`.
- **Кодировщик картинок** — `Platform/Imaging/ImageConvertAction` (WinRT, ARCHITECTURE
  «Platform без WPF»): метаданные `ImageMetadata` (MetadataExtractor, и с
  RAW) пишутся номерами тегов (`Imaging/ExifTags`, GPS); JPEG без
  изменения пикселей — transcoding без пережатия.
- **Исполнение** — `ExternalActionRunner`: по одному; `OperationTracker`
  (`OperationVerbs.RunAction`); гард на папку выхода; `{list}` — временный
  файл в `AppPaths.Tmp`; код ≠ 0 — `Failed` с хвостом stderr; отмена
  убивает процесс (`IProcessRunner.WasKilled`), недописанный выход — в
  корзину, остальные `Cancelled`. **Undo — только объявленный выход**
  (`CreateAction`); процесс не откатывается — осознанно. **`{outdir}`**
  (решение человека 2026-09-29) — программе, которая называет выход сама
  и пишет поверх готового (`soffice --convert-to`): пустая папка
  `out-<guid>` в `AppPaths.Tmp` на запуск; после кода 0 `TakeOutput`
  переносит оттуда файл с именем шаблона под имя из `OutputNames.Resolve`,
  нет такого — `Failed`; папка удаляется в `finally`, после вылета — под
  `TempFiles.Sweep`. `IProcessRunner` в Core, `WindowsProcessRunner` в
  Platform: `UseShellExecute = false`, оба потока читаются на ходу (иначе
  полный пайп вешает программу), `Kill(entireProcessTree)`. Встроенные —
  `IBuiltinAction` по имени в `Program`.
- **Отладочные действия** — `CustomAction.DebugOnly`: меню показывают их
  при меню отладки (`ContextMenuTarget.ShowDebug`), таблица настроек —
  никогда, в `state.json` не попадают. `HoldFileAction` (Core) держит
  выделенный файл `FileShare.None` 5 или 30 с: занятость настоящая —
  `PathClaims`, часы, отказ другой операции, Restart Manager с Wander в
  держателях.
- **Групповое переименование** — `Core/Rename/`: `RenameRules` (найти /
  заменить, шаблон, регистр — фиксированный порядок; расширение — только
  регистр; счётчик до `MaxCounterWidth` цифр) → `RenamePlanner.Preview` —
  чистая функция от правил, элементов и `RenameContext` (`exists`,
  спутники, дата съёмки — `[X]` читает EXIF на пуле): «было → станет»,
  дубликаты в пачке, совпадения снаружи; имя, которое пачка сама
  освобождает, — не совпадение. `BatchRenameGate` — два и более **одного
  вида**. Применение — `FileOperationService.RenameMany`, **двухфазный**:
  член, чьё имя занято поздним членом, паркуется на `…<8 hex>.wander-tmp`
  (сторож не видит); один composite, откат в обратном порядке. Память
  окна — `AppState.RenameRules` и пять шаблонов `AppState.RenameTemplates`.
