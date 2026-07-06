# Wander

**Кастомный файловый проводник для Windows 11 на WPF / .NET 10.**

Личная альтернатива встроенному Explorer: убрать лишнее, стабилизировать поведение,
добавить удобств. На текущем этапе — рабочая бета с навигацией, файловыми
операциями, корзиной и превью.

<p>
  <a href="https://github.com/lekta/wander/releases/latest">
    <img alt="Latest release" src="https://img.shields.io/github/v/release/lekta/wander?include_prereleases&label=download&sort=semver">
  </a>
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%2F11-blue">
  <img alt="License" src="https://img.shields.io/badge/license-PolyForm%20Noncommercial-orange">
</p>

![Главное окно](docs/screenshots/main.png)

> ⚠️ **Beta 0.2** — ранняя версия. Работает, но возможны баги. Распространяется
> «как есть», без гарантий (см. [Безопасность и гарантии](#безопасность-и-гарантии)).
> 
> Лицензия **PolyForm Noncommercial 1.0.0** - свободное некоммерческое использование

## Как скачать и запустить

1. **[Страницапоследнего релиза](https://github.com/lekta/wander/releases/latest)**
2. Скачай `Wander.exe`
3. Запусти двойным кликом. **Установка не требуется** — это портативная версия,
   ничего в систему не прописывает

При первом запуске Windows SmartScreen может показать предупреждение (файл не
подписан сертификатом) — это нормально для бесплатного ПО: «Подробнее» →
«Выполнить в любом случае».

Хочешь проверить целостность файла — рядом лежит `Wander.exe.sha256`:

```pwsh
(Get-FileHash Wander.exe -Algorithm SHA256).Hash
# сравни с содержимым Wander.exe.sha256
```

### Требования

- **Windows 10 / 11** (x64).
- Ничего ставить не нужно — .NET встроен в exe (self-contained).
- Для превью веб-/markdown-контента используется **WebView2 Runtime**, который
  уже предустановлен в Windows 11 и в актуальной Windows 10 (идёт с Edge). Если
  вдруг его нет — [Evergreen Runtime от Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/).

## Фичи и планы

- Дерево дисков и папок с lazy-load по раскрытию. Более удобная навигация, стабильные деревья файлов
- Иконки и превью файлов (в т.ч. картинки, GIF, WebM, markdown, текст). В планах - превью всего, что может читать система
- Навигация: Back / Forward / Up, адресная строка с Enter. Адресная строка пока слабовата
- Открытие файлов через системную ассоциацию (двойной клик), рабочие системные ссылки (.lnk)
- Контекстное меню пока слабое. В планах рабочая системная версия с возможностью гибкой кастомизации
- **Корзина** вместо безвозвратного удаления по умолчанию. Подтверждение на любой деструктивной операции (**Cancel по-умолчанию**).
- Хоткеи: `Alt+←/→/↑`, `F5`, `Del`, `Ctrl+C/X/V`.
- Вариативное отображение и связи. Есть типы файлов/папок, которые хочется скрывать, чтобы не было шума (desktop.ini, .meta).
- Связи. Те же .meta файлы и другие связанные - относятся непосредственно к своим файлам, и операции над ними должны проводиться совместно 
- Плагины. Если идея зайдёт, можно будет впилить кастомные поведения

Текущее превью: можно через ЛКМ зумить ров-файлы, отображаются мета-данные
![Превью](docs/screenshots/preview.png)

Корзина: просмотр содержимого доступен, но контекстных отображений и операций пока нет
![Корзина](docs/screenshots/recycle-bin.png)


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

## Безопасность и гарантии

- **Вредоносного кода в проекте нет.** Это можно не принимать на слово:
  **весь исходный код открыт для чтения** — бери и проверяй.
- **Релизный `Wander.exe` собирается на серверах GitHub**
  ([workflow](.github/workflows/release.yml)) прямо из кода этого репозитория.
  Логи каждой сборки публичны (вкладка *Actions*), а SHA256 в релизе позволяет
  убедиться, что скачанный файл — ровно тот, что собрал CI.
- **Аналитики, рекламы и т.п нет.** Приложение работает
  локально с твоей файловой системой, интернет не требуется
- **Но:** гарантировать отсутствие *багов* я не могу. Это ранняя бета, и в
  теории ошибка может привести к потере данных. Именно поэтому в проекте есть
  подтверждения на удаление и корзина по умолчанию — но полагайся на бэкапы и
  не тестируй на единственной копии важных файлов. Ответственности за возможный
  ущерб я не несу (см. дисклеймер в [LICENSE](LICENSE)).

## Лицензия

[PolyForm Noncommercial 1.0.0](LICENSE) © Lekta, 2026.

Коротко: приложением можно свободно пользоваться, изучать код, изменять его и
распространять — **в любых некоммерческих целях**. Коммерческое использование
требует отдельной договорённости с автором. ПО поставляется «как есть», без
гарантий и ответственности.

Это source-available лицензия (не OSI open-source): исходники открыты для чтения
и некоммерческого использования, но зарабатывать на них без разрешения нельзя.

---

## Сборка из исходников

### Требования

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
  (`winget install Microsoft.DotNet.SDK.10`).

### Разработка

```pwsh
# Восстановить зависимости и собрать всё решение
dotnet build Wander.slnx

# Прогнать юнит-тесты Core
dotnet test Wander.slnx

# Запустить приложение
dotnet run --project src\Wander.App
```

### Собрать релизный портативный exe

Ровно то, что делает CI, — один самодостаточный файл `Wander.exe`, которому не
нужен установленный .NET:

```pwsh
dotnet publish src\Wander.App `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish
```

Готовый файл — `publish\Wander.exe` (~несколько десятков МБ, весь .NET внутри).

> Трим (`PublishTrimmed`) для WPF не поддерживается — не включать.
> Нужен файл поменьше — можно собрать framework-dependent (без `--self-contained`),
> но тогда пользователю понадобится установленный
> [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

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
