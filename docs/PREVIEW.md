# Wander — панель просмотра и форматы

Конвейер панели просмотра, маршруты по форматам и их читатели, зум и
полный экран, медиа, миниатюры мимо шелла, хелперы отсмотра. Слои и швы
с остальным приложением — ARCHITECTURE («Области»); поведение
и пределы — DONE «Панель просмотра», «Хелперы отсмотра снимков»;
таблица форматов для пользователя — GUIDE (`check-formats.ps1` сверяет
её с кодом).

## Конвейер

`PreviewController` (App) — конвейер с отменой и спиннером. `PreviewPane`
берёт контроллер **своим `DataContext`**: всё, что панели нужно от
хозяина, — свойства контроллера (`ContentPalette` кладёт
`MainViewModel.PushPalette`), наружу — события `RatingRequested`,
`RevealRequested`. Поэтому панель живёт и вторым экземпляром: половина
сплита, «Сравнить», полный экран.

- **Что показывать.** Пара — `PreviewPair.Of(выделение, листинг)` (Core,
  тест): ровно два файла с маршрутом, порядок по списку. Один из
  нескольких — `PreviewSubject.Of(цель, каретка, листинг)` (Core, тест):
  `Primary` — каретка, если она в выделении (`SelectedItem` WPF стоит на
  первом выделенном), в паре — верхний. `RefreshPreviewPrimary` зовётся
  на каждую смену строк, главной и каретки — список сообщает их в любом
  порядке. Сводка по нескольким — `ShotSummary` (Core): одно значение,
  два-три через запятую, больше — поле не пишется; EXIF у первых ста;
  `CameraName` убирает двойную марку.
- **Сплит.** `MainViewModel.PreviewSecond` (`ShowFooter = false`,
  `ShowPictureBar = true`; `ShowRawDecode` зеркалится) + второй
  `PreviewPane`, созданный при первой паре (`MainWindow.ApplyPreviewSplit`)
  и вложенный в первый (`PreviewPane.ShowSecond`). Футер первой — под
  обеими и про выделение; звёзды, балл и гистограмма — на полосе снимка
  каждой половины (`IsPreviewSplit` включает `ShowPictureBar` и первой,
  её футер свои прячет), звезда половины — про её файл
  (`ApplyRatingFromPane(wholeSelection: false)`).
- **Стопкой или рядом** — `SplitOrientation.Stacked` (Core, тест): сумма
  площадей двух вписанных кадров в обоих раскладах по месту над футером
  (`PreviewPane.PairArea`). Формы — из заголовков (`PictureLoader.ShapeOf`:
  EXIF или WIC, встроенный JPEG у RAW, поворот 5–8), на пуле до показа
  (`MainViewModel.ShowPair`, до 150 мс); неизвестная — квадрат. Переворот
  — только когда другой расклад крупнее в `TurnAbove` = 1,1 раза. То же
  правило у `CompareWindow` (`Arrange`).
- **Зум синхронный**: `PreviewPane.Link(a, b)` через `ZoomLink` (Core,
  тест) — ведущая отдаёт точку долями области, вторая повторяет
  (`FollowZoom`, без захвата мыши); с зажатой правой вторая стоит, сдвиг
  запоминается; `Reset` — новая пара.
- **Фон** — `MainViewModel.ContentPalette` (палитра области файлов), не
  `Settings.GalleryPalette`. `Foreground` / `Dim` **считаются от фона по
  контрасту** (фиксированная пара на сером давала 2.2:1). Ловушка: путь
  `DataContext.ContentPalette.…` — без `DataContext.` биндинг через
  `RelativeSource` молча не находит. Текст, код, документы — светлые.
- **Оценка**: строка вне блока спутников — `OfferRating` предлагает и
  файлу без сайдкара; полоса — у всего, у чего `HasRating`.
- **Полоса снимка** (`PreviewController.ShowPictureBar`) — в `ContentArea`,
  левый нижний угол; шаблоны футера (`RatingControls`, `HelperSwitches`,
  `HistogramChart`). Приглушение — `ReachBar` / `UpdateBar`: 30 %, пока
  мышь не в нижних `BarReach` = 96 px и не пришёл `RatingChanged`
  (`NoteRatingShown`, 1,5 с); переход 60 мс; на зуме прозрачна.
  `PreviewPane.BarPinned` (полный экран) — в полную силу всегда, и на
  зуме, плюс имя `PreviewController.PictureName` — файл показанной
  картинки (`_pictureOf`). Строк две: сверху факты — имя (приглушено,
  0,65) и балл резкости, снизу звёзды и переключатели. В футере кнопки
  хелперов, **RAW** и `</>` — на строке звёзд в `WrapPanel`. Кнопки
  хелперов (`HelperSwitches`) — две группы: фокус (точка AF, резкость,
  пикинг) и экспозиция (клиппинг, тени, света, гистограмма — у RAW, он её
  меняет); **RAW** — словом на той же плашке (`HelperTextToggle`), между
  группами и до RAW — 6 px. Видны (`ShowsHelperSwitches`) под картинкой
  и под открытой папкой, когда ничего не выделено, а список — галерея
  (`ListIsGallery` из `MainViewModel.ViewMode`, 2026-10-23): хелперы на
  ячейках включаются без выбора снимка.
- **Отступ** — `PreviewPane.PictureMargin` (4 px, на полном экране 0): на
  него смотрят `ImgFit`, `ImgOverlay`, `GifPreview`, размер декода
  (`ReportViewport`) и зум (`UpdateZoomPosition`). Картинка посередине по
  обеим осям; лупа на непрокручиваемой оси ставит кадр так же.
- **Между картинками панель не гаснет**: прежняя стоит до готовности
  следующей (`ClearPreviewContent(keepImage)`), вуаль — после
  `VeilDelayMs` = 250. Футер ждёт картинку (`FooterWaitsForPicture`,
  спутники очищаются и заполняются в один такт) — иначе он дважды менял
  высоту и вертикальный снимок дёргался; высоту держит содержание:
  спутники — `CompanionLabel` → `SummaryNote`, факты через
  `SummaryText.Gap`.
