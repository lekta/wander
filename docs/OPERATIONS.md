# Операции: надёжное копирование — спека блока (0.6)

Спека Фабле, 2026-09-22, для блока 4 версии 0.5; решением человека
2026-09-24 блок перенесён в 0.6. Сверена с кодом после блока 2 —
2026-09-24 (номера строк — по дереву на эту дату; финализация блока 2 и
работы 0.5 их сдвинут — перед запуском блока сверить шаги 1, 5 и 7).
Реализует Опус («делай блок „Надёжное копирование“»), ревью и
финализация — Фабле; шаги с COM / P/Invoke (5, 6) Фабле делает сам или
ревьюит первыми, шаг 7 трогает модель панелей блока 2 — её правило тоже
первым смотрит Фабле. Файл живёт до финализации блока, затем удаляется
(механизмы → FILEOPS «Файловые операции», снапшот → DONE).

**Цель.** Копирование с флешки и карты фотоаппарата идёт минутами и должно
быть надёжным, прозрачным и ошибкоустойчивым: видно, что происходит и чем
кончилось, сбой не теряется, упавшее можно повторить. Решения человека
2026-09-22: [цель — съёмный том с буквой диска (Canon R8 виден как диск),
приоритет — надёжность, не скорость]; [MTP / PTP — 0.6]; [в 0.5 — самое
полезное, сложное — 0.6]; [завершённые операции живут в строке состояния,
пока пользователь не убрал; с ошибкой — ждут решения]; [журнал — сеанса].

## Границы

**Этот блок:** запись операции с итогом по элементам, строка
состояния держит завершённые, «повторить» и «убрать»; повтор при
временных ошибках ввода-вывода внутри операции; понятные сообщения по
кодам (место, FAT32 4 ГБ, устройство пропало); временное имя при
копировании; компьютер не засыпает; съёмные диски появляются и исчезают в
дереве, операция на пропавшем томе честно падает и повторяется после
возврата; журнал структурный с итогом и строкой в лог; `Ctrl+Z` ждёт
занятых; страховка корзины; имя занятого файла в папке.

**В спринте с 2026-10-09 (PLAN Y4, V, E):** телефон и фотоаппарат,
проверка копии, извлечение — раздел «Устройства» ниже; спека шагов —
перед запуском каждого блока, развилки — PLAN «Спринт 2».

**Вторая очередь (BACKLOG «Копирование — вторая очередь», «Импорт с
камеры»):** продолжение после вылета (журнал операции на диске, уборка
`.wander-tmp`); robocopy, небуферизованный ввод-вывод; таймауты UNC;
запись на устройство и удаление с него; папка по дате съёмки, вторая
копия, «удалить с карты после проверки»; `IRecycleBin.RestoreMany`.

## Устройства

