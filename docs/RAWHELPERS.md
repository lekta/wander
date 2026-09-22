# Хелперы отсмотра RAW — спека блока 1 (0.5.0)

Спека Фабле, 2026-09-22. Реализует Опус («делай блок 1», WORKFLOW «Блок по
спеке»), ревью и финализация — Фабле. Файл живёт до финализации блока:
механизм уходит в ARCHITECTURE, снапшот — в DONE, файл удаляется.

**Цель.** При отборе снимков в галерее видеть, что не вытянуть (недосветы и
пересветы), что вытянется (тени и света поднятием кривой), куда попал фокус
(точка AF) и где резко (контрастные грани). Хелперы — только отображение:
галерея показывает и размечает, но не проявляет (BACKLOG «Поворот и
коррекция»).

Решения человека 2026-09-22: [хелперы включаются для RAW и для обычных
изображений]; [кнопки — в полосе над списком, справа от фильтра по оценке;
1–3 буквы, расшифровка тултипом]; [хочет попробовать сразу — блок первый].

## Исследование: готовое или своё

| Что | Вариант | Вердикт |
|---|---|---|
| Focus peaking, карта и скор резкости, клиппинг, гистограмма, кривая | своя реализация над BGRA-массивом: Собель / Лаплас 3×3, порог, LUT — по 20–60 строк каждый | **своё**, в Core, под тесты на синтетике |
| Где считать | `ShaderEffect` (HLSL): нужен `fxc.exe` из Windows SDK и `.ps` в ресурсах — новая цепочка сборки; CPU на рабочей копии ≤ 2560 px по длинной стороне (4,4 Мп, Собель `Parallel.For` 15–30 мс) | **CPU** в фоне; шейдер — если 4K-панель окажется медленной |
| Пиксели из `BitmapSource` | прецедент один: `GifImage.cs:246-252` — `FormatConvertedBitmap(Pbgra32)` + `CopyPixels` | так же: `FormatConvertedBitmap(Bgra32)` + `TransformedBitmap` под рабочий размер |
| Точка AF | MetadataExtractor 2.9.3 (уже в Platform): `CanonMakernoteDirectory` — `TagAfInfoArray` / `TagAfInfoArray2`, подтеги `TagNumAfPoints`, `TagValidAfPoints`, `TagAfImageWidth` / `Height`, `TagAfAreaWidth` / `Height`, `TagAfAreaXPositions` / `YPositions`, `TagAfPointsInFocus`; CR3 пакет читает (`Crx`). Сегодня makernote не читается нигде (`MetadataExtractorImageReader` — только `ExifIfd0`, `ExifSubIfd`, `Gps`, `Jpeg`, `Png`) | **стенд** на CR3 с R8 и на CR2: приходят ли теги; Nikon / Sony — нет в 0.5 |
| Честный клиппинг и RAW-гистограмма по данным сенсора | `Sdcb.LibRaw` 0.21.1.7 (2025-01, MIT-обёртка над LibRaw 0.21, CR3 читает своим CRX-декодером) + `Sdcb.LibRaw.runtime.win64` (LGPL-2.1 / CDDL): `raw_r.dll` 1,1 МБ + `jpeg8` 0,6 + `lcms2` 0,35 + `zlib1` 0,09 ≈ 2,2 МБ нативных DLL. API: `RawContext.OpenFile` / `OpenBuffer`, `Unpack`, `RawData` / `ExportRawImage`, `LibRawColorData.Black` / `Maximum` / `CamMul` / `LinearMax`, `LibRawImageSizes.RawWidth` / `RawHeight` / `TopMargin` / `LeftMargin` / `Filters`. Системный WIC-кодек RAW отдаёт уже проявленное — не годится | **слой 3, шаг 8**: после того как слой по JPEG посмотрели глазами. Цена — 2,2 МБ в однофайловом exe (`IncludeNativeLibrariesForSelfExtract` уже включён), чужой нативный код читает файл неизвестного происхождения в процессе |

Что даёт слой по вшитому JPEG и чего не даёт: peaking, карта резкости,
гистограмма и зебра — по JPEG камеры (после баланса белого и кривой),
приближение честное для отбора; «поднять тени / приглушить света» по 8 битам
показывает, что **есть** в тенях, но не что **вытянется** из 14 бит — это
только слой 3.