- **Футер**: пусто — папка (рекурсивно, async); файл — имя, размер, дата
  и EXIF (`MetadataExtractor`, RAW включая CR2 / CR3 / NEF / ARW / DNG);
  несколько — агрегат; под ним спутники (GUID из `.meta` с копированием).
  **Том** — `IVolumeInfoProvider` / `WindowsVolumeInfo` над `DriveInfo`,
  только на корне; неготовое свойство бросает — «не готово» = том с
  нулевой ёмкостью. **Перепись папки** — `FolderStatistics` (Core,
  итеративно; глубина 64 — защита от junction'ов, «числа неполные»);
  `IsCensusLoading` отдельно от `IsLoading`; числа раз в 150 мс через
  `IProgress<FolderProgress>`, типы — в конце. На корне тома переписи нет
  — только блок тома (`ShowFolderCensusAsync` выходит, разметка прячет
  перепись по `HasVolume`): обход всего диска на каждую стрелку.
- **Строка панели** в панели просмотра — `PreviewSubject`: запись папки
  читается на пуле (`ShowPanelFolder`), показывается, если цель ещё она.
- **Ярлык прозрачен**: `.lnk` резолвит `IShortcutService`
  (`ShellShortcutService`, `IShellLinkW`), рисуется цель; `LinkTarget` в
  футере и «Перейти к оригиналу» → `MainViewModel.RevealPath`.
- **Файл отпускается перед операцией** — FILEOPS, «Файловые
  операции», занятые пути (`PreviewController.Release` / `Restore`).
- **`Ctrl+C` в панели** — выделенный текст, не файл; решает окно
  (`PreviewPane.TryCopySelectedText`). Веб-вид копирует сам: WebView2
  отдаёт браузеру клавишу, которую никто в WPF не пометил, а привязка
  окна помечает всегда — оконный `Ctrl+C` к нему не подходит
  (`Util/FileCopyGesture`).
- **Не открылось и занято** — `ExplainUnreadableAsync`: держатель через
  Restart Manager на пуле.
- **`App/Preview/`**: `ImageDecoder`, `ModelBuilder` + `ModelScene` (Core →
  `MeshGeometry3D`), `PreviewText` (бюджет, кодировка, Markdown,
  HTML-обёртка), `SummaryText`, `PictureLoader`, `PictureCache`,
  `ExecutableCard`. Контроллеру — конвейер. Разбор — в Core (`Preview/`,
  `Imaging/`, `Icons/`), отрисовка — в App.

## Маршруты

`PreviewRouter` (Core, `PreviewRouterTests`) — «расширение →
`PreviewRoute`», без диска; `Route` (каким загрузчиком) ≠ `Kind` (каким
контролом). Таблица — **порядок правил**, побеждает первое: `.gif` —
картинка и многокадровый контейнер (`GifImage`, если кадров больше
одного — `ContentSniffer.IsAnimatedGif` обходит блоки до второго; иначе
картинкой, с лупой), `.mtl` — текст, `.svg` рисуется, а
для действий остаётся текстом (`TextLike`), `.webp` — картинка,
анимированный по флагу заголовка `VP8X` (`ContentSniffer.IsAnimatedWebp`)
панель отдаёт `GifImage`. Архив в папке — `PreviewRoute.Archive`: решает
не расширение — `PreviewRouter.Route(path, isArchive)` берёт факт от
вызывающего (`Archives.Of` + `CanNavigate`).

| Kind | Чем |
|---|---|
| `Image` | `BitmapImage`, `DownOnly`; RAW — встроенное превью |
| `Gif` | `Controls/GifImage` |
| `Video` | `MediaElement`; не открыл — `SystemPlayer` (WinRT) |
| `Audio` | тот же транспорт, карточка трека; играет `MediaPlayer` |
| `Text` | `TextBox`; документ текстом — с переносом (`TextWrap`) |
| `Code` | AvalonEdit |
| `Document` | `RichTextBox` (RTF) |
| `Web` | WebView2 — PDF / HTML / MHTML / Markdown / FB2 / SVG / таблицы |
| `Model` | `Viewport3D` — STL / OBJ / glTF / GLB / PLY / 3MF / FBX |
| `Folder` | перепись; на корне тома — только блок тома |
| `Executable` | карточка программы: значок, строки `ExecutableCard` |
| `Font` | карточка шрифта: имена, образец кеглями (`FontCard`) |

- **По содержимому** (решение человека 2026-10-02): `ContentSniffer`
  (Core, тест) называет формат по первым 64 КБ — подписи картинок,
  контейнеров, PDF, zip по содержимому (ODF / EPUB по `mimetype`, OOXML
  по папкам частей, 3MF), моделей, PE, шрифтов; из текста — RTF, XML /
  SVG, HTML, JSON. `LoadFileAsync` спрашивает его, когда расширение
  неизвестно или его нет у не-текста, и когда загрузчик по расширению не
  справился (`Kind = Unsupported`, а подпись говорит о другом маршруте).
  Видео и звук идут в проигрыватели как есть, остальное — копией в
  `TempFiles` с верным расширением (до 256 МБ) по обычному маршруту. Не
  названный подписью, но текст (`TextProbe`) — текстом при любом
  расширении.
- **Архив**: первый уровень на пуле, папки сверху, до 200 строк и «и ещё
  N»; тот же список — для папки архива без выделения. Файл внутри до
  32 МБ — временная копия (FILEOPS, «Shell-namespace»);
  распаковка **одна за раз** (`SemaphoreSlim` в `PreviewController`):
  движок не останавливается внутри записи, и стрелки по RAR набирали
  десятки потоков пула.
- **RAW с устройства** (2026-10-19) — не копия, а голова файла
  (`TempExtraction.CopyHead`): поток устройства читается только вперёд,
  `HeadStream` (Core, тест) держит прочитанное, `RawPreviewExtractor`
  (`fullSize`) ходит по нему, и читается ровно до конца встроенного
  JPEG — у CR3 ~6 МБ из 30 (movie box, `PRVW`, полноразмерный JPEG,
  дальше — данные матрицы). Голова — в `head-<размер>` папки источника,
  куда «Открыть» и окно совпадений не смотрят; потолок 32 МБ — на
  прочитанное, не на файл; картинок в нём нет — путь записи архива.
  Готовая полная копия («Открыть») берётся вместо головы. Отпускание
  потока посреди файла у R8 стоит ~0,4 с, `Cancel` перед ним не помогает
  (2026-10-22) — PLAN 0.7, «RAW с устройства — 0,5 с на снимок». Голова
  рисует и плитку снимка, с поворотом (`DeviceThumbnail`).
- **Кодеки из Store**: `StoreCodecs` (Core, тест) — какие расширения
  нужны файлу по расширению (`ForFile`: HEIC — HEIF + HEVC, AVIF — HEIF +
  AV1, JXL, WebP, RAW кроме DNG, контейнер Ogg — Web Media) и потоку по
  кодеку из `MediaProbe` (`ForStream`: HEVC, AV1, VP9, MPEG-1/2, Theora,
  Vorbis); первое недостающее — `FirstMissing`, `FirstMissingForMedia`.
  `ICodecProbe.Has` → `WindowsCodecProbe` (`MFTEnumEx`): расширения
  картинок регистрируют декодер MF с входным типом = CLSID своей заглушки
  WIC в `windowscodecs.dll` (список WIC не годится — заглушка в нём есть
  всегда), видео — обычные видеодекодеры, Web Media — звуковой Vorbis.
  Ответ держится минуту; не смог спросить (нет `mfplat.dll` в Windows N)
  — «есть». Встроит Microsoft кодек мимо MF — миниатюры этого формата
  станут значками. Панель: не открылось →
  `PreviewController.ExplainMissingCodecAsync`, плашка `PreviewNeedsCodec`
  (HEVC — своя, платное) и `OpenStoreCommand`
  (`ms-windows-store://pdp/?ProductId=…`, номера — в `OpenStore`); GIF и
  анимированный WebP — заголовок через `ImageDecoder.StoredSize` до
  `GifImage`, иначе пустая панель. Миниатюры —
  `SystemIconProvider.LacksCodec`: шелл не спрашивается (ответил бы
  значком и записал его в `thumbcache`), значок — путём значков, на диск
  не пишется (кроме RAW — у него своё превью).

## Картинки

- **Декод под поле и соседи** (PLAN AK). Поле — `SetViewport`: место под
  картинку в пикселях устройства с шагом `BoxStep` = 64.
  `PictureLoader.Decode` (пул): JPEG и встроенный JPEG — под поле
  (`PictureFit.DecodeWidth`, Core, тест: от 95 % поля — целиком; масштаб в
  DCT), остальное целиком → `DecodedPicture`. Поле выросло — перерез через
  `BoxSettleMs` = 300 (`PictureFit.TooSmall`). Первый размер панели
  ждётся до `BoxWaitMs` = 500 (`_boxKnown`).
  - `PictureCache` (UI-поток) — три кадра и байты, `SizedCache` над
    `MemoryShare`; ключ путь + время + размер + поле; спрятанная панель и
    `Detach` отдают кадры. Соседи — `PreviewNeighbors.Of` (Core, тест: выше
    и ниже, только картинки, первым — по направлению движения), по одному
    после показа (`DecodeNeighborsAsync`; законченный декод кладётся в кэш
    и при отмене), только для кадра своей строки (цель ярлыка и запись
    архива — каждый раз).
  - **Память под картинки** — `PictureMemory` (Core, тест): МБ из
    настройки либо 1/16 `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`;
    четверть — `IconImageCache` (байты `W × H × bpp`), остальное — кадрам
    всех панелей; показанный и греющиеся — `Keep`; `Fits` решает соседа по
    `PictureLoader.DecodedBytes`. Предел ставит
    `MainViewModel.ApplyThumbnailCacheSettings`, сразу.
  - Серия — смена чаще `BurstMs` = 150: промах кэша ждёт `BurstDelayMs` =
    90 и переспрашивает. Полный экран (`WalksEveryPicture`) серии не знает:
    повтор клавиши шагает, только когда загрузка показанного кончилась
    (`IsSettled`, `FullscreenWindow.MayStep`) — решение человека
    2026-10-08: на полном экране не видно, что проскочило. `preview.shown`
    — раз на смену выделения (`_shownMeasured`).
- **DPI**: `BitmapPixelSizeConverter` — пиксели ÷ `PreviewPane.DpiScale`
  (`Loaded`, `OnDpiChanged`) для `ImgFit`, `GifPreview`, обложки аудио;
  вписанный вид — `HighQuality` по пиксельному размеру; `MagnifierCursor`
  — `scaleWithDpi`; 1:1 — на целых пикселях.
- **RAW не декодируется**: WIC на `.CR3` — секунда на файл
  (`DecodePixelWidth` и `Thumbnail` не помогают). `RawPreviewExtractor`
  (Core) достаёт JPEG из контейнера за миллисекунды: ISO-BMFF (`uuid`
  Canon с `PRVW`); TIFF (IFD → JPEG: CR2, NEF, ARW, DNG, PEF, 3FR; RW2 /
  RWL — тег `0x002E`; ORF — Exif IFD → MakerNote `OLYMPUS` / `OM SYSTEM`
  → `0x2020` → `0x0101` / `0x0102`, смещения от начала заметки; старый
  `OLYMP` — нет); RAF — указатель в заголовке (байт 84, big-endian); CIFF
  (CRW) — запись `0x2007` корневой кучи. SHORT в слоте значения — первые
  два байта (`MM`-файл иначе даёт старшую половину: iPhone DNG терял
  превью). Кандидаты от большего к меньшему с проверкой маркера (в DNG /
  NEF самый большой поток — raw-данные, SOF3). У CR3 `PRVW` — 1620×1080,
  полноразмерный — первая дорожка `moov` (`Extract(fullSize: true)`).
  - Не прочитан (FFF — превью несжатое) — **превью кодека**:
    `ImageDecoder.CodecPreview` (`BitmapDecoder.Preview`, без `Frames`;
    Raw Image Extension на порядок быстрее матрицы), вписанное;
    `DecodedPicture.CodecPreview` — для кнопки RAW и зума 1:1 как
    встроенное. Нет и его (DNG-декодер, IIQ от WIC как TIFF) — декод файла
    целиком.
  - Панель берёт оба (`LoadImageAsync` → `LoadFullSizeAsync`): быстрый —
    сразу и вписанным (`Image`), большой — после 150 мс на файле и
    **только в зум** (`ZoomSource`; по нему меряют `ImgZoom`,
    `IsImageDownscaled`, `UpdateZoomPosition`), иначе пересэмплировал бы
    картинку после каждой стрелки. Одинаковые байты не декодируются
    дважды; `PreviewPane.RefreshImageZoom` — зум под сменой картинки.
  - Предел вписанного (`ImageCapWidth` / `Height`) у RAW — кадр из EXIF
    (`DecodedPicture.FrameWidth`, `PictureLoader.WholeFrame`: больше превью
    и той же формы в пределах 2 %), пока такой кадр придёт — `ShowPicture`:
    превью сразу растянуто и только дорезчивается. RAW с одним маленьким
    превью — предел по превью. `RefitWhenSettledAsync` переспрашивает
    `TooSmall`. Миниатюрам — только быстрый.
  - JPEG лежит **неповёрнутым**: ориентация IFD0 контейнера —
    `ImageMetadata.Orientation` → `ApplyOrientation` (`TransformedBitmap`).
    Тег применяется к **каждой** картинке: `BitmapImage` не поворачивает и
    JPEG с камеры.
- **`IgnoreImageCache` — только для файлов**: у картинки из `MemoryStream`
  URI нет, и `FinalizeCreation` на .NET 10 падает на `null` — быстрый путь
  RAW молча уходил в полный декод.
- **HEIF**: `ImageFormats.Heif` (`.hif` с камер — тот же контейнер), декод
  под панель (`PictureLoader._scaled`); поворот контейнера — `UprightHeif`
  в `MetadataExtractorImageReader` (WIC EXIF-тег игнорирует); 1×1 без
  пикселей — `DecodeFailed` в `ImageDecoder.Decode`.
- **PSD / PSB** (решение человека 2026-10-09: свой декодер, как TGA):
  `PsdDecoder` (Core, тест) — не слои, а составная картинка в конце файла
  («Максимальная совместимость»): плоские каналы, без сжатия или PackBits;
  RGB, серый, дуплекс, индексный, CMYK, битовая карта; 8 / 16 / 32 бита;
  четвёртый канал — альфа, если число слоёв отрицательное. Без составной
  (`hasRealMergedData` = 0 в ресурсе 1057) или больше 36 Мп — вшитое
  JPEG-превью (ресурс 1036, до 160 px). Панель — `ImageDecoder.Psd`,
  плитки — `PsdThumbnail`: до 160 px — превью без декода, крупнее —
  составная (файл до 256 МБ).
- **TGA**: `TgaDecoder` (Core, тест) → `BgraImage`: несжатый и RLE,
  палитра (индекс 8 / 16, записи 15–32 бит), цвет 15–32 бит, серый 8,
  угол из дескриптора, нулевая во всём кадре альфа — непрозрачно; до
  16384²; палитра, поле id и объём данных проверяются до буфера. Панель —
  `ImageDecoder.Tga` (`BitmapSource.Create`, Bgra32), миниатюры —
  `TgaThumbnail` (WinRT-кодер PNG, до 64 МБ); `.tga` в `ImageFormats.All`.
- **SVG**: `PreviewRoute.Svg` перед кодом; `PreviewText.SvgPage` — `<img>`
  с `data:` base64 (скрипт не исполняется) в WebView2; `ShowSvgSource` —
  разметка, на обе половины; больше 1 МБ — разметкой (строка WebView2 до
  2 МБ). Снимок из панели в кэш — TECHDEBT.

## Зум и полный экран

- **Полный экран** (`Views/FullscreenWindow`): план —
  `FullscreenPlan.Of(выделение, листинг, каретка)`, шаг —
  `PictureWalk.Step` (оба Core, тест; `stood` — место снимка, скрытого
  фильтром, `skip` — левый для правого). У каждой панели свой контроллер
  (`MainViewModel.NewPictureViewer`; хелперы, фон и RAW общие); `Detach`
  при закрытии отписывает от `ReviewHelpers` — иначе жил бы с кэшем
  кадров.
  - `←` / `→` в паре меняют роли панелей (`KeepSide`), а не грузят правый
    снимок в левую — иначе на кадр виден левый во весь экран.
  - Панель прозрачна, пока ничего не показывает (`Kind` None, не
    `IsLoading`); `IsPlaceholderVisible` молчит на первой загрузке. Полоса
    — `BarPinned`.
  - Пока окно открыто, `MainWindow.OnViewEffectRequested` гасит
    `FocusRow` / `FocusZone` модели (`_fullscreen`): отложенный до
    активации ход и `FocusFell` на старую каретку срабатывали после
    `RevealPath` закрытия и уводили выделение обратно. Клавиатуру в
    список возвращает само закрытие (`FocusList`).
  - `Delete` — `MainViewModel.DeletePictureAsync` → тот же `DeleteAsync`,
    про снимок `KeyTarget`; автоповтор не удаляет подряд. Картинки
    декодируются в память (`OnLoad`), файл не держат.
  - Курсор: `CursorIdleMs` = 1,5 с → `ForceCursor` + `Cursors.None`;
    синтетический `MouseMove` без сдвига не будит. На `ContentRendered` —
    сразу прячется, открытые подсказки закрываются (`CloseToolTips`: обход
    только всплывающих `HwndSource`) и `Mouse.Synchronize`: без движения
    мыши WPF считал её над главным окном — подсказка ячейки галереи
    висела поверх.
  - `Ctrl` + `Z` — `UndoCommand` из `OnPreviewKeyDown`; `Alt` + `Space` и
    `F10` (`Key.System`) гасятся — системное меню ломало окно без рамки.
  - `MainViewModel.Say` → `StatusSaid` → плашка 4 с. Строки после оценки
    — заново (`RatingsController.FindInSource`, и скрытые фильтром) по
    `Entries.CollectionChanged` и `Ratings.CompanionsChanged`.
  - Окно закрывается на KeyDown, и автоповтор уходит в главное: `Enter` /
    `Space` в галерее и `Esc` в списке с `IsRepeat` ничего не делают.
- **Лупа и `Z`** — `PreviewPane.ToggleZoom` (`_zoomPinned`,
  `ZoomMove.Pinned`). При спрятанном курсоре — `ZoomToSpotAsync`:
  `PreviewController.ZoomSpotAsync` по `AppSettings.ZoomSpot` — центр
  точек AF в фокусе (`AfGeometry.FocusCentre`), иначе центр резкой зоны
  (`ReviewOverlay.SharpSpot` → `SharpZone.Centre`: ячейки 1/24 длинной
  стороны, побеждает блок 3×3 с большим числом меток пикинга; маска из
  кэша пикинга), иначе середина. `CentredOn` пересчитывает точку в место
  мыши, которое ставит её посреди экрана (лупа — навигатор, вся область на
  весь кадр); `_zoomMouse` — где стояла мышь: синтетический `MouseMove`
  без сдвига лупу не уводит, настоящий — забирает. Колесо (по 120,
  накопление тачпада) и `XButton1` / `XButton2` — `WalkByMouse`: как
  стрелки, с `Shift` — правый из пары; с `Ctrl` колесо окно пропускает —
  его берёт панель под мышью.
- **Зум ступенями**: `Ctrl`+колесо над картинкой — `PreviewPane.ImageZoom_Wheel`,
  уровни `PictureZoom.Step` (Core, тест: 0,5 / 1 / 2 / 4 только выше
  вписанного масштаба `FitScale`, ниже наименьшего — снова вписан);
  масштаб `_zoomScale` — на всех путях лупы (`CanZoomTo`,
  `UpdateZoomPosition`, `ZoomMove.Scale` для пары); закреплён без кнопки,
  как `Z`. Отображение: меньше 1:1 — `HighQuality`, больше — блоками.
  Масштаб — у имени на полосе полного экрана (`ZoomLevelText`, только при
  `BarPinned`); миникарта `ZoomMap` (левый верх, 140 px, рамка — видимая
  доля кадра) — `FlashZoomMap` из `UpdateZoomPosition`, гаснет через
  `ZoomMapMs`. `Ctrl`+стрелки — `NudgeZoom` → `PictureZoom.Nudge` (треть
  экрана, в долях места, через `MoveImageZoom` — пара едет вместе);
  `AppSettings.ZoomArrows` = `Move` меняет стрелки и `Ctrl`+стрелки
  местами, только пока что-то увеличено (`ZoomedPane`). Видео —
  `VideoZoom_*`: `ScaleTransform` на `VideoStage` с центром под мышью
  (2× / 4×), рамка `VideoClip` режет; новый клип — вписан. Почему так
  (разбор 2026-10-15 по FastStone, XnView, Lightroom, Photo Mechanic,
  FastRawViewer, Capture One, darktable, Narrative, Honeyview):
  `Ctrl`+колесо — зум почти везде, якорь под курсором; навигатор —
  оверлей только при движении, не постоянный; место — в долях кадра
  (кадры серии разного размера); fit — режим, не процент; стрелки по
  умолчанию листают с тем же местом — так проверяют серию, FastStone /
  XnView наоборот — отсюда настройка.

## Медиа

- `Audio` и `Video` делят `MediaUri` и транспорт; проигрыватель разный
  обязательно: `MediaElement` без площади открывает файл и стоит на нуле
  (200×120 играет, 1×1 молчит). Контроллер ставит `Kind` **до**
  `MediaUri`. Громкость — `PreviewController.MediaVolume`: хозяин держит
  обе половины на одной и пишет её в `SessionState.MediaVolume`;
  `MediaElement` берёт привязкой, `MediaPlayer` — из кода.
- `RestartMedia` (повтор и второе нажатие кнопки): файл объявил
  длительность — `Position = 0; Play()`, нет — только `Close()` + `Open()`
  (стенд `MediaPlayer`); пустой кадр переоткрытия закрывает снимок
  последнего кадра (`HoldLastFrame`, `VideoHold`, элемент держит размер
  через `MinWidth` / `MinHeight`) до первого тика со сдвигом. «Доиграл» —
  флаг, не позиция: движок сам отматывает на ноль. `PlaybackClock`: повтор
  по умолчанию для роликов короче 3 с и без длительности
  (`LoopsByDefault`, не для звука), «позиция стоит секунду» = доиграли;
  `Timecode` — длительность вверх, без объявленной — самая дальняя позиция.
- **Что внутри**: `MediaProbe` (Core, тест) по первым байтам — ISO (MP4,
  MOV, 3GP; `moov` целиком, до 64 МБ), Matroska / WebM (`Info`, `Tracks`,
  `Tags` до первого кластера, дальше — только по `SeekHead`), AVI (`hdrl`)
  → `MediaInfo`: длительность, дорожки — кодек, размер с поворотом матрицы
  `tkhd`, к/с, глубина (`hvcC`, `av1C`, `vpcC`, `Colour`), HDR —
  передаточная характеристика контейнера (16 — HDR10, 18 — HLG) или
  конфиг Dolby Vision, каналы (`esds`, `dac3`, `dec3`, `dOps`), язык, имя,
  битрейт (MP4 — сумма `stsz`, MKV — тег `BPS`). HDR, записанный только в
  потоке (VUI), не виден. `LoadVideoAsync` читает на пуле после `MediaUri`.
  Футер — таблица `MediaRows` (решение человека 2026-10-09: строки
  текстом — «неорганизованно»): значок вида дорожки, язык, кодек,
  частота, битрейт в общих столбцах (`SharedSizeGroup`), имя дорожки
  последним и приглушённо; до четырёх звуковых, субтитры одной строкой.
- **Не проигрывается движком WPF** (WebM, Ogg, Opus, AV1 — кодеки из
  Store он не видит): `MediaFailed` → `PreviewPane.PlayWithSystem` —
  `SystemPlayer` (WinRT `MediaPlayer`, открывает по содержимому при любом
  расширении). Звук — сам; картинка — frame server: `VideoFrameAvailable`
  → `CopyFrameToVideoSurface` в D3D-поверхность → `CopyToAsync` в
  программный кадр → `WriteableBitmap` в `SystemVideo` (больше 1920 по
  длинной стороне — ужимается на GPU); кадр в полёте один, лишние
  пропускаются. Не открыл и он — `PreviewController.ReportUnplayableAsync`:
  `Kind = Unsupported`, транспорта нет, в заглушке — причина по кодеку из
  `MediaProbe`; нет расширения из Store — его название и кнопка.
- **`AudioTags`** (Core): ID3v2.2–2.4 (синх-безопасные размеры), ID3v1,
  Vorbis, MP4-атомы, RIFF-INFO, Ogg Vorbis / Opus (`OggTags`: первые два
  пакета первого потока через страницы, длительность — гранула последней
  страницы минус pre-skip, обложка — `METADATA_BLOCK_PICTURE`); WMA, AIFF,
  AU — нет; AAC — только ID3 (кадр ADTS проходит проверки MP3 и врёт).
  Длительность FLAC из `STREAMINFO`, WAV — `data` / байты в секунду, MP3
  по первому кадру, VBR — `Xing` / `Info` / `VBRI`; кодировка 0 — по всем
  однобайтовым полям сразу. Кодек и разрядность — из тех же заголовков;
  M4A / MKA — первая звуковая дорожка `MediaProbe` (`WithStream`). Строка
  карточки — `PreviewController.AudioDetail`, каналы — `MediaRows.Layout`;
  `ReportUnplayableAsync` без `MediaProbe` берёт кодек карточки. Обложка: `APIC` / `PICTURE`, иначе
  `Cover.jpg`, `folder.jpg`, `front.*`, одноимённая, единственная картинка
  в папке.

## Текст, код, документы, веб

- **Текст**: `EncodingProbe` (Core): BOM → строгий UTF-8 → счёт 1251 / 866
  по регистру (строчные ×3; порог 8 кириллических букв — `ä ö ü` это
  кириллица в 1251). `TextProbe`: 8 КБ, BOM, нулевой байт — приговор, доля
  управляющих. Текст — первый мегабайт, оборванный `U+FFFD` срезается.
- **Поиск в тексте**: `TextFind` (Core, тест) — смещения, первое от
  каретки, по кругу, без регистра, от 10 000 — «N+». `FindBar`: смещения
  в тексте и коде, диапазоны в RTF (по run'ам — через смену формата не
  находится); `ForgetMatches` на смене `Text` / `CodeText` /
  `DocumentPath`; дочитанный RTF ищется заново в `LoadDocumentAsync`.
  Подхват — `PreviewController.FindRequest` в конце загрузки, запрос —
  `FindTextFor` (`MatchSnippet` и `ContentSearch.TextQuery`; у второй
  половины и «Сравнить» — `MainViewModel.FoundText`). `Ctrl+F` — сначала
  `OpenFind` панелей (вторая половина отвечает сама, `SecondSlot`), иначе
  поле над списком. `F3` / `Shift+F3` — `PreviewPane.FindAgain`; `PastLast`
  над выдачей по содержимому → `MainViewModel.NextFoundRow` (`FindWalk`,
  Core: по порядку списка, мимо найденных только по имени) и
  `FileList.SelectRow`. За показанным началом: `PreviewController.NoteRest`
  помнит остаток, `CountPastShownAsync` считает на пуле (`TextFind.Count`
  блоками по 64K с переносом хвоста; `EncodingProbe.Reader` — та же
  кодировка); `ShownTextLength` — слова заметки не ищутся.
- **Документ текстом**: `PreviewRoute.DocumentText` →
  `ContentSearchService.DocumentText` — только форматные экстракторы
  (общий показал бы бинарник буквами), кэш общий с поиском; `Kind = Text`,
  `TextWrap = Wrap`, пометка первой строкой; до 64 МБ и 200 000 знаков.
  Экстрактор ничего не дал, а файл — текст (`TextProbe`, «read me.doc»
  старых архивов) — `LoadTextAsync`.
  `ZipDocumentExtractor` ставит `\n` после `_lineEnds` (p, h, h1–h6, li,
  tr, table-row, row, si, br): абзац — строка и в панели, и в сниппете.
- **Таблица**: `PreviewRoute.Table` (CSV, TSV, XLSX, XLSM, ODS — до
  `DocumentText` и `Text`; CSV / TSV остаются в `TextLike`) →
  `SheetReader` (Core, тест) → `PreviewText.SheetPage` → `WebHtml`. Строки
  — как показал бы табличный редактор: CSV по RFC 4180, разделитель —
  `sep=` или по ровности первых 20 строк (`,` `;` таб `|`); XLSX — общие
  строки, ячейка по ссылке `r`, число по стилю `cellXfs` (дата, время,
  процент), формула — кэш; ODS — текст `text:p`, повторы раскрываются до
  последней заполненной. Первые 1000 строк, 100 столбцов, 2000 знаков в
  ячейке; пустой хвост отрезается, итог — до последней заполненной строки
  (XLSX дочитыванием до 200 000 строк: Excel пишет строку на каждую
  оформленную, `dimension` врёт). Страница без скрипта: листы —
  радиокнопки и CSS, буквы столбцов и номера строк `sticky`; ячейка —
  целиком, с переносом в ширину столбца до 360 px; больше
  `MaxSheetPageBytes` — строк вдвое меньше (`NavigateToString` берёт
  2 МБ). `</>` у CSV / TSV — `ShowTableSource`. Не читается таблицей —
  текстом. `.xls` — нет (BIFF, PLAN).
- **Markdown** — свой `MarkdownPipeline` (`UsePipeTables`, `UseGridTables`,
  `UseEmphasisExtras`, `UseTaskLists`, `UseAutoLinks`, `UseFootnotes`; не
  `UseAdvancedExtensions` — тянет iframe и `{#id .class}`).
- **Книги**: `Fb2Document` (HTML-фрагмент, потоковый `ReadCover`,
  namespace по локальному имени, бюджет 400 000 по ходу обхода с закрытием
  тегов, картинки `data:` до 6 МБ); `BookCover` (`.fb2`, `.epub`:
  `container.xml` → OPF → манифест, EPUB 3 `cover-image` / EPUB 2
  `<meta name="cover">` / по имени; DjVu, CHM, `.doc` — нет).
- **Подсветка кода**: AvalonEdit по расширению (и `.diff` / `.patch`);
  свои `Highlighting/*.xshd` (`Batch`, `ShaderLab`, `YAML`) через
  `HighlightingCatalog.EnsureRegistered()`; битый пропускается.
- **WebView2 изолирован**: `NavigationStarting` — только `file:` /
  `about:` / `data:`, попапы режутся; `WebResourceRequested` режет
  `http` / `https` / `ws` / `wss` / `ftp` (deny-list: рендерер раздаёт
  обвязку по внутренним схемам) — внешние картинки в Markdown не
  грузятся. Скрипты локального `.html` исполняются (TECHDEBT). Профиль —
  ARCHITECTURE, «Состояние и логи».

## Модели

`MeshFile` + `Stl` / `Obj` / `GltfReader` → `MeshData` (позиции +
`MeshPart` с индексами и цветом `Kd` / `baseColorFactor`; без текстур и
нормалей — WPF считает пофасеточные, обратная сторона притемнена). STL
двоичный — по `84 + 50 × n`; OBJ — отрицательные индексы от конца,
четырёхугольники веером; glTF — обход узлов с матрицами и памятью
посещённых, буфер `BIN` / `data:` / `.bin` рядом по простому имени. PLY —
текст и оба порядка байт, многоугольники веером, чужие элементы — по
размерам заголовка, облако точек — не модель; 3MF — `build` → объекты и
компоненты с матрицами 4×3, чужие части по `p:path` (Bambu, Orca), цвет —
`basematerials`. FBX 7 (решение человека 2026-10-09: свой разбор) —
`FbxDocument`: двоичный (записи с 32- или 64-битными смещениями с версии
7500, массивы zlib) и текстовый в одно дерево узлов; `FbxReader`:
`Geometry` (Mesh) через связи «OO» к `Model` и цепочке родителей, формула
FBX (смещения и опоры поворота и масштаба, пред- и пост-поворот, порядок
Эйлера) плюс геометрический сдвиг, цвет — `DiffuseColor` материала
полигона, ось «вверх» из `GlobalSettings`. FBX 6, NURBS, скиннинг,
текстуры — нет. Камера — из радиуса описанной сферы.

**Ракурс** — `ModelView` (Core: поворот вокруг вертикали, затем наклон,
вокруг центра коробки — порядок панели); по умолчанию 30° (решение
2026-10-09: «в лоб» шкаф или бензопила — плоский силуэт). Повёрнутое
мышью — `PreviewController.RememberModelView` → `IModelViews`
(`model-views.json`, ARCHITECTURE «Состояние и логи») и
`AsyncIcon.Invalidate`: модель открывается так же, плитка
перерисовывается в этом ракурсе (`MeshRaster` поворачивает так же; 3MF с
повёрнутым видом рисуется, а не берёт картинку слайсера).

## Карточки: программа, шрифт

- **Программа**: `PreviewRoute.Executable` → `LoadExecutableAsync` →
  `IExecutableInfoReader` (Core) ← `WindowsExecutableInfo`:
  `FileVersionInfo`, `PeHeader.Parse` (Core, тест: первые 4 КБ — машина,
  подсистема, DLL, CLR), `CreateFromSignedFile`, `WinVerifyTrust` без UI и
  сети (`WTD_REVOKE_NONE`, `CACHE_ONLY_URL_RETRIEVAL`; `TRUST_E_NOSIGNATURE`
  — «нет подписи», иное — «не подтверждается»; каталожные не смотрятся) →
  `ExecutableCard.Facts`. Подпись хэширует весь файл — отдельный
  `ReadSignature` после `FullSizeDwellMs`, по одной через `_signatureGate`,
  без отмены (ответ про ушедший файл отбрасывается по ссылке); больше
  `SignatureByItselfBytes` = 200 МБ — «не проверялась» и
  `CheckSignatureCommand`. Ответ помнится по пути, размеру и времени
  (`_signatures`, до 512).
- **Шрифт** (TTF, OTF, TTC): `PreviewRoute.Font` → `LoadFontAsync` — копия
  в `TempFiles` (своя папка на путь, время, размер: WPF не отпускает
  прочитанный файл шрифта до конца процесса) → `FontCard`: имена из
  `GlyphTypeface` по самому файлу, семейство — `Fonts.GetFontFamilies` по
  папке копии (`GetTypefaces` на файле перечисляет всю папку с
  синтетическими начертаниями), число начертаний `.ttc` — из заголовка
  `ttcf`; образец — только знаки шрифта (иначе WPF подставит чужие глифы;
  глиф 0 — «нет»): панграмма кеглями 12–48 целиком или никак, строки
  алфавита — теми знаками, что есть; символьный шрифт
  (`GlyphTypeface.Symbol`) и шрифт без строк — первые 64 своих знака.

## Миниатюры мимо шелла

Общая механика значков (тиры, ступени, ключ кэша, поколение) —
WINDOW, «Значки и миниатюры». Свои отрисовщики — ветка
`HasOwnCover` в `SystemIconProvider`, ключ по пути:

- **RAW** — `RawThumbnail` (`RawPreviewExtractor` + WinRT
  `Windows.Graphics.Imaging` с масштабом на разжатии; не `System.Drawing`
  — GDI+ сериализуется; оверлей ярлыка на RAW потерян осознанно).
- **Книги** — `Medium` / `Large` сначала спрашивают `BookCover` (`.fb2`,
  `.epub`; обложка «страницей»; 16 / 32 не получают).
- **PDF** — `PdfPageImage` (`Windows.Data.Pdf`), **всегда** (читалки вроде
  SumatraPDF provider не регистрируют); синхронно
  `.AsTask().GetAwaiter().GetResult()` — вызывающие на фоне. Пустая
  страница — не обложка (`IsBlankPage`: на белом почти ни один пиксель не
  отходит от цвета угла; строка `PDF page rendered blank` в логе): в
  долгом сеансе сканы (JPEG 2000 под маской JBIG2) выходят пустыми, а
  пустая в дисковом кэше переживала перезапуск. Значок шелла вместо неё на
  диск не пишется (`_blankPages`) — следующий процесс рисует страницу.
  Причина сбоя рендера не найдена: стенд в одном процессе (400 кругов,
  16 потоков) и свежий Wander рисуют те же файлы верно.
- **Аудио** — `AudioCover` (тег, иначе картинка рядом; кэш на папку по
  mtime).
- **Картинка, которую zip-файл хранит сам** — `EmbeddedThumbnail` (Core,
  тест): ODF `Thumbnails/thumbnail.png`, OOXML и 3MF — связь
  `…/metadata/thumbnail` из `_rels/.rels`, Krita / OpenRaster — превью,
  CBZ — первая картинка по имени; тоже «страницей», нет картинки —
  Office-документы — шелл.
- **Модели** (`MeshFile`) — `ModelThumbnail`: картинка 3MF без рамки,
  иначе `MeshRaster` (Core, тест: ракурс панели — сохранённый или по
  умолчанию, её свет, z-буфер, сглаживание 2×2, кадр по проекции, а не по
  сфере; до 64 МБ).
- **PSD** — `PsdThumbnail`, **TGA** — `TgaThumbnail` («Картинки»).
- **Шрифты** — `FontThumbnail` (Platform): GDI, файл на время рисунка
  `AddFontResourceEx(FR_PRIVATE)`, гарнитура по `FontHeader` (Core, тест:
  семейство из `name` 1 Windows, насыщенность и курсив из `OS/2`, у `.ttc`
  — первое начертание), подмена отсекается по `GetTextFace`; «Abc» / «Абв»
  по индексам глифов (`ETO_GLYPH_INDEX`), чего нет — не рисуется;
  символьный (`SYMBOL_CHARSET`) и без строк — три первых своих знака.
  Страница — в рамке обложки (`RenderCoverPlate`); не вышло — миниатюра
  шелла.
- **Картинки внутри архива** (шелл их не даёт): `ArchiveThumbnail` —
  `SizeOf` (`PKEY_Size`, до 32 МБ); zip — `ReadEntry` в память и
  `RawThumbnail.RenderPicture(bytes)` (ориентация —
  `MetadataExtractorImageReader.Read(Stream)`), остальное — `CopyOut` в
  `TempFiles.FolderFor("thumbnail|" + path)` под `SemaphoreSlim(1)` (у
  solid-архива запись стоит распаковки блока) и удаление сразу; RAW и TGA
  — всегда копией. Готовая копия панели (`TempExtraction.CopyPathFor`)
  читается. Ключ дискового кэша — путь записи + mtime и размер архива
  (`ThumbnailDiskCache.TryBuildFileName`). Хвосты — TECHDEBT.

## Хелперы отсмотра — `Wander.Core/Imaging/`

Папка уровня 0: чистые функции над `BgraImage`
(`byte[] Pixels, Width, Height, Stride`) — факты на входе, числа и маски
на выходе, тесты на синтетике. App подаёт пиксели и забирает маски
(`Preview/ReviewOverlay`), Platform меряет файл (`ISharpnessProbe` /
`Icons/SharpnessProbe`: WinRT, декодируется только нужная область).
Набор включённых — `ReviewHelpers` (App), один на окно: обе половины
сплита, полный экран, галерея; между запусками не хранится.

**Мера одна — крутизна края** (`Sharpness.Measure` → `CrispMap`):
градиент Собеля, делённый на местный контраст (окно 7×7): у ступеньки
около 4, у края шириной w — около 4/w, от контраста сцены не зависит.
«Верхние N % градиентов» резкий кадр и его размытую пару не различают,
крутизна — различает (стенд). Порог пикинга — 2,0. Мерить — только в
родном разрешении: на уменьшенном промах фокуса читается как попадание.

**Что на чём.** Пикинг и балл — полный кадр (у RAW — вшитый JPEG, у CR3
большой); в панели маска ужимается максимумом, в лупе — 1:1. Клиппинг,
гистограмма и кривые — рабочая копия ≤ 2560 px. Миниатюрам маска — с
шагом 2 (`Measure(step)`). Кривые теней и светов двигают середину не
больше чем на 10 уровней из 255.

**Точки против линий** (`FocusPeaking.Continuous`): пятно меньше шести
точек выбрасывается; вытянутое (в 2,5 раза длиннее ширины) или крупное
(от 200 точек) — грань в полный вес; мелкое круглое (снег, блик, зерно) —
вчетверо меньше. Балл (`Sharpness.Score`) — 98-й перцентиль крутизны по
области × доля «настоящих» граней среди всех краёв. Шкала крутизны — от
1,5 до потолка карты (255/64 ≈ 4), изогнута (`Bend` = 2,4; 2026-10-23):
70 на месте, а верх, где прямая шкала от 1,5 до 3,0 давала 100 всякому
резкому кадру, — от 84 (крутизна 3) до 100.

**Балл — по середине кадра, не по точке AF**: Canon пишет зону AF в
makernote (`CanonAfInfo`, поворот — `AfGeometry`), но человек
фокусируется и перекомпоновывает — зона бывает в углу над потолком; по
такой зоне промах получал балл выше попадания. Рамка рисуется — видно,
когда камера пишет ерунду.

**Режим RAW**: декод сенсора не шарпится, зерно в нём чётче граней —
перед замером `Luma.Denoise`, медиана 3×3. Балл и в этом режиме — по
вшитому JPEG.

**Что живёт между кадрами**: балл — `SharpnessProbe` по пути и штампу (до
4000); маски пикинга — `ReviewOverlay`, бит на пиксель, бюджет 32 МБ;
рендеры ячеек — `ReviewThumbs`, 120.

**Наложения**: метки клиппинга и пикинга — одна `Indexed4`, рамки AF —
геометрия поверх, вполовину прозрачные (`ReviewOverlay.AfOpacity`, решение
человека 2026-10-22: сплошная рамка перекрикивала снимок), всё —
`DrawingImage` с клипом по кадру (без клипа перо
у края раздувало картинку, и лупа уезжала). Панель — `ImgOverlay`
размером с `ImgFit`; лупа — своё, в разрешении кадра, место ставит
`UpdateZoomPosition`.

**Кто когда считает.** Панель — `PreviewController.ScheduleHelpers`:
токен привязан к загрузке, публикация — только если картинки те же
(`ReferenceEquals`); пока у CR3 не приехал большой JPEG, пикинг и балл
ждут (`Request.FullReady`). В галерее считают ячейки: `ReviewThumb`
заказывает при появлении и отменяет при уходе (`ReviewThumbs.Ask`:
пикинг — по полному кадру, клиппинг и кривые — по самой миниатюре, метки
— той же `ReviewOverlay.Marks`, что в панели; рамки AF — только точки в
фокусе, приглушённым `ReviewAfThumb`), `SharpnessController` складывает
баллы в строки пачками; перечитывание той же папки замеры не отменяет —
ответ о файле и штампе, а строка, вернувшаяся тем же объектом, ячейку не
переспрашивает. Очередь — `RankedGate` (Core, два потока): ранг
спрашивается, когда освобождается место, — файлы в панели (`ShownPath`),
остальное выделенное, прочее на экране (`MainViewModel.HelperRank`). Сама
панель — мимо очереди. `Alt` (`ReviewHelpers.Peek`) снимает метки, не
балл: ячейка заказывает балл и при нём. Отпускание `Alt` после сочетания
(`Alt+←`, `Alt+Shift`) приходит обычной клавишей, не системной, —
`OnPreviewKeyUp` в `MainWindow` и `FullscreenWindow` ловит обе
(2026-10-23: пропущенное держало `Peek`, и ячейки новых папок оставались
без баллов).

## Новый формат — по шагам

1. Разбор — в Core (`Preview/`, `Imaging/` или `Icons/`), чистая функция
   от байтов с тестом на байтах из билдера; чтение файла — `System.IO`
   только внутри `Preview/` (ARCHITECTURE, «Когда `IFileSystem`»).
2. Маршрут — строка в `PreviewRouter` в нужном месте порядка правил и
   тест; расширение — в `ImageFormats` / `FileTypeGroups`, если формат
   входит в группу (панель, миниатюры, действия, тип в поиске берут один
   список).
3. Отрисовка — App (`ImageDecoder`, `PreviewText`, `ModelBuilder`);
   кодек Store — строка в `StoreCodecs`.
4. Миниатюра — свой отрисовщик в Platform (ветка `HasOwnCover`) и
   `ThumbnailDiskCache.Generation` вверх без оглядки.
5. Доки: GUIDE — таблица форматов (`check-formats.ps1`); DONE «Панель
   просмотра»; фикстура в `tests/Fixtures` и `selfcheck` харнесса (QA);
   сюда — только новый механизм или ловушка.
