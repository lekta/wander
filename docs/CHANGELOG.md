# Changelog

Формат — [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
версии — по [SemVer](https://semver.org/lang/ru/). Даты в ISO-формате.

## [Unreleased]

### Added
- **Контекстное меню в духе Windows 10** — весь список сразу, без «Показать
  дополнительные параметры». Отдельные меню для выделения и для фона папки.
- Буфер обмена и файловые операции собраны в подменю **File**
  (`Cut` / `Copy` / `Paste`, `Copy path`, `Copy name`, `Create shortcut`).
- Пункты сторонних приложений (7-Zip, TortoiseGit, Notepad++, антивирусы) и
  системное подменю «Создать» — с их иконками и вложенными подменю.
- Настройки → «Контекстное меню»: выключение расширений целиком или по
  одному, сворачивание в подменю «More options», галочка на каждый пункт
  самого Wander.
- Новые команды: `Open with`, `Show in Explorer`, `Open in Terminal`,
  `Copy path` (`Ctrl` + `Shift` + `C`), `Copy name`, `Create shortcut`.
- Правый клик по элементу вне выделения переносит выделение на него; по
  пустому месту — снимает. `Menu` / `Shift` + `F10` открывают меню
  с клавиатуры.

### Changed
- `Alt` + `Enter` без выделения открывает свойства текущей папки.
- Создание ярлыков (меню и `Alt` + перетаскивание) откатывается через
  `Ctrl` + `Z`.

## [0.2.1-beta] — 2026-07-07

### Added
- `SystemPathGuard` — блок-лист системных путей: операции над `C:\Windows`,
  `C:\Program Files` и т.п. блокируются.
- Глобальная обработка необработанных исключений с возможностью отправить
  отчёт в GitHub Issues.

### Security
- Правки WebView2 (изоляция превью).
- Синхронизация и корректная отмена файловых операций.

### Docs
- README переориентирован на пользователя; кодстиль и roadmap вынесены в
  `CLAUDE.md`.
- Добавлены `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`.

## [0.2.0-beta] — 2026-07-06

### Added
- Первый публичный бета-релиз.
- Дерево дисков/папок с lazy-load, список файлов с колонками.
- Навигация Back / Forward / Up, адресная строка.
- Иконки и превью файлов (картинки, GIF, WebM, markdown, текст).
- Контекстное меню, хоткеи, корзина вместо permanent delete.
- Подтверждение на деструктивных операциях (Cancel по-умолчанию).
- Портативная self-contained сборка и CI-релиз по тегу.
- Лицензия PolyForm Noncommercial 1.0.0.

[0.2.1-beta]: https://github.com/lekta/wander/releases/tag/v0.2.1
[0.2.0-beta]: https://github.com/lekta/wander/releases/tag/v0.2.0