## Архитектура

- **Core `Wander.Core/Imaging/`** (новая папка, уровень 0, ссылок наружу
  нет): чистые функции над `BgraImage` (`byte[] Pixels, int Width, int
  Height, int Stride`) — факты на входе, маска / числа на выходе. Тесты на
  синтетических картинках.
- **App `Preview/ReviewOverlay.cs`**: рабочая копия из `BitmapSource`,
  вызов Core, сборка `ImageSource` для наложения. Всё в `Task.Run`, результат
  `Freeze()`.
- **Состояние `ReviewHelpers`** (App, `ViewModels/ReviewHelpers.cs`,
  `INotifyPropertyChanged`): семь переключателей. Один экземпляр в
  `MainViewModel`, отдаётся обоим `PreviewController` (`Preview`,
  `PreviewSecond` — сплит) через `SetHelpers`; полоса привязана к нему же.
  Между запусками не хранится [решение: сеанс].
- Оверлей рисуется на **вписанной** картинке (`ImgFit`); лупа (`ImgZoom`)
  показывает чистые пиксели [решение: в 0.5 без оверлея в лупе].

## Шаги

### 1. Core: алгоритмы и тесты

`Wander.Core/Imaging/`:

- `BgraImage` — `sealed record BgraImage(byte[] Pixels, int Width, int
  Height, int Stride)`; `Luma.Of(BgraImage) -> byte[]` (Rec.601 целыми:
  `(77 R + 150 G + 29 B) >> 8`).
- `FocusPeaking.Mask(byte[] luma, int w, int h, double fraction = 0.03, int
  floor = 24) -> byte[]` — величина градиента Собеля 3×3 по яркости; порог
  адаптивный: квантиль `1 − fraction` по гистограмме величин, не ниже
  `floor` (плоское небо не должно «звенеть» шумом). Маска 0 / 255, край в 1
  px не считается.
- `Sharpness.Score(byte[] luma, int w, int h, RectI? roi = null) -> double`
  — дисперсия Лапласа 3×3 в ROI (по умолчанию центральные 50 %). Сравнимо
  только между кадрами одного размера копии — вызывающий даёт копии одного
  размера. `Sharpness.Map(luma, w, h, int block = 32) -> byte[]` — та же
  дисперсия по блокам, нормированная к 0..255 по максимуму кадра: «резкое
  светлее» (идея человека).
- `Clipping.Mask(BgraImage, byte high = 250, byte low = 5) -> byte[]` —
  флаги на пиксель: биты 0–2 — R / G / B ≥ `high`, бит 3 — яркость ≤ `low`.
- `Histogram.Compute(BgraImage) -> HistogramData` — `int[256]` на R, G, B и
  яркость, плюс доли пикселей в бинах 0 и 255 (числа для подписи «2,1 %
  пересвет»).
- `ToneCurve.Lut(double gamma) -> byte[256]`, `ToneCurve.Apply(BgraImage,
  byte[] lut) -> BgraImage` — по каналам. Тени: `gamma 0.6`; света:
  `gamma 1.7` [решение: две кнопки взаимоисключающие — включённая
  выключает другую; обе сразу смысла не имеют].
- `AfPoint(double X, double Y, double W, double H, bool InFocus)` в долях
  кадра 0..1 (уже с учётом ориентации) — тип для `ImageMetadata`;
  `AfGeometry.Orient(IReadOnlyList<AfPoint>, int? orientation)` — поворот
  прямоугольников на 90 / 180 / 270 и отражения (те же восемь случаев, что
  `ImageDecoder.ApplyOrientation`).

Тесты `tests/Wander.Core.Tests/Imaging/`: плоское поле — маска пустая,
скор 0; вертикальная ступень — пики на столбце ступени, вне его нет; та же
ступень с размытием 5 px — скор меньше; белый квадрат на сером — клиппинг
только внутри квадрата, все три бита; чёрный — бит 3; гистограмма — сумма
бинов = число пикселей; LUT монотонна, `Lut(1.0)` тождественна; `Orient`
для 8 ориентаций на одном прямоугольнике.

