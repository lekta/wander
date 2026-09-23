# Перепроектирование: панели, выделение, папки (блок 2)

Сбор, разбор и целевая схема блока 2 PLAN: AN (модель состояния и
правил), AG (чей вид), Z1 (база параметров папки), H1, долги 0.4.x по
списку и сторожу, AD3-хвост, U1 / U2, AB, J4, TECHDEBT «Список файлов и
навигация», «Дерево и закладки». Решения и обязательства — в PLAN; здесь
наблюдения, диагноз, устройство сейчас, целевая схема, развилки с
рекомендациями и порядок работ. Файл живёт, пока модель не написана и не
описана в ARCHITECTURE; после — удаляется, история остаётся в гите.

Заведён 2026-09-22 по итогам дня правок 0.4.1 вокруг дерева, закладок,
выделения и цели операций (§1–2). В тот же день — полный разбор кода
(§3–4, §6–7, §9): только чтение, без запусков; находки §3.11 проверяются
на шаге 1. Кода по блоку нет, пока человек не ответил на §6.2–6.4.

## 0. Коротко

- Корень — не отдельные ошибки: **у цели операций, подсветки панели и
  фокуса нет своих значений.** Цель — «набор без главной» в
  `SelectedEntries`, подсветка — WPF `IsSelected` TwoWay, фокус — вход, по
  которому переназначается цель. Любое внутреннее движение WPF (фокус,
  перенос выделения) пишет в модель, и модель не отличает его от жеста.
- Чтение кода дало 17 находок (§3.11), большинство — расхождения с
  поведением, заявленным в QA и DONE. Самые заметные: «Свойства» и
  `Alt+Enter` узла панели показывают открытую папку; цель панели
  переживает уход клавиатуры в список (выделения нет, а `Delete` /
  `Ctrl+V` / `Enter` / `F2` — про папку из панели); меню узла судит о
  глаголах по открытой папке; две подсветки в панелях; `Tab` в панель без
  подсветки выделяет первую строку (со «стрелки открывают папку» —
  переходит в неё); висящее намерение приземления рассинхронизирует
  выделение списка и VM.
- Предложение (§4): одно состояние окна в Core (`WorkspaceState`),
  события с причиной, правила по срезам, эффекты данными, один
  исполнитель в App. **Цель — производная** (зона клавиатуры + выделение
  списка + курсор панели + снимок меню), а не хранимое поле. Вью переводят
  ввод в события и отражают состояние привязками OneWay.
- Первые решения человека — §6.2: В1 что значит подсветка панели, В2
  держит ли список выделение, пока клавиатура в панели (рекомендация —
  держит: «слетание выделения» становится невозможным по построению),
  В3–В9 правила цели и клавиатуры. Контрол панели — стендом (§4.7:
  П1 / П2 / П3).
- Порядок (§7): воспроизведение и таблица поведения → цель и контекст
  меню → модель панелей рядом со старым кодом → стенд → панели на модели →
  клавиатура → список → перетаскивание вглубь → J4 / H1 → харнесс и доки.
  Вид папки и база параметров (AG + Z1 + AD3-хвост, шаг 8) от модели не
  зависят и могут идти в любой момент после §6.3.
- §6.5 — решения человека 2026-09-22, уже сделанные в коде 0.4.x, модель
  их повторяет: исчезновение выделенной строки = удаление (В6);
  подсветка у каждой панели своя, `Ctrl+1` в «Диски» — на их строку;
  дерево не уезжает вбок к длинному имени; трасса и пути в журнале — по
  флагам отладки.

## 1. Хроника 2026-09-22

| Симптом | Причина | Правка | Что вылезло |
|---|---|---|---|
| Пустая папка, получившая подпапку, не раскрывается в дереве | `RefreshChildrenAsync` выходил на незагруженном узле; лист без заглушки шеврон не возвращал | перепроверка шеврона (`ReprobeChevronAsync`) | — |
| `F2` в панели не работает | окно отсекало `F2` по пустому `SelectedEntry`; цель панели живёт как «`SelectedEntries` без `SelectedEntry`» — неназванное соглашение; редактора у узла нет | адорнер над узлом, `RenameFolderAsync`, маршрут `MainWindow.StartRename`, `Trees.Follow` на месте, `RenameAction.MovesOnUndo` | восемь файлов ради одной клавиши |
| `Delete` на папке «через раз» | удалили открытую папку из панели → её строка исчезла → WPF отдал фокус окну → перечитывание списка сняло цель (`SettleDeparture`) → следующий `Delete` не про кого | цель панели исключена из «ушедших» в `ReconcileEntries`; возврат рамки по `IsKeyboardFocusWithinChanged` с отложенным вызовом; строка `Delete: no target` | из лога причину было не увидеть: несработавшее нажатие следа не оставляет |
| Перенос открытой папки из закладок раскрывал дерево дисков | `RewritePaths` → навигация → `ExpandTo` искал строку в закладках до того, как ветка перечитана → откат на диски | панели перечитываются до `RewritePaths` | **регрессия**: удаление выделенной строки из уровня заставляет родительский `TreeViewItem` выделить себя (WPF `OnItemsChanged`), панель приняла это за клик → список ушёл в прежнего родителя |
| ↑ регрессия | см. выше | перед удалением строки выделение снимается (`RefreshChildrenAsync`, `Remove`) | — |
| «Вставить» из меню узла — в открытую папку | `PasteAsync` брал `_nav.Current` | цель панели, затем параметр меню `PasteIntoSelection` | всё равно в открытую: см. следующую строку |
| Меню узла действует на текущую папку («Открыть в терминале», «Вставить») | WPF исполняет пункт после закрытия меню и возврата фокуса (`MenuItem.InvokeClickAfterRender`); возврат фокуса в панель → `OnZoneFocusChanged` → `TargetSelected` → цель = подсвеченная строка | переназначение цели после меню отложено на `Background` | зависимость от приоритетов диспетчера |
| Шеврон сбивает выделение | схлопывание ветки над подсвеченной строкой: WPF переносит выделение на узел (`HandleSelectionAndCollapsed`), панель принимала за жест → смена цели или переход | перенос отменяется, строка остаётся выделенной скрытой; `FocusTree` раскрывает путь к ней | **не закрыто**: «слетание» человек видит и после этого; в логе ничего |
| Правый клик в закладках подсвечивает строку | нажатие не перехватывалось, `TreeViewItem` выделяет себя при фокусе | нажатие перехвачено | — |

Все правки — точечные, каждая опирается на знание внутренностей WPF по
памяти (`TreeViewItem.OnItemsChanged`, `HandleSelectionAndCollapsed`,
`OnGotFocus → Select`, `InvokeClickAfterRender`, фокус на окно при
удалении элемента) и проверена только рассуждением: ни тесты Core, ни
харнесс до этих мест не дотягиваются.

## 2. Диагноз: что мешало

1. **Поведение живёт в обработчиках событий WPF.** `FolderTreesView`
   — 1379 строк и 17 полей состояния (`_treeClickNavigates`,
   `_userClickedExpander`, `_altWasHeld`, `_treeDragNode`, `_treeMenuNode`,
   `_treeRightDragArmed`, `_pendingTreeNav`, `_restoringSelection`,
   `_targetRow`, `_renameBox`…). `SelectedItemChanged` приходит без
   причины; причина восстанавливается по флагам, выставленным в прошлых
   событиях. Каждое новое внутреннее поведение WPF (перенос выделения при
   удалении, при схлопывании, выделение при фокусе, возврат фокуса из
   меню) — новое правило вывода, и каждое ломало или пропускало соседний
   случай. Это и есть «паутина».
2. **Три правды о том, «о чём следующая операция».** Выделение списка
   (WPF `SelectedItem`/`SelectedItems`), `MainViewModel.SelectedEntry` /
   `SelectedEntries`, выделение каждой панели (WPF) плюс
   `FolderTreesController._selected`, и сверху «цель панели» —
   закодированная как «набор без primary». Соглашение нигде не названо;
   его пришлось сначала открыть, потом на него опереться
   (`ExternalTargetFolder`, правило в `ReconcileEntries`, маршрут `F2`).
3. **Двусторонние привязки делают WPF писателем модели.** `IsSelected`,
   `IsExpanded` TwoWay на `TreeNodeViewModel`, `SelectedItem` TwoWay на
   вьюмодели. Внутренняя бухгалтерия WPF пишет в модель, и модель не
   отличает намерение человека от побочного эффекта контрола.
   `IsSyncingSelection` — заплатка ровно на это, но только для записей
   самого контроллера.
4. **Фокус — скрытый вход.** `OnZoneFocusChanged` (`GotKeyboardFocus`
   окна) переназначает цель при каждом приходе фокуса в панель. Фокус
   двигается по десятку причин без человека: удаление элемента, открытие
   и закрытие меню, сворачивание панели, снятие редактора. Две
   сегодняшних ошибки — ровно это.
5. **Правильность зависит от очерёдности диспетчера.** Пункт меню —
   после рендера; переназначение цели — `Background`; возврат рамки —
   `Input`. Работает, пока никто не переставит приоритеты; проверить
   нечем.
6. **Строки панели — и состояние, и элементы вью.** У панели нет
   отдельной модели «что должно быть показано»: `TreeNodeViewModel` с
   `Children` и есть состояние. Поэтому порядок «перечитать панели /
   переписать историю» в `FollowRelocatedAsync` имел значение, и его
   перестановка починила одно и сломала другое.
7. **Нет наблюдаемости.** Лог пишет операции и навигацию, но не смены
   выделения, подсветки, цели и фокуса. «Через раз», «слетает» — не
   сопоставить с логом. Добавлены `Delete: no target`, `Paste: … (how)`,
   `Target:`, `tree: highlight … (причина)`, `Listing follows` — это
   заплатки на месте трассы состояния.
8. **Нет автопроверки взаимодействия.** Харнесс ведёт вьюмодель, но не
   WPF-ввод; каждая правка проверялась только рассуждением о WPF.
9. **Файлы-монолиты со смешанными заботами.** `MainViewModel` — 5374
   строки: навигация, листинг, сверка выделения (`ReconcileEntries`,
   `SettleDeparture`, `ApplyArrival` — машина состояний на флагах
   `_rowsReplacing`, `_departure`, `FocusListAfterRestore`), операции,
   вставка, удаление, переименование, синхронизация панели просмотра.
10. **Правила закреплены комментариями с датами, не тестами.** «Один
    подсвеченный набор на экране» обеспечивается в четырёх местах и нигде
    не проверяется.
11. **Цель — не функция зоны клавиатуры** (разбор кода). Цель меняют
    приход фокуса в панель и арифметика «нет главной»; уход клавиатуры из
    панели её не снимает. Одно и то же нажатие в списке действует то на
    строку списка, то на папку из панели (Н4, Н12).
12. **Подсветка панели значит три вещи**: где открытая папка (ставит
    `ExpandTo`), где курсор клавиатуры (двигает WPF), о чём следующая
    операция. Одно `IsSelected` на три смысла — отсюда две подсветки
    (Н5), подсветка «не там» после ухода из панели и первая строка,
    выделенная фокусом (Н6).
13. **Строка панели ходит на диск.** `TreeNodeViewModel` читает уровень
    через локатор, пробует шевроны на пуле и возвращается через
    `Application.Current.Dispatcher`: состояние, ввод-вывод и потоки в
    одном объекте, который WPF ещё и пишет привязками.

## 3. Как устроено сейчас

### 3.1. Термины

Слова, которыми дальше пользуется разбор и модель; в коде сегодня у
половины нет своего имени.

| Термин | Что это | Где сейчас |
|---|---|---|
| Открытая папка | чей листинг в списке; путь + панель-источник | `NavigationService.Current` / `CurrentSource` |
| Листинг | строки открытой папки и эпоха, к которой они относятся | `Entries`, `SearchController.Source`, `FolderSession` |
| Выделение списка | набор строк + главная | WPF `SelectedItems` у каждого из четырёх видов, `SelectedEntries`, `SelectedEntry` (TwoWay `SelectedItem`) |
| Каретка | строка, от которой пойдёт следующая стрелка; рамка | список — `CaretPath`; панель — фокус `TreeViewItem` |
| Место | строка открытой папки в панели-источнике | ставит `FolderTreesController.ExpandTo` |
| Подсветка панели | закрашенная строка панели | WPF `IsSelected` (по одной на каждый `TreeView`); до 2026-09-22 ещё `FolderTreesController._selected` |
| Цель | о чём следующая операция | `SelectedEntries`; цель панели — «набор без главной» |
| Предмет меню | о чём открытое меню | явно нет: цель на момент исполнения пункта |
| Зона клавиатуры | область окна с фокусом | `WindowZone`, `MainWindow.ZoneOf` |
| Приземление | что выделить, когда листинг долетит | `ArrivalIntent` в `FolderSession` |
| Уход строки | выделенная строка или каретка пропала из листинга | `Departure`, `SettleDeparture` |

