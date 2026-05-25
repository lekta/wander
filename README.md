# Wander

Кастомный файловый проводник для Windows 11 на WPF / .NET 10.

Личный проект, альтернатива встроенному Explorer. На текущем этапе — MVP
с навигацией и базовыми файловыми операциями.

## Столпы проекта

1. **UX**. Смысл всего проекта — сделать проводник лучше встроенного:
   убрать лишнее (планшетоориентированные элементы Win11, навязчивые
   ссылки на OneDrive, выпячиваемые Microsoft сервисы), починить баги
   Win11 Explorer (самосворачивающиеся ветки дерева, тормоза, потеря
   фокуса после операций), добавить удобств.
2. **Надёжность**. Любая деструктивная операция (удаление, перемещение,
   перезапись, переименование системных путей) сопровождается
   диалогом подтверждения с **Cancel по-умолчанию** — Enter и Esc
   отменяют. Случайно удалить или перенести системную папку
   не получится.

## Возможности (MVP)

- Дерево дисков и папок с lazy-load по раскрытию.
- Список файлов с колонками Name / Type / Size / Modified.
- Навигация: Back / Forward / Up, адресная строка с Enter.
- Открытие файлов через системную ассоциацию (двойной клик).
- Контекстное меню: Open, Cut, Copy, Paste, Delete, Rename, New folder.
- Хоткеи: `Alt+←/→/↑`, `F5`, `Del`, `Ctrl+C/X/V`.

## Требования

- Windows 10 / 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
  (`winget install Microsoft.DotNet.SDK.10`).

## Сборка и запуск

```pwsh
# Восстановить зависимости и собрать всё решение
dotnet build Wander.slnx

# Прогнать юнит-тесты Core
dotnet test Wander.slnx

# Запустить приложение
dotnet run --project src\Wander.App
```

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

## Кодстиль

Зафиксирован в [`.editorconfig`](.editorconfig). `dotnet format` подхватит
все правила автоматически. Краткая выжимка:

- **1TBS** (Egyptian / One True Brace Style): `{` на той же строке,
  `} else {`, `} catch {`, `} finally {`.
- **Braces обязательны** для всех `if/for/while/foreach`, даже однострочных.
- **File-scoped namespaces**: `namespace Wander.Core.FileSystem;`.
- **Namespaces по доменам**: `Wander.<модуль>.<раздел>` (например,
  `Wander.Core.FileSystem`, `Wander.Core.Navigation`), без излишней
  вложенности.
- **var** — везде, где тип понятен из выражения; **явный тип** для
  примитивов (`int`, `bool`, `string`, ...).
- **`_camelCase`** для private полей; **PascalCase** для констант,
  свойств, методов и типов.
- **Отступ** — 4 пробела, не табы.
- **Пустая строка** после `return` / `continue` / `break` внутри метода
  и между логически разными блоками.
- **Две пустые строки** между полями и методами, а также между группами
  методов (инициализация / публичный API / приватные хелперы).

## Roadmap

Что осознанно отложено:

- Иконки файлов и виртуальные папки (нужен Shell API / Vanara).
- Вкладки, поиск, превью.
- Drag & drop из и в систему.
- Перенос долгих операций с UI-потока (большие каталоги пока могут
  подтормаживать).
- Тёмная тема.
- **Надёжность**: guard на системные пути (`C:\Windows`,
  `C:\Program Files`, ...), Undo последней операции, корзина по
  умолчанию вместо permanent delete.
- **UX**: явная борьба с известными багами Win11 Explorer —
  стабильное дерево без самосворачивания, асинхронная загрузка
  больших каталогов, сохранение выделения после операций.

## Лицензия

Личный проект, лицензия пока не выбрана.