### 2. App: рабочая копия и наложение

`Wander.App/Preview/ReviewOverlay.cs` (static):

- `BgraImage Working(BitmapSource source, int longSide = 2560)` —
  `TransformedBitmap(ScaleTransform)` до `longSide`, `FormatConvertedBitmap`
  в `Bgra32`, `CopyPixels` (по образцу `GifImage.cs:246-252`). Копия и её
  `Luma` кэшируются в контроллере на время жизни картинки: переключение
  хелпера не декодирует заново.
- `ImageSource MaskToImage(byte[] mask, int w, int h, Color color)` и
  `ImageSource FlagsToImage(byte[] flags, int w, int h)` (R / G / B / белый
  для пересвета по каналам, синий для недосвета) — `BitmapSource.Create`
  `Bgra32` premultiplied, `Freeze()`.
- `ImageSource Compose(ImageSource? mask, IReadOnlyList<AfPoint>? af, int
  w, int h)` — `DrawingImage` из `ImageDrawing` + `GeometryDrawing`
  (рамки AF: в фокусе — зелёная 2 px, остальные — серая 1 px). Всё
  заморожено, строится в фоне.
- `BitmapSource Toned(BgraImage toned)` — картинка для показа вместо
  оригинала.

Замер `PerfLog.Measure("preview.helpers")` вокруг вычисления.

### 3. Контроллер

`PreviewController`:

- `SetHelpers(ReviewHelpers)` — подписка на `PropertyChanged`; смена любого
  переключателя → `RecomputeHelpersAsync()`.
- Новые свойства: `ImageSource? DisplayImage => _toned ?? Image` (в XAML
  `ImgFit.Source` переводится на него; `MaxWidth` / `MaxHeight` остаются
  привязаны к `Image` — иначе рабочая копия меньше кадра ограничит вписывание),
  `ImageSource? Overlay`, `HistogramData? Histogram`, `string?
  HistogramNote` («пересвет 2,1 %, недосвет 0,3 %»), `bool HelpersActive`.
- `RecomputeHelpersAsync`: токен на картинку (тот же `_previewCts`, что у
  загрузки); если ни один хелпер не включён — обнулить всё и выйти без
  работы; иначе `Task.Run`: рабочая копия (кэш) → по включённым: маска
  peaking **или** карта резкости (взаимоисключающие: карта заменяет
  картинку, как кривая, а не накладывается) → клиппинг → гистограмма →
  тон → `Compose` → публикация через `Raise`. Публикация только если
  `ReferenceEquals(_image, картинка, для которой считали)` — образец
  `LoadFullSizeAsync:1738`.
- Точка AF: `ImageMetadata.AfPoints` (шаг 7) → в `Compose` без пересчёта
  пикселей.
- Смена картинки (`LoadImageAsync`): сбросить копию, оверлей, гистограмму;
  если хелперы активны — пересчитать после публикации новой картинки (тот же
  путь, что `LoadFullSizeAsync` — после `Image = image`).

### 4. Панель

`PreviewPane.xaml`, внутри `ImagePreviewHost` (203–243):

- `Image x:Name="ImgOverlay"` сразу после `ImgFit`: `Source="{Binding
  Overlay}"`, `IsHitTestVisible="False"`, `Stretch="Fill"`, `Width` /
  `Height` = `ImgFit.ActualWidth` / `ActualHeight` (`ElementName`), тот же
  `Margin="8,12,8,8"`, `VerticalAlignment="Top"`,
  `HorizontalAlignment="Center"` — прямоугольник совпадает с вписанной
  картинкой при любом размере панели; `RenderOptions.BitmapScalingMode`
  `NearestNeighbor` не нужен — маска в масштабе панели.
- Гистограмма: `Border` в правом верхнем углу хоста (192×72, фон
  `#80000000`, `Margin 16`), внутри `Canvas` с тремя `Polygon`
  (`Points="{Binding HistogramR}"` и т. д. — `PointCollection` считает
  контроллер из `HistogramData`, максимум по всем каналам, 192 точки) и
  `TextBlock` с `HistogramNote`. Видимость — `Histogram != null`.