### 3.2. Карта кода

| Файл | Строк | Что внутри | Состояние |
|---|---|---|---|
| `Views/FolderTreesView.xaml.cs` | 1379 | жесты обеих панелей, цель, навигация из панели, коалесирование стрелок, редактор имени, приём drop, полоса «+» | 17 полей: жесты мыши (6), шеврон (2), коалесирование (3), цель (`_targetRow`), редактор (4), откат подсветки |
| `Controllers/FolderTreesController.cs` | 279 | корни дисков, `ExpandTo` / `RevealIn` / `Select`, `Follow`, `RefreshFor`, сбор раскрытых | `IsSyncingSelection` (`_selected` снят 2026-09-22, §6.5) |
| `Controllers/BookmarksController.cs` | 426 | список закладок, пересборка строк с нуля, встроенные, `Follow` | `_favorites`, `_specialSwitches`, `IsBuilding` |
| `ViewModels/TreeNodeViewModel.cs` | 591 | строка = данные + дети + чтение уровня (`IFileSystem`, `IShellNamespace` из локатора) + сверка уровня + пробы шевронов + обходы | `_isExpanded` / `_isSelected` (TwoWay), `_loaded`, общая статическая `_placeholder` |
| `Views/FileListView.xaml.cs` | 1867 | четыре вида, выделение по дельте, каретка, края сетки, type-ahead, рамка, drag, меню, редактор, возврат фокуса | 16 полей, про фокус и применение выделения пять: `_focusRowAfterRestore`, `_focusFellOutOfTheList`, `_currentRowFocusPending`, `_applyingSelection`, `_detachedViews` |
| `MainWindow.xaml.cs` | 1364 | области и рамка, хоткеи, сборка меню и его цели, маршрут `F2`, `FolderTargeted` → снять выделение списка | `_lastFolderPane` |
| `MainViewModel.cs` | 5374 | навигация, листинг, сверка выделения, цель панели, операции, вид папки, состояние | выделение: `_selectedEntry`, `_selectedEntries`, `_caretPath`, `_rowsReplacing`, `_departure`, `_entriesAreResults`, `_previewPair`, `FocusListAfterRestore`; вид: `_viewMode`, `_userViewMode`, `_manualViewModes` + очередь |

Коммитов за 30 дней: `MainWindow.xaml.cs` 47, `MainViewModel` 31,
`FileListView` 22, `FolderTreesView` 12, `TreeNodeViewModel` 12 — горячая
зона. PLAN «Чистки» называет `FileListView.xaml.cs` «~1200 строк» —
устарело, сейчас 1867.

### 3.3. Кто что решает

| Решение | Где | На чём |
|---|---|---|
| что выделено в списке после приземления листинга | `FolderSession.DecideArrival` (Core) + `ApplyArrival`, `ReconcileEntries`, `SettleDeparture` (VM) | `ArrivalIntent`, `_departure`, `_rowsReplacing`, `_selectedEntry is null` как признак цели панели |
| какая строка подсвечена в панели | `FolderTreesController.ExpandTo` / `RevealIn` / `Select` + WPF сам (`HandleSelectionAndCollapsed`, `OnItemsChanged`, фокус) + `FolderTreesView.OnTreeSelectionChanged` (откат переноса) | `IsSyncingSelection`, TwoWay `IsSelected`; подсветка гасится по панели (`Unlight`, с 2026-09-22) |
| о чём следующая операция | `SelectExternalPath` (VM) из `FolderTargeted` (вью) из `TargetTreeNode` / правого клика / `OnZoneFocusChanged` (окно) / `ApplyArrival` (SelectFolder) | `SelectedEntries` без `SelectedEntry` |
| навигация из панели | `OnTreeSelectionChanged` → `NavigateFromTree` | `_treeClickNavigates`, `TreeKeyboardNavigates`, дебаунс |
| куда идёт клавиатура после операции | `FocusListAfterRestore` (VM → `FileListView`), `Tree_IsKeyboardFocusWithinChanged` (вью), `FocusTree` | фокус WPF как факт |
| что панели делают при переносе / переименовании / удалении | `FollowRelocatedAsync`, `RefreshTreesAboveAsync` (VM) → `Trees.Follow` / `RefreshFor` → `TreeNodeViewModel.RefreshChildrenAsync` (+ `BranchReconcile`, Core) | порядок await'ов |
| куда идёт вставка / терминал / открыть / свойства / ярлык | `PasteAsync`, `TerminalFolder`, `OpenCommand`, `PropertiesTarget`, `CreateShortcutsForSelection` | `ExternalTargetFolder`, параметр `PasteIntoSelection`; свойства и ярлык — `_selectedEntry` / `_nav.Current` |
| о чём меню узла панели | `MainWindow.ShowContextMenu(folderPath)` → `ContextMenuTarget` | `Selection` = цель; `IsReadOnlyLocation`, `CanPaste` — про **открытую** папку |
| где подсветка после пересборки закладок | никто: строки создаются заново | — |
| куда фокус после диалога, переименования, ухода строки, смены вида | `RestoreListSelection` (пять условий), `OnCurrentRowLeft`, `KeepFocusAcrossViewSwap`, `Tree_IsKeyboardFocusWithinChanged` | флаги вью + `FocusListAfterRestore` |
| что показывает панель просмотра | `SyncPreviewSelection`, `RefreshPreviewPrimary`, `ActiveEntry`, `SelectExternalPath` (напрямую `Preview.SetPrimary`) | `SelectedEntries`, `CaretPath`, `PreviewPair` |
| какой вид у папки | `AutoSelectViewMode` (приход), `SetViewMode` | `_manualViewModes`, `_userViewMode`, `ImageFolderProbe` |

Десяток мест, три слоя, общие поля. Стрелки идут во все стороны: вью
пишет в VM (`SelectExternalPath`), VM дёргает вью
(`SelectionRestoreRequested`, `InlineRenameRequested`,
`FocusListAfterRestore`), окно дёргает обе (`TargetSelected`,
`ClearSelection`), WPF пишет во все три.

### 3.4. Кто пишет состояние

| Что | Писатели |
|---|---|
| `SelectedEntries` | `FileListView.ReportSelection` (жест или раунд `SetListSelection`); `SelectExternalPath` (из `FolderTargeted`, `ApplyArrival` SelectFolder, `DeleteFromBookmark`, харнесс `tree-target`); `ApplyArrival` SelectRows; `ReconcileEntries` (снять); `SettleDeparture` (преемник); `AdoptSelection` (поля напрямую) |
| `SelectedEntry` | привязка TwoWay `SelectedItem` у всех четырёх видов; `ApplyArrival`; `ReconcileEntries` (поле + `Raise` → привязка схлопывает многовыделение до одной строки); `SettleDeparture`; `AdoptSelection` |
| выделение WPF в списке | человек; `SetListSelection` (по дельте); `SelectionController` (отложенный клик); `RubberBandController`; `TryEnterList`, `TryGridStep`, type-ahead; привязка `SelectedItem` |
| `CaretPath` | `FileListView.FocusRow`, `UpdateCaret` (из фокуса или последней выделенной), нажатие на строку, `SettleDeparture`, `OnNavigationChanged` (сброс) |
| подсветка панели (`IsSelected` TwoWay) | WPF (клик, стрелки, фокус, схлопывание, удаление выделенного); `ExpandTo` / `RevealIn` / `Select`; откат переноса (`_restoringSelection`); снятие перед `RemoveAt`; `TryExpandToPath`; харнесс |
| раскрытие (`IsExpanded` TwoWay) | WPF (шеврон — `ToggleButton.IsChecked` TwoWay к `TreeViewItem.IsExpanded`, двойной клик по строке); `TryExpandToPath`; Alt-обходы; восстановление при старте и при пересборке закладок; харнесс |
| намерение приземления | `Refresh` («оставить выделенное»); `NavigateAndSelectFolder`; `RevealPath`; удаление; откат; переименование; групповое переименование; вставка; извлечение; создание папки; `FolderSession.OnNavigating` (подъём, память) |
| фокус | WPF; `FocusList` / `FocusRow` / `FocusRowWhenReady` / `FocusCaretRow`; `FocusTree`; `Tree_IsKeyboardFocusWithinChanged` (отложенно, `Input`); `KeepFocusAcrossViewSwap` (`Loaded`); `FocusZone`; `ApplyFoldersLayout`; `CommitRenameOnClickAway`; `FocusWorkArea` после диалогов |

### 3.5. Потоки

**Клик по строке X в «Дисках», клавиатура была в списке.**

1. `Tree_PreviewMouseLeftButtonDown`: `_treeClickNavigates = true`, взвод
   перетаскивания.
2. `TreeViewItem.OnMouseLeftButtonDown` → `Focus()` → `GotKeyboardFocus`
   окна → `OnZoneFocusChanged` → `TargetSelected` → `TargetTreeNode` по
   **текущей** `SelectedItem` (старая подсветка) → `FolderTargeted` →
   `FileList.ClearSelection()` + `SelectExternalPath(старая)`. Порядок
   `GotKeyboardFocus` против выделения — проверить (Н10, Н11).
3. `OnGotFocus` → `Select` → `SelectedItemChanged` → `OnTreeSelectionChanged`
   → `NavigateFromTree` → `NavigateAndSelectFolder` (намерение `Folder(X)`)
   → `NavigateTo`.
4. `OnNavigationChanged`, синхронно: `OnNavigating(…, _selectedEntry)` —
   память выделения покидаемой папки; `CaretPath = null`; `Refresh`
   (листинг в фоне); `ExpandTo(X, Drives)` под `IsSyncingSelection`;
   панели просмотра; сторож; `SaveState`.
5. Листинг: `Dispatcher.Yield(Background)` → очистка, если не успел →
   `PublishRows` → `SetSource` → `FilteredChanged` →
   `ReconcileEntries(SyncEntries)` → `ApplyArrival` → `SelectFolder` →
   `SelectExternalPath(X)` → `SettleDeparture`.
6. `AutoSelectViewMode` на приходе → смена вида → `KeepFocusAcrossViewSwap`
   (только если клавиатура в списке).

**Стрелка в «Дисках», стрелки не навигируют.** WPF двигает фокус на
соседа → `OnZoneFocusChanged` → `TargetSelected` (строка ещё старая?) →
`Target:`; затем `OnGotFocus` → выделение → `OnTreeSelectionChanged` →
`TargetTreeNode(новая)` → `Target:` и `_fs.GetEntry` на UI-потоке. Два
`Target:` и до двух `stat` на нажатие; `FolderTreesController._selected`
о новой строке не знает.

**Сторож перечитывает открытую папку с выделением.** `OnWatchTick` →
`DecideWatchTick` = `Relist` → `Refresh`: выделение есть, намерения нет →
`SetArrival(Rows(оставить))` → листинг → `ReconcileEntries` видит
намерение → `ownsSelection = false` → `ApplyArrival` →
`SelectionRestoreRequested` → `RestoreListSelection` → `ScrollIntoView`
(Н9) и, по пяти условиям, фокус.

**Меню узла Y, открыта A.** Нажатие правой: `_targetRow`,
`FolderTargeted(Y)` → выделение списка снято, `SelectExternalPath(Y)`;
взвод правого перетаскивания; `Handled`. Отпускание →
`ContextMenuRequested(Y)` → `ContextMenuTarget { Selection = [Y],
FolderPath = Y, IsReadOnlyLocation = про A, CanPaste = про A }`. Пункт:
меню закрывается → фокус возвращается в панель → `OnZoneFocusChanged`:
старый фокус в меню → `TargetSelected` отложен на `Background` → пункт
исполняется на `Render` с целью Y → затем цель — подсвеченная строка.

**`Delete` открытой папки из панели.** `DeleteSelectedAsync` →
подтверждение → `RunDeleteAsync` → `RefreshTreesAboveAsync` (фоном) +
`NavigationFallback.AfterDelete` → `NavigateTo(родитель)` → `ExpandTo`.
Строка уходит из уровня (`RefreshChildrenAsync`: снять подсветку, потом
`RemoveAt`) → фокус WPF падает на окно → `Tree_IsKeyboardFocusWithinChanged`
→ отложенно (`Input`) фокус на `SelectedItem` (родитель) →
`OnZoneFocusChanged` → цель — родитель.

### 3.6. Поведение панелей — инвентарь

