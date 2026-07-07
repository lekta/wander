# CLAUDE.md — заметки для работы над проектом

Внутренний документ для разработки (и для Claude Code при старте сессии).
Пользовательская витрина — в [README.md](README.md); сюда вынесено то, что
стороннему пользователю не нужно.

Где что лежит по отложенной работе (см. память `feedback_backlog_vs_techdebt`):

- **[PLAN.md](PLAN.md)** — активный план текущей работы.
- **[BACKLOG.md](BACKLOG.md)** — отложенные фичи.
- **[TECHDEBT.md](TECHDEBT.md)** — техдолг и мелочёвка по ходу.
- **Roadmap** (ниже) — что осознанно отложено на уровне продукта.

Архитектура и структура проектов описаны в разделах «Структура проектов» и
«Архитектурные правила» в [README.md](README.md).

## Столпы проекта

1. **UX.** Смысл всего проекта — сделать проводник лучше встроенного: убрать
   лишнее (планшетоориентированные элементы Win11, навязчивые ссылки на
   OneDrive и 3д-объекты), починить баги Win11 Explorer (самосворачивающиеся ветки дерева,
   тормоза, потеря фокуса после операций), добавить удобств (навигация через отдельные
   поддеревья, детальный просмотр информации, чтение множеста форматов).
2. **Надёжность.** Любая деструктивная операция (удаление, перемещение,
   перезапись, переименование системных путей) сопровождается диалогом
   подтверждения с **Cancel по-умолчанию**. Случайно снести системную папку
   не получится.

## Кодстиль

Зафиксирован в [`.editorconfig`](.editorconfig). `dotnet format` подхватит
все правила автоматически. Краткая выжимка:

- **1TBS** (Egyptian / One True Brace Style): `{` на той же строке,
  `} else {`, `} catch {`, `} finally {`.
- **Braces обязательны** для всех `if/for/while/foreach`, даже однострочных.
- **File-scoped namespaces**: `namespace Wander.Core.FileSystem;`.
- **Namespaces по доменам**: `Wander.<модуль>.<раздел>`, без излишней вложенности.
- **var** — где тип понятен из выражения; **явный тип** для примитивов.
- **`_camelCase`** для private полей; **PascalCase** для констант, свойств,
  методов и типов.
- **Отступ** — 4 пробела, не табы.
- **Пустая строка** после `return` / `continue` / `break` внутри метода и между
  логически разными блоками; **две пустые строки** между полями и методами.

## Roadmap

Что осознанно отложено:

- Вкладки, поиск.
- Drag & drop из и в систему.
- Перенос долгих операций с UI-потока (большие каталоги пока могут
  подтормаживать).
- Тёмная тема.
- Undo последней операции (Ctrl+Z) для всех файловых действий.
- ~~Guard на системные пути~~ — сделан блок-лист (`SystemPathGuard`,
  2026-07-07); warn-уровень для содержимого системных деревьев — в TECHDEBT.
- Явная борьба с известными багами Win11 Explorer — стабильное дерево без
  самосворачивания, асинхронная загрузка больших каталогов, сохранение
  выделения после операций.


## Структура проектов

```
Wander.slnx
├── src
│   ├── Wander.Core               net10.0          — POCO, интерфейсы, чистая логика
│   │   ├── FileSystem            IFileSystem, FileSystemEntry, FileOperationService, EntryKind
│   │   ├── Navigation            NavigationService
│   │   ├── Shell                 IShellLauncher
│   │   └── ServiceLocator.cs     простой статический локатор
│   │
│   ├── Wander.Platform.Windows   net10.0-windows  — реализации интерфейсов Core
│   │   ├── FileSystem            SystemIOFileSystem (System.IO)
│   │   ├── Shell                 ShellLauncher (Process.Start)
│   │   └── PlatformBootstrapper.cs
│   │
│   └── Wander.App                net10.0-windows  — WPF UI (тонкий слой)
│       ├── ViewModels            MainViewModel, TreeNodeViewModel, ObservableObject
│       ├── MainWindow.xaml(.cs)  главное окно
│       ├── App.xaml(.cs)         регистрация платформенных реализаций
│       ├── RelayCommand.cs
│       └── PromptDialog.cs
│
└── tests
    └── Wander.Core.Tests         xUnit, тесты только для Core
        ├── Fakes/FakeFileSystem.cs
        ├── NavigationServiceTests.cs
        ├── FileOperationServiceTests.cs
        └── ServiceLocatorTests.cs
```

### Архитектурные правила

- `Wander.Core` не зависит от Windows и от UI. Никаких `using System.Windows.*`,
  никаких COM/PInvoke.
- Любая платформенная логика (Windows Shell, COM, нетривиальный System.IO)
  живёт в `Wander.Platform.Windows`.
- WPF — отвинчиваемый слой: ViewModel'и дёргают сервисы Core через
  `ServiceLocator`, никакой WPF-специфики в Core.
- Точка композиции — `App.OnStartup` → `PlatformBootstrapper.RegisterDefaults()`.
- Тесты используют фейковые реализации интерфейсов Core (см. `Fakes/`).