- `ImgZoom` без изменений (чистые пиксели в лупе).
- Панель-сплит (`PreviewSecond`) получает то же автоматически: XAML один.

### 5. Полоса над списком

`FileListView.xaml`, `GalleryStrip` (518–627): после `StackPanel` с
фильтром оценок — второй `StackPanel x:Name="HelpersStrip"`
`Orientation="Horizontal"` `Margin="12,0,0,0"`, видимость — `ViewMode ==
Gallery` **или** панель показывает картинку (`Preview.Kind == Image`;
`MainViewModel` даёт `bool PreviewShowsImage`, обновляется по
`Preview.PropertyChanged(Kind)`). Стиль `HelperToggle` (`ToggleButton`,
`BasedOn` визуально как `GalleryBackgroundButton` 128: 24×18, без рамки в
покое, заливка при `IsChecked`, шрифт 11), `ToolTip` из ресурсов.

| Кнопка | Свойство `ReviewHelpers` | Ключи `Strings.resx` |
|---|---|---|
| `Фк` | `Peaking` | `HelperFocus`, `HelperFocusTip` («Фокус-пикинг: контрастные грани») |
| `Рз` | `SharpnessMap` | `HelperSharp`, `HelperSharpTip` («Карта резкости: резкое светлее») |
| `Кл` | `Clipping` | `HelperClip`, `HelperClipTip` («Клиппинг: пересветы по каналам, недосветы») |
| `Гс` | `Histogram` | `HelperHist`, `HelperHistTip` |
| `Тн` | `Shadows` | `HelperShadows`, `HelperShadowsTip` («Поднять тени») |
| `Св` | `Highlights` | `HelperHighlights`, `HelperHighlightsTip` («Приглушить света») |
| `AF` | `AfPoints` | `HelperAf`, `HelperAfTip` («Точка фокуса камеры») |

Подписи кнопок — тоже ресурсы (`check-strings`). Взаимоисключения
(`Фк` / `Рз`, `Тн` / `Св`) — в `ReviewHelpers` (сеттер снимает соседа).
Хоткеев в 0.5 нет [решение]; `HotkeyCatalog` не трогать.

### 6. Резкость в галерее: «резкое светлее»

Только в виде «Галерея» и только при включённом `Рз` (иначе папка на 300
RAW не декодируется зря).

- `FileSystemEntry`: новое замыкающее поле `double? Sharpness = null`
  (0..1, нормировано по максимуму папки); учесть в `SaysTheSameAs`.
- Platform `Icons/SharpnessProbe.cs`: `double Score(string path)` — для
  RAW `RawPreviewExtractor.Extract(fullSize: false)`, иначе файл; декод
  WinRT `BitmapDecoder` с `BitmapTransform.ScaledWidth` до 1024 по длинной
  стороне в `Bgra8` (образец `RawThumbnail.Render`), `Luma.Of` →
  `Sharpness.Score` (ROI — центр; точка AF — 0.6, если стенд шага 7
  удался). Интерфейс Core `ISharpnessProbe` в `Icons/`, регистрация в
  `PlatformBootstrapper`, `TryGet` в App.
- App `Controllers/SharpnessController.cs` по образцу `RatingsController`
  (`StartPass(items, path, epoch)` → `Task.Run` → `SharpListing.WithScores`
  (Core, копия `RatedListing.WithRatings`: копия списка при первом
  попадании, нормировка после прохода) → `_publish(epoch, rows)`); параллель
  2–4 файла (`Parallel.ForEachAsync`), отмена по уходу из папки и по
  выключению `Рз`; кэш в памяти `(путь, mtime, размер) → double` с потолком
  2000 записей [решение: дисковый кэш — не в 0.5]; статус «Резкость: 120 из
  300» через тот же путь, что `Ratings.StatusReported`.
- Ячейка галереи (`FileListView.xaml` 960–1039): `Opacity` корня `Grid` =
  `0.35 + 0.65 · Sharpness` через конвертер `SharpnessOpacity` (null → 1);
  бейдж в правом нижнем углу по образцу `Badge` (1006, `DataTrigger` на
  `Sharpness != null`): число 0–100. Выделенная ячейка — `Opacity 1`
  всегда (иначе рамку не видно).