| Поведение | Где | Под тестом |
|---|---|---|
| ленивое чтение уровня — **синхронно** при раскрытии | `TreeNodeViewModel.EnsureLoaded` | нет |
| оптимистичный шеврон, проба фоном; перепроверка незагруженного | `ProbeForChevrons`, `ReprobeChevronAsync` | нет |
| сверка уровня без пересборки | `RefreshChildrenAsync` + `BranchReconcile` | правило — да |
| архивы среди папок уровня; папки внутри архива | `ReadChildFolders`, `ReadArchiveFolders` | нет |
| «никогда не сворачивается сама» | всё выше | QA |
| раскрытие до открытой папки в панели-источнике; спуск наследует панель | `ExpandTo`, `DescendSource`, `GoUp` | частично (`NavigationServiceTests`) |
| `Ctrl+1` / `Ctrl+Shift+E` | `WindowZones.FolderPane` + `RevealAndFocus` | правило — да |
| клик — переход, в том числе по уже подсвеченной | `Tree_PreviewMouseLeftButtonDown` | нет |
| стрелки — курсор, `Enter` — переход; «стрелки открывают папку» с коалесированием 250 / 90 мс | `OnTreeSelectionChanged`, `NavigateFromTree`, `ArmTreeNavDebounce` | нет |
| цель клавиатурой, правым кликом, приходом фокуса | `TargetTreeNode`, `FolderTargeted`, `OnZoneFocusChanged` | нет |
| `Alt` + шеврон — ветка целиком | `TreeViewItem_Expanded` / `_Collapsed` | нет |
| откат переноса подсветки при схлопывании | `_restoringSelection` | нет |
| `F2` и «Переименовать» | `StartRename`, `RenameFolderAsync`, `MainWindow.StartRename` | операция — харнесс `tree-rename` |
| следование за переименованием и переносом | `Follow`, `FollowRelocatedAsync` | правила — да |
| удаление: список уходит в ближайшего живого предка | `NavigationFallback.AfterDelete` | да |
| возврат рамки после ухода строки с клавиатурой | `Tree_IsKeyboardFocusWithinChanged` | нет |
| закладки: встроенные, свои, пропавшие, порядок, «…», «+», drop | `BookmarksController`, вью | нет |
| перетаскивание узла левой и правой кнопкой | вью + `OutgoingDrag` | нет |
| `Shift` + колесо, меню пустого места | вью | нет |
| сохранение раскрытых, восстановление при старте | `CollectExpanded`, `RestoreState` | `NavigationFallbackTests` |

### 3.7. Поведение списка и клавиатуры — инвентарь

| Поведение | Где | Под тестом |
|---|---|---|
| листинг вне UI, эпохи, спиннер через 150 мс | `RefreshFolderAsync`, `FolderSession` | эпохи — да |
| сверка строк без пересборки | `SyncEntries` + `ListingDiff` | да |
| выделение через пересборку строк | `ReconcileEntries` (`_rowsReplacing`, порядок «главная, потом набор») | нет |
| приземление | `ApplyArrival` + `DecideArrival` | решение — да, исполнение — нет |
| уход текущей строки → преемник | `SettleDeparture` + `CurrentRowFallback` | правило — да |
| память выделения 64 папок; подъём — папка, из которой вышли | `FolderSession` | да |
| сторож: строки или перечитывание | `DecideWatchTick` + `FolderChanges` | да |
| каретка | `CaretPath`, `UpdateCaret`, `FocusRow` | нет |
| выделение по дельте, одним отчётом | `SetListSelection`, `ApplyDelta`, `_applyingSelection` | нет |
| отложенный клик, `Ctrl`-протяжка | `SelectionController` | нет |
| рамка выделения | `RubberBandController` | нет |
| стрелки на краях сетки | `GridNavigation` + `TryGridStep` | правило — да |
| первая стрелка в список без строки | `TryEnterList` | нет |
| type-ahead | `TypeAheadController` | да |
| возврат клавиатуры после приземления (пять условий) | `RestoreListSelection` | нет |
| строка ушла — клавиатура на преемника; окно неактивно — после `Activated` | `OnCurrentRowLeft`, `OnWindowActivated` | нет |
| выделение и фокус через смену вида | `ApplyViewAttachment`, `KeepFocusAcrossViewSwap` | нет |
| редактор имени | `TryStartInlineRename`, `RenameAdorner`, `CommitRenameOnClickAway` | нет |
| оценки с клавиатуры, полный экран в галерее | `TryRateFromKeyboard`, `FullscreenRequested` | `RatingToggleTests` |
| области, `Tab`, `Ctrl+1/2`, `Ctrl+Shift+E` | `WindowZones` + `MainWindow` | правило — да |
| рамка области | `OnZoneFocusChanged` | нет |

### 3.8. Что уже в Core

Под тестами: `FolderSession` (эпохи, намерение, память, сторож),
`ArrivalIntent`, `CurrentRowFallback`, `ListingDiff`, `BranchReconcile`,
`PathRewrite`, `NavigationFallback`, `NavigationService` (+ `RewritePaths`),
`RecentPaths`, `PathCrumbs`, `WindowZones`, `GridNavigation`,
`TypeAheadController`, `ImageFolderProbe`, `PreviewPair`,
`PreviewNeighbors`, `RatingToggle`. Прецедент есть: каждое вынесенное
правило перестало ломаться.

Не под тестом ничего из того, что ломалось 2026-09-22: цель, подсветка и
курсор панели, раскрытие, фокус, сверка выделения при пересборке (VM),
исполнение приземления, выбор вида папки (VM), следование за путём
вне истории и закладок.

### 3.9. Вид папки (AG) сейчас

- `_viewMode` — на экране; `_userViewMode` — «последний ручной выбор»,
  хранится в `SessionState.ViewMode`; `_manualViewModes` — закрепления
  «путь → вид», 128 штук, `SessionState.ManualViewModes`.
- `SetViewMode` (меню, `Ctrl+Shift+1/2/6/7`) пишет **оба**: общий
  последний выбор и закрепление за папкой. Поэтому ручной выбор в одной
  папке меняет вид всех незакреплённых — наблюдение человека.
- `AutoSelectViewMode` — только на приходе (`arriving`), только в
  `RefreshFolderAsync`: закреплена → её вид; `AutoGallery` выключена →
  **ничего не делает** (Н13); папка со снимками → «Галерея» (не
  закрепляется); иначе → `_userViewMode`.
- Корзина и архивы (`RefreshShellAsync`) вид не выбирают вовсе (Н14).
- Потолок 128 — по порядку **первого** закрепления, повторный выбор
  место в очереди не обновляет (Н15). Ключ — путь: переименование или
  перенос папки закрепление теряет (AD3-хвост).
- Меню «Вид»: четыре пункта с галочкой по `ViewMode`; к чему относится
  выбор, не сказано.

### 3.10. Перетаскивание, массовое выделение, оценка на значках, desktop.ini

- **U1 / U2.** Приём — `DropTargetController` (App): цель под курсором,
  эффект по модификаторам и дискам (`ChooseEffect` читает
  `Keyboard.Modifiers`), подсветка строки; таймеров наведения и
  автопрокрутки нет; рамка выделения за край не тянется (TECHDEBT).
- **AB.** `SetListSelection` добавляет строки по одной
  (`SelectedItems.Add` — линейный внутри WPF): 5000 строк — 10,8 с,
  `SetSelectedItems` — 0,36 с (стенд `SelectionProbe`). Обработчик уже
  отчитывается один раз (`_applyingSelection`), квадрат остался внутри
  WPF.
- **J4.** Бейдж оценки — только в «Галерее» (`GalleryRatingBadge` +
  `DataTrigger`, `FileListView.xaml`), столбец — в «Таблице»; «Крупные
  значки» и «Плитка» оценку не показывают, хотя второй проход читает её в
  любом виде.
- **H1.** `desktop.ini` не читается и не пишется; решение Z1 —
  собственная база, в чужие папки не пишем.

### 3.11. Находки чтения кода

Расхождения с QA, DONE и комментариями кода. Уверенность — по чтению; всё
проверяется на шаге 1 и становится тестом модели (§4.10).

| # | Сценарий | Должно (QA / DONE) | Делает код | Уверенность | Шаг |
|---|---|---|---|---|---|
| Н1 | открыта A, правый клик по B в панели → «Свойства»; или клавиатура в панели на B, `Alt+Enter` | свойства B | свойства A: `PropertiesTarget` = `_selectedEntry ?? _nav.Current`, у цели панели главной нет | высокая | 2 |
| Н2 | меню узла B → «Создать ярлык» | [В8] | ярлык на B в открытой A (`CreateShortcutsForSelection` → `_nav.Current`) | высокая | 2 |
| Н3 | открыта корзина или архив, правый клик по обычной папке в дереве | меню папки со всеми глаголами | нет «Удалить», «Вырезать», «Вставить», «Переименовать»: `IsReadOnlyLocation` и `CanExecute` смотрят на открытую папку | высокая | 2 |
| Н4 | клавиатура в панели на B → `Ctrl+2`, `Esc` или клик по пустому месту списка; в списке ничего не выделено | ничего / вставка в открытую [В3] | `Delete` спрашивает про B, `Ctrl+V` кладёт в B (`panel row`), `Enter` открывает B, `F2` открывает редактор в панели: цель — `SelectedEntries = [B]`, вход в список её не снимает | высокая | 2 |
| Н5 | стрелками в «Дисках» на Z (без навигации), затем клик по закладке | ~~одна подсветка~~ с 2026-09-22 — Z остаётся в «Дисках» неактивной подсветкой, туда вернёт `Ctrl+1` (§6.5) | Z остаётся подсвеченной — теперь это правило, не случайность: `_selected` снят, `ExpandTo` из закладок «Диски» не трогает, подсветка гасится по панели (`Unlight`) | — | 5 |
| Н6 | папка открыта из «Дисков», `Tab` из фильтра в закладки; или `Ctrl+1` в закладки, когда папка вне закладок | курсор без побочных эффектов [В5] | `FocusTree` фокусирует первую строку → `TreeViewItem` выделяет себя → цель = первая строка, выделение списка снято; со «стрелки открывают папку» — переход в первую строку («Загрузки») | средняя | 5 |
| Н7 | добавить / убрать закладку, «Указать расположение», перенос с закладкой, выключить встроенную — открыта папка из закладок | подсветка на месте | `Bookmarks.Build` создаёт строки заново: подсветка в закладках пропадает до следующей навигации; клавиатура с удалённой строки падает на окно, а отложенный возврат рамки не находит подсвеченной строки (кроме `Ctrl+↑/↓`, который ставит её сам) | подсветка — высокая, клавиатура — средняя | 5 |
| Н8 | «Вставить» в строку панели или в выделенную подпапку (меню); `F2` результата поиска из другой папки; затем оценка на N выделенных | выделены N | намерение `Rows(target)` для другой папки ждёт навигации туда; пока ждёт, `ReconcileEntries` сам выделение не возвращает (`ownsSelection = false`), `ApplyArrival` его не применяет: после замены строк в списке подсвечена одна, в VM — N, следующая звезда или `Delete` — на все N; заход в ту папку двойным кликом потом выделит вставленное и заберёт клавиатуру | средняя | 7 (быстрая мера — намерение только для открытой папки) |
| Н9 | папка прокручена колесом, сторож перечитал её с выделением | прокрутка на месте | `Refresh` ставит «оставить выделенное» → `RestoreListSelection` → `ScrollIntoView` (долг 0.4.x) | подтверждено | 7 |
| Н10 | выделен файл в A, клик по папке B в дереве, потом назад в A | выделен тот же файл | возможно, нет: приход фокуса в дерево снимает выделение списка раньше, чем `OnNavigating` читает `_selectedEntry` | низкая-средняя; в логе `Target: <старая>` перед `Navigate (Drives)` | 1 → 2 |
| Н11 | любое движение фокуса внутри панели | одна смена цели | две (`GotKeyboardFocus` со старой строкой, затем выделение с новой) и до двух `GetEntry` на UI-потоке: шум `Target:` и `stat` на стрелку, на сети — задержка | средняя | 2 |
| Н12 | клавиатура в панели, `Backspace` / `Alt+←` | [В1–В2] | приземление выделяет в списке папку, из которой вышли; `ExpandTo` подсвечивает в панели родителя, а фокус остаётся на строке покинутой папки — два выделенных набора, `Delete` про строку списка, следующая стрелка идёт от покинутой строки | средне-высокая | 2 |
| Н13 | «Автогалерея» выключена; папка A закреплена за «Галереей», переход в B | вид по умолчанию | «Галерея»: `AutoSelectViewMode` выходит до присвоения | высокая | 8 |
| Н14 | вход в корзину или архив со снимками | выбор вида по правилу | вид предыдущей папки: `RefreshShellAsync` вид не выбирает | высокая | 8 |
| Н15 | 129-е закрепление | вытеснить давно не открываемое | вытесняется первое закреплённое, даже если в нём бывают каждый день | высокая | 8 |
| Н16 | ветка в тысячи подпапок | — | панели не виртуализированы; `ContainerFor` обходит все реализованные контейнеры с `UpdateLayout()` на каждом раскрытом уровне | средняя | 4–5 |
| Н17 | TECHDEBT «левый клик по узлу не делает его целью» | — | по коду устарело: после клика цель — открытая из панели папка (`ApplyArrival` SelectFolder или приход фокуса) | средняя | 1 (проверить и вычеркнуть) |

