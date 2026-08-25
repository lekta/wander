# Wander — бэклог

Конкретные фичи и доработки, которые мы обсудили и **сознательно отложили**.
В отличие от:

- [`TECHDEBT.md`](TECHDEBT.md) — мелкие шероховатости, которые встретили
  по пути и решили не чинить сразу.
- [`CLAUDE.md`](../CLAUDE.md) Roadmap — крупные стратегические направления
  ("тёмная тема", "вкладки").

Сюда идут вещи: **знаем что делать, знаем зачем, но решили не сейчас**.

## Открыто

### Файловые операции

- **Path too long (>260 без `\\?\`)** — `PathTooLongException`. Включить
  long-path support в манифесте + понятная ошибка как fallback.
- **Network paths (UNC)** — disconnected share / timeout. Сейчас длинные
  таймауты с сырой ошибкой. Нужны явные таймауты + понятный текст.
- **FAT32 4 GB limit** — `IOException` при копировании большого файла на
  FAT32. Нужна подсказка про целевую FS.
- **Симлинки / junction points / reparse-points** — `CopyDirectory` сейчас
  идёт рекурсивно и не различает links → может зациклиться или скопировать
  содержимое вместо link. Нужно: детектить `FileAttributes.ReparsePoint`
  и решать (skip / follow / copy as link).

### Прочее

(пусто)

## Закрыто

- **Shift+Delete (permanent delete)** — сделано. `KeyBinding` на
  `PermanentDeleteCommand`, всегда спрашивает подтверждение, затирает
  undo-стек. Обычный Delete теперь уходит в корзину.
- **Прогресс долгих операций** — сделано. Async batch через `BatchExecutor`
  + `OperationTracker`, модальный `ProgressDialog` с кнопкой отмены.
  Остаётся per-file/per-byte гранулярность — вынесено в TECHDEBT.