### 7. Точка AF

- `ImageMetadata`: замыкающее поле `IReadOnlyList<AfPoint>? AfPoints =
  null`.
- `MetadataExtractorImageReader.Read`: `CanonMakernoteDirectory` →
  `TagAfInfoArray2` (у новых камер) либо `TagAfInfoArray`: число точек,
  ширина / высота AF-кадра, массивы X / Y / W / H, биты «в фокусе»;
  перевод в доли: `x = 0.5 + X / AfImageWidth`, `y = 0.5 − Y / AfImageHeight`
  (знак Y — по стенду), затем `AfGeometry.Orient`. Ошибки разбора глотаются
  (нет данных — нет рамок), в лог `DEBUG`.
- **Стенд до кода** (Фабле): консоль в scratchpad на `MetadataExtractor` —
  какие теги отдаёт CR3 с Canon R8 и CR2; сверить с ExifTool (`-AFInfo*`).
  Не приходят — шаг выпадает из 0.5, кнопка `AF` не добавляется.

### 8. Слой 3: клиппинг и гистограмма по данным сенсора — [решить после 1–7]

- Core `Icons/IRawSensorReader.cs`: `RawSensor? Read(string path)` →
  `RawSensor(ushort[] Bayer, int Width, int Height, int Black, int
  Maximum, string Cfa)`; `Clipping.MaskFromSensor(RawSensor, double
  headroom = 0.98)` — по каналам CFA против `Maximum`, недосвет — против
  `Black + шум`; `Histogram.ComputeEv(RawSensor)` — бины в log2 (EV) по
  каналам.
- Platform `Imaging/LibRawSensorReader.cs` на `Sdcb.LibRaw` (`OpenFile` →
  `Unpack` → `RawData`, без `DcrawProcess`); ~0,5–1 с на 24 Мп CR3 — в
  фоне, оверлей обновляется, когда готов; JPEG-приближение показывается
  сразу.
- Цена: +2,2 МБ exe, нативный код в процессе (падение — падение Wander;
  ловить `AccessViolation` нельзя). Смягчение — отдельный процесс-помощник:
  дорого, не в 0.5.
- Решение человека после глаз на слое 1: нужен ли честный клиппинг сейчас.

## Проверка

- `check.bat`: тесты шага 1; `check-strings` — 14 новых ключей.
- Харнесс: сценарий `preview-formats` — новый шаг `helpers` (`{"step":
  "helpers", "on": ["Peaking", "Clipping"]}` → `ReviewHelpers` через
  `MainViewModel`), затем `screenshot`; `assert-log` на отсутствие ошибок.
  Шаг добавляет Опус в `ScenarioRunner` рядом с `preview`.
- Глазами: совпадение оверлея с картинкой при узкой и широкой панели, при
  125 % / 150 %, в сплите; порог пикинга на небе и на листве; клиппинг на
  заведомо пересвеченном кадре; гистограмма против Lightroom / RawTherapee
  на том же JPEG; `Тн` / `Св` — видно ли содержимое; `Рз` в галерее на серии
  одной сцены (резкий кадр действительно светлее); время до появления
  оверлея на 24 Мп (ожидание: < 100 мс после картинки); память при
  включённых хелперах на 300 RAW (`SYS ws=`).

## Критерии ревью (сверх FINALIZING п. 2)

- В Core ни `System.Windows`, ни `BitmapSource`; все пороги — параметры с
  умолчаниями, не магия в теле.
- Ни одного декода на UI-потоке; отмена при смене картинки и уходе из
  папки; результат публикуется только для актуальной картинки.
- Список не пересобирается при публикации резкости (`ReconcileEntries`,
  правило CLAUDE.md «список не дёргается»).
- Выключенные хелперы стоят ноль: ни копии, ни таймеров.

## Не в этом блоке

Полноэкранный просмотр и «Сравнить» (PLAN Q5, блок 5) — хелперы туда
переедут тем же `ReviewHelpers`; хоткеи хелперов (PLAN M); дисковый кэш
резкости; Nikon / Sony AF; отдельный процесс для LibRaw.