## 4. Целевая схема

### 4.1. Принципы разделения

1. **Факты на входе, решения на выходе** (правило проекта, прецеденты
   `FolderSession`, `TileLayout`, `BranchReconcile`). Правила Core не
   читают `Keyboard`, `Mouse`, часы, диск; часы подставляются (прецедент
   `TypeAheadController`).
2. **Одно состояние — много правил.** Состояние окна — одна запись; каждое
   правило владеет своим срезом, читать может всё. Координатор применяет
   правила в фиксированном порядке — порядок явный и под тестом, а не
   «кто подписался раньше».
3. **Производное не хранится.** Цель, подсветка панели, видимые строки,
   предмет панели просмотра — функции состояния. Хранятся факты: зона,
   курсоры, выделение, раскрытое, место, намерение, снимок меню. Нет
   хранимой цели — нет устаревшей цели (Н4, Н12).
4. **Событие несёт причину.** Неизвестная причина — консервативна: ничего
   не выделяет и не навигирует.
5. **Эффект — данные.** `Navigate`, `ReadBranch`, `FocusRow`,
   `ApplyListSelection`, `OpenEditor`… Исполняет один контроллер в App,
   каждый эффект в трассе.
6. **Вью отражают.** Всё, чем владеет модель, привязано OneWay; TwoWay —
   только чистое состояние вью (прокрутка, сплиттеры).
7. **Причуды WPF — в адаптере**, таблицей (§4.8), а не датой в
   комментарии.
8. **Приоритет диспетчера не несёт смысла.** «После X» — событие от X, а
   не `BeginInvoke(Background)`.
9. **Асинхронность — событиями с эпохой.** Чтение уровня панели, листинг,
   проходы оценок приходят событием; правило не ждёт.
