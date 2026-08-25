# Changelog

Формат — [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
версии — по [SemVer](https://semver.org/lang/ru/). Даты в ISO-формате.

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
