# Contributing to Wander

Спасибо за интерес к проекту! Ниже — как предлагать изменения и на каких
условиях они принимаются.

## Как внести вклад

1. **Баги и идеи** — через [Issues](https://github.com/lekta/wander/issues)
   (есть шаблоны для bug report и feature request).
2. **Код** — форк → ветка → Pull Request в `master`.
3. Перед PR прогони проверку:
   ```pwsh
   tools\check.bat
   ```
   Она собирает решение, проверяет форматирование (`dotnet format`) и гоняет
   тесты. Кодстиль зафиксирован в [`.editorconfig`](.editorconfig).
4. Держись архитектурных правил из [README](README.md#архитектурные-правила):
   `Wander.Core` не зависит от Windows и UI, платформенное — в
   `Wander.Platform.Windows`.

## Права на вклад (Contributor License Agreement)

**Открывая Pull Request, ты соглашаешься со следующими условиями. Без этого
согласия вклад не может быть принят.**

1. **Авторство и чистота прав.** Ты — автор вклада (или иным образом обладаешь
   всеми правами на него) и вправе передать его. Вклад не нарушает прав третьих
   лиц и не связан обязательствами перед работодателем или иными лицами.
2. **Передача прав.** Ты передаёшь автору проекта (**Lekta**) все
   исключительные имущественные права на свой вклад — в полном объёме, по всему
   миру и на весь срок действия прав.
3. **Резервная лицензия.** В той мере, в какой полная передача прав невозможна
   по применимому праву, ты предоставляешь **Lekta** бессрочную, безотзывную,
   всемирную, неисключительную, безвозмездную лицензию с правом сублицензирования
   и **правом использовать вклад в любых целях, включая коммерческие, а также
   переиздавать его под любой лицензией** (в том числе проприетарной).
4. **Патенты.** Ты предоставляешь патентную лицензию на любые свои патентные
   права, необходимые для использования вклада.
5. **Как есть.** Вклад предоставляется «как есть», без гарантий.

Смысл простой: **после принятия PR его код становится частью проекта, которым
Lekta распоряжается так же свободно, как собственным** — включая право
выпускать коммерческие версии. Это нужно, чтобы всё приложение оставалось под
единым контролем правообладателя, а не превращалось в мозаику из чужих прав.

---

### CLA (English summary)

By opening a Pull Request you agree that: (1) you are the author of the
contribution and have the right to submit it, free of third-party or employer
claims; (2) you **assign to Lekta all exclusive economic/proprietary rights** in
your contribution worldwide, for the full term; (3) to the extent such
assignment is not permitted by applicable law, you grant Lekta a **perpetual,
irrevocable, worldwide, royalty-free, sublicensable license to use the
contribution for any purpose, including commercial use, and to relicense it
under any terms, including proprietary licenses**; (4) you grant a patent
license for your contribution; (5) the contribution is provided "as is", without
warranty.

This keeps the entire project under the sole control of the copyright holder
(Lekta), who may release commercial versions.