10. **Размер.** Модуль правил — до ~300 строк; делить — по второй причине
    трогать файл (правило чистки #4).

### 4.2. Состояние

```
WorkspaceState                 Core, Wander.Core/Workspace; неизменяемые record'ы
  Folder    FolderFacts        путь, панель-источник, место: диск | архив | корзина | результаты
  List      ListState          выделение (пути + главная), каретка, редактор (путь), намерение
  Bookmarks PanelState
  Drives    PanelState
    Rows       путь → уровень (строки PanelRow; загружен / читается / эпоха)
    Expanded   множество путей
    Location   строка открытой папки в этой панели или null
    Caret      строка курсора или null
    Editing    путь строки под редактором
  Keyboard  KeyboardState      зона (WindowZone | None), последняя зона, окно активно
  Menu      MenuContext?       снимок на время открытого меню

PanelRow   путь, имя, вид (папка | диск | архив | папка архива | shell),
           скрыта, дети (есть | нет | неизвестно), пропала,
           роль (обычная | встроенная закладка | своя закладка), черта над

Производные: Target, Highlight(панель), видимые строки, PreviewSubject.
```

Отличие от первой редакции (там `Target` — поле): цель вычисляется
(§4.3). `FolderSession` остаётся изменяемым классом внутри правила
листинга — прецедент под тестами, ломать нечего. Коллекции —
`System.Collections.Immutable` из BCL (пакет не нужен): тест сравнивает
до / после, трасса пишет разницу.

`Pane { Bookmarks, Drives }` — своё перечисление панели вместо
`NavigationSource` (у того шесть значений, из них про панели два).

### 4.3. Цель — производная

```
Target(s) =
  s.Menu is { } m                          -> m.Subject
  s.Keyboard.Zone is Bookmarks or Drives   -> PanelRow(панель, Caret)      (Caret null -> None)
  s.Keyboard.Zone is None                  -> по последней зоне
  иначе: список, адрес, фильтр, тулбар     -> ListRows(выделение)          (пусто -> None)
```

| Команда | Цель | Цель None |
|---|---|---|
| `Delete`, `Ctrl+C` / `X`, действия | строки цели (список — в порядке списка) | ничего; действия для папок — на открытой |
| `Ctrl+V` | в папку: предмет меню-папка → она; строка панели → она; из меню единственная выделенная папка списка → она | открытая папка |
| `Enter` / «Открыть» | строка списка — открыть; строка панели — перейти | ничего |
| `F2` | редактор на поверхности цели; две и больше — групповое | ничего |
| `Alt+Enter`, «Свойства» | одна строка цели | открытая папка |
| «Открыть в терминале» | одна папка цели | открытая папка |
| «Создать ярлык» | строки списка → в открытую; строка панели → [В8] | — |
| панель просмотра | `PreviewSubject` (§4.5) | перепись открытой папки |
| «Выбрано: N · размер» | строки цели | пусто |

`MenuContext` — снимок при открытии меню: предмет (строки списка, строка
панели или фон папки X) и факты **о месте предмета** (только чтение,
корзина, архив, можно ли вставить в него). `ContextMenuTarget` строится из
него, пункты получают его параметром. Команда с хоткея считает цель
сейчас; команда из меню — по снимку. Зависимость от порядка «меню
закрылось → фокус вернулся → пункт исполнился» уходит вместе с отложенным
`TargetSelected`.

### 4.4. События — с причиной

Вход в модель — только события; сегодняшние обработчики сводятся к ним.

| Группа | События | Откуда сегодня |
|---|---|---|
| ввод панели | `RowPressed(панель, путь, кнопка, модификаторы)`, `RowClicked`, `RowActivated` (`Enter`), `ChevronToggled(путь, открыть, Alt)`, `CaretMoveRequested(клавиша)` или `CaretMoved(путь)` (по стенду), `RowRightClicked`, `BookmarkMove(±1)`, `BookmarkDelete(навсегда)`, `ZoneLeaveRequested` (`Esc`) | `Tree_Preview*`, `SelectedItemChanged`, `TreeViewItem_Expanded/Collapsed`, `Tree_PreviewKeyDown` |
| ввод списка | `ListSelectionChanged(пути, главная)`, `CaretMoved(путь)`, `ListBackgroundClicked` | `ReportSelection`, `UpdateCaret`, нажатие на пустое место |
| редактор | `EditRequested` (`F2`, меню), `EditEnded(применить / отменить; как: Enter / клик мимо / потеря фокуса; имя)` | `StartRename`, `CommitInlineRename`, `CancelInlineRename` обеих вью |
| окно | `ZoneEntered(зона, причина)`, `FocusFell(зона)`, `MenuOpened(контекст)`, `MenuClosed(исполнен / сброшен)`, `WindowActivated`, `WindowDeactivated`, `PaneHidden` | `OnZoneFocusChanged`, `IsKeyboardFocusWithinChanged`, `Activated`, `ApplyFoldersLayout` |
| факты | `Navigated(путь, источник, как: переход / назад / вперёд / вверх / перепись)`, `ListingLanded(эпоха, до, после, причина)`, `BranchRead(панель, путь, строки, эпоха)`, `RowsReplaced(строки)`, `Relocated(откуда, куда)`, `Removed(пути)`, `OperationFinished(вид, пришло, ушло, за диалогом)`, `BookmarksChanged(пути)`, `VisibilityChanged`, `ViewModeChanged`, `ThrottleElapsed` | `OnNavigationChanged`, `PublishRows` / `FilteredChanged`, `RefreshChildrenAsync`, `ReplaceRows`, `FollowRelocatedAsync`, `RunDeleteAsync`, операции |

Причины прихода фокуса (`ZoneEntered`): `Tab`, хоткей (`Ctrl+1/2`,
`Ctrl+Shift+E`), клик, возврат из меню, возврат после диалога, активация
окна, падение фокуса, программно, неизвестно. Определяет их адаптер окна
(`ZoneTracker`): окно само двигало фокус — причина записана до вызова;
перед этим было нажатие мыши в зоне — клик; старый фокус внутри
`ContextMenu` — меню; перед этим `Activated` — активация; новый фокус —
окно — падение. Цель от причины не зависит (она производная), причина
нужна правилам клавиатуры (§4.5) и случаю В5.

Чего в модель **нет**: того, что WPF делает сам (перенос выделения при
удалении и схлопывании, выделение при фокусе, фокус на окно) — адаптер
гасит это до события или откатывает по состоянию (§4.8).

### 4.5. Правила

Координатор `WorkspaceReducer.Apply(state, event) -> (state, effects)`
вызывает модули по порядку; модули не зовут друг друга, читают итог
предыдущих.

| # | Модуль | Решает | Вбирает из сегодняшнего кода |
|---|---|---|---|
| 1 | `NavigationRules` | какое событие — навигация: клик, `Enter`, «стрелки открывают» через `TreeNavThrottle` (часы подставлены: сейчас / в момент T / никогда), `Backspace` из панели | `NavigateFromTree`, коалесирование `_pendingTreeNav`, эхо «уже там» |
| 2 | `PanelRules` | строки, раскрытое, место, курсор: раскрыть / свернуть / `Alt`; `BranchRead` (правки уровня по `BranchReconcile`); раскрытие до пути асинхронным спуском (ждущее раскрытие в состоянии); `Relocated` (строки следуют); `Removed` (курсор → сосед → родитель); пересборка закладок по путям с сохранением раскрытого, места и курсора; скрытые выключены — курсор на родителя | `ExpandTo`, `RevealIn`, `TryExpandToPath`, `Follow`, `RefreshChildrenAsync`, `EnsureLoaded`, `ProbeForChevrons` (как факт), `_restoringSelection`, снятие выделения перед `RemoveAt` |
| 3 | `ListingArrival` | после приземления: выделение, каретка, прокручивать ли, преемник ушедшей строки или просто снять, потребление намерения; причина перечитывания (переход, F5, сторож, операция, фильтр, настройки, оценки) решает, прокручивать и искать ли преемника | `ReconcileEntries`, `SettleDeparture`, `ApplyArrival`, `AdoptSelection`, намерение «оставить» в `Refresh`; использует `FolderSession.DecideArrival` и `CurrentRowFallback` |
| 4 | `KeyboardRules` | куда клавиатура: строка с клавиатурой ушла или заменена; операция за диалогом кончилась; редактор закрыт `Enter` / кликом; смена вида; панель убрана; вход в панель без курсора (В5); окно активировано после ухода строки; фильтр спрятал текущую | пять условий `RestoreListSelection`, `FocusListAfterRestore`, `_focusRowAfterRestore`, `_focusFellOutOfTheList`, `_currentRowFocusPending`, `Tree_IsKeyboardFocusWithinChanged`, `KeepFocusAcrossViewSwap`, `FocusTree` |
| — | `TargetRules` | производная цель, куда вставлять, предмет каждой команды (§4.3) | `SelectExternalPath`, `ExternalTargetFolder`, `PasteIntoSelection`, `PropertiesTarget`, `TerminalFolder`, `SelectedPathsOrCurrent` |
| — | `MenuContext` | снимок при открытии меню, факты о месте предмета → `ContextMenuTarget` | `MainWindow.MenuTarget` |
| — | `PreviewSubject` | что показывают панели просмотра: строки цели + каретка (активная в многовыделении) → `PreviewPair.Of`; строка панели → перепись папки; None → перепись открытой | `SyncPreviewSelection`, `RefreshPreviewPrimary`, `ActiveEntry`, `PrimaryForPane` |
| — | `PathFollowing` | перенос или переименование → переписи для всех, кто помнит путь (§4.12) | `FollowRelocatedAsync`, `Bookmarks.Follow`, `RewritePaths` |

Модули 1–4 пишут состояние; строки без номера — чистые функции от
состояния (зовутся исполнителем и вью). Каждое правило — «факты на
входе, решения на выходе», без ссылок вбок.

### 4.6. Эффекты и исполнитель

`Navigate(путь, источник)`, `ReadBranch(панель, путь, эпоха)`,
`ProbeChevrons(пути)`, `ApplyListSelection(пути, главная, прокрутить)`,
`SetCaret(путь)`, `FocusRow(поверхность, путь, прокрутить)`,
`FocusZone(зона)`, `OpenEditor(поверхность, путь)`, `CloseEditor`,
`ShowMenu(контекст)`, `ScheduleThrottle(момент)`, `Rewrite(держатель,
откуда, куда)`.

Исполнитель — `Controllers/WorkspaceController` (App): принимает события
от вью, VM и фона, зовёт редьюсер, исполняет эффекты, отдаёт вью новое
состояние (`StateChanged`), пишет трассу. Результаты фона (уровень,
листинг) возвращаются событием с эпохой; устаревшая эпоха отбрасывается
правилом, а не исполнителем. `MainViewModel` отдаёт ему выбор цели,
сверку выделения и панели и худеет на эту часть; операции остаются в VM
до блока 4.

### 4.7. Вью и адаптеры

**Панели — контрол решает стенд** (открытый вопрос первой редакции).

| | П1: `TreeView`, выделение WPF, откат по состоянию | П2: `TreeView` без выделения WPF | П3: плоский `ListBox` видимых строк |
|---|---|---|---|
| как | как сейчас + адаптер сравнивает `IsSelected` с состоянием и откатывает | свой `FolderTreeViewItem`: `OnGotFocus` без `Select`, подсветка и курсор — свои свойства строки (OneWay), шеврон — событие | видимые строки считает Core (отступ = глубина), обновление — по `ListingDiff`; раскрыть / свернуть — свои |
| причуды WPF | остаются, гасятся откатом | `HandleSelectionAndCollapsed` и `OnItemsChanged` не срабатывают без выделения; фокус при удалении строки — остаётся | нет древесных; остаётся фокус при удалении строки |
| клавиатура | WPF | WPF двигает фокус, модель читает `CaretMoved`; либо своя (`PanelKeyNavigation`) | своя: `↑↓` — `ListBox`, `←→ Home End PgUp PgDn` — `PanelKeyNavigation` (Core, как `GridNavigation`) |
| виртуализация | нет (сегодня) | `VirtualizingPanel.IsVirtualizing` на `TreeView` — проверить | из коробки |
| цена | меньше кода, паутина остаётся | подкласс ~80 строк + шаблон | вью заново; UIA — список, не дерево |

Стенд (консоль в scratchpad, окно за экраном, песочница: три уровня и
уровень на 3000 подпапок; события WPF синтетически внутри процесса, как
шаг `key` харнесса; системный ввод не трогается):

1. П2: `OnGotFocus` без `base` — строка не выделяется, `SelectedItem`
   всегда null.
2. П2: `↑ ↓ ← → Home End PgUp PgDn` при `SelectedItem == null` —
   `TreeView.OnKeyDown` при пустом выделении зовёт `FocusFirstItem` —
   не ломает ли `PgUp` / `PgDn`.
3. Удаление строки с фокусом: куда фокус (окно или дерево) — П1, П2, П3.
4. Схлопывание ветки с фокусом внутри: куда фокус без выделения.
5. Двойной клик по строке: раскрытие WPF — перехватывать ли.
6. Прокрутка к строке в закрытой ветке после раскрытия.
7. Ветка на 3000: время раскрытия, память, поиск контейнера (Н16) —
   П2 с виртуализацией против П3.
8. UIA (Inspect): что видит чтение с экрана.

Рекомендация — П2, если пункты 1–4 проходят; иначе П3. Решение человека.

**Список.** Выделение применяется эффектом `ApplyListSelection` (по
`SetSelectedItems`, AB) и сообщается событием; `SelectedItem` без TwoWay —
уходят `_rowsReplacing`, порядок «главная, потом набор» и схлопывание
многовыделения привязкой. Фокус на строку — по
`ItemContainerGenerator.StatusChanged`, без синхронного `UpdateLayout()`
(TECHDEBT `ui.restore` до 500 мс). Жесты мыши (`SelectionController`,
`RubberBandController`, drag) остаются во вью — это механика жеста, их
выход — `ListSelectionChanged`.

**Меню.** Команды принимают `MenuContext` параметром; `ShowContextMenu`
строит `ContextMenuTarget` из контекста. Меню «Операции» в шапке —
контекст по цели в момент открытия.

**Окно.** `ZoneTracker` — причина прихода фокуса (§4.4); рамка области —
по `Keyboard.Zone` из состояния.

### 4.8. Адаптер причуд WPF

| WPF делает | Когда | Ответ |
|---|---|---|
| выделяет родителя при удалении выделенной строки | `TreeViewItem.OnItemsChanged` | П2 / П3: не случается; П1: не отражать, подсветка из состояния |
| выделяет схлопнутый узел | `TreeView.HandleSelectionAndCollapsed` | то же |
| выделяет `TreeViewItem` при фокусе | `TreeViewItem.OnGotFocus` | П2: переопределено; П1: фокус только на строку курсора |
| раскрывает строку по двойному клику | `TreeViewItem.OnMouseLeftButtonDown` (`ClickCount % 2`) | событие `ChevronToggled` (Проводник так же, [В9]) |
| шеврон пишет `IsExpanded` через `IsChecked` TwoWay | шаблон `WanderTreeViewItem` | шеврон — событие, `IsExpanded` OneWay |
| `↑` / `↓` / `PgUp` при пустом выделении — фокус на первую строку | `TreeView.OnKeyDown` | стенд, п. 2 |
| отдаёт фокус окну при удалении элемента с фокусом | `KeyboardDevice.ReevaluateFocus` | `FocusFell(зона)` → `FocusRow` по правилу |
| исполняет пункт меню после закрытия и возврата фокуса | `MenuItem.InvokeClickAfterRender` | предмет в `MenuContext` |
| правый клик выделяет через фокус (если не перехвачен) | — | перехват на нажатии, `RowRightClicked` |
| выбрасывает заменённый объект из `SelectedItems` | `ItemCollection` при `Replace` | выделение применяет модель после сверки (`ApplyListSelection`) |
| присвоение `SelectedItem` схлопывает многовыделение | `Selector` | `SelectedItem` не привязан TwoWay |
| стрелки `DataGrid` идут от текущей ячейки | `DataGrid` | `CurrentCell` ставит исполнитель `FocusRow` (как сейчас) |
| `SelectedItems.Add` линейный — массовое выделение квадратичное | `ListBox`, `MultiSelector` | `SetSelectedItems` (AB) |
| после модального диалога — первый фокусируемый элемент окна | `Window` | `DialogReturn` → `FocusZone` по правилу |
| свёрнутый элемент не держит фокус | панель убрана `Ctrl+B` | `PaneHidden` → `FocusZone(список)` |
| прокручивает к строке с фокусом по обеим осям — панель уезжает вбок к концу длинного имени | `FrameworkElement.OnGotFocus` → `BringIntoView` (у `TreeViewItem` — заголовка) | перехват `RequestBringIntoView` на строке, горизонталь — видимая (P-23, §6.5); П1–П3 одинаково |

### 4.9. Наблюдаемость

- Трасса: строка на событие — `WS <событие> (<причина>) -> <изменённые
  поля>; эффекты: …` — пишется `Log.Detail` под `AppSettings.LogActions`
  (с 2026-09-22 есть: Параметры → Отладка, «Подробно: клавиши, клики,
  выделение»; отдельный `TraceWorkspace` не нужен); без настройки — только
  навигация и операции. Пути в строках — метками, пока не включено
  `LogPaths` (`Core/Logging/LogMask`, §6.5).
- Смена цели пишется, когда меняется **производное** значение, одной
  строкой (сегодня `Target:` дважды на нажатие, Н11).
- Контрольные строки 0.4.1 — прообраз, снимаются на шаге 11: `Target:`,
  `tree: highlight … (причина)`, `Delete: no target` с 2026-09-22 уже под
  `LogActions`, рядом с `Key:`, `Click:`, `Menu:`, `Focus:`, `Selection:`
  из `MainWindow` и `MainViewModel.SelectedEntries`; `Listing follows` и
  `Paste: … (how)` пишутся всегда.

### 4.10. Тесты и таблица поведения

Правила чек-листов QA («Дерево и закладки», «Фокус, выделение и
клавиатура») и находки §3.11 — последовательности событий с ожидаемым
состоянием, до кода модели. Черновик ниже; человек утверждает его на
шаге 1 вместе с ответами на §6.2 — это приёмка модели. `[Вn]` — зависит от
решения.

**Панели (P).**

- P-1. Клик по X в «Дисках» → `Navigate(X, Drives)`; после приземления
  место и курсор X, клавиатура в «Дисках», цель X.
- P-2. Клик по строке открытой папки → навигации нет, цель — она.
- P-3. `↓` в панели («стрелки открывают» выключено) → курсор на следующую
  видимую; навигации нет; цель — строка курсора; подсветка [В1].
- P-4. «Стрелки открывают» включено: одиночное нажатие — переход сразу;
  серия (< 250 мс после перехода) — один переход через 90 мс покоя;
  клавиатура ушла из панели во время серии — перехода нет.
- P-5. `Enter` в панели → переход на строку курсора.
- P-6. Открыта `A\B`, шеврон закрывает `A` → папка `A\B`, место `A\B`
  (скрыто), цель и выделение списка не меняются, клавиатура не двигается.
- P-7. `Tab` в панель, место в закрытой ветке → ветка раскрыта до места,
  курсор на нём.
- P-8. `Tab` / `Ctrl+1` в панель без места → курсор [В5], навигации нет
  даже со «стрелки открывают».
- P-9. `Alt` + шеврон → раскрыть детей / свернуть потомков; строка на
  месте.
- P-10. Переход из списка → место в панели-источнике (спуск наследует
  панель); недостижимо в закладках → «Диски».
- P-11. `Alt+←` / `Alt+→` → место в панели записи истории.
- P-12. `Relocated(A\B → C\B)`, открыта `A\B` → папка `C\B`, место `C\B` в
  той же панели; «Диски» сами не раскрываются, если панель — закладки.
- P-13. `Removed(A\B)`, открыта `A\B`, клавиатура в панели → папка `A`,
  место и курсор `A`, цель `A`; второй `Delete` — про `A`.
- P-14. `Removed(X)` не в текущей ветке, курсор на X → курсор на
  следующую строку уровня, иначе предыдущую, иначе родителя; папка не
  меняется.
- P-15. Пересборка закладок (добавить, убрать, указать расположение,
  следование) → место, курсор и раскрытое — по путям на месте (Н7).
- P-16. `Ctrl+↑/↓` на своей закладке → порядок, курсор на переехавшей.
- P-17. `F2` → `Enter` в панели → строка под новым именем на месте,
  ветка не свернулась, курсор и цель — она.
- P-18. Раскрытие ветки → уровень читается фоном; ответ после
  схлопывания отброшен; шеврон незагруженной строки перепроверяется на
  `RefreshFor`.
- P-19. «Скрытые» выключены, курсор на скрытой папке → курсор на
  родителя.
- P-20. Стрелками в «Дисках» на Z, клик по закладке → закладка
  подсвечена, Z остаётся подсвеченной в «Дисках» неактивным цветом (Н5,
  решение человека 2026-09-22, §6.5).
- P-21. Подсветка — своя у каждой панели, не больше строки на панель;
  переход из закладок строку «Дисков» не трогает, переход откуда угодно
  ещё гасит строку закладок; активная подсветка на окне одна —
  инварианты, проверяются после каждого события таблицы [В2]. (Было до
  2026-09-22: «одна подсветка на обе панели».)
- P-22. Открыта папка из закладок, `Ctrl+1` из закладок в «Диски» →
  курсор на строке, которую «Диски» держат; её нет — раскрыть открытую
  папку в «Дисках»; со «стрелки открывают» — переход в строку курсора,
  как стрелкой (решение человека 2026-09-22: «вернулись к предыдущей
  папке на диске»). `Ctrl+Shift+E` по-прежнему раскрывает открытую папку в
  её панели.
- P-23. Строка под курсором получает фокус (клик, стрелка, `Ctrl+1`) →
  панель прокручивается к ней вверх-вниз, вбок — нет; вбок к концу
  длинного имени — только с `AppSettings.TreeScrollsSideways` (решение
  2026-09-22; прокрутка вбок руками — `Shift` + колесо).

**Цель (T).**

- T-1. Клавиатура в списке, выделено {a, b} → цель {a, b}.
- T-2. Клавиатура в списке, ничего не выделено → цель None: `Delete`,
  `Enter`, `F2` — ничего; `Ctrl+V` — в открытую; `Alt+Enter` — свойства
  открытой (Н4) [В3].
- T-3. Клавиатура в панели на X → цель X; выделение списка [В2].
- T-4. Открыта A, правый клик по Y → меню о Y: «Свойства», «Открыть в
  терминале», «Вставить», «Открыть», «Переименовать» — про Y (Н1); место
  на A; рамка на Y [В4].
- T-5. Меню закрыто `Esc` → цель снова по зоне, предмета нет.
- T-6. Открыта корзина, правый клик по обычной папке в дереве → меню
  папки со всеми глаголами (Н3).
- T-7. `Ctrl+V`, клавиатура в панели на X → в X; «Вставить» в меню
  строки-папки списка → в неё; `Ctrl+V` в списке с выделенной папкой → в
  открытую.
- T-8. `F2`: цель — строка панели → редактор в панели; одна строка
  списка → в списке; две и больше → групповое.
- T-9. `Backspace` с клавиатурой в панели → папка — родитель, курсор на
  нём; `Delete` — про родителя, не про строку списка (Н12) [В1, В2].
- T-10. Клик по пустому месту списка → цель None, остатка цели панели нет.
- T-11. «Создать ярлык» из меню строки панели → [В8] (Н2).
- T-12. Возврат из меню, активация окна, падение фокуса выделение списка
  не трогают [В2].

**Клавиатура (K).**

- K-1. Строка с клавиатурой ушла из списка → клавиатура на преемнике, без
  прокрутки.
- K-2. То же при неактивном окне (удалили в другой программе) → после
  активации.
- K-3. Операция за диалогом кончилась (вставка, групповое
  переименование, удаление) → клавиатура на приземлившейся строке, если
  была в списке или нигде; в панели — остаётся в панели.
- K-4. Редактор закрыт `Enter` → клавиатура на переименованной строке,
  когда та долетит; кликом мимо — там, куда кликнули.
- K-5. Смена вида с клавиатурой в списке → на каретке в новом виде.
- K-6. `Ctrl+Z` вернул файл, клавиатура в списке на другой строке → на
  вернувшемся; клавиатура в панели — остаётся.
- K-7. Перечитывание (сторож, `F5`) не двигает клавиатуру и не
  прокручивает, пока строка под клавиатурой на месте; заменённая строка
  (новые метаданные) — клавиатура на ней же (долг 0.4.x).
- K-8. Панель папок убрана с клавиатурой в ней → в список.
- K-9. Меню закрылось → клавиатура там, где была; цель по зоне.
- K-10. Строка с клавиатурой ушла из панели → курсор на соседа, фокус на
  него.
- K-11. Фильтр по оценке спрятал текущую строку (оценку сменили под
  фильтром) → выделена следующая видимая, клавиатура на ней, без
  прокрутки — как после удаления (В6, решено 2026-09-22; в коде 0.4.x
  сделано `SettleDeparture`).
- K-12. `Ctrl`-клик снял выделение со строки с клавиатурой, затем
  перечитывание → клавиатура остаётся на ней (TECHDEBT
  `keyboardLeftBehind`).

**Список (L).**

- L-1. Приход в папку: память выделения → та строка, с прокруткой;
  подъём → папка, из которой вышли.
- L-2. Перечитывание той же папки без намерения → выделение на месте, без
  прокрутки и фокуса (Н9).
- L-3. Выделенную удалили снаружи → преемник выделен, каретка на нём.
- L-4. Выделенную спрятал фильтр, «скрытые» или свёртка спутников →
  преемник выделен, клавиатура на нём, без прокрутки — как L-3 и L-6 (В6,
  решено 2026-09-22).
- L-5. Выделенную переименовали снаружи → выделено новое имя [В7].
- L-6. `Delete` → следующий уцелевший, клавиатура на нём.
- L-7. Вставка, групповое переименование → выделено пришедшее,
  клавиатура на нём.
- L-8. Намерение для другой папки не трогает выделение открытой и не
  срабатывает позже само (Н8).
- L-9. Оценка на N выделенных → после замены строк выделены все N и в
  списке, и в VM (Н8).
- L-10. Выделение переживает смену вида: три строки через четыре вида.
- L-11. Пока правится имя и пока коммит правки не вернулся — тик сторожа
  ждёт (TECHDEBT).
- L-12. Создание папки → выделена, редактор открыт.

**Вид (V)** — после §6.3.

- V-1. Закреплена → её вид.
- V-2. Не закреплена, автогалерея, снимков не меньше порога → «Галерея»,
  не закрепляется.
- V-3. Иначе → вид по умолчанию из настроек (Н13).
- V-4. Ручной выбор закрепляет только эту папку; умолчание не меняется.
- V-5. «Автоматически» → закрепление снято, вид выбран заново.
- V-6. «Сделать видом по умолчанию» → настройка [В13].
- V-7. Переименование или перенос папки Wander'ом → закрепление следует.
- V-8. Переименование снаружи → подхват по дате создания (Z1).
- V-9. Корзина, архив → то же правило (Н14) [В11].
- V-10. `F5` вид не меняет — только приход.

**Путь (F).**

- F-1. Перенос открытой папки → листинг, история, закладки, строки
  панелей, MRU адреса, закрепление вида, память выделения — по новому
  пути.
- F-2. Вырезанные файлы внутри перенесённой папки → буфер следует или
  вставка честно говорит, где они теперь [В22, §4.12].
- F-3. `Ctrl+Z` переноса → всё выше обратно.

Что остаётся глазам: адорнеры и их размер, настоящий клик мимо
редактора, перетаскивание мышью, цвета подсветки — список «что
посмотреть» в отчёте шага.

**Харнесс.** Шаги `post <событие>` (JSON) и `assert-state <поле>: <значение>`
над `WorkspaceController`, `assert-focus <зона> [строка]` для исполнителя.
Взаимодействие проверяется без WPF-ввода; `tree-target` перестаёт
копировать логику цели. Окно харнесса никогда не активно (TECHDEBT) —
активность окна становится фактом, `post WindowActivated` доводит K-2 до
проверки.

### 4.11. Вид папки и база параметров (AG, Z1, H1)

**`ViewChoice`** (Core, `Folders/`, тест) — заменяет `AutoSelectViewMode`:

```
ViewChoice.Decide(факты) -> (вид, причина)
  факты: закрепление (Z1) | подсказка FolderType (H1) | папка со снимками
         (ImageFolderProbe, считается только если нужно) | AutoGallery |
         DefaultViewMode | место: диск | архив | корзина | результаты
  закреплена                                   -> (закрепление, Закреплён)
  результаты поиска                            -> вид не меняется
  AutoGallery и (снимки или FolderType=Pictures) -> (Галерея, АвтоСнимки)
  иначе                                        -> (DefaultViewMode, ПоУмолчанию)
```

Зовётся на приходе в любую папку, в том числе корзину и архив (Н14).
Причина — в подсказке меню «Вид»: «закреплён», «авто: снимки», «по
умолчанию».

**Меню «Вид»** (компактно, столп 1): четыре вида с галочкой — выбор
закрепляет за этой папкой; ниже «Автоматически» (снять закрепление) и
«Сделать видом по умолчанию» (в настройки); к чему относится выбор — в
подсказке пункта и в подписи группы «Эта папка». `Ctrl+Shift+1/2/6/7` —
закрепление. Настройки: вид по умолчанию, «Автогалерея» и порог — как
есть. `SessionState.ViewMode` и `ManualViewModes` уходят после миграции
(В12).

**`FolderSettingsBook`** (Core, `Folders/`, тест) — записи о папках:

```
FolderRecord   путь, дата создания (UTC, может быть неизвестна),
               день последнего захода, вид (или null); дальше — [В16]
Find(путь) / Touch(путь, создана, сегодня) / SetView(путь, вид)
Follow(откуда, куда)                       — PathRewrite.Under по ключам
Adopt(путь, создана, exists)               — подхват внешнего переименования
Trim(потолок)                              — LRU по дню захода
```

- Хранение: `IFolderSettingsStore` (Core, `Persistence/`) →
  `JsonFolderSettingsStore` (Platform, рядом с `JsonAppStateStore`),
  файл `folders.json` в data-dir, своя версия формата (согласуется с AD11:
  версии по блокам), атомарная запись, `--yield` = только чтение.
  REJECTED «бинарный state.json» прямо предлагает отдельный файл для
  тысяч записей.
- Загрузка на пуле при старте параллельно первому листингу; выбор вида
  первого прихода ждёт её.
- `Touch` пишет, только если сменился день или запись новая — иначе
  каждая навигация была бы записью файла; запись по дебаунсу.
- Подхват внешнего переименования: у пути записи нет → записи с той же
  датой создания (100 нс, NTFS), чей путь пропал (`stat` на пуле), на
  том же томе, ровно одна → ключ переписывается. Копия с сохранёнными
  датами (robocopy `/DCOPY:T`, распаковщики) даёт двух кандидатов —
  подхвата нет.
- Дата создания: одна `GetCreationTimeUtc` на приход, на пуле вместе с
  листингом; для записей архива и корзины — неизвестна, ключ — путь.
- Чистка: потолок по дню захода; без `stat` (шары, флешки — не ждать).
- Миграция: `ManualViewModes` (до 128) → записи без даты создания,
  дата допишется на следующем заходе.

**H1 — чужой `desktop.ini`** (P3, после AG и Z1, [В17]): только читать
`[ViewState] FolderType=` (Pictures / Photos → подсказка «снимки»), если
перечисление видело `desktop.ini` (флаг до фильтра видимости — файл
скрытый и системный); разбор строки — Core, чтение — на пуле с листингом.
Писать — нет (решение Z1). Реестр `Bags` Проводника — недокументированный
формат, в REJECTED.

### 4.12. Следование за путём (AD3-хвост)

| Кто помнит путь | Переименование / перенос Wander'ом | Внешнее |
|---|---|---|
| история (`NavigationService`) | да (`RewritePaths`) | нет |
| открытая папка | да (листинг по новому пути) | панель «папки нет» |
| закладки | да (`Bookmarks.Follow`) | серая строка |
| строки и раскрытые ветки панелей | да (`Follow` + `RefreshFor`) | по `F5` и сторожу открытой папки |
| MRU адресной строки (`RecentPaths`) | **нет** | нет |
| закрепления вида / Z1 | **нет** | Z1 — по дате создания |
| память выделения `FolderSession` (64) | **нет** | нет |
| намерение приземления | нет — живёт до ближайшей навигации | нет |
| буфер обмена (вырезанное внутри перенесённой папки) | **нет** — вставка не найдёт файлы | нет |
| результаты поиска | нет, `PruneMissingAsync` убирает | нет |
| шаги отката | нет | нет |

`PathFollowing` (Core, тест): `Relocated(откуда, куда)` → переписи
каждому держателю по `PathRewrite.Under`; исполнитель применяет. Новые:
`RecentPaths.Rewrite`, `FolderSession.RewriteMemory`, `FolderSettingsBook.
Follow`; буфер — [В22].

### 4.13. Перетаскивание вглубь (U1, U2)

- **`DragHover`** (Core, `Layout/`, часы подставлены):
  `Decide(сейчас, цель под курсором, наведение с момента, задержка) ->
  Ждать(до) | Раскрыть(путь) | Войти(путь) | Ничего`. Та же цель — тот же
  путь под курсором, дрожь внутри строки не сбрасывает; смена цели,
  уход из окна, бросок — сброс. Панель: свёрнутая папка с детьми →
  раскрыть. Список: папка → войти [В18]; ярлык на папку → в неё; архив,
  корзина, перетаскиваемая папка и её потомки — нет. Задержка — от
  `SystemParameters.MouseHoverTime` (400 мс) ×2, глазами.
- **`EdgeScroll`** (Core, `Layout/`): `Velocity(до края, зона, максимум)`,
  `Step(скорость, прошло мс)`. Ориентир Windows — `DD_DEFSCROLLINSET` 11 px,
  интервал 50 мс; PLAN хочет 20–30 px [В20]. Одно правило для списка
  (четыре вида), обеих панелей и рамки выделения (TECHDEBT).
- Исполнение: таймер в `DropTargetController` (общий для поверхностей) и
  в `RubberBandController`; раскрыть — эффект модели панели; войти —
  `Navigate` с `RightPane`.
- Проверять глазами: ввод не эмулируется.

### 4.14. Массовое выделение (AB) и оценка на значках (J4)

- **AB.** `Controls/FileListBox : ListBox` и `Controls/FileDataGrid :
  DataGrid` открывают защищённый `SetSelectedItems`; четыре правки
  типов в `FileListView.xaml`; `SetListSelection` — одним вызовом (все
  случаи или от порога — Фабле по замеру `ui.selection-apply`). Закрывает
  TECHDEBT «`List_SelectionChanged` пересобирает список выделенного».
- **J4.** «Крупные значки» — тот же приём, что в галерее: пустой
  `ContentControl x:Name="Badge"` + `DataTrigger` на `Rating`
  подкладывает шаблон бейджа (ограничения шаблона ARCHITECTURE «Подсветка
  плитки и что шаблону нельзя» соблюдены; `LAYOUT … visuals` — сравнить до
  и после). Цвета бейджа — на светлом фоне окна, не `GalleryPalette`.
  «Плитка» — [В21].

### 4.15. Раскладка кода

| Где | Что |
|---|---|
| `Core/Workspace/` (новая) | `WorkspaceState`, события, эффекты, `WorkspaceReducer`, `TargetRules`, `KeyboardRules`, `MenuContext`, `PreviewSubject`, формат трассы |
| `Core/Panels/` (новая) | `PanelState`, `PanelRow`, `Pane`, `PanelRules`, `PanelKeyNavigation` (по стенду), `TreeNavThrottle`; `BranchReconcile` переезжает сюда из `Navigation/` |
| `Core/Listing/` | `ListingArrival` (новый); `ArrivalIntent` + «прокрутить», причина перечитывания; `FolderSession` как есть |
| `Core/Navigation/` | `PathFollowing` (новый), `RecentPaths.Rewrite` |
| `Core/Folders/` (новая) | `ViewChoice`, `FolderSettingsBook`, `FolderRecord`, разбор `FolderType` |
| `Core/Layout/` | `DragHover`, `EdgeScroll` |
| `Core/FileSystem/` | `DirectoryChange` + пара «было → стало» у переименования [В6, В7] |
| Platform | `JsonFolderSettingsStore`; чтение `desktop.ini`; `WindowsDirectoryWatcher.OnRenamed` отдаёт пару |
| App | `Controllers/WorkspaceController` (исполнитель); `Controls/FolderTreeView` (П2) или панель-список (П3); `Controls/FileListBox`, `FileDataGrid`; адаптеры — тонкий код-бихайнд вью; `ZoneTracker` в окне |

Уровни `deps.ps1`: `Panels` над `FileSystem` и `Navigation`,
`Workspace` над `Listing`, `Panels`, `Layout` и `Menu` — верхний уровень
Core; циклов нет. В App остаются: разводка событий WPF, визуалы и
адорнеры, цикл перетаскивания (`OutgoingDrag`), раскладка
(`VirtualizingWrapPanel`), значки, таймеры-исполнители.

## 5. Правки 0.4.1 и заплатки, которые модель снимет

Все — симптомы отсутствующей модели; при переезде убрать, не переносить:

- `Tree_IsKeyboardFocusWithinChanged` с отложенным возвратом рамки;
- `OnZoneFocusChanged`: отложенное переназначение цели после меню
  (`IsInsideMenu`) и переназначение цели на каждый приход фокуса;
- `OnTreeSelectionChanged`: откат переноса при схлопывании
  (`_restoringSelection`), передача `previous`, `_treeClickNavigates`;
- снятие выделения перед `Children.RemoveAt` в `RefreshChildrenAsync`;
- правило «цель без primary не строка списка» в `ReconcileEntries` и
  условие на `_selectedEntry` в `Refresh`;
- `ExternalTargetFolder`, параметр `PasteIntoSelection`, `TerminalFolder`,
  `PropertiesTarget`, `SelectedPathsOrCurrent`, `OpenCommand` с целью
  панели — заменяются `TargetRules` и `MenuContext`;
- порядок «перечитать панели → закладки → история» в
  `FollowRelocatedAsync` — становится правилом `PathFollowing`;
- `_targetRow` в `FolderTreesView`, маршрут `MainWindow.StartRename` по
  «`SelectedEntry` пуст», `FolderTargeted` → `FileList.ClearSelection()`;
- `IsSyncingSelection` (`_selected` снят 2026-09-22); общая
  `_placeholder` (шеврон — флаг строки, не фальшивый ребёнок);
- `_rowsReplacing`, `AdoptSelection`, `FocusListAfterRestore`,
  `_focusRowAfterRestore`, `_focusFellOutOfTheList`, условия
  `focusFellToTheList` / `keyboardLeftBehind` в `RestoreListSelection`;
- `SyncPreviewSelection`, `ActiveEntry`, `PrimaryForPane`, прямой
  `Preview.SetPrimary` в `SelectExternalPath` → `PreviewSubject`;
- `AutoSelectViewMode`, `RememberManualViewMode`, `_userViewMode` →
  `ViewChoice` + `FolderSettingsBook`;
- копия логики цели в харнессе (`ScenarioRunner.TreeTarget`).

Остаются как есть: `BranchReconcile`, `PathRewrite`, `NavigationService.
RewritePaths`, `RenameAction.MovesOnUndo`, `RenameAdorner`,
`ReprobeChevron` (как факт для правила), `FolderSession`,
`CurrentRowFallback`, `ListingDiff`, `WindowZones`, `GridNavigation`.

## 6. Вопросы и развилки

### 6.1. Открытые вопросы первой редакции — предложения

1. **Своё выделение в панелях или WPF с откатом** → стенд §4.7;
   рекомендация П2 (TreeView без выделения WPF), запасной П3.
2. **Один редьюсер или три** → одно состояние и одна точка входа, модули
   по срезам в фиксированном порядке (§4.5): цель и фокус режут поперёк,
   поэтому состояние одно; «без паутины» дают модули без ссылок вбок.
3. **`FolderSession` как есть или расщепить** → как есть: память выделения
   — про строки списка, не про подсветку панели. Рядом новый
   `ListingArrival` — то, что сегодня в VM.
4. **Стрелки-навигируют и дебаунс** → правило `TreeNavThrottle` с
   подставленными часами решает «сейчас / в момент T / никогда»; таймер —
   исполнителя, срабатывание — событие `ThrottleElapsed`.
5. **Две панели просмотра и `PreviewPair`** → вход — производное
   `PreviewSubject(цель, каретка, строки)`; `PreviewPair.Of` остаётся.

### 6.2. Цель, подсветка, клавиатура — решает человек

- **В1. Что значит подсветка строки панели.** (а) «Где я»: подсветка всегда
  на открытой папке, курсор клавиатуры — рамкой, как каретка в списке,
  цель — строка под рамкой; (б) курсор, пока клавиатура в панели (как
  сейчас), при уходе клавиатуры подсветка возвращается на открытую папку;
  (в) как сейчас, без возврата. **Рекомендация — (б)**: подсветка = то,
  о чём клавиши, как в списке; «не там» после ухода лечится возвратом;
  метка «где я» (жирное имя) — по желанию. **Частично решено человеком
  2026-09-22** (§6.5): подсветка у каждой панели своя, «Диски» держат
  строку, где их оставили, пока открыта папка из закладок, — туда
  возвращает `Ctrl+1`; (а)–(в) решают остальное.
- **В2. Выделение списка, пока клавиатура в панели.** (а) снимать (сейчас:
  «один подсвеченный набор на экране»); (б) хранить неактивным цветом,
  цель — по зоне клавиатуры, как у Проводника. **Рекомендация — (б)**:
  выделение не пропадает от прихода фокуса в панель (меню, активация
  окна, `Tab`), «слетание» невозможно по построению, `Ctrl+2` возвращает
  к рабочему набору. Цена — отменяется правило «один подсвеченный набор»,
  его место занимает «одна активная подсветка».
- **В3. Клавиатура в списке, выделение пусто.** `Delete`, `Enter`, `F2` —
  ничего; `Alt+Enter` — свойства открытой; `Ctrl+V` — в открытую (сейчас —
  про остаток цели панели, Н4). **Рекомендация — да.**
- **В4. Рамка «о чём меню»** на строке панели, по которой кликнули правой
  (подсветка остаётся на месте), кистью каретки — как у Проводника.
  **Рекомендация — да.**
- **В5. `Tab` / `Ctrl+1` в панель, где нет места.** Курсор — на прошлую
  строку курсора этой панели, иначе на первую; без навигации даже со
  «стрелки открывают» (Н6). **Рекомендация — да.** Для `Ctrl+1` из
  закладок в «Диски» человек решил 2026-09-22 (P-22): курсор на строку,
  которую «Диски» держат, и со «стрелки открывают» — переход в неё; для
  `Tab` и закладок вопрос открыт.
- **В6. «Нет в листинге» ≠ «ушёл из папки»** (долг 0.4.x). **Решено
  человеком 2026-09-22: любое исчезновение выделенной строки — как
  удаление**: выделение на следующую, клавиатура за ней, без прокрутки —
  фильтр (оценку сменили под фильтром по звёздам), «скрытые», свёртка
  спутников, удаление снаружи (L-3, L-4, K-11). В коде 0.4.x —
  `SettleDeparture` без исключения для строки, которую лишь спрятал
  фильтр. Варианты были: (а) по причине перечитывания снимать выделение
  без преемника; (б) `stat` пути; (в) как было. Переименование снаружи —
  В7, уточнение поверх: пара «было → стало» известна — за новым именем.
- **В7. Файл переименован снаружи** → выделение идёт за новым именем, как
  у Проводника. Нужна пара «было → стало» в `DirectoryChange` (у
  `FileSystemWatcher.Renamed` она есть). **Рекомендация — да.**
- **В8. «Создать ярлык» для строки панели**: (а) рядом с папкой, в её
  родителе; (б) в открытую папку (сейчас, Н2); (в) не предлагать в меню
  строки панели. **Рекомендация — (в)**: меню короче, ярлык в чужой
  папке неожидан.
- **В9. Двойной клик по строке панели** раскрывает / сворачивает (WPF, так
  же у Проводника) после перехода первым кликом. **Рекомендация —
  оставить**, но событием модели.

### 6.3. Вид и параметры папки — решает человек

- **В10. Наследование вида подпапками** — нет (как в PLAN AG).
- **В11. Корзина и архивы** — то же правило: закрепление по пути,
  автогалерея в архиве по именам, корзина — вид по умолчанию.
  **Рекомендация — да.**
- **В12. Начальный `DefaultViewMode` при обновлении** — последний ручной
  выбор из `SessionState.ViewMode`, иначе «Таблица». **Рекомендация — да.**
- **В13. «Сделать видом по умолчанию»** снимает закрепление этой папки
  (иначе она не пойдёт за следующим умолчанием). **Рекомендация — да.**
- **В14. Ключ Z1** [решено: путь + дата создания]: дата — только для
  подхвата внешнего переименования, основной ключ — путь. Вариант точнее
  — `FileId` тома (не путается с копией, сохранившей даты; на шарах
  ненадёжен, нужен дескриптор папки). **Рекомендация — дата сейчас,
  `FileId` — если подхват ошибётся на деле.**
- **В15. Потолок и старение Z1** — 2000–5000 записей по дню захода, без
  `stat` при чистке; число — по замеру загрузки `folders.json`.
- **В16. Что в Z1 после вида** — сортировка, фон галереи, размер плиток:
  порядок и нужно ли.
- **В17. H1**: читать `FolderType` чужого `desktop.ini` как подсказку
  «снимки» (P3, после AG + Z1) или в REJECTED. Реестр `Bags` — в REJECTED.
  **Рекомендация — читать, по запросу.**

### 6.4. Перетаскивание и мелочи — решает человек

- **В18. U1 в списке** — входить в папку при удержании (Finder — да,
  Проводник — нет). **Рекомендация — да**, только папки, задержка дольше,
  чем в панели.
- **В19. Откат раскрытого или вошедшего**, если бросок не случился.
  **Рекомендация — нет**: раскрыто жестом человека, «Назад» есть.
- **В20. U2**: зона 24 px, скорость до ~1500 px/с, рост квадратичный; то
  же для рамки выделения. Цифры — глазами.
- **В21. J4 в «Плитке»**: звёзды второй строкой (`TileSecondLineConverter`,
  без нового визуала), бейдж или ничего. **Рекомендация — второй строкой.**
- **В22. Вырезанное внутри перенесённой папки**: буфер следует
  (`PathFollowing`) или вставка честно говорит «файлы уехали».
  **Рекомендация — следует**: перенос сделал сам Wander.
- **В23. Панели и чужие изменения** (TECHDEBT: сторож только на открытой
  папке): (а) один `SHChangeNotifyRegister` на всё — уведомления оболочки
  о папках, дёшево, столп интеграции, но изменения из консоли и скриптов
  не видит; (б) `FileSystemWatcher` на каждый раскрытый уровень — дорого,
  лимиты на шарах; (в) `F5` и строка в GUIDE. **Рекомендация — (в)
  сейчас, (а) — пунктом BACKLOG** вместе со значком корзины (TECHDEBT
  «Корзина: статичная иконка» просит тот же вызов).

### 6.5. Решено по ходу, до модели (2026-09-22)

Правки в коде 0.4.x по просьбе человека; модель обязана повторить их
поведение, таблица §4.10 уже поправлена.

- **Исчезла выделенная строка — как удаление** (В6, L-4, K-11).
  `MainViewModel.SettleDeparture` берёт преемника (`CurrentRowFallback`)
  и когда строку лишь спрятал фильтр: выделение на следующую, клавиатура
  за ней (`CurrentRowLeft`), без прокрутки. Перенос в модель —
  `ListingArrival` (шаг 7): у причины перечитывания исключения «фильтр» нет.
- **Подсветка у каждой панели своя** (В1 частично, Н5, P-20…P-22).
  `FolderTreesController._selected` снят: подсветка гасится по панели
  (`Unlight` — у строк спрашивают, кто подсвечен, а не помнят; стрелки
  WPF память портили). `ExpandTo` из закладок строку «Дисков» не трогает —
  она остаётся неактивной подсветкой («фантомной»); переход откуда угодно
  ещё гасит строку закладок. `RevealIn` трогает только свою панель.
  `FolderTreesView.RevealAndFocus`: «Диски» при открытой папке из закладок
  — фокус на свою строку, со «стрелки открывают» — переход в неё; строки
  нет — раскрыть открытую папку. В модели: `PanelState.Caret` «Дисков»
  живёт, пока место — в закладках; `Ctrl+1` — событие `ZoneEntered(хоткей)`
  с правилом P-22.
- **Панель не уезжает вбок к длинному имени** (P-23). WPF прокручивает к
  строке, получившей фокус (`FrameworkElement.OnGotFocus` → `BringIntoView`
  заголовка), по обеим осям. `FolderTreesView.TreeViewItem_RequestBringIntoView`
  (стиль `WanderTreeViewItem`) перевыпускает запрос с горизонтальной частью,
  равной видимой, — вверх-вниз как было. Флаг
  `AppSettings.TreeScrollsSideways`, по умолчанию выключен. Причуда WPF —
  строкой в §4.8 для любого из П1–П3 (у П3 то же на `ListBox`).
- **Журнал: подробности и пути — по флагам отладки.**
  `AppSettings.LogActions` → `Log.Details`: трасса действий (`Key:`,
  `Click:`, `Menu:`, `Focus:`, `Selection:`) и контрольные строки 0.4.1
  (`Target:`, `tree: highlight`, `Delete: no target`) — только с ним.
  `AppSettings.LogPaths` → `Log.RevealPaths`: без него пути и имена в логе
  и в отчёте о падении — метки `Core/Logging/LogMask`. Трасса модели
  (§4.9) — под тем же `LogActions`, `Log.Detail`.

## 7. Порядок работ

Каждый шаг — свой коммит с зелёным `check.bat`; Фабле ведёт, человек — по
каждому шагу (PLAN, блок 2). Сценарии харнесса — называются, запускаются
по слову.

1. **Воспроизведение и таблица поведения.** Человек — прогон по гипотезам
   §8 с логом сессии. Фабле — сверка Н1–Н17 (глаза, лог), статусы в
   §3.11; таблица §4.10 с ответами на §6.2 — утверждена человеком.
   Опционально контрольная строка `Zone: <откуда> -> <куда>` в
   `OnZoneFocusChanged`. Выход: приёмка модели.
2. **Цель и контекст меню.** Core `Workspace/TargetRules`, `MenuContext`,
   тесты T-1…T-12. VM: зона клавиатуры (сообщает окно), курсор панели
   (сообщает панель), производная `Target`; команды берут цель или
   `MenuContext` параметром; `ContextMenuTarget` — из контекста. Снимается
   §5 про цель и меню; `tree-target` — событием. Закрывает Н1–Н4, Н11,
   Н12, по В2 — Н10. Задето: `focus-keys`, `tree-bookmarks`; глазами — QA
   «Фокус…» и «Дерево…», пункты про цель и меню.
3. **Модель панелей в Core рядом со старым кодом.** `Panels/PanelState`,
   `PanelRow`, `PanelRules`, `TreeNavThrottle`; тесты P-1…P-21. Старый
   код не трогается.
4. **Стенд контрола панели** (§4.7) — параллельно шагу 3; решение
   человека: П1 / П2 / П3.
5. **Панели на модели.** `TreeNodeViewModel` → проекция строки без
   ввода-вывода; чтение уровня — эффект `ReadBranch` на пуле, ответ —
   `BranchRead` с эпохой (асинхронный спуск, TECHDEBT «синхронное
   раскрытие»); `FolderTreesController` — исполнитель и проектор;
   `BookmarksController` — пути закладок; `FolderTreesView` — адаптер
   ввода; `IsSelected` / `IsExpanded` OneWay, шеврон — событие; §5 про
   панели снимается. Закрывает Н5–Н7, Н16. Задето: `tree-bookmarks`,
   `focus-keys`, `smoke-walk`; глазами — QA «Дерево и закладки» целиком и
   «Скорость» (зажатая стрелка).
6. **Клавиатура.** `KeyboardRules`, `ZoneTracker`; тесты K-1…K-12; снимаются
   флаги фокуса вью и `FocusListAfterRestore`. Может слиться с 5 или 7 —
   по размеру диффа.
7. **Список.** `ListingArrival`, `ArrivalIntent` + «прокрутить» и только
   для открытой папки (Н8), причина перечитывания, пара переименования от
   сторожа (В6, В7), тик ждёт идущее переименование; VM исполняет
   решение; `SelectedItem` без TwoWay; AB. Тесты L-1…L-12. Закрывает долги
   0.4.x, Н8, Н9. Задето: `focus-keys`, `watcher`, `file-ops`,
   `smoke-walk`; глазами — QA «Фокус, выделение и клавиатура» целиком.
8. **Вид папки и база параметров** (AG, Z1, AD3-хвост). `ViewChoice`,
   `FolderSettingsBook`, `IFolderSettingsStore` + Platform, миграция,
   `DefaultViewMode`, меню «Вид», `PathFollowing` (+ MRU, память
   выделения, буфер по В22). Тесты V-1…V-10, F-1…F-3. От шагов 2–7 не
   зависит — может идти первым после §6.3; механика — кандидат для Опуса.
   Задето: `state-upgrade`, `smoke-walk`; глазами — меню «Вид», две
   папки, перезапуск, переименование закреплённой.
9. **Перетаскивание вглубь** (U1, U2) — после 5 и 7; `DragHover`,
   `EdgeScroll` с тестами; проверка глазами.
10. **J4 и H1.** J4 — в любой момент (шаблон); H1 — по В17.
11. **Харнесс и доки.** `post` / `assert-state` / `assert-focus`; трасса
    под `LogActions` (`Log.Detail`); контрольные строки 0.4.1 сняты; ARCHITECTURE
    (раздел о модели окна), DONE, QA (матрица «трогал → проверь»);
    этот файл удаляется.

Зависимости: 1 → 2 → (3 ∥ 4) → 5 → 6 → 7 → 9; 8 и 10 — независимо; 11 —
последним. PLAN AN перечисляет прежний порядок (меню отдельным шагом
после панелей) — поправить по решению человека.

**Мера до** (`metrics.ps1` снимает перед шагом 2 и после шага 11, одним
измерением):

| Мера | Сейчас |
|---|---|
| `MainViewModel.cs` / `FileListView.xaml.cs` / `FolderTreesView.xaml.cs` / `MainWindow.xaml.cs` / `TreeNodeViewModel.cs` | 5374 / 1867 / 1379 / 1364 / 591 строк |
| полей состояния в двух вью | 17 + 16 |
| писателей выделения и цели в VM | 9 мест + привязка TwoWay |
| привязок TwoWay на данные модели | `IsSelected`, `IsExpanded`, `SelectedItem` ×4 |
| решений на приоритете диспетчера | 3: цель после меню (`Background`), возврат рамки (`Input`), пункт меню (`Render`) |

**Критерии ревью блока** сверх FINALIZING п. 2: ни одной TwoWay-привязки
на то, чем владеет модель; ни одного `BeginInvoke` как условия
правильности (только исполнение и раскладка); правила Core не читают
`Keyboard`, `Mouse`, часы, диск; каждая строка таблицы §4.10 и каждая
находка §3.11 — тест или строка «глазами» с причиной; одна строка
трассы на событие; харнесс не копирует логику цели.

## 8. Незакрытое сейчас

«Слетание выделения» после правок 2026-09-22 воспроизводится у человека,
в логе не видно. Следующий прогон — с контрольными строками: `Target:`
(смена цели), `tree: highlight … (sync | branch closed … | no gesture)`
(смена подсветки без клика), `Listing follows` (листинг перенаправлен без
навигации). Ловим: последовательность действий человека рядом с этими
строками, время с точностью до секунды.

С 2026-09-22 для такого прогона — Параметры → Отладка: «Подробно:
клавиши, клики, выделение» (без него `Target:` и `tree: highlight` не
пишутся) и «Писать пути к файлам» (иначе пути — метки). Последовательность
действий тогда в том же логе — `Key:`, `Click:`, `Menu:`, `Focus:`,
`Selection:`; записывать её руками не нужно. Гипотеза 6 ниже с того же
дня — правило (P-20), не ошибка.

Гипотезы по чтению кода — что может выглядеть как «слетание». Первые
четыре снимают выделение списка приходом фокуса в панель
(`FolderTargeted` → `FileList.ClearSelection()`), остальные разводят
подсветку и выделение без этого:

1. `Tab` или `Ctrl+1` в панель без подсвеченной строки → `Target: <первая
   строка>` (Н6).
2. Клик по строке дерева, пока в списке выделен файл → `Target: <старая
   подсветка>` перед `Navigate (Drives)` (Н10, Н11).
3. `Alt+Tab` обратно, когда логический фокус был в панели → `Target:` без
   действия в окне (активация восстанавливает фокус в панель).
4. Правый клик по строке панели и `Esc` → два `Target:` (нажатие и
   отложенный возврат).
5. Добавить или убрать закладку, когда открытая папка из закладок —
   подсветка в закладках пропала (Н7).
6. Стрелками в «Дисках», затем клик по закладке — две подсветки (Н5; с
   2026-09-22 так и задумано, P-20).
7. «Вставить» в подпапку из меню, затем оценка на нескольких выделенных —
   в списке подсвечена одна (Н8).

## 9. Долги и TECHDEBT → шаги

| Пункт | Шаг |
|---|---|
| долг 0.4.x: сторож с выделением прокручивает список | 7 (L-2, K-7) |
| долг 0.4.x: «нет в листинге» ≠ «ушёл» | В6 решён 2026-09-22 (преемник, §6.5); 7 — В7 и перенос в модель |
| AD3-хвост: MRU адреса, закрепления вида | 8 (`PathFollowing`, Z1) |
| «Список…»: `List_SelectionChanged` пересобирает выделенное | 7 (AB) |
| «Список…»: какой флаг превращал эхо выделения в навигацию | 5 (флагов нет) |
| «Список…»: `ui.restore` до 500 мс | 7 (фокус по генератору) |
| «Список…»: `SelectExternalPath` — `GetEntry` на UI | 2 (цель — путь) |
| «Список…»: бросок из `CollectionChanged` глотается | 7 (ловить в `ApplyAsync`, писать сразу, перечитать) |
| «Список…»: `ListingDiff` — квадрат и `MaxReconciledChanges` без `Move` / `Replace` | 7 (правка в Core, тест) |
| «Список…»: правка на ходу и тик сторожа | 7 (L-11) |
| «Список…»: ушла строка с рамкой, остальное выделенное осталось | 6–7 (K-1) |
| «Список…»: `keyboardLeftBehind` и обычное перечитывание | 6–7 (K-12) |
| «Список…»: фильтр по оценке спрятал текущий снимок | закрыт 2026-09-22 (§6.5); 6–7 — K-11 тестом модели |
| «Список…»: `Shift`+клик от каретки, `Ctrl`+стрелки на краях | вне блока (якорь и курсор списка отдельно от фокуса — по запросу) |
| «Список…»: адрес берёт фокус отложенно | 6 (`FocusZone` исполнитель) |
| «Список…»: рамка выделения не тянется за экран | 9 (U2) |
| «Список…»: `IsCutRow`, `IconImageCache`, `AsyncIcon`, `TileLayout`, компаратор имён, ошибки привязки, `Alt` над полосой | вне блока, TECHDEBT |
| «Дерево…»: раскрытие перечисляет уровень синхронно | 5 |
| «Дерево…»: `LoadRoots` ходит на диск до окна (`IsReady`, `VolumeLabel`) | 5 (корни — тоже `BranchRead`) |
| «Дерево…»: архив не появился узлом | 1 (проверить) |
| «Дерево…»: сторож только на открытой папке | [В23], после 5 |
| «Дерево…»: пропавшая закладка сереет с пересборкой | 5 (`Missing` — факт строки, `Removed` его ставит) |
| «Дерево…»: `_placeholder` подписывают заново | 5 (заглушки нет) |
| «Дерево…»: `F2`, возврат клавиатуры, меню не покрыты харнессом | 11 (`post` / `assert-state`) |
| «Дерево…»: ширина строки закладок | вне модели (шаблон), попутно с 5 |
| «Дерево…»: drop на закладку = «добавить в закладки» | 1 (проверить: по коду drop на строку-папку копирует в неё) |
| «Дерево…»: левый клик не делает целью | 1 (по коду устарело, Н17) |
| PLAN «Чистки»: `FileListView.xaml.cs`, `TreeNodeViewModel` | 5–7 (худеют переездом правил; мера — §7) |