**Стенд Y4a** — `artifacts\stands\DeviceStand` (вне гита, своя
COM-обвязка без ссылки на Wander; `run.ps1` — с таймаутом, вывод в
`runs\`): без аргументов — элементы «Этого компьютера» без
`SFGAO_FILESYSTEM`, корень хранилища и уровень под ним (`Android` — два),
атрибуты, `System.Size` / `DateModified` / `DateCreated`, открытие
обратно по parsing name; `deep N` — первые N снимков: листинг больших
папок, миниатюры (два прохода), `IStream`, поток, брошенный посреди,
`IFileOperation` в `out\`; `list <папка> [wpd|shell]` — одна папка
шеллом и WPD напрямую (`IPortableDevicePropertiesBulk`, ресурсы
миниатюры и содержимого); `watch S [read] [папка]` — события интерфейсов
(`CM_Register_Notification`), состояние устройств раз в 2 с, чтение по
кругу. Прогон 2026-10-19 — «Стенд Y4a — факты» ниже.

Разбор 2026-10-15 по документации WPD и шелла и по программам импорта
(Rapid Photo Downloader, Photo Mechanic, Hedge, ShotPut, Lightroom);
«стенд» — не проверено до Y4a. Устройства для стендов и живого прогона
(человек, 2026-10-15): пара флешек, OnePlus 6T и Pixel 6 (Android, MTP),
Quest 3 (MTP, Android внутри: снимки и видео в `Oculus\Screenshots` и
`Oculus\VideoShots`), Canon R8; iPhone — по просьбе. Подключает человек,
стенд читает из сессии, только чтение.

### Стенд Y4a — факты (2026-10-19)

Pixel 6 и OnePlus 6T (MTP, «Передача файлов»), Canon R8 (PTP); кабель —
USB 2, R8 — ещё и USB 3 (обычный кабель человека для копирования). Логи —
`DeviceStand\runs\`.

- **Имена.** Устройство —
  `::{20D04FE0-…}\\\?\usb#vid_18d1&pid_4ee1#<серийный>#{6ac27878-a6fa-4155-ba85-f98f491d4f33}`:
  «Этот компьютер» и путь интерфейса WPD — он же PnP ID для
  `IPortableDevice::Open` (у OnePlus и R8 — `…&mi_00#6&…#{6ac27878…}`).
  У Pixel в пути серийный номер, у OnePlus и R8 — номер экземпляра от
  порта: R8 по USB 3 — `…pid_330c#5&1f57c5c5&0&20#…`, по USB 2 —
  `…pid_330c&mi_00#6&1e70e772&1&0000#…`; другой порт — другой parsing
  name и PnP ID. Display name — модель (`Pixel 6`, `ONEPLUS A6010`, `Canon EOS R8`).
  Хранилище — `SID-{10001,,<ёмкость>}` (у R8 `20001`), имя — от устройства
  («Внутренний общий накопитель», `SD1`), `System.Size` — ёмкость. Объект —
  `{PUID}`: у Pixel первые 32 бита — время изменения (секунды Unix), у
  OnePlus — число (`{1746EA32-0000-…}`), у R8 — свой вид.
- **Parsing name обратно не открывается**: `SHCreateItemFromParsingName`
  на `…\SID-{…}\{PUID}\…` и на `<устройство>\SID-{…}` — `E_INVALIDARG`
  (0x80070057) у всех трёх; адрес редактирования («Этот компьютер\Pixel
  6\…») — 0x80070002. Открывается `<parsing name устройства>\<имя
  хранилища>\<display names…>`: папка — 4–16 мс, файл в большой папке —
  её листинг (тёплым: 1743 — 0,3 с, 7644 — 1,2 с). `FullPath` строки
  устройства — parsing name устройства и display names, не PUID.
- **ID после возврата**: PUID тот же (Pixel — кабель вынут и вставлен, R8
  — выключена и включена: файл найден по именам, PUID совпал); ID объектов
  WPD (`o1A`) — сеансовые: `Camera` у Pixel `o1A` → `o10`.
- **`BHID_EnumItems`**: имя; `System.Size` у файлов (= размер потока);
  `System.DateModified` у всех; `System.DateCreated` — только у камеры.
  Атрибуты Android — `copy, move, ren, del, drop` (файл — и `stream`), R8 —
  `ro, copy, del`, без `ren` / `move`. `SFGAO_HASSUBFOLDER` — «да» у всех
  папок, и пустых (бесполезен); имена с точки шелл скрытыми не помечает;
  `SFGAO_ISSLOW` не ставит. WPD: даты — `VT_DATE` в местном времени,
  `DATE_CREATED` у Android нет; тип содержимого CR3 — `UNSPECIFIED`.
- **Листинг большой папки телефона шеллом — минуты.** Холодный (первый
  после подключения): Pixel `DCIM\Camera` (1743) — 32,7 с; OnePlus
  `Camera` (7644) — 182 с, и это после прогрева WPD (у Pixel после него —
  17 с); строки идут потоком (первая — 10–20 мс, сотая — 0,6–1,2 с).
  Тёплый — 1,3 с и 5,9 с; кэш — в драйвере, общий для процессов, до
  переподключения. WPD холодным: `EnumObjects` — 0,44 с и 2,4 с, свойства
  пачкой (`QueueGetValuesByObjectFormat`, глубина 1) — 0,76 с и 7,4 с, по
  одному — 2,9 и 10,3 мс на объект; пачка отдаёт по строке на свойство (6
  на объект) — собирать по `WPD_OBJECT_ID`. Мелкие папки — 2–60 мс у
  Pixel, до 0,7 с у OnePlus. R8 (PTP): `IPortableDevicePropertiesBulk` нет
  (`E_NOINTERFACE`), по одному — 6,1 мс на объект, шелл — столько же (928
  — 5,5 с холодным, 0,7 с тёплым).
- **Миниатюры**: `IShellItemImageFactory` (256, только миниатюра) —
  медиана 17–23 мс у всех (300 CR3 — 5,4 с), второй проход не быстрее;
  `WTS_E_FAILEDEXTRACTION` (0x8004B200) — у части mp4 и jpg приложений (1
  из 50 у Pixel, 6 из 50 у OnePlus). WPD `WPD_RESOURCE_THUMBNAIL` у R8 — 9
  мс, ~14 КБ (миниатюра камеры, мелкая).
- **Содержимое**: `BHID_Stream` → `IStream`, буфер 1 МБ — 33–35 МБ/с у
  телефонов (USB 2), у R8 — 38 по USB 2 и 63 по USB 3; WPD
  `WPD_RESOURCE_DEFAULT` у R8 — 39 и 64 (оптимальный буфер 256 КБ);
  `IFileOperation` — так же (38 и 64). Листинг R8 от шины не зависит
  (715 — 5,0 с по USB 3, ~7 мс на объект — камера). Открытие — 0 мс, `Stat` даёт размер. Поток, брошенный
  посреди файла, — следующий файл читается сразу: отмена внутри файла —
  отпустить поток. `IFileOperation` с устройства на SSD (`FOF_NO_UI`) —
  та же скорость, окон нет; время изменения копии = устройства (20 из 20
  у каждого), время создания — момент копии.
- **Устройство ушло посреди чтения** (Pixel — кабель, R8 — выключатель):
  `IStream::Read` — 0x8007001F (`ERROR_GEN_FAILURE`, «присоединённое к
  системе устройство не работает»), в ту же 0,1 с — `REMOVAL` интерфейса
  WPD и USB-устройства; кабель и выключатель не различить. Пока нет —
  разбор по именам `E_INVALIDARG`. Вернулось — `ARRIVAL`, хранилище через
  ~2 с. `E_WPD_DEVICE_NOT_OPEN` / `IS_HUNG` через шелл не пришли.
- **Заблокированный телефон**: подключён запертым — в «Этом компьютере»
  есть, хранилищ 0, листинг пуст за 1 мс, **кода ошибки нет**;
  разблокировали и выбрали «Передача файлов» — телефон переподключается
  (`REMOVAL` + `ARRIVAL` за 1 с), хранилище появляется. Заперли после —
  доступ остаётся (OnePlus: чтение по кругу без сбоя; Pixel: 3 мин без
  изменений).
- **Новое на телефоне**: скриншот OnePlus — в листинге шелла через ≤ 4 с;
  дата изменения папки в листинге осталась старой.
- **Корень Pixel** (15): пусты `Alarms`, `Audiobooks`, `Podcasts`,
  `Recordings`, `Ringtones`, `Documents`; `Music`, `Movies` — один
  `.thumbnails`; `Notifications` — mp3 Viber; `Android` — `data` (73
  пакета), `media` (`com.whatsapp`, `org.telegram.messenger`, ещё 2),
  `obb` (`.nomedia`); `Pictures` (`Screenshots`, `ChatGPT`, `.thumbnails`),
  `DCIM` (`Camera`, `Ezviz`), `Download`, `Books`, `eboox_books`.
- **Корень OnePlus** (35): те же стандартные (пусты пять, `Notifications`
  — mp3 Viber); `Android` — `data` (200), `media` (9), `obb` (6), `obj`
  (`.um`); с точки — `.estrongs`, `.chartboost`, `.vkontakte` (516 файлов,
  листинг 11,6 с), `.org.ebookdroid`, `.Ota`, `.oprecyclebin`,
  `.ColombiaMedia`, `.TotalCommander`, файлы `.profig.os`, `.userReturn`;
  пустые вне списка — `Subtitles`, `bluetooth`; рабочие папки приложений
  — `AI Factory Stats`, `imgcache` (`.nomedia`), `backups`, `MapsWithMe`,
  `inshot`, `beam`; с медиа человека — `Telegram`, `VK`,
  `EasyVoiceRecorder`, `Books`, `Music`. `LOST.DIR` нет ни там, ни там.
  Пустая папка — листинг 2–16 мс.
- **Не проверено**: Quest 3 (человек: позже, тот же Android); телефоны по
  USB 3; автовыключение R8 по таймеру посреди копии (человек: кажется, при
  копировании не засыпает); меняется ли PUID папки Pixel с новым файлом;
  WPD-миниатюры и поток у телефонов; холодный листинг шеллом с
  `SHCONTF_FASTITEMS`; сколько ту же `Camera` открывает Проводник; отмена
  `IFileOperation` посреди файла; iPhone.

### Как источники видит Windows

| Источник | Для Windows | Что умеет | Подвохи |
|---|---|---|---|
| Флешка, внешний SSD / HDD, кардридер (SD, CF, CFexpress) — USB Mass Storage / UASP | том с буквой: FAT32 / exFAT / NTFS | всё, что диск: `CopyFileEx`, `DriveInfo`, `WM_DEVICECHANGE` (`DBT_DEVICEARRIVAL` / `REMOVECOMPLETE`) | FAT32: файл до 4 ГБ, 65 534 записей в папке, mtime с шагом 2 с в местном времени (допуск «новее» 2 с уже есть); exFAT без этих пределов; плохая карта — `ERROR_CRC` 23 / `IO_DEVICE` 1117 посреди файла; вынули раньше времени — `NOT_READY` 21; по умолчанию «быстрое извлечение», но «Извлечь» всё равно нужен (сброс кэша, размонтирование) |
| Фотоаппарат по USB — PTP / MTP (Canon, Nikon, Sony в режиме MTP); у Fujifilm «USB Card Reader», Panasonic «PC(Storage)», Sony «Mass Storage» — том | узел в «Этот компьютер» без буквы: WPD (`WpdMtpDr`), объекты с ID вместо путей, своя сессия | чтение, у PTP — удаление; одна передача за раз, поток только последовательный; миниатюры отдаёт сама (`WPD_RESOURCE_THUMBNAIL`); даты `WPD_OBJECT_DATE_CREATED` / `MODIFIED` — R8 отдаёт обе | выключение рвёт сессию и всю операцию (R8: 0x8007001F и `REMOVAL`); батарея; 20–60 МБ/с против 90–250 у кардридера (R8: 38 по USB 2, 63 по USB 3); после включения PUID тот же, ID WPD — новые |
| Телефон Android — MTP («Передача файлов»; PTP — только DCIM); Quest 3 — то же | то же, узел WPD | чтение, запись, удаление, переименование | доступ только при разблокированном экране (подключён запертым — без хранилищ, кода нет; заперли после — доступ остаётся); листинг из кэша медиасканера — новое видно не сразу, удалённое висит (OnePlus: скриншот — через ≤ 4 с); большая папка шеллом холодным — минуты; «объект не найден» после переиндексации; корень засорён папками системы и пустыми стандартными (`Android`, `LOST.DIR`, `.thumbnails`, `Alarms`, `Ringtones`…) — прячутся как системные (PLAN Y4b) |
| iPhone / iPad — PTP через драйвер Apple (iTunes / «Устройства Apple» из Store) | узел WPD, только `Internal Storage\DCIM` | только чтение | «Доверять этому компьютеру» и разблокированный экран; HEIC / HEVC или конвертация в JPEG / H.264 — настройка телефона («Перенос на Mac или ПК») |
| Сетевая папка — SMB | UNC | всё, что диск | Y3; таймауты спящей шары — вторая очередь |

### Чего ждёт фотограф и что из этого наше

Из практики (Rapid Photo Downloader, Photo Mechanic, Hedge, ShotPut):
карта в кардридере — основной путь (быстрее, не сажает камеру), кабель —
для удобства; копировать, не переносить; карту не чистить, пока копия не
проверена, форматировать в камере; копия с проверкой (хэш или побайтно)
— норма в программах импорта, в Проводнике и Lightroom её нет; вторая
копия сразу; папка назначения по дате съёмки и переименование; повтор
имён с карты (`IMG_0001` по кругу) — конфликты решаются до копии; с
карты ничего не удаляется само. Наше в спринте: K (повтор, сообщения,
временное имя, компьютер не спит), Y2 (пришёл / ушёл, закладка сереет),
V (проверка), E («Извлечь»), Y4 (устройство — папка, копирование с
него, пауза при пропаже); совпадения — окном, «идентичны» у устройства —
по размеру и дате. Папка по дате, вторая копия, удаление после проверки,
«только новые» — BACKLOG «Импорт с камеры» (PLAN, развилка 4).

### Чем читать устройство

- **Шелл**: `IShellItem` по parsing name устройства и display names
  (PUID обратно не разбирается — «Стенд Y4a — факты»); перечисление
  `BHID_EnumItems`, как у корзины и архивов;
  свойства `System.Size`, `System.DateModified`, `System.ItemNameDisplay`,
  `SFGAO_FOLDER`; миниатюра — `IShellItemImageFactory` (устройство отдаёт
  свою, быстро); содержимое — `BHID_Stream` → `IStream`, только
  последовательно; копирование — `IFileOperation` (прогресс
  `IFileOperationProgressSink`, отмена между элементами — как у
  извлечения, TECHDEBT) либо свой цикл по `IStream`. Плюсы: одна ветка с
  корзиной, архивами и «Компьютером», миниатюры и значки даром. Минусы:
  `FullPath` строки — parsing name с ID, не путь (крошки — по display
  name); даты при копировании **на** устройство `IFileOperation` теряет
  (отчёт Zabkat); отмена внутри файла и хэш — только своим циклом.
- **WPD напрямую**: `IPortableDeviceManager::GetDevices` →
  `IPortableDevice::Open` → `IPortableDeviceContent::EnumObjects`,
  свойства пачкой `IPortableDevicePropertiesBulk` (у телефонов холодный
  листинг в 20–30 раз быстрее шелла, у R8 пачки нет),
  `IPortableDeviceResources::GetStream` (`WPD_RESOURCE_DEFAULT`,
  `WPD_RESOURCE_THUMBNAIL`), события `IPortableDeviceEventCallback`
  (объект добавлен, устройство ушло). Плюсы: полный контроль, события,
  размер и дата заранее. Минусы: ~10 COM-интерфейсов и
  `PROPVARIANT`-коллекции руками в Platform; пакет `MediaDevices` (MIT,
  обёртка WPD) — сопровождение не подтверждено, чужой код ради одной
  ветки — против правила «без пакетов без причины».
- **Рекомендация** (до стенда): шелл — узел, навигация, миниатюры, панель
  просмотра (временная копия, как у записи архива — `TempExtraction`);
  свой цикл по `IStream` — копирование (хэш на лету, отмена внутри файла,
  даты из свойств); WPD напрямую — если стенд Y4a покажет, что шелл не
  отдаёт события или размеры.
- **После стенда** (развилка 6; решение человека 2026-10-19: [WPD
  напрямую, как предложено; в настройках — фолбэк на шелл]): размеры,
  даты и поток шелл отдаёт; события — не шелл, а уведомления интерфейса
  WPD (есть без WPD API); косяк — холодный листинг большой папки телефона
  (33 с и 3 мин против 1,2 и 10 с у WPD). Предложение: **WPD напрямую для
  содержимого всех устройств** — листинг (пачкой, где есть; у PTP — по одному, как
  шелл), миниатюры (`WPD_RESOURCE_THUMBNAIL`) и поток копирования
  (`WPD_RESOURCE_DEFAULT`): `IShellItem` файла из большой папки — тот же
  листинг шеллом, гибрид «листинг WPD, миниатюры шеллом» упирается в него.
  Шелл — строка устройства в «Компьютере» (имя, значок), parsing name
  устройства = PnP ID. Одна ветка на телефоны и камеру. Цена: ~10
  COM-интерфейсов и `PROPVARIANT` в Platform (обвязка — в стенде), сборка
  строк пачки по объекту. До спеки Y4b — WPD-миниатюры телефона и их
  размер (у R8 ~14 КБ — мелко для крупных плиток галереи) и Проводник на
  той же папке.

### Пропажа устройства и пауза

`WM_DEVICECHANGE` с `DBT_DEVICEINTERFACE` после
`RegisterDeviceNotification(GUID_DEVINTERFACE_WPD)` — та же
`VolumeChangeHub` (шаг 7), расширенная до устройств; ключ «то же
вернулось» — не PnP ID: у OnePlus и R8 он от порта (стенд; серийный
номер WPD — проверить). Стенд: `REMOVAL` приходит в ту же 0,1 с, что
ошибка чтения 0x8007001F; кабель и выключенная камера — одно и то же,
причину «камера выключилась» по коду не отличить. `OperationTracker`
получает состояние `Paused` с
причиной (ключ ресурса: «камера выключилась», «телефон заблокирован»,
«устройство отключено»); операция ждёт возврата до 10 минут [решить], по
возврату объекты находятся заново по папке, имени и размеру (стенд: PUID
у Pixel и R8 тот же, ID WPD — новые), продолжает с первого непроверенного элемента
(`OperationItem.Verified`), не дождалась — `Failed`, «Повторить».
Машина состояний — Core, тест с фейком устройства; на UI — строка
операции «ждёт устройство» с «Отмена».

### Ошибки устройств — понятными словами

К шагу 4 (`DescribeError`): `ERROR_DEVICE_NOT_CONNECTED` 1167 и
`ERROR_NOT_READY` 21 — «устройство отключено или не готово»;
`E_WPD_DEVICE_NOT_OPEN`, `E_WPD_DEVICE_IS_HUNG` — «камера выключилась
или уснула — включите и повторите» (через шелл не пришли — путь WPD
напрямую); `ERROR_GEN_FAILURE` 0x8007001F посреди чтения с устройства —
«устройство отключено или выключено» (стенд: и кабель, и выключатель);
телефон с заблокированным экраном — кода нет: устройство без хранилищ →
«разблокируйте телефон и выберите "Передача файлов"»; «объект не найден»
у телефона — «телефон обновил список файлов — обновите папку (`F5`)»;
`ERROR_CRC` 23 / `ERROR_IO_DEVICE` 1117 — «ошибка чтения с карты —
носитель или кабель» (есть в шаге 4). Числа — в комментарии Core,
текст — ресурсы.

### Проверка копии (V)

`CopyFileEx` байтов не отдаёт — свой цикл: `FileStream` с буфером 1–4 МБ,
`FileOptions.SequentialScan`, `IProgress<long>` и токен как сейчас,
хэш на лету (SHA-256 из BCL: аппаратно ускорен, быстрее любого USB; или
CRC32C своей таблицей — без пакетов); после закрытия цель перечитывается
и сравнивается — на SSD это секунды, на карте источник второй раз не
читается. Стенд скорости до решения (V1): свой цикл против `CopyFileEx`
на SSD → SSD, HDD, флешке USB 3, кардридере, SMB — числа в
`measurements/`. Итог — `OperationItem.Verified`, строка операции
«проверено N»; расхождение — `Failed` «копия не совпала», повтор как у
временной ошибки. **Стенд V1** (2026-10-15, `measurements/0.6-dev.md`):
SSD → тот же SSD — свой цикл 1 МБ с `SetLength` вровень с `CopyFileEx` в
разбросе самого диска (100–220 МБ/с), перекрытие и 4 МБ выигрыша не дали;
проверка — +10–20 % (копия читается без кэша, `FILE_FLAG_NO_BUFFERING`).
Флешка USB 3, кардридер, HDD, SMB — не сняты, устройства человека. Настройка «Проверять скопированное»: со съёмных и
устройств (по умолчанию) / всегда / никогда. Тот же цикл — для `IStream`
устройств (Y4c).

**Надёжность своего цикла.** Это те же `ReadFile` / `WriteFile`, что под
`CopyFileEx`; так копируют robocopy, TeraCopy, rsync, программы импорта.
Ошибки приходят теми же кодами (`IOException.HResult`), недописанное
убирает временное имя (шаг 5). Что `CopyFileEx` делает сверх цикла и
где цикл проигрывает: копия на стороне SMB-сервера, когда обе стороны
на одном сервере (без прокачки через клиент); клонирование блоков на
ReFS / Dev Drive (мгновенная копия на том же томе); дополнительные
потоки NTFS (`Zone.Identifier` — метка «из интернета» — и редкие
метаданные программ); зашифрованный EFS остаётся зашифрованным (свой
цикл расшифровал бы тихо); разреженные файлы остаются разреженными;
плейсхолдеры OneDrive. Атрибуты и время изменения свой цикл ставит
после копии, время создания — новое, как у Проводника.

**Откат на `CopyFileEx`** (решение человека 2026-10-15: не настройка
пользователя — прецедент REJECTED «корзина чтением `$I`»: выбор «быстро
или надёжно» пользователь оценить не может): правило в коде — системное
копирование берётся само для источника с атрибутами `Encrypted` или
`SparseFile`, с дополнительными потоками (`FindFirstStreamW`, дёшево),
для пары путей на одном SMB-сервере и для пары на одном томе ReFS;
проверка для них — перечитыванием обеих сторон, когда настройка велит.
Свой цикл упал незнакомой ошибкой — элемент повторяется системным
(в пределах тех же трёх попыток), в записи операции и в логе — каким
движком шёл каждый элемент. Переключатель «Отладка» → «Копировать
средствами Windows» выключает свой цикл целиком — для разбора жалоб,
пишется в лог первой строкой операции.

### Безопасное извлечение (E)

После операции со съёмным источником или целью — «Извлечь» в строке
завершённой операции: глагол оболочки у корня тома (тот же, что в меню
строки «Компьютера»); не дал — `CM_Request_Device_Eject` (вторая
очередь); занято — кто держит (`IFileLockInspector`). Устройствам WPD
извлечение не нужно. Том ушёл сам — кнопка гаснет (Y2).

Как сделано (2026-10-19, по логу прогона: Wander держал флешку сам):

- **Один путь** — `MainViewModel.EjectAsync` у кнопки операции и у строки
  `Eject` меню диска (глагол оболочки `eject` — своя строка, MENU):
  открытая папка на томе уходит, панель просмотра отпускает файл, глагол
  — свежей сессией меню; панели после глагола **не** перечитываются
  (чтение `F:\` в момент запроса и было «занято»). Ушёл ли том — по букве
  (`Environment.GetLogicalDrives`), носитель — раз в 2 с: спросить сам
  том — открыть его. Ушёл — диски перечитываются сами (`Ejected`), не
  дожидаясь сообщения системы; не ушёл за 5 с — «не извлечён» и ещё 2 мин
  ожидания: окно Windows «используется» может отпустить.
- **Запрос Windows** (трей, Проводник, свой глагол) — `VolumeChangeHub`:
  на том открытой папки (съёмный и «фиксированный» USB) — свой хэндл
  корня и `RegisterDeviceNotification(DBT_DEVTYP_HANDLE)` (`Guard`, из
  `WindowsDirectoryWatcher.Watch`). `DBT_DEVICEQUERYREMOVE` — хэндл
  закрыт, `Releasing`: сторож закрывается, панель просмотра отпускает
  файл (без возврата — вторая попытка не упрётся в тот же файл);
  `QUERYREMOVEFAILED` — хэндл и сторож снова; `REMOVECOMPLETE` /
  `REMOVEPENDING` — «том ушёл», раз на два пути (с широковещательным —
  окно 10 с). Строки лога: `Volume guarded`, `Volume removal asked`,
  `refused`, `Volume removed: F:\ (broadcast | notice)`.
- **WebView2** уходит на `about:blank`, когда панель уходит с веб-вида
  (`PreviewPane.LeaveWebPage` по смене `Kind`): скрытый, он держал PDF.
- F5 перечитывает и список дисков (`PanelRules.Reread`); открытый
  «Компьютер» в списке — при смене томов.

## Сейчас

- `SystemIOFileSystem.CopyFile` (`:175-186`) → `CopyFileWithProgress`
  (`:304-344`): `CopyFileExW` без флагов, кроме `COPY_FILE_FAIL_IF_EXISTS`
  (`ApplyOne` всегда передаёт `overwrite: false`); без прогресса и отмены
  — `File.Copy`; прогресс дельтами, отмена — `PROGRESS_CANCEL` (1235 →
  `OperationCanceledException`), недописанное при отмене убирает Windows;
  любая другая ошибка — `IOException(Win32-сообщение и оба пути, HRESULT
  0x8007xxxx)`. `MoveEntry` (`:212-238`): между томами папка —
  `CopyDirectory` + `Directory.Delete`, файл — `CopyFileWithProgress` +
  `File.Delete`; уборки при сбое нет. Повторов, проверки, временного имени
  нет (временные имена — только `ReplaceAtomic` `:259-276` и
  `FileOperationService.ScratchName`).
- `BatchExecutor.ApplyOne` (`:722-773`) — единственное место вызовов
  `CopyFile` / `CopyDirectory` / `MoveEntry` в пакетном пути; занятых ждёт
  только перенос (`Gate.Retry`, `WaitForFile`), копия не ждёт. `ApplyEntry`
  (`:353-425`) ловит `OperationCanceledException` (статус `Cancelled`,
  `CreateAction` на любой существующий `dest` — файл или папку) и
  `Exception` (`Outcome(Failed, false, ex)`, ошибка доезжает до результата
  через `failure ??=`). Результат — `BatchItemResult(Source,
  FinalDestination, Status, Error, Busy)` (`:910`), **один на группу**:
  спутники свёрнуты в основной файл; `BatchItemStatus { Ok, Skipped,
  Replaced, Renamed, Merged, Cancelled, Failed }`. Удаление —
  `DeleteResult(Path, Status, Error, Busy)`, `DeleteStatus { Ok, Failed,
  Cancelled }` (`:915-916`).
- `OperationTracker`: `Begin(verb, total, totalBytes, bytesAreWork, token)`
  (`:73-83`) → `IOperationHandle` (`Advance`, `AdvanceBytes`,
  `SetCurrentPath`, `SetTotalBytes`, `SetWeighing`, `Dispose`; `:321-348`);
  `Dispose` удаляет операцию (приватный `Remove`) — завершённых записей
  нет. `OperationSnapshot` (`:361`) — только ход; `WhenIdleAsync` ждёт
  пустого списка идущих. Все вызывающие только `Dispose`: `BatchExecutor`
  `:102` (удаление) и `:119` (пакет), `UndoService:139`,
  `ExtractionService:103`, `ExternalActionRunner:81`, `DebugOperation:91`.
  `OperationViewModel` — только живая строка.
- Строка состояния — `MainWindow.xaml:622-785`: блок операций `:658-748`
  (видимость — `HasActiveOperations`, кнопка `OperationsButton` с
  `OperationsSummary` и `AggregateProgress`, попап `:694-746` по
  `Operations`); значок важности у строки статуса — `:756-781`
  (`StatusSeverity`, ресурсы `StatusWarningGlyph` / `StatusErrorGlyph`).
- **`HasActiveOperations => Operations.Count > 0`** (`MainViewModel:773`)
  держит не только полосу: тик сторожа ждёт, пока он истинен (`busy`,
  `:2455-2457`), выход спрашивает про идущие (`MainWindow.xaml.cs:131-148`,
  `:245-262`). Поэтому список назначения и не мигает: сторож молчит до
  конца операции, вставка и drop перечитывают сами (`:5229-5232`,
  `:1799-1801`).
- Итог: `MainViewModel.ReportBatchResults` (`:5406-5436`) — счётчики и
  **первая** ошибка (`DescribeError` `:5497-5517`), `Say(text,
  StatusSeverity)` (`:3278`) → `Journal.Note` + строка статуса;
  `SetStatusQuietly` (`:3263`) — строка без журнала, всегда Info.
  `ActionJournal` (`Core/Logging`): `Note(text, at, severity)`, `Render()`,
  предел 500; окна нет — `ShellCommandsController.OpenJournal` (`:88-105`)
  пишет `journal-<pid>.txt` и открывает редактором.
- `TransientFiles.ReplaceSuffix = ".wander-tmp"`; сторож
  (`WindowsDirectoryWatcher.OnRenamed:118-121`) временные не сообщает, а
  переименование **из** временного имени считает нестроструктурным и без
  `OldPath`. Тик по пути, которого нет в листинге, уже перечитывает папку
  (`FolderSession.DecideWatchTick:425-430` через `FolderChanges.RowsFor`,
  тест `AChangedFileNobodyShows_Relists_WithoutBotheringTheTrees`).
- Устройства: строки дисков — `PanelRow` вида `Drive` на верхнем уровне
  панели «Компьютер» (ключ `""`; модель блока 2 — `PanelState`,
  `PanelRules`). Читаются один раз: `WorkspaceStarted` →
  `WorkspaceController.ReadLevel("")` из `GetRoots()` (неготовые диски
  пропускаются); перечитывание (`PanelRules.Reread:450`) верхний уровень
  пропускает. `WM_DEVICECHANGE` никто не слушает; хуков `HwndSource` в App
  нет (`MainWindow.OnSourceInitialized:120-129` — только геометрия);
  `SetThreadExecutionState` / power requests нет.
- Харнесс: `fs` — create / mkdir / append / delete / lock / unlock, без
  `rename` (шаг `rename` — переименование в приложении); проверки итога
  операции нет; `wait-idle` операций не ждёт — только тишину лога и конец
  чтения листинга (`WaitIdleAsync:320-340`).

## Шаги

Порядок: сначала запись операции (1) — на неё опираются повтор (2), журнал
(3) и отладочная операция («Отладка» → «Операция», AI1 — стенд для всего
блока); затем то, что меняет поведение копирования (4, 5); затем окружение
(6, 7) и хвосты.

### 1. Запись операции: завершённые остаются

Core `Operations/`:

- `OperationItem(string Source, string? Destination, BatchItemStatus Status,
  Exception? Error, int Attempts = 1)`; `OperationOutcome { Done, Problems,
  Cancelled, Failed }` (`Problems` — есть `Failed` / `Skipped` с ошибкой при
  сделанном, `Failed` — ничего не сделано).
- `OperationRecord(long Id, string Verb, OperationOutcome Outcome, int
  Total, int Done, int Failed, int Skipped, int Cancelled, long Bytes,
  TimeSpan Elapsed, DateTime FinishedAtUtc, string? Target,
  IReadOnlyList<OperationItem> Items)` — `Items` обрезаны до 200 (первые и
  все проблемные), `ItemsTruncated` числом.
- `IOperationHandle.Finish(IReadOnlyList<OperationItem> items, string?
  target)` — считает итог, переводит операцию из `Running` в `Finished`;
  `Dispose` без `Finish` — как сейчас: запись `Done` без элементов, если
  токен не отменён, иначе `Cancelled` (извлечение, откат, свои действия
  переходят на `Finish` позже, не в этом шаге).
- `OperationTracker`: список завершённых (потолок 50, старые вытесняются),
  `IReadOnlyList<OperationRecord> Finished`, `Dismiss(long id)`,
  `DismissFinished(bool problemsToo)`, событие `Changed` — общее;
  `Snapshot()` и `WhenIdleAsync` — только идущие (выход из приложения
  завершённых не ждёт).
- `BatchExecutor.RunBatchAsync` (`:112-132`): после `ApplyBatch` —
  `op.Finish(items, destinationFolder)`; элемент — один на
  `BatchItemResult`, то есть на группу (спутники свёрнуты в основной).
  `DeleteManyAsync` (`:99-107`) — то же из `DeleteResult`.

App:

- `OperationViewModel`: `IsFinished`, `Outcome`, `OutcomeGlyph`
  (`Segoe MDL2`: `E73E` готово, `E7BA` с проблемами, `EA39` ошибка, `E711`
  отменено), `OutcomeText` («готово 12», «с ошибкой 1, пропущено 2»),
  `ProblemLines` (до 10: имя — причина через `DescribeError`), `RetryCommand`
  (`CanExecute` = есть `Failed` или `Cancelled`), `DismissCommand`.
- `MainViewModel.RebuildOperations` (`:5550-5592`): строки идущих как
  сейчас, затем завершённые из `Finished`. **`HasActiveOperations` —
  только идущие**, не `Operations.Count`: на нём тик сторожа и вопрос при
  выходе, завершённая строка не должна их держать. Новое `HasOperations` —
  видимость блока; `OperationsSummary` при отсутствии идущих — «✓ 2 · ⚠ 1».
- `MainWindow.xaml`, попап (`:694-746`): строка завершённой — глифы,
  глагол, итог, кнопки «Повторить» / «Убрать», раскрывающиеся
  `ProblemLines`; внизу «Убрать завершённые» (`DismissFinishedCommand`, без
  проблемных; `Shift` — все). Строки — ресурсы `Operation*` (есть `Items`,
  `Bytes`, `Speed`, `Remaining`, `Percent`, `One`, `Weighing`, `Many`).
- Выход: вопрос про идущие операции как есть (`OnClosing`,
  `ConfirmExitWithOperations`, `StopOperationsThenClose`; при сбое —
  `App.StopOperationsBeforeDying`); завершённые с проблемами выход не
  держат [решение].
- Отладочная операция — на `Finish` в этом шаге (комментарий
  `DebugOperation:105-107` это обещает): «ошибка на 4-м» — элемент `Failed`
  с `IOException` (сейчас только счётчик и строка лога); «отмена посреди»
  — отменять токен, под которым операция заявлена (сейчас отменяется
  связанный, а операция стоит под токеном окна — запись вышла бы `Done`).

Тесты: `OperationTrackerTests` — `Finish` считает итог, `Dispose` без
`Finish`, потолок 50, `Dismiss`, `Changed` на каждое; `BatchExecutorTests`
— запись после копии с одним `Failed` (FakeFileSystem, шаг 4).

### 2. Повторить

- Core `Operations/OperationRetry.cs`: `RetryPlan? Plan(OperationRecord)` →
  `RetryPlan(string Verb, IReadOnlyList<(string Source, string
  DestinationFolder)> Items)` из `Failed` и `Cancelled` элементов (источник
  на месте — переносился ли он, неважно: не доехал); элемент — основной
  файл группы, спутники собираются заново, как при вставке; нет элементов
  — null. Тест: смешанная запись → только упавшие; переименованные и
  пропущенные не повторяются.
- `MainViewModel.RetryOperationAsync(long id)`: план → по папкам назначения
  → тот же путь, что вставка: группировка спутников (как `:5192`) и
  `CopyManyAsync` / `MoveManyAsync` (`FileOperationService:294-302`) с
  окном прогресса, конфликты спрашиваются заново; новая запись, старая
  убирается при старте повтора. Выделение после повтора приземляется, как
  после вставки, — только если папка назначения открыта
  (`FolderSession:217-225`). Копирование: источник изменился — конфликт
  решает окно, как обычно.
- Удаление: «Повторить» — `DeleteAsync` (`:4151`) по `Failed`, с
  подтверждением, как у обычного удаления (столп 2), → `RunDeleteAsync`
  (`:4233-4333`); `AskRetryInUse` (`:4340`) остаётся (спрашивает сразу).

### 3. Журнал структурный и строка итога в лог (AD5)

- Core `Logging/ActionJournal.cs`: `Record(JournalEntry)`;
  `JournalEntry(DateTime At, JournalKind Kind, string Headline,
  IReadOnlyList<string> Lines, StatusSeverity Severity)`; `JournalKind {
  FolderOpened, FileOpened, Copied, Moved, Renamed, Recycled, Deleted,
  Restored, Created, Extracted, Cancelled, ActionRun, Error, Note }`;
  `Render()` — заголовок как сейчас, строки с отступом два пробела, первые
  50 и «… ещё N»; предел 500 — по записям. `Note` остаётся для строк без
  структуры (поиск, буфер, ошибки, открытие папки — `MainViewModel:2711`,
  `:2828`).
- App: `ReportBatchResults` строит запись из `OperationRecord`
  («Скопировано в E:\Video — 12 файлов, 12,3 ГБ, 5 мин 27 с»; строка на
  элемент; у переименования «было → стало» из `plan`), зовёт
  `Journal.Record`, а строку статуса ставит без второй записи в журнал:
  `SetStatusQuietly(text, severity)` — сейчас он всегда Info, получает
  важность. Открытие и запуск — запись `FileOpened`: `OpenEntry`
  (`:1661-1698`, `_shell.Open` `:1690`), «Открыть с помощью» (`OpenWith`
  `:4061` → `ShellCommandsController.OpenWith` `:120-127`), временная
  копия из архива (`OpenArchiveEntryAsync` `:5389-5403`).
- Лог: `BatchExecutor` в конце `RunBatchAsync` — `Copy done: 12 items,
  12.3 GB, 327.4 s, 38.5 MB/s; failed 1, skipped 0` (INFO, английский —
  технический лог; сейчас только строка на элемент, `:397`).
- Строки отката: `IUndoableAction` (`Undo/IUndoableAction.cs`, default-члены
  уже есть — `PathsAfterUndo`, `MetadataTargets`, `MovesOnUndo`, `Steps`)
  получает `string Verb => OperationVerbs.Undo` и `int ItemCount => 1`,
  реализации ставят своё: `MoveAction` (`UndoableActions.cs:28-34`),
  `CompositeAction` (`:81-126`), две в `CompanionMetadataService` (`:354`,
  `:378`), составные из `ExtractionService` (`:326-328`) и `RenameMany`
  (`:205`). Английский `Description` сейчас видят `StatusUndone`
  (`MainViewModel:4475`), подсказка «Отменить» (`NextDescription`, `:1566`)
  и `NamesOf` (`:4543`) — все три строятся из `Strings.Get(verb)` и числа;
  в лог (`Unwind:214`, `PushComposite:701-713`) `Description` остаётся.
- Окно журнала — как сейчас (файл в редакторе); формат текста — тот же
  `Render`.

Тесты: `ActionJournalTests` — `Record` + `Render` (отступы, «ещё N»,
предел по записям); `UndoableActionsTests` — `Verb` / `ItemCount` у
`MoveAction`, `CompositeAction`.

### 4. Повтор при временных ошибках и понятные сообщения

- Core `FileSystem/TransientIo.cs`: `bool IsTransient(Exception)` — по
  `HResult & 0xFFFF` ∈ {21 `NOT_READY`, 23 `CRC`, 31 `GEN_FAILURE`, 59
  `UNEXP_NET_ERR`, 64 `NETNAME_DELETED`, 121 `SEM_TIMEOUT`, 1117
  `IO_DEVICE`, 1167 `DEVICE_NOT_CONNECTED`, 1231 `NETWORK_UNREACHABLE`}
  (не путать с `TransientFiles.IsTransient(string)` — то про путь);
  `IoRetry.Run(Action, RetryPolicy, CancellationToken, Action<int>?
  onRetry)`, `RetryPolicy(int Attempts = 3, TimeSpan[] Delays = 1, 3, 5 с)`
  — сон отменяемый, последняя ошибка наружу.
- `BatchExecutor.ApplyOne` (`:722-773`): копирование файла и перенос между
  томами — в `IoRetry.Run`; перед повтором цель убирается (временное имя,
  шаг 5, делает это само); `onRetry` → номер попытки числом в
  `OperationSnapshot.Attempt` (текста в Core нет — `OperationViewModel`
  пишет «повтор 2 из 3»). **`CurrentPath` не трогать**: его как настоящий
  путь читают строка операции и панель просмотра (`OperationViewModel:134`,
  `PreviewController:1506-1507`). Число попыток — в
  `BatchItemResult.Attempts` → `OperationItem.Attempts`. `BusyGate.Retry`
  (занятость, `FileInUse` = только 0x80070020) остаётся отдельным слоем:
  занятость — не временная ошибка ввода-вывода.
- App `DescribeError` (`:5497-5517`; сейчас — отказ корзины, путь занят
  другой операцией, `IOException` с держателем, иначе `ex.Message`): коды →
  текст: 112 `DISK_FULL` («нет места на X:»), 223 `FILE_TOO_LARGE` («файл
  больше 4 ГБ, а диск — FAT32», если `IVolumeInfoProvider.Describe(цель)?
  .FileSystem == "FAT32"`; у неготового тома `FileSystem` пустой), 1167 /
  21 («устройство отключено или не готово»), 23 / 1117 («ошибка чтения с
  диска — носитель или кабель»); прочие как сейчас.
- FakeFileSystem: `CopyFailures` — `Dictionary<string, Queue<Exception>>`:
  очередь исключений на путь, каждый `CopyFile` снимает одно; пустая —
  успех (рядом уже есть `MoveInUseFor`, `RenameFailures`, `CopyChunk`;
  `CopyFile` пишет цель до прогресса).

Тесты: `TransientIoTests` (коды); `IoRetryTests` (две ошибки — третий
успех; три — наружу; отмена во сне); `BatchExecutorTests` — копия с двумя
временными ошибками = `Ok`, `Attempts = 3`; с постоянной = `Failed`,
`Attempts = 1`.

### 5. Временное имя при копировании

- `TransientFiles.TempNameFor(string destination)` → `destination +
  ".wander-tmp"` (тот же суффикс).
- `SystemIOFileSystem.CopyFile` в ветке с прогрессом / отменой: `temp =
  TempNameFor(dest)`; сирота с таким именем — удалить (наша);
  `CopyFileWithProgress(src, temp)`; `File.Move(temp, dest, overwrite)`; при
  исключении и отмене — `TryDelete(temp)`, дальше как было. `File.Copy`-ветка
  без прогресса (тесты, мелочь) — без изменений. `MoveEntry` файла между
  томами — тем же путём.
- `BatchExecutor.ApplyEntry`, ветка отмены (`:400-419`): частичного файла
  больше нет — undo-шаг `CreateAction` для файла не создаётся (для папки
  остаётся). **Меняет поведение под тестом:** `BatchExecutorTests:1081-1109`
  (файл, отменённый посреди, откатывается) правится этим шагом.
- Сторож — уже готово (блок 2): тик по пути, которого нет в листинге,
  перечитывает папку, и во время операции тики и так ждут
  (`HasActiveOperations`). Остаётся шаг харнесса ниже.
- `MoveEntry` папки между томами при исключении (`:218-223`): удалить
  частичную копию, если её создали мы (закрывает TECHDEBT «`MoveEntry`
  cross-volume»).

Глазами: во время копирования большого файла в открытую папку назначения
файла не видно до конца, потом появляется готовым; отмена — ни файла, ни
`.wander-tmp`; вылет процесса (убить из диспетчера) — остаётся
`имя.wander-tmp`, виден в списке, удаляется руками (уборка — 0.6).

### 6. Компьютер не засыпает

- Core `Operations/IPowerGuard.cs`: `IDisposable KeepAwake(string reason)`.
- Platform `Power/PowerRequestGuard.cs`: `PowerCreateRequest`
  (`REASON_CONTEXT` с текстом) → `PowerSetRequest(PowerRequestSystemRequired)`
  → `PowerClearRequest` + `CloseHandle` в `Dispose`; без
  `PowerRequestDisplayRequired` — экран гаснет, система нет. Причина —
  `ITextSource` ключ `PowerRequestReason` («Wander: файловая операция»);
  видна в `powercfg /requests`.
- `OperationTracker`: `IPowerGuard?` через конструктор (сейчас `(TimeSpan?
  minInterval, Func<DateTime>? now)`, создаётся в `PlatformBootstrapper:107`);
  первая идущая операция берёт, последняя `Dispose` отпускает. Харнесс
  берёт настоящие сервисы платформы (подменяет только лог и диалоги) —
  машина не спит и во время его операций; счётчик — `FakePowerGuard` в
  тестах Core. Тест: `OperationTrackerTests` — счётчик 1 при двух
  операциях, 0 после обеих.

### 7. Съёмные диски (Y2)

- Core `FileSystem/IVolumeChanges.cs`: `event EventHandler<VolumeChange>?
  Changed`; `VolumeChange(string Root, bool Arrived)`.
- Platform `FileSystem/VolumeChangeHub.cs : IVolumeChanges`: `bool
  HandleMessage(int msg, nint wParam, nint lParam)` — `WM_DEVICECHANGE`
  (0x219), `DBT_DEVICEARRIVAL` (0x8000) / `DBT_DEVICEREMOVECOMPLETE`
  (0x8004), `DEV_BROADCAST_VOLUME` (`dbcv_devicetype == 2`), буквы из маски
  `dbcv_unitmask`; событие на каждую букву. Platform без WPF — хук ставит
  App: `MainWindow.OnSourceInitialized` (`:120-129`) → `HwndSource.AddHook`,
  вызов `hub.HandleMessage`.
- Панель «Компьютер» — модель блока 2, узлов дерева больше нет: событие
  `VolumesChanged` в `WorkspaceEvents` → `PanelRules` перечитывает верхний
  уровень панели дисков (`ReadBranch("")`; `Reread` его сейчас пропускает —
  `PanelRules:450-455`, а `FolderChanged("")` лишь пометит уровень
  непрочитанным) → `BranchRead` сливает строки по порядку букв и уводит
  курсор с ушедших (`OnBranchRead`, `PanelRules:161-183`), шевроны —
  эффектом `ProbeChevrons`. **Не** `Removed(paths)`: он снимает совпавшие
  строки в обеих панелях, включая закладки верхнего уровня
  (`PanelRules:235-240`, `:624-643`), а закладка на флешку должна сереть.
  Закладки — `PostBookmarks()` (`MainViewModel:1969`): пересчитает
  `PanelRow.IsMissing`. Тест `PanelRulesTests`: диск пришёл — строка на
  своём месте; ушёл — курсор на соседе, закладка осталась.
- Открытая папка на ушедшем томе —
  `NavigationFallback.AfterVolumeRemoved(current, roots, recent)` (новый
  рядом с `AfterDelete`, `AfterRestore`; тест: последний недавний путь на
  живом томе, иначе первый фиксированный корень); сторож текущей папки
  уходит без исключения (`WindowsDirectoryWatcher.Stop` уже ловит и пишет
  в лог). Событие приходит с UI-потока (хук) — маршалинг не нужен.
- Операция с пропавшего тома: временные ошибки (шаг 4) → три попытки →
  `Failed` → «Повторить» после возврата тома. Пауза и автопродолжение —
  0.6.
- «Извлечь» [стенд Фабле]: даёт ли `ShellContextMenu` для `E:\` глагол
  «Eject»; да — пункт уже приходит чужим, ничего не делать; нет — 0.6.
- Глазами: вставить / вынуть флешку при открытой папке на ней, при
  копировании с неё и на неё; закладка на флешку сереет и оживает.

### 8. `Ctrl+Z` ждёт занятых

`UndoService.Unwind` (`Undo/UndoService.cs:191-226`, сейчас зовёт
`step.Undo()` напрямую): каждый шаг через `BusyGate.Retry` / `RetryMany`
того же ворот, что у операций. `UndoAsync(tracker, ct, PathClaims?
claims)` (`:127-153`) получает только заявки, а `HeldPaths` создаётся
внутри `FileOperationService()` (`:66-69`) и в локаторе не зарегистрирован
— отдать ворота наружу (свойство сервиса) и передать в `UndoAsync`.
Занятость → ожидание с бюджетом, отказ называет держателя. **Меняет
поведение под тестом:** сейчас упавший шаг выбрасывается из стека
(doc-комментарий `UndoService:108-112`, `UndoServiceAsyncTests:131`,
`:147`) — становится «остаётся, следующий `Ctrl+Z` пробует снова»; тесты
правятся шагом. App: `ReleasePreview` перед `UndoAsync` (единственный
вызов — `UndoLast`, `MainViewModel:4440-4441`), как перед удалением. Тест
`UndoServiceTests`: `MoveInUseFor` на один раз — откат прошёл со второй
попытки, шаг не выброшен из стека при отказе.

### 9. Корзина: страховка

`ShellRecycleBin` (`:144-148`): оболочка ответила успехом без
идентификатора элемента корзины — сейчас `WARN` («Ctrl+Z will not find
it») и хэндл с путём файла в корзине; откат ищет по id, затем по этому
пути (`MoveOut:294-299`), иначе падает. Страховка — когда нет **ни id, ни
пути**: элемент `DeleteStatus.Ok` с флагом `Permanent = true` (новое поле
`DeleteResult`, `BatchExecutor:915`), undo-шаг не создаётся
(`BatchExecutor:686-689`), итог и журнал говорят «удалён безвозвратно
(корзина не приняла)». Id нет, путь есть — откат живой, строку `WARN`
поправить. Запасной поиск по времени (`IRecycleBin.cs:10-18`) описан, но
не сделан — не в этом шаге. Стенды границ (а), (б) — PLAN T.

### 10. Занятая папка: назвать файл и программу (AF)

Отказ переноса / переименования папки с «что-то внутри занято»: отчёт о
занятости папки собирает `BusyGate.Retry` (`:202-227`), держателя называет
`NameHolder` (`:304-318`); `RestartManagerLockInspector` уже раскрывает
папку в первые 500 файлов одной сессией (`:68-90`) — называет программы,
но не файл; `IFileBusyProbe.IsBusy` для папки всегда `false`. Добавить:
обход `IFileBusyProbe.IsBusy` по файлам папки до первого занятого (предел
5 с, отменяемый), Restart Manager — про него одного; текст «занят `имя` —
`программа`». Проверять — «Отладка: занять на 30 с» в «Действиях» (AI2).

### 11. Хвосты

- I2: спутник упал после переезда основного — откат уже переехавших
  членов группы в `ApplyGroup` (`:301-345`; как `RenameMany`, `:160-206`,
  `:253-263`), статус группы `Failed`.
- `DeleteForGood` (`:598-629`, сейчас только `gate.Check`) ждёт занятый
  через `BusyGate` (TECHDEBT).
- Проверка длинного пути для корзины — один раз на операцию:
  `TooLongForBin` (`ShellRecycleBin:222-244`) идёт на каждый путь в каждом
  `SendMany` (`:95`), а `RetryMany` шлёт всё заново (`BatchExecutor:666-678`)
  (TECHDEBT).
- BACKLOG «Reparse-points»: `CopyDirectory` (`:188-210`) пропускает
  junction-подпапки молча (`:199-205`) — в итог элемент `Skipped` с
  причиной «ссылка не копируется». Символические ссылки на файлы внутри
  копируются содержимым цели, источник-junction верхнего уровня —
  насквозь: так и оставить, записать в FILEOPS.

## Харнесс и стенды

- `ScenarioRunner`: шаг `assert-operations` (`{"step":
  "assert-operations", "finished": 1, "failed": 1, "outcome":
  "Problems"}`) с ожиданием конца идущих (`wait-idle` операций не ждёт),
  команды `retry-last-operation`, `dismiss-operations`; `fs` получает
  `rename` — переименование снаружи.
- `file-ops.json` (сейчас: копия и вставка, три ответа на конфликт,
  перенос, цепочка откатов, занятый файл, длинный путь, read-only; упавшей
  копии и записи операции нет): `fs lock` источника → `copy` + `paste` →
  `assert-operations failed 1` → `fs unlock` → `retry-last-operation` →
  `assert-operations failed 0` → `assert-entries`. Шаг с `.wander-tmp`: `fs
  create x.wander-tmp` → `fs rename` в `x` → `assert-entries contains x`
  (сторож перечитывает путь, которого не было в листинге; логика уже есть —
  шаг проверяет её вместе с фильтром временных).
- Отладочная операция (AI1) — глаза на строку состояния без диска:
  сценарии «ошибка на 4-м» и «отмена посреди» дают запись с проблемами
  (после правки `DebugOperation` в шаге 1).
- Стенд Фабле: `powercfg /requests` при идущей операции; `Eject` в меню
  тома.
- Живой прогон человека: копия 2–4 ГБ с карты R8 на SSD и обратно; вынуть
  карту посреди, вернуть, «Повторить»; строка итога в логе и журнале.

## Критерии ревью

- Core без Windows: коды ошибок — числа с именами в комментарии, power и
  устройства — за интерфейсами; Platform без WPF (хук в App).
- Ни одна ветка не теряет исключение: `Failed` всегда с `Error`.
- Повтор не удваивает undo: старая запись убирается, новая операция — свой
  шаг отката.
- Запись операции не держит `FileSystemEntry` и битмапы — только строки и
  числа (50 записей × 200 элементов).
- Завершённые строки не держат тик сторожа, полосу хода и вопрос при
  выходе: `HasActiveOperations` — только идущие.
- Список назначения не мигает: `.wander-tmp` не появляется в листинге при
  копировании, готовый файл появляется один раз.
- Тесты, правленные по существу (`BatchExecutorTests:1081-1109`,
  `UndoServiceAsyncTests:131`, `:147`), — только те, что названы в шагах 5
  и 8.
