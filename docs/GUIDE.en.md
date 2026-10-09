# Wander user guide

🌐 [Русский](GUIDE.md) · **English**

What Wander can do and how to use it.
You can download the app from the link in the [README](../README.md); this guide is also on the website, one page per topic: [lekta.github.io/wander](https://lekta.github.io/wander/en/guide/).

## Getting started

Wander is a file manager for Windows with a focus on ease of use and media.
It replaces File Explorer: easier to use, more stable, and with more features.
Everything familiar is still there: keyboard shortcuts, the clipboard, drag and drop, the context menu, undo with `Ctrl+Z`.
On top of that:

- bookmarks, a folder tree that remembers expanded branches, and state saved between launches;
- preview and thumbnails for many formats, from RAW to code;
- convenient batch processing: batch rename, conversion, custom actions;
- photo culling: ratings, side-by-side comparison, tools to check sharpness and exposure;
- companion files such as `.xmp` and `.meta`, shown as one item with the main file.

![Main window: Bookmarks and Computer on the left, a folder in Icons view in the middle, the Preview pane with a video on the right](screenshots/main.webp)

**The window** has these main parts:

- **the Folders pane** on the left: Bookmarks and Computer;
- **the file area** in the center: the files of the open folder, search, filters;
- **the Preview pane** on the right: the selected file, two files side by side, a folder summary;
- **the address bar and menus** at the top: navigation, file operations, settings;
- **the status bar** at the bottom: the selection, operations in progress, messages and the action journal.

**Where to start.**

- [**Navigating folders**](#navigation): with the tree and Bookmarks on the left, or with the address bar.
  To bookmark a folder, drag it to the “+” zone below the bookmarks.
  `Ctrl+1` moves the keyboard to the Folders pane.
  Bookmarks and expanded folders are kept between launches.
  Archives open as read-only folders.
- [**Display mode**](#views-and-sorting) for folder contents: Details, Tiles, Icons or Gallery in the “View” menu.
  Each folder remembers its view and sort order, and a folder of images and photos opens in Gallery.
- [**Search**](#search): press `Ctrl+F` and start typing a name.
  To search for text inside files, type it after a colon (`name:text` or just `:text`); this works in Office documents and EPUB too.
  `Esc` clears the search.
  For complex queries, there's a separate window.
- [**Viewing**](#viewing): the Preview pane on the right is open from the first launch; `Ctrl+Q` hides and shows it.
  For the selected file, it shows an image, RAW, video, music, PDF, highlighted code, or a summary of an archive or folder.
  Select two files to see them side by side for comparison.
- [**Basic operations**](#working-with-files): copying, moving, deleting and dragging to other programs work the way you're used to.
  Anything reversible is undone with `Ctrl+Z`, and anything irreversible asks first.
  Name conflicts are resolved in a single window that compares the files.
- [**Culling photos**](#culling-photos): fast RAW viewing and comparison is what sets Wander apart.
  Ratings right in Gallery, side-by-side comparison of two photos, including in full screen, and RAW files that open instantly.
  Review helpers mark focus and sharpness, show the histogram and clipped highlights, lift shadows and tone down highlights.
- [**Advanced operations**](#actions-and-menus): the “Actions” menu has batch rename, conversion of videos, documents and images, and custom actions.
- **Also**: the action journal in the status bar answers “where did I move that?”, and [settings](#settings) change behavior, appearance and performance.

**Keyboard shortcuts**, the essentials:

| Keys                        | What they do                                         |
|-----------------------------|------------------------------------------------------|
| `Tab`, `Ctrl+1` / `2` / `3` | Go to the Folders pane, the files, the Preview pane  |
| `Ctrl+Q`                    | Show or hide the Preview pane                        |
| `Ctrl+F`                    | Search                                               |
| `Alt+D`                     | Address bar                                          |
| `Esc`                       | Cancel input, return the keyboard to the files       |
| `Alt+arrows`                | Move through folders and browsing history            |
| `F2`                        | Rename (one item / batch)                            |
| `Delete`                    | Move to the Recycle Bin                              |
| `Ctrl+Z`                    | Undo                                                 |

All keyboard shortcuts are on the [“Keyboard shortcuts”](#keyboard-shortcuts) page.

**Settings** open from `⋯` → “Settings”; there, `F1` opens this guide at the section about the open page.
Frequently used toggles are also in the right-click menu on empty space in the Folders pane.

## Navigation

You move between folders with the Folders pane on the left, the address bar and the buttons next to it.
Bookmarks, expanded folders, and the current folder and file are kept between launches.

- [“Folder panes”](#folder-panes): Bookmarks and Computer, working with bookmarks, keyboard use.
- [“Archives as folders”](#archives-as-folders): browsing `.zip`, `.7z`, `.rar` without extracting.

**Back, forward, up.** The buttons to the left of the address bar or `Alt+←` and `Alt+→` move through your browsing history, and `Alt+↑` (or `Backspace`) goes up one level.
After going up, the folder you came from is selected; when you return to a folder, what was selected before is selected again.

**The address bar** shows the path in parts; click a part to go to that level.
To enter a path, click empty space in the bar or press `Ctrl+L` (`Alt+D`), type or paste the path and press `Enter`; `Esc` cancels.
The triangle on the right or `F4` opens the list of recent folders.

**At startup**, Wander opens in the last folder, on the same file, with the same scroll position and expanded branches.
If the file is gone, a neighboring one is selected.
If the folder itself is gone, the nearest parent folder opens; if the folder was on a removable drive that isn't connected now, the working folder opens (“Documents” out of the box).
You set the working folder in [“Settings” → “Folders and bookmarks”](#folders-and-bookmarks).

### Folder panes

The Folders pane on the left holds two folder trees: “Bookmarks” at the top and “Computer” with your drives below.
Expanded branches stay expanded as you move around and after a restart.

Both trees hide and come back together: use the ![Folders pane](icons/folders-pane.svg) button to the left of “←”, “View” → “Folders pane”, or `Ctrl+B`; the width is remembered.

<img src="screenshots/folders.webp" align="right" width="240" alt="Folders pane: standard and custom bookmarks, the “+” zone, the drive tree with expanded branches, the open folder in bold">

#### Folder trees

- Click a row to open the folder; click the triangle ![](icons/tree-arrow.svg) to expand or collapse it.
- `Alt`+click on the triangle expands the folder together with its first-level subfolders; on an expanded folder, it collapses the whole subtree.
- The open folder is shown in bold in the tree you opened it from.
- Archives appear in the tree among folders and expand the same way.

#### Bookmarks

- **Add**: drag a folder to the “+” zone below the bookmarks.
- **Reorder or remove**: use the `⋯` button on the bookmark's row or right-click it; `Ctrl+↑` and `Ctrl+↓` move a bookmark up and down.
- **Standard bookmarks** are at the top: Downloads, Documents, Pictures, Recycle Bin.
  Desktop, Music and Videos can be turned on in [“Settings” → “Folders and bookmarks”](#folders-and-bookmarks).

A bookmark to a folder that no longer exists turns gray and italic; clicking it offers to point to the new location or remove the bookmark.

`Delete` on a bookmark you added asks what to remove: the bookmark or the folder itself (to the Recycle Bin).
On a standard bookmark, `Delete` leaves the folder alone and only removes the bookmark from the pane; you can bring it back in the same settings.

#### From the keyboard

- `Ctrl+1` moves the keyboard to the current folder tree, at the open folder; press it again to switch to the other tree (Bookmarks ↔ Computer).
- The arrow keys, `PgUp` / `PgDn` and `Home` / `End` move the cursor.
- `←` and `→` collapse and expand a branch; typing letters jumps to a folder whose name starts with them.
- A folder opens as soon as the cursor lands on it; holding down an arrow key opens only the folder where you stop.
  The “Open folders as you move to them with the keyboard” checkbox is in [“Settings” → “Folders and bookmarks”](#folders-and-bookmarks) and in the right-click menu on empty space in the pane; with it cleared, a folder opens on `Enter`.

#### Folder in the pane

- Right-click a folder in the tree or in Bookmarks to open that folder's menu: “Paste” puts files into it, “Rename” renames it.
- `F2` renames the folder right in the pane; the open folder, history and bookmark follow the new name, and `Ctrl+Z` restores the old one.
- While the keyboard is in the pane, `Delete`, `Ctrl+C`, `Ctrl+V`, `F2` and `Alt+Enter` act on the folder under the cursor.
- Right-clicking empty space in the pane opens quick settings: hidden and system files, opening folders from the keyboard, asking before deleting, changes from other programs, and “Settings”.

### Archives as folders

Archives in `.zip`, `.7z`, `.rar` and other formats that Windows can open work as folders: a path in the address bar, `Backspace` to go up, sorting, search by name, photo thumbnails.
This works even if your archives are associated with 7-Zip or WinRAR.
In the tree, an archive appears among folders and expands the same way.

**Extracting files** works the same as copying them from a regular folder:

- copy (`Ctrl+C`) and paste (`Ctrl+V`) into a folder, or drag, including onto a folder in the Folders pane; `Ctrl+Z` undoes the extraction;
- **“Extract here”** in the menu of a file or of the archive itself extracts without asking into the folder that contains the archive; matching names aren't replaced, and the new item gets “(1)”;
- “Extract…” asks for a folder first.

You can also copy or drag files from an archive to other programs as usual.

**Viewing.** The Preview pane shows the archive's contents, and previews a file inside it like any other file (up to 32 MB).
“Open” extracts the file to a temporary copy and opens it in its program; **changes to that copy don't go back into the archive**.

**Limitations.** Archives are read-only: you can't delete, rename, move or create anything inside.
Search by text doesn't look inside archives.
In a password-protected archive, you see the file names but not the contents, and a fully encrypted archive looks empty.

## File area

The middle of the window shows the files of the open folder.
The area has a toolbar with a search box; in a folder with rated photos, it also has a rating filter, and in Gallery, a background picker.

- [“Views and sorting”](#views-and-sorting): Details, Tiles, Icons, Gallery; each folder remembers its view and sort order.
- [“Search”](#search): by name and by text inside files.
- [“Thumbnails”](#thumbnails): file contents instead of icons.
- [“Gallery”](#gallery): a view for photos, with ratings and review helpers.

**Selection** works as usual: click, `Ctrl`+click, `Shift`+click, dragging a rectangle across empty space, `Ctrl+A`; typing the first letters of a name jumps to the file.
The right side of the status bar shows how many items are selected and how much space they take; folder sizes are calculated by the Preview pane.

The selected file doesn't leave the screen: when new files appear or the filter, sort order or window size changes, it stays in view.

**What to show** is set in [“Settings” → “File list”](#file-list):

- **hidden files** aren't shown by default, and when turned on, they're drawn paler; together with them, you can show protected operating system files such as `desktop.ini`;
- **system folders and files at drive roots** (`$RECYCLE.BIN`, `System Volume Information`, `pagefile.sys`) have their own checkbox: they stay hidden even when hidden and system files are shown.
  There's no reason to turn it on: you can't open these folders anyway, and they only clutter the list;
- **a file and its companions** can be shown as one item; learn more on the [“Companion files”](#companion-files) page;
- **changes from other programs** show up right away or only on `F5`.

Hidden and system files can also be turned on from the right-click menu on empty space in the Folders pane.

### Views and sorting

Switch views in the “View” menu or with the keyboard:

| View | Keys | What it shows |
|---|---|---|
| Details | `Ctrl+Shift+6` | Name, date, size and type in columns |
| Tiles | `Ctrl+Shift+7` | A thumbnail with details next to it |
| Icons | `Ctrl+Shift+2` | A grid of thumbnails |
| Gallery | `Ctrl+Shift+1` | Large photos for culling, see [“Gallery”](#gallery) |

“View” and “Sort” are also in the right-click menu on empty space in a folder.

![Folder contents in Details view](screenshots/table_view.webp)

**Each folder remembers its view.** Choose Tiles for a folder, and it keeps opening in Tiles.
Other folders use the default view (Icons out of the box), and folders of photos use Gallery.
The first line of the “View” menu tells you why the folder looks the way it does: “pinned”, “auto: photos” or “default”.

- “Automatic” unpins the view from the open folder.
- “Set as default view” applies the current view to all folders without a pinned view; you can also choose the default view in [“Settings” → “View”](#view).

**Sorting**: by name, date, size, type or rating, plus “Ascending” and “Folders first”.
In Details, just click a column header; click it again to reverse the order.
The sort order is pinned to a folder just like the view, and “Default” unpins it; out of the box, files are sorted by name, with folders first.
A pinned view and sort order move with the folder when it's renamed or moved.

#### Sizes

`Ctrl`+mouse wheel makes items larger or smaller; each view has its own size, and it's saved.
`Ctrl`+wheel click restores the standard size.
Exact values are set in [“Settings” → “View” → “Sizes”](#view--sizes):

- Details: row height and icon size;
- Tiles: width, icon size and font size;
- Icons and Gallery: cell width, image size, spacing and font.

### Search

**Quick search.** Press `Ctrl+F` and type a name: the files of the open folder are filtered, and matches from subfolders appear below as a separate group.
In Details, a “Folder” column is added.
`Esc` clears the search and returns the keyboard to the files.

![Search results: the path, the matching line and a preview of the found file](screenshots/find_results.webp)

**Masks.** Without `*` and `?`, Wander looks for part of a name; with them, the whole name must match the pattern.
Separate multiple masks with semicolons.

| Typed | Finds |
|---|---|
| `doc` | everything with “doc” in the name |
| `*.cs` | only `.cs` |
| `*.t*` | extensions starting with “t” |
| `IMG_?.jpg` | `IMG_1.jpg`, but not `IMG_12.jpg` |
| `*.cs;*.xaml` | both |

**Text inside files** goes after a colon: `*.cs:budget` finds `.cs` files with the word “budget”, and `:budget` finds any file with it.
Everything after the first colon is searched as is, spaces and colons included (`:http://example.com`).
A “Match” column appears in the results.
The Preview pane opens a found file right at the match; `F3` goes to the next match, and after the last one, to the next found file.

**The Search window** (`Ctrl+Shift+F` or `⋮` in the search box) splits the same query into fields:

- name or mask;
- text inside files; name and text are combined with **and**, so `*.cs` plus a word gives you four files, not four hundred;
- the folder to search in;
- “Search subfolders”: cleared by default in the window, so the window searches only the open folder; the search box above the files always searches subfolders;
- “Also search binary files”.

The search box above the files and the window are the same search: what you type in one shows up in the other.

**Supported formats.** Text in any encoding (UTF-8, UTF-16, Windows-1251, DOS-866), `.docx`, `.xlsx`, `.pptx`, `.epub` and OpenDocument; `.doc` and `.rtf` are read with Windows' own tools, and `.pdf` if a PDF reader is installed.
Images, archives and programs are skipped; with “Also search binary files” checked, they're searched for Latin letters and digits.

**Results** are sorted like regular files.
“Stop search” stops it and keeps what was found, `F5` repeats the search, and going to another folder clears it.
There's no index: files are read on every search, and nothing is left on disk.

### Thumbnails

In Tiles, Icons and Gallery, file contents are shown instead of icons; Details keeps regular icons.

| Files                   | Thumbnail                                                |
|-------------------------|----------------------------------------------------------|
| Images, HEIC, RAW       | The image itself; RAW from the embedded preview          |
| Video                   | A frame                                                  |
| FB2 and EPUB books, PDF | Cover or first page                                      |
| Music                   | Cover art from the file or an image next to it (`Cover.jpg`, `folder.jpg`) |
| Folder                  | An overview of its contents                              |
| Shortcut                | The thumbnail of what it points to                       |

All formats are listed on the [“Formats”](#formats) page.

![Thumbnail examples](screenshots/thumbnails.webp)

- **Speed**: thumbnails are cached, so a folder draws faster when you come back to it.
  Cache size and memory for images are set in [“Settings” → “Cache and memory”](#cache-and-memory).
- **Freshness**: thumbnails are regenerated when a file changes.

### Gallery

A view for images and photos: large thumbnails, ratings, an adjustable background.
[Review helpers](#review-helpers) are turned on with the buttons at the bottom of the Preview pane and are drawn right on the thumbnails: focus peaking ![](icons/helper-focus.svg) shows where the frame is sharp, the sharpness score ![](icons/helper-sharpness.svg) shows how sharp, and the clipping overlay ![](icons/helper-clipping.svg) shows clipped highlights.
Lifting shadows and toning down highlights work here too.
You can spot a miss before you even open the photo.

- `Enter` opens the photo in [full screen](#full-screen).
- The digits `1`–`5` set a [rating](#ratings-and-labels), `0` clears it, and `Shift`+digit sets a color label.
- The filter in the toolbar shows only photos with the stars and labels you want.

How to use all this for culling is covered on the [“Culling photos”](#culling-photos) page.

![Photo gallery, Preview pane and review helpers](screenshots/preview.webp)

**Gallery turns on automatically** in a folder where more than 80% of the files are images (RAW files count; subfolders, companion files and backups don't), and in a folder that Windows marks as a pictures folder.
When you leave such a folder, the usual view returns.
The checkbox and threshold are in [“Settings” → “View”](#view).
A view you choose manually is pinned to the folder like any other.
An archive of photos opens in Gallery too.

**The background** is switched with three squares in the toolbar: light, gray or dark.
Captions, overlays and the Preview pane adapt to it.
The brightness of the gray and dark backgrounds is set in [“Settings” → “View” → “Gallery”](#view--gallery).

## Viewing

For many formats, you see a file's contents as soon as you select it: the Preview pane on the right shows images and RAW, video and music with a player, PDFs and documents, code with syntax highlighting, 3D models, archive contents, and program details.
For a folder, the pane counts its files, their total size, and which types take up the space; for a drive, it shows how full the drive is.

- [“Preview pane”](#preview-pane): loupe, two photos side by side, search in text, shooting data.
- [“Full screen”](#full-screen): photos across the whole screen, one at a time or in pairs.
- [“Formats”](#formats): what Wander can do with each file type.

### Preview pane

`Ctrl+Q` or “View” → “Preview pane” shows and hides the pane; drag its edge to change its width.
At the bottom of the pane is a summary: name, size, date, and shooting data, RAW files included.
With several files selected, the summary shows how many there are and what the photos have in common: camera, ISO, aperture, shutter speed.
With nothing selected, you see a summary of the open folder.

**Images.** Hold down the left mouse button to turn on the loupe.
A large image shrinks to fit the pane; a small one isn't stretched.
A RAW file opens instantly from its embedded preview, and the **RAW** button at the bottom turns on full development of the sensor data: the frame switches once development is done, and the mode stays on for the whole folder.

Buttons at the bottom of the pane turn on [review helpers](#review-helpers), including the histogram ![](icons/helper-histogram.svg) and the focus point ![](icons/helper-af.svg).

<img src="screenshots/preview_compare.webp" align="right" width="360" alt="Two photos side by side under the loupe, with the sharpness of each">

**Two photos side by side.** Select two files and the pane splits in half: the photos go one above the other or side by side, whichever shows them larger.
Each half has its own stars, label, sharpness, and histogram.

- The loupe (left button held down) on one half shows the same spot on the other.
- If you also hold down the right button while using the loupe, only the photo under the pointer moves; this is how you line up frames that are shifted.
- With three or more selected, the pane shows the file added to the selection last, and the stars at the bottom apply to all of them.

**Text and code.** `Ctrl+3` moves the keyboard to the pane:

- select text and copy it with `Ctrl+C`;
- `Ctrl+F` searches the text, a counter shows “3 of 17”, and `Enter` and `Shift+Enter` step through the matches;
- `Esc` returns the keyboard to the files.

`F3` and `Shift+F3` step through the matches without leaving the files.
For a long file, the pane shows the first megabyte, but matches beyond it are counted too: “N more later in the file”.

**Video and music** play in the built-in player with pause, seeking, repeat, and volume; for music, you also see the cover art and tags.
The volume is shared by all files and is remembered between launches.
`Ctrl+3` moves the keyboard to the play button.

**File in use.** If another program holds the file and it can't be read, the pane says so and names that program.

What the pane shows for each type is listed on the [“Formats”](#formats) page.

### Full screen

In Gallery, `Enter` or `Space` opens photos in full screen: the photo fills the screen, the arrow keys browse the folder, and `Esc` or `Enter` takes you back to Gallery at the last photo shown.
The mouse wheel and the mouse's “back” and “forward” buttons browse too; holding down an arrow key moves through the folder, showing every photo.

- With two photos selected, they appear side by side.
- With more selected, they're shown one at a time, and the arrow keys browse only the selected ones.
- If the selection includes anything other than photos, `Enter` opens the files as usual.

![Two photos in full screen, each with a bar showing its name, sharpness, and stars](screenshots/fullscreen_compare.webp)

**Comparing photos.** `Shift`+arrow splits the screen: the current photo stays on the left, the adjacent one appears on the right, and further `Shift`+arrow presses, like `Shift`+wheel, browse the right one.
`←` or `→` without `Shift` keeps the left or the right photo on screen.

**Photo bar.** A bar is always visible at the bottom of the photo (of each photo in a pair), under the loupe too: the file name and sharpness score on top, and below them the stars, color labels, and [helper](#review-helpers) buttons.
The histogram, when turned on, sits in the corner of the photo.

- Digits `1`–`5` set the rating and `0` clears it; in a pair, for the photo under the mouse.
- `Delete` moves the photo to the Recycle Bin and shows the next one.
- `Ctrl+Z` undoes a rating or a deletion.
- `Z` turns on the 1:1 loupe without a mouse button, and you move around the frame with the mouse; press `Z` again to turn it off.
  While the pointer is hidden, the loupe goes to the sharpest spot of the photo or to the camera's focus point; you choose which in [“Settings” → “View” → “Gallery”](#view--gallery).
- Hold `Alt` to see the frame without helper overlays.

![Full screen with the 1:1 loupe and the photo bar](screenshots/fullscreen_zoom.webp)

### Formats

This page lists which formats are supported and to what extent.
An empty cell means “not supported”.
For files not in the table, you see the system icon instead of a thumbnail, the Preview pane shows the name, size, and date, and content search reads them if they look like text.

| Files | Thumbnail | Preview pane | Full screen, ratings, helpers | Content search | Conversion |
|---|---|---|---|---|---|
| **Images** `.jpg` `.jpeg` `.jpe` `.jfif` `.png` `.gif` `.bmp` `.ico` `.tif` `.tiff` `.webp` `.tga` `.jxr` `.wdp` | image | image, loupe; GIF and WebP: animated, with a player | yes | | to JPEG, to PNG, shrink to 1920: Wander itself; to WebP: ffmpeg |
| **RAW** `.cr2` `.cr3` `.crw` `.nef` `.nrw` `.arw` `.srf` `.sr2` `.raf` `.orf` `.ori` `.rw2` `.rwl` `.pef` `.3fr` `.fff` `.iiq` `.dng` | embedded preview | preview instantly; **RAW** button: decoding of the sensor data | yes | | JPEG preview from the file: Wander itself; after that, as images |
| **HEIC / HEIF** `.heic` `.heif` `.hif` | image[^heic] | image[^heic] | yes[^heic] | | as images |
| **AVIF, JPEG XL** `.avif` `.jxl` | image[^avif] | image[^avif] | yes[^avif] | | as images |
| **SVG** `.svg` | icon | image; `</>`: markup | | as code | |
| **Video** `.mp4` `.m4v` `.mov` `.wmv` `.avi` `.mkv` `.webm` `.ogv` `.mpg` `.mpeg` `.asf` `.3gp` `.3g2` | frame | player; WebM and OGV don't play yet, so convert them to MP4 | | | to MP4 (H.264), compress, audio to M4A or MP3, frame to PNG: ffmpeg |
| **Music** `.mp3` `.flac` `.m4a` `.m4b` `.aac` `.wav` `.wma` `.ogg` `.opus` `.aif` `.aiff` `.aifc` `.au` `.mka` `.mp2` `.mpa` | cover art | cover art, tags, player; OGG and Opus don't play yet | | | to MP3, M4A, FLAC, WAV: ffmpeg |
| **3D** `.stl` `.obj` `.gltf` `.glb` | icon | model: rotate, zoom | | | |
| **Text** `.txt` `.log` `.csv` `.tsv` `.ini` `.cfg` `.conf` `.toml` `.env` `.mtl` `.gitignore` `.gitattributes` `.editorconfig` | icon | text, encoding detected automatically | | yes | |
| **Code**: languages, markup, configs, shaders and Unity assets[^code] | icon | syntax highlighting | | yes | |
| **Markdown** `.md` `.markdown` | icon | rendered document | | yes | to DOCX: pandoc |
| **PDF** `.pdf` | first page | document | | via Windows, if a PDF reader is installed | |
| **HTML, MHT** `.html` `.htm` `.mht` `.mhtml` | icon | page, without network access | | as text | |
| **Office documents** `.doc` `.dot` `.docx` `.xlsx` `.pptx` `.odt` `.ods` `.odp` | icon | text only | | yes; `.doc` and `.dot` via Windows | to PDF, except `.dot`: LibreOffice; DOCX to Markdown: pandoc |
| **Older Excel and PowerPoint** `.xls` `.ppt` | icon | | | | to PDF: LibreOffice |
| **RTF** `.rtf` | icon | with fonts, tables, images | | via Windows | to PDF: LibreOffice |
| **Books** `.fb2` `.epub` | cover | FB2: cover, description, text; EPUB: text only | | EPUB: yes; FB2: as text | |
| **Archives** `.zip` `.7z` `.rar` `.tar` `.gz` `.tgz` `.bz2` `.xz` and anything Windows opens as a folder, such as CAB | icon; images inside get thumbnails | contents; a file inside works like any other | | doesn't look inside | extract |
| **Programs** `.exe` `.dll` `.sys` `.msi` `.ocx` `.scr` `.cpl` `.drv` | its own icon | info card: version, publisher, signature | | | |
| **Shortcuts** `.lnk` | target's thumbnail with an arrow | target's contents | | | |
| **Companions** `.xmp` `.pp3` `.meta` | one item with the main file | rating and label; GUID | | | |
| **Folder** | preview of contents | breakdown: files, folders, size, top types | | | |
| **Drive** | icon | volume label, file system, usage, breakdown | | | |

[^heic]: Requires Windows extensions from the Microsoft Store; which ones are listed in the [“Preview by file type”](#preview-by-file-type) table, in the “HEIC / HEIF” row.

[^avif]: Requires Windows extensions from the Microsoft Store: HEIF and AV1 for AVIF, JPEG XL for JPEG XL. Without them, you see an icon instead of a thumbnail, and the pane says there's no preview.

[^code]: `.cs` `.csproj` `.props` `.targets` `.sln` `.slnx` `.js` `.ts` `.jsx` `.tsx` `.mjs` `.cjs` `.py` `.rb` `.go` `.rs` `.java` `.kt` `.swift` `.php` `.c` `.cpp` `.cc` `.cxx` `.h` `.hpp` `.m` `.mm` `.css` `.scss` `.less` `.sh` `.bash` `.zsh` `.ps1` `.bat` `.cmd` `.sql` `.xml` `.xaml` `.json` `.yaml` `.yml` `.diff` `.patch` `.shader` `.cginc` `.hlsl` `.compute`; Unity assets `.asset` `.prefab` `.unity` `.mat` as text, if the project serializes them to YAML.

The programs that conversion needs are covered on the [“Conversion”](#conversion) page, and ratings and helpers in the [“Culling photos”](#culling-photos) section.

#### Preview by file type

| Type | What it shows, what you can do |
|---|---|
| Images, including TGA, which Windows has no codec for | Loupe on the left button; large images shrink to fit, small ones aren't stretched |
| HEIC / HEIF | The image, correctly rotated. If Windows is missing an extension from the Microsoft Store (“HEIF Image Extensions”, free; “HEVC Video Extensions”, paid, unless your PC's manufacturer installed it), the pane says which one, and a button opens its page in the Store |
| SVG | The image; the `</>` button at the bottom switches to the markup and back. A file over 1 MB is shown as markup right away |
| RAW (CR3, CR2, NEF, ARW, DNG…) | The embedded preview, instantly, rotated per EXIF. The **RAW** button turns on full development of the sensor data: the preview shows right away, and the developed frame replaces it when ready; the mode stays on for the whole folder |
| GIF, video | A player: play and pause, repeat (on from the start for clips under three seconds), seeking |
| Music | Cover art, title, artist, album, year, bitrate, and a player. Tags are read from MP3, FLAC, M4A and M4B, and WAV; for other formats, the name and duration. The cover art comes from the file, or else from an image next to it (`Cover.jpg`, `folder.jpg`, `front.*`, one with the same name, or the only image in the folder). Cyrillic tags in Windows-1251 are read correctly |
| 3D (STL, OBJ, glTF, GLB) | The model: drag to rotate, wheel to zoom; triangle and vertex counts at the bottom. Colors come from `.mtl` and glTF; textures aren't loaded. FBX, DAE, and DXF aren't supported |
| Text | The encoding is detected automatically, including 1251 and 866. A large file is shown from the start, with “… showing the start of the file, 12 MB in total” at the bottom |
| Code | Syntax highlighting: common languages, bat and cmd, sh, Unity shaders, YAML and Unity assets, `.diff` and `.patch` |
| PDF, HTML, MHT, Markdown | The rendered document; in Markdown, tables, `- [x]`, `~~strikethrough~~`, links, and footnotes. The pane doesn't go online: external images and scripts aren't loaded |
| FB2, RTF | Cover, title, authors, description, and text (a long book from the beginning); RTF with fonts, tables, and images |
| Word, Excel, PowerPoint, OpenDocument, EPUB | The document's text, marked “Text only; formatting isn't shown” |
| Programs and libraries | An info card: version, product, publisher, platform (x64, console, .NET…), signature (whose, and whether it's valid), copyright. The signature is checked a moment after the rest; for a file over 200 MB, only when you click “Check” |
| Shortcut `.lnk` | The target's contents; at the bottom, the target's name and “Go to target”. A broken shortcut is reported as such |
| Drive | Volume label, file system, a usage bar (yellow from 85%, red from 95%), and a breakdown |
| Folder | A breakdown: files, folders, size, the largest types with bars. The numbers grow as the scan goes on; leaving the folder stops it |

## Working with files

Copying, moving, renaming and deleting work as you'd expect, with the same keys as everywhere else in Windows.
The clipboard is shared with the system, and drag and drop works between Wander and any program in both directions.

| Keys | What they do |
|---|---|
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | Copy / cut / paste |
| `F2` | Rename; with several files, [as a batch](#batch-rename) |
| `Delete` / `Shift+Delete` | Move to the Recycle Bin / delete permanently |
| `Ctrl+Shift+N` | New folder |
| `Ctrl+Z` | Undo |

Wander takes care of your files:

- **almost everything can be undone**: copying, moving, renaming, deleting to the Recycle Bin, a new folder, a shortcut, a rating; what can't be undone is deleting permanently and a [custom action](#custom-actions) without an output file;
- **name conflicts are resolved in one window** before anything changes, with thumbnails and comparison; see [“Resolving conflicts”](#resolving-conflicts);
- **long operations run in the background**, each in its own window with progress and a cancel button, without blocking your work; see [“Long operations”](#long-operations);
- **companions follow their file** (`.xmp`, `.meta`): they're moved, copied and deleted along with it; see [“Companion files”](#companion-files).

The clipboard and drag and drop are covered on the [“Copying and moving”](#copying-and-moving) page, the Recycle Bin and permanent deletion on the [“Deleting and Recycle Bin”](#deleting-and-recycle-bin) page.

**New folder**: `Ctrl+Shift+N`, or “New” → “Folder” in the right-click menu on empty space.
The folder appears right away, with a box for its name.

**Renaming**: `F2` opens the name for editing in place, `Enter` applies, `Esc` cancels.
With several files, `F2` opens [batch rename](#batch-rename).

**Undo.** `Ctrl+Z` undoes the last operation, and the next press undoes the one before it:

- what was moved goes back where it was, and what was renamed gets its old name back;
- what was deleted comes back from the Recycle Bin, and what was created goes to the Recycle Bin.

A long undo shows its progress, and you can stop it.
A file that couldn't be restored doesn't hold up the rest; the status bar names it.
The undo history lasts until you close Wander (deleted items stay in the Recycle Bin) and doesn't include other programs' operations: if you cut a file here and paste it in another program, that program does the move.
After a permanent deletion, only what involved the deleted items leaves the history: their move or rename can no longer be undone, and `Ctrl+Z` undoes everything else as before.
While a long operation is running, `Ctrl+Z` does nothing.

**Confirmations.** Deleting and moving ask for confirmation, and “Cancel” is selected by default.
You can turn off the questions before deleting to the Recycle Bin and before moving in [“Settings” → “File operations”](#file-operations-1): `Ctrl+Z` brings everything back anyway.
Permanent deletion always asks.

**System folder protection.** You can't delete, move or rename `C:\Windows` with all its contents, the drive roots themselves, `Program Files`, `ProgramData`, the Users folder, or the profile folders that Windows manages (Desktop, Documents, Downloads and others).
What's inside them is yours; only `C:\Windows` is closed entirely: Wander doesn't create anything inside it either (no folder, no extracted archive, no action result), and the status bar tells you it refused.

**File in use.** Wander names the program that holds the file: “the file is open in Word (PID 1234)”.
If that kept the file from being deleted, a “File in use” window appears with a “Retry” button: close the file in that program and retry.
Wander releases videos and PDFs open in the Preview pane by itself.
Files that an operation is working on have a clock on their icon.

### Copying and moving

**The clipboard** is the system one: what you copy in Wander pastes in other programs, and the other way around.
If you paste a file into the same folder it was copied from, a copy named “name (1)” appears next to it.

**Text or an image from the clipboard** is pasted as a file: `Ctrl+V` creates `Text.txt` or `Image.png` in the folder (for example, from Excel cells or a Snipping Tool screenshot) and offers to rename it right away.
Email attachments and files from a phone come from the system as content rather than as files; Wander can't paste those yet and tells you so.

**Dragging with the left mouse button** moves files within one drive and copies them between drives.
Keys change the action:

- with `Shift`, move;
- with `Ctrl`, copy;
- with `Alt`, create a shortcut.

If you hold files over a collapsed folder in the Folders pane, it expands.
A folder among the files doesn't open this way; to make it open, turn on “Open a folder when a dragged file hovers over it” in [“Settings” → “File operations”](#file-operations-1).
Near the window edge, the files scroll by themselves, faster the farther past the edge you go.
You can drag folders from the tree too.

**Dragging with the right mouse button** opens a menu after the drop: copy, move, create shortcuts, plus “Convert” and “My actions”, whose results land right in that folder.
The item in bold is what a normal drop would do.

<img src="screenshots/context_actions_with_folder.webp" width="480" alt="The menu after dragging with the right mouse button onto another folder">

### Deleting and Recycle Bin

`Delete` sends files to the Windows Recycle Bin, and `Ctrl+Z` brings them back.
`Shift+Delete` deletes permanently, bypassing the Recycle Bin: it's the only one of Wander's own operations that can't be undone, so it always asks for confirmation.
Whatever involved the deleted files leaves the undo history; the rest stays.
There's no “Delete permanently” item in the menu, on purpose.

**The Recycle Bin** opens right in Wander, from a standard bookmark on the left.
You can see where each file was deleted from and when; “Restore” is the first menu item and works on several files at once.
Other operations in the Recycle Bin are turned off, and you empty it with Windows tools.

**If a file doesn't fit in the Recycle Bin** (its path is longer than 259 characters, or it's on a drive without a Recycle Bin), Wander doesn't delete it silently: the rest goes to the Recycle Bin, and for those files you get a question with a list of names: “Delete permanently” or “Don't delete”.
“Don't delete” is selected by default.

**Replacing can be undone too.** A file replaced during copying goes to the Recycle Bin, and `Ctrl+Z` restores both sides.

### Resolving conflicts

When you copy or move files into a folder that already has files or folders with the same names, Wander doesn't ask about each one in turn.
It shows **one window for the whole operation** before it touches a single file, and you resolve all the name conflicts there at once.

Each pair takes a row: on the left, what's being copied; on the right, what's already there; each with a thumbnail, size and date.
The larger file and the newer file are shown in bold.
Wander checks byte by byte whether files are identical, and if they are, says so: “Files are identical”.

<img src="screenshots/conflict_compare.webp" width="640" alt="The conflict window for files with the same names">

A check mark on a side means that side stays:

| Checked | What happens |
|---|---|
| Left | Replace: what's already there goes to the Recycle Bin |
| Right | Keep as is; when moving, the file stays where it was |
| Both | Keep both: the copy gets the name “name (1)”, and folders merge |

**Merging folders.** If both check marks are set on a pair of folders, the conflicts inside them appear below, and you resolve each one separately.
If only the left one is checked, the folder is replaced entirely.

**Quick answers.**

- “Replace all” and “Skip all” at the top right resolve the whole list at once and close the window.
- Below the list, you pick an answer for the pairs not yet resolved: replace, keep existing, keep both, or replace if newer.
- The “Don't ask about identical files” checkbox is selected by default; its initial state is set in [“Settings” → “File operations”](#file-operations-1).

**Compare.** The ![Compare](icons/compare.svg) button next to a pair's name opens both files side by side: texts with shared scrolling, images side by side or overlaid (`Space` switches); the loupe shows the same spot on both.

The “OK” button becomes available once all pairs are resolved; `Esc` cancels the whole operation, and at that point nothing has been done yet.
Mistakes aren't a problem: replaced files are in the Recycle Bin, and `Ctrl+Z` brings everything back.

### Long operations

Copying, moving, deleting and extracting run in the background.
If an operation takes more than an instant, it gets its own window: the current file name, “Files: N of M”, the amount of data, the speed and the time remaining.
Meanwhile, Wander works as usual: you can browse folders and start other operations.

<img src="screenshots/file_operation.webp" width="640" alt="The operation window, and in Gallery, clocks on the files it's working on">

- **“Minimize”** moves the window to the status bar: “Copying: 45%”; with several operations, there's one combined bar.
  Clicking it opens the list of operations with “Show” and “Cancel” buttons.
  You can't close the window before the operation ends, so that it doesn't get lost; when the operation finishes, the window closes by itself.
- **“Cancel”** stops the operation at once, not at the end of the current file: no partly written file is left behind, and `Ctrl+Z` removes whatever was already copied.
- **Exiting Wander** during an operation asks whether to stop it; “Cancel” is selected by default.

<img src="screenshots/file_operation_status.webp" width="480" alt="A minimized operation in the status bar and the list of operations with “Show” and “Cancel” buttons">

### Companion files

A companion is a file that describes another file: `Sprite.png.meta` in Unity, `IMG.CR2.xmp` or `IMG.CR2.pp3` for a photo.
Wander shows it as one item with the main file, and moves, copies, renames and deletes it along with that file; `Ctrl+Z` brings back the whole group.
Duplicates made in darktable (`IMG_01.CR2.xmp`, `IMG_02.CR2.xmp`) also travel with the photo, as long as there's no `IMG_01.CR2` file next to them.

| Companion | What the Preview pane shows |
|---|---|
| Unity `.meta` | The GUID with a “Copy” button, the importer type |
| `.xmp`, `.pp3` | The rating and color label; you can change them: only one line of the file is edited, the rest is left untouched |

`IMG.xmp` next to `IMG.CR2` and `IMG.jpg` counts as the RAW file's companion: moving the JPEG doesn't take it along.
If there are two JPEGs or two RAW files with the same name next to it, the companion stays a separate item: Wander doesn't try to guess its owner.

To see companions as separate files, clear “Show a file and its companions as one item” in [“Settings” → “File list”](#file-list).

<img src="screenshots/sidecar_guid.webp" width="480" alt="A Unity asset shown as one item with its .meta, and its GUID with a “Copy” button in the Preview pane">

## Culling photos

You can quickly cull a whole shoot in Wander, without an editor: find the misses, compare near-duplicates, rate and keep the best, then sort and process what doesn't need your attention.
Ratings are saved to companion files next to the photos, and Adobe apps, darktable and RawTherapee understand them.

![Gallery of rated photos on a dark background and the Preview pane: a RAW file under the loupe, shooting data, stars and color labels](screenshots/preview.webp)

1. **Open a folder** of photos: it opens in [Gallery](#gallery) with large thumbnails, and RAW files are shown from their embedded previews.
   Pick a dark or gray background in the toolbar.
2. **Weed out the misses.** Turn on the [review helpers](#review-helpers) with the buttons at the bottom of the Preview pane: focus peaking ![](icons/helper-focus.svg) and sharpness ![](icons/helper-sharpness.svg) appear right on the thumbnails, and clipping ![](icons/helper-clipping.svg) shows clipped highlights.
3. **Compare** similar frames: select two, and they appear side by side in the Preview pane, with the loupe showing the same spot on both.
   `Enter` opens the pair [in full screen](#full-screen), and `Shift`+arrow compares a photo with its neighbor.
4. **Rate**: digits `1`–`5` give stars to the selected photos, `Shift`+digit sets a color label, `0` clears the rating, and `Ctrl+Z` undoes.
   Learn more on the [“Ratings and labels”](#ratings-and-labels) page.
5. **Pick the best**: the star filter in the toolbar shows the photos you picked.
6. **Sort and convert your picks right away.** Select the filtered photos (`Ctrl+A`) and drag them with the **right mouse button** onto another folder: in the tree, in Bookmarks or among the files.
   In the menu at the drop point, you can copy or move them, or choose “Convert”, for example to save RAW previews as JPEG: the result lands in that folder, and the originals stay where they are.
   Learn more on the [“Conversion”](#conversion) page.

### Ratings and labels

You can give each photo one to five stars and a color label.
In Gallery and Icons, they appear in the corner of the thumbnail; in Tiles, as stars on the second line; in Details, in the “Rating” column, which you can sort by.

**How to set them.**

- In Gallery, digits `1`–`5` give stars to all selected photos, and `0` clears the rating.
  `Shift`+digit sets a color label; pressing the same one again removes it.
- With the mouse, use the stars and label circles at the bottom of the Preview pane.
  With several photos selected, the rating applies to all of them (“and N more” appears next to it); in a pair and in full screen, each photo gets its own.
- In other views, digits type a file name, so there you rate with the mouse.

Stars and the label don't depend on each other.
A batch of ratings is undone with a single `Ctrl+Z`.
Files don't jump around when you rate them: when sorted by rating, they move into place after `F5`.

**The filter** in the file toolbar appears in a folder that has rated photos, works in all views, and resets when you go to another folder.

- Clicking a star keeps photos with that rating and higher, and `Ctrl`+click adds or removes a single rating.
  For example, to keep only four-star photos, `Ctrl`+click the fourth star in an empty filter.
- The crossed-out star keeps unrated photos.
- Color labels work the same way.
- The reset button ↺ clears the filter.

![Gallery toolbar: the star and color label filter, the background, the search box](screenshots/gallery_toolbar.webp)

**Where the rating is stored.** In a companion file next to the photo: `.xmp` (Adobe, darktable, exiftool) or `.pp3` (RawTherapee); the photo itself isn't changed.
If two companions have a rating, it's read from the program's file (darktable's `IMG.CR2.xmp` or `IMG.CR2.pp3`) rather than from `IMG.xmp`, and a new rating is written to both; the tooltip on the stars names the files.
For a photo without a companion, the stars are dimmed, and the first rating creates that file.
Wander asks first; you can turn the question off in [“Settings” → “File operations” → “Ratings”](#file-operations--ratings).
`Ctrl+Z` deletes the created file.

**Rating from the camera.** Stars set in the camera, in File Explorer or on export are read from the photo itself: RAW, JPEG, TIFF, HEIC.
They look like regular ones and work the same way in the filter and in sorting; the tooltip on the stars says the rating comes from the photo.
Wander doesn't change the photo itself: a new rating goes to a companion, and you can also clear the camera's rating: Wander then creates a companion with a zero rating, asking first, as for any new companion.
A rating in a companion takes precedence over a rating in the photo.

**`.xmp` or `.pp3`.** By default, Wander creates `.xmp`: it affects nothing but the rating.
`.pp3` is a RawTherapee settings file: as soon as it appears next to a photo, RawTherapee stops applying its default processing profile to that photo, even if the file has a single line.
RawTherapee reads ratings from `.xmp` since version 5.7 and syncs them since version 5.11.
You can switch to `.pp3` on the same settings page.

### Review helpers

The buttons at the bottom of the Preview pane, to the left of **RAW** (in full screen, in the corner of the photo), highlight what you cull photos by: where it's sharp, what's clipped, what's hiding in the shadows.
You can turn them on in any combination, and they stay on while Wander is open; a button that's on has a double border.
Hold `Alt` to see the frame without the overlay.
Each button's name appears in its tooltip.

| Button | What it shows |
|---|---|
| ![Focus peaking](icons/helper-focus.svg) | **Focus peaking**: crisp edges in the frame in pink, where it's really sharp. Computed on the full frame, drawn 1:1 in the loupe. Isolated dots (snow, glints, grain) are discarded: focus means lines |
| ![AF point](icons/helper-af.svg) | **AF point**: the box the camera focused on (Canon). The camera sometimes records it inaccurately, so check it against peaking |
| ![Sharpness](icons/helper-sharpness.svg) | **Sharpness** from 0 to 100 at the center of the frame. The number appears next to the date taken, in the corner of a gallery cell, and, in a pair and in full screen, next to the stars. 70 and above is sharp, 45–70 is soft, and close to zero is how I see the world without my glasses |
| ![Clipping](icons/helper-clipping.svg) | **Clipping**: clipped highlights in the channel's color (red, green, blue or a mix of them; white means all three), clipped shadows in blue |
| ![Histogram](icons/helper-histogram.svg) | **Histogram** per channel; in a pair and in full screen, each photo has its own. A bright bar at the edge shows how much of the frame is clipped in the shadows (left) and in the highlights (right); the percentages are in the tooltip |
| ![Shadows](icons/helper-shadows.svg) | **Shadows**: the frame with lifted shadows, to see what's hiding in them |
| ![Highlights](icons/helper-highlights.svg) | **Highlights**: the frame with toned-down highlights, to see what can be rescued from clipping |

**On thumbnails.** In Gallery, focus peaking, the AF point, clipping, shadows and highlights are drawn right on the thumbnails, and sharpness is a number in the corner, so you can spot misses across the whole folder at once.
Only what's currently on screen is computed.

- In **RAW** mode, peaking waits for the full frame to be developed from the sensor data and measures that.

## Actions and menus

Two menus work on the selected files.

- **The context menu** (right-click) in the Windows 10 style: all items at once, including items from other programs such as 7-Zip, TortoiseGit and Notepad++.
  Learn more on the [“Context menu”](#context-menu) page.
- **The “Actions” menu** in the window header: what Wander does itself.
  Its first line tells you what the action applies to: “3 CR3”, “4 images”, “5 files”, “2 folders”, “7 items”, the name of a single file, or “Folder: D:\Photos” if nothing is selected.

In the “Actions” menu:

- “Batch rename…”, see [“Batch rename”](#batch-rename);
- “My actions”: custom actions, such as opening files in your own program or running a script, see [“Custom actions”](#custom-actions);
- “Convert”: video, music, images, documents, previews from RAW, see [“Conversion”](#conversion);
- “Extract here” and “Extract…” for archives;
- “Create shortcut”, “Copy path”, “Open in Terminal”.

The menu shows only what fits the selection.
“My actions” and “Convert” are in the context menu too, and when you right-drag files onto a folder, their results go straight there.
You can remove unneeded items from both menus in [“Settings” → “File operations” → “Context menu”](#file-operations--context-menu).

### Context menu

The right-click menu shows the whole list at once, without “Show more options”.

- At the top, the most-used items: “Open”, “Open with”, and other programs' items with their icons and submenus.
- At the bottom, the **“File”** submenu: cut, copy, paste, copy path or name, rename, create shortcut, delete, plus “Send to” and other Windows file items.
- Duplicate items are removed.

From the keyboard, open the menu with `Shift+F10` or the Menu key.

Right-clicking empty space opens the folder menu: “New” (a new folder you can undo, with its name ready to type, then the Windows templates), “View”, “Sort”, “Open in Terminal” (Windows Terminal in this folder, or PowerShell if it isn't installed), “Copy path” and “Properties”.

![Right-click menu on empty space in a folder: “New”, “View”, “Sort”, “Open in Terminal”, “Copy path”, “Properties”](screenshots/folder_context_menu.webp)

The first right-click in a newly opened folder may open the menu with a delay while other programs' extensions respond; after that, the menu opens instantly.

#### Customizing

You customize the menu in [“Settings” → “File operations” → “Context menu”](#file-operations--context-menu):

- **“Items from other apps”** can be turned off entirely: their extensions are then not loaded, and the menu opens faster;
- **one by one**, items are turned off in the table, which shows the item, the program and the file types.
  The row with a program's name (“7-Zip”) controls its whole section.
  The search above the table finds items by name, program and file type (`.mp4`).
  The table lists what has already appeared in the menu, and “Add a program or file type…” shows the items without waiting for the menu;
- **“Main items”** are the ones Wander draws itself: open, copy, properties, “Open in Terminal”, custom actions, “View” and “Sort” in the empty-space menu.
  Clearing a checkbox removes the item from the menu, but its keyboard shortcut keeps working.

“Reset context menu settings…” at the bottom of the page puts everything back the way it was.

### Batch rename

Press `F2` on two or more files, or choose “Actions” → “Batch rename…”.
One batch renames either only files or only folders.

<img src="screenshots/group_rename.webp" width="640" alt="Batch rename window">

**The template** builds the new name from text and tokens:

| Token | What goes in |
|---|---|
| `[N]` | the original name |
| `[C]` | a counter; the start number, step and number of digits are in the fields below the template |
| `[D]` | date modified; `[D:yyyy-MM-dd]` in your own format |
| `[X]` | date taken from EXIF, or date modified for a file without it |
| `[P]` | the folder name |

For example, `[P]_[C]` with a three-digit counter in the “Vacation” folder gives `Vacation_001`, `Vacation_002` and so on.

Below the template:

- **“Find and replace”**, case-insensitive, optionally with a regular expression (`$1` in the replacement is the first group);
- name and extension case; the extension itself can change only its case.

On the right, the **“Before” → “After”** table: conflicts are highlighted in red, unchanged names in gray.
Numbering follows the order of the files on screen.
Only the “OK” button applies the changes, and `Ctrl+Z` restores all the names at once.
Files can swap names: Wander sorts that out itself.
The last five templates and the rules are remembered.

### Conversion

“Actions” → “Convert” offers ready-made conversions.
Select files and choose an item: the result is saved next to the source under a new name, and the source file stays unchanged.
`Ctrl+Z` moves the result to the Recycle Bin.

| For | What | Requires |
|---|---|---|
| RAW | Small and large preview: the JPEG the camera embedded in the file | nothing |
| Images | To JPEG (quality 90), to PNG, shrink to 1920 on the long side | nothing |
| Images | To WebP | FFmpeg |
| Video | To MP4 (H.264); compress to `_small.mp4`; audio to M4A or MP3; frame to PNG | FFmpeg |
| Audio | To MP3, M4A, FLAC, WAV | FFmpeg |
| Documents | To PDF | LibreOffice |
| Markdown, DOCX | Markdown to DOCX, DOCX to Markdown | Pandoc |

An item whose program is missing is grayed out, and its tooltip names that program.

**To another folder.** At the end of the submenu is “To another folder…”: Wander asks for a folder, and the results of the whole batch go there.
The same in one gesture: drag the files with the **right mouse button** onto a folder in the file area, the tree or Bookmarks, and choose a conversion in the menu that opens at the drop point.

**The output name** is built from the source's name by a template, next to the source: `clip.mp4` becomes `clip_small.mp4`.
If the name is taken, a number is added: next to “clip (3)” you get “clip (4)”.
Nothing is overwritten.

**Conversion progress** shows in the operation window, as with copying, with a cancel button; an unfinished result goes to the Recycle Bin.
If something fails, a window appears with a line for each file and the program's message.

#### Programs

Wander finds FFmpeg, LibreOffice and Pandoc by itself, in `PATH` and in the usual install locations.
[“Settings” → “File operations” → “Programs”](#file-operations--programs) shows whether each one was found and where it is.
**“Browse…”** picks the exe manually, **“Reset”** goes back to finding it automatically.
If a program isn't found, the same page shows an install command you can copy: `winget install Gyan.FFmpeg`, `TheDocumentFoundation.LibreOffice`, `JohnMacFarlane.Pandoc`.

#### Images

Wander re-encodes images itself:

- a photo is rotated according to EXIF and stays upright from then on;
- date taken, camera, shutter speed, aperture, ISO, focal length, coordinates, rating and copyright carry over to JPEG and TIFF, including from RAW; title, keywords and author carry over when Windows provides them, but are not taken from an `.xmp` next to the photo;
- a JPEG that needs neither shrinking nor rotating isn't re-compressed;
- transparent areas are filled with white in JPEG, and PNG, BMP and GIF don't store metadata;
- a RAW that Windows can't open is converted from the JPEG embedded in it.

**Preview from RAW** is the embedded JPEG as is, without re-compression, so it's fast.
**Large** (`IMG_lpv.jpg`) is the biggest one the camera embedded (for CR3, CR2 and ARW, it's the full frame); **small** (`IMG_spv.jpg`) exists only in CR3, at 1620×1080.
Rotation, camera, date taken and coordinates are written to the file.

### Custom actions

Your own menu items: “Open in Notepad++”, “Pack with 7-Zip”, any script.
You set them up in [“Settings” → “File operations” → “Actions”](#file-operations--actions): the table of actions is at the top, and the card of the selected one is below it.
“Add” creates a new row, “Duplicate” creates a copy.
The built-in conversions are here too: you can't edit them, but you can turn them off with the checkbox, or duplicate one and edit the copy.

On the card:

- **Name**: how the item appears in the menu;
- **Applies to**: a group (images, video, audio, text and code, documents, archives, folders, all) or a mask such as `*.psd;*.ai`, which overrides the group.
  The action is offered only when **all** the selected files match, and an action for folders works on the open folder when nothing is selected;
- **Program**: one from the list of programs already in use, or your own, with the folder button (`.exe`, `.cmd`, `.bat`);
- **Arguments** with placeholders (below);
- **Mode**: “per file” (one process per file, one after another) or “all in one command”;
- **Show in**: “both menus”, “context menu” or “header” (the “Actions” menu); **In submenu**: inside the “My actions” submenu or as a separate item;
- **Show console**: by default the program runs without a window, and Wander shows its error message itself;
- **Output file** (below).

An action that won't work as set up (no program, arguments that don't fit the mode) is marked red in the table, and the card explains what's wrong.

**Placeholders** in the arguments; quotes are added automatically:

| Placeholder | What goes in |
|---|---|
| `{path}` | the file |
| `{name}` | its name without the extension |
| `{ext}` | the extension without the dot |
| `{dir}` | the file's folder |
| `{paths}` | all the selected files, for “all in one command” mode |
| `{list}` | a temporary file with the list of paths, one per line |
| `{out}` | the output file |
| `{outdir}` | an empty folder for a program that names its output itself |

Example: for “Open in Notepad++”, the program is `C:\Program Files\Notepad++\notepad++.exe` and the arguments are `{path}`.

**The output file** is set by a name template and goes next to the source: `{name}.mp4`, `{name}_small.mp4`.
A taken name gets a number, nothing is overwritten, and `Ctrl+Z` moves the result to the Recycle Bin.
An action without an output file can't be undone: Wander doesn't know what the program did.

For a program that picks the name itself and replaces an existing file (LibreOffice does this), pass `{outdir}` instead of a folder: the program writes its result to an empty folder, and Wander moves the file with the output name next to the source, by the same rules.
This works in “per file” mode.

**The built-in image encoder** runs the built-in image conversions, and you can choose it in your own action.
Its arguments are settings separated by semicolons:

- `format=jpeg` (or `png`, `bmp`, `tiff`, `gif`): required;
- `quality=1…100`: JPEG quality, 90 by default;
- `maxside=`: the long side in pixels;
- `source=preview` or `preview-full`: take the JPEG the camera embedded in the RAW, the small one or the largest.

Example: `format=jpeg;quality=85;maxside=1920` with the output file `{name}_1920.jpg`.

## Reference

Everything that's easier to look up than to read through:

- [“Keyboard shortcuts”](#keyboard-shortcuts): all keyboard shortcuts and mouse gestures;
- [“Settings”](#settings): every settings page, item by item;
- [“Data and cache”](#data-and-cache): where Wander keeps settings, thumbnails and logs;
- [“Command line”](#command-line): startup switches, opening a folder, portable mode.

If something doesn't work, see the [“Troubleshooting”](#troubleshooting) page.

### Keyboard shortcuts

A similar list, shorter and searchable by shortcut and by action, is in [“Settings” → “Keyboard”](#keyboard).

**Window areas.** `Tab` and `Shift+Tab` switch between areas: navigation buttons → address bar → Bookmarks → tree → search box → files; the active area has a frame around it.
`Ctrl+1`, `Ctrl+2` and `Ctrl+3` move the keyboard straight to the Folders pane, the files and the Preview pane.
`Esc` returns the keyboard to the files from anywhere.
After operations and dialogs, the keyboard stays where it was: after deleting, the next file is selected; after pasting, the pasted one.

#### Navigation

| Keys | Action |
|---|---|
| `Alt+←` / `Alt+→` | Back / forward through the folders you visited |
| `Alt+↑`, `Backspace` | Up one level |
| `Enter` | Open the selection: enter a folder, open a file in its default app |
| `Ctrl+L`, `Alt+D` | Edit the path in the address bar, with all the text selected |
| `F4` | Recently visited folders |
| `Enter` / `Esc` *in the address bar* | Go to the path / cancel editing and return the keyboard to the files |
| `F5` | Refresh the folder and the expanded branches in both panes |

#### Moving around the window

| Keys | Action |
|---|---|
| `Tab` / `Shift+Tab` | Next / previous window area |
| `Ctrl+1` | To the current folder tree, at the open folder; press again for the other tree (Bookmarks ↔ Computer) |
| `Ctrl+2` | To the files |
| `Ctrl+3` | To the Preview pane: text, code, document, page, player button; press again for the other half of a pair |
| `Esc` *in the Preview pane* | Return the keyboard to the files |
| `Ctrl+Shift+E` | Show the current folder in the tree and move to it |
| `Ctrl+Q` | Show / hide the Preview pane |
| `Ctrl+B` | Show / hide the Folders pane; the button to the left of “←” does the same |
| `↑` / `↓`, `Home` / `End`, `PgUp` / `PgDn` *in the tree* | Move the cursor through the rows; a folder opens once the cursor stops on it, unless “Open folders as you move to them with the keyboard” is turned off |
| `←` / `→` *in the tree* | Collapse a branch, or on a collapsed one go to the parent / expand a branch, or on an expanded one go to the first subfolder |
| Letters *in the tree* | Jump to a folder by the start of its name |
| `Enter` *in the tree* | Open the folder under the cursor |
| `Shift+F10`, Menu key *in the tree* | Menu of the folder under the cursor |
| `Esc` *in the tree* | Return the keyboard to the files |
| `F2` *in the tree* | Rename the folder under the cursor |
| `Ctrl+↑` / `Ctrl+↓` *in Bookmarks* | Move your bookmark up / down |

#### File operations

| Keys | Action |
|---|---|
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | Copy / cut / paste; text or an image on the clipboard is pasted as a file, `Text.txt` / `Image.png` |
| `Ctrl+Shift+C` | Copy the full path |
| `Delete` | Move **to the Recycle Bin**; on your own bookmark, asks whether to remove the bookmark or the folder; a standard bookmark is removed from the pane |
| `Shift+Delete` | Delete **permanently**: always asks, can't be undone |
| `F2` | Rename in place (`Enter` applies, `Esc` cancels); on two or more files, batch rename in a separate window |
| `Ctrl+Shift+N` | Create a folder and type its name right away |
| `Ctrl+Z` | Undo the last operation |
| `Shift+F10`, Menu key | Context menu of the selection, or of the open folder if nothing is selected |

#### Selection and search

| Keys | Action |
|---|---|
| `Ctrl+A` | Select all |
| `Ctrl+F` | Search box above the files: the open folder right away, subfolders right after |
| `Ctrl+F` *in the Preview pane with text* | Find in the text: `Enter` / `Shift+Enter` for next / previous, `Esc` to close |
| `F3` / `Shift+F3` | Next / previous match in the Preview pane; after the last one, the next found file |
| `Ctrl+Shift+F` | Search window |
| `Enter` *in the Search window* | Search now, without waiting for a pause |
| `Esc` *in the Search window* | Close the window and return the keyboard to the files |
| `Esc` *in the search box* | Stop and clear the search, return the keyboard to the files |
| `F5` *on search results* | Repeat the search |
| `Esc` *in the files* | Clear the selection |
| Letters *in the files* | Jump to the file whose name starts with the typed letters; a one-second pause starts over, and repeating the same letter cycles through the files starting with it |
| Arrows *in Tiles and Icons* | `→` at the right end goes to the next row, `←` at the left end to the previous one, `↑` in the top row to the first file, `↓` in the bottom row to the last; with `Shift`, the selection extends |
| `Alt+Enter` | Properties of the selection, or of the open folder if nothing is selected |

#### Views and gallery

| Keys | Action |
|---|---|
| `Ctrl+Shift+1` / `2` / `6` / `7` | Gallery / Icons / Details / Tiles |
| `0`–`5` *in the gallery* | Rate the selected files; `0` clears the rating |
| `Shift+0`–`5` *in the gallery* | Color label for the selected files; the same one again or `0` clears it |
| `Enter` / `Space` *on photos in the gallery* | Full screen: one photo; two selected, side by side; more, one at a time |

#### Full screen

| Keys | Action |
|---|---|
| `→` `↓` `Space` `PgDn` / `←` `↑` `Backspace` `PgUp` | Next / previous photo |
| `Home` / `End` | First / last photo |
| `Shift`+arrows | Adjacent photo on the right: the screen splits, and from then on you browse the right one |
| `←` / `→` *on a pair* | Keep the left / right one on screen |
| `0`–`5`, `Shift+0`–`5` | Rate and label the photo; with a pair, the one under the mouse |
| `Delete` / `Shift+Delete` | Photo to the Recycle Bin / delete permanently, then the next one is shown; with a pair, the one under the mouse |
| `Alt` (hold) | Frame without helper overlays |
| `Ctrl+Z` | Undo rating or deletion |
| `Z` | 1:1 loupe without a mouse button; with the pointer hidden, on the sharpest spot or the AF point, depending on the setting; press again to turn it off |
| `Esc` / `Enter` | Close |

#### Settings window

| Keys | Action |
|---|---|
| `F1` | The guide, at the section about the open page |

#### Mouse

| Gesture | Action |
|---|---|
| Double-click | Open |
| Right-click an item | Context menu; the selection moves to the item if it was outside the selection |
| Right-click empty space | Menu of the open folder; the selection is cleared |
| Click empty space | Clear the selection; the dark frame stays on the last item, and an arrow key takes you back there |
| Drag across empty space | Selection rectangle; at the edge and beyond it, the files scroll by themselves and the rectangle keeps stretching |
| `Ctrl`+click | Add to the selection or remove from it |
| Click a row in the tree or Bookmarks | Go to the folder; you can click anywhere on the row |
| Right-click a folder in the tree | That folder's menu; the open folder doesn't change |
| “Paste” in a folder's menu (in the files, the tree, Bookmarks) | Paste into that folder; `Ctrl+V` in the files pastes into the open one |
| `Alt`+click a triangle | On a collapsed folder, expand it together with its first-level subfolders; on an expanded one, collapse the subtree |
| `Shift`+wheel | Scroll sideways: tree, Bookmarks, Details |
| `Ctrl`+wheel *in the files* | Larger / smaller; each view has its own sizes, and they're saved |
| `Ctrl`+wheel click | Reset the current view to its default size |
| Click a column header | Sort; click again to reverse the order |
| Click a star / circle in the filter bar | This rating and higher / only this label; click again to clear the filter |
| Click the crossed-out star | Only photos without a rating |
| `Ctrl`+click a star / circle | Add or remove one rating or label without touching the others |
| Drag | Within a drive, move; between drives, copy; you can drag a folder out of the tree |
| Drag and hold over a folder | In a pane, a collapsed folder expands; in the files, the folder opens only if “Open a folder when a dragged file hovers over it” is on; at the edge, the list scrolls, faster the farther past the edge |
| Drag to the “+” zone under Bookmarks | Add the folder to Bookmarks |
| `Shift` / `Ctrl` / `Alt` while dragging | Move / copy / `.lnk` shortcut |
| Right-drag | Menu at the drop point: copy, move, shortcuts, “Convert” and “My actions”; in bold, what a normal drop would do |
| Left button on an image in the Preview pane | Loupe |
| Wheel, “back” / “forward” buttons *in full screen* | Browse photos; with `Shift`, the right one of a pair |
| Left, then right button on an image *in a pair* | Only the photo under the pointer moves: this is how you align shots taken with an offset; once the right button is released, the photos move together and the offset is kept |
| `Alt` (hold) *over an image* | Helper overlays are hidden while the key is held |

### Settings

Settings open from `⋯` → “Settings”.
Pages come in three groups: what's on screen, what happens to files, and housekeeping; subpages are indented under their parent page.
`F1` or “?” next to the window buttons opens the guide at the section about the open page.

A setting that works only together with another one is indented under it.
In a number field, the mouse wheel changes the value by 1, `Shift`+wheel by 10, and dragging sideways changes it smoothly.
“Cancel” in the Settings window reverts everything you changed while it was open.

#### Folders and bookmarks

- **On startup**: “Last folder” opens the session where it ended, “Working folder” starts in the chosen folder every time (“Documents” out of the box).
- **Folders pane**: “Open folders as you move to them with the keyboard” and “Scroll sideways to long names”.
- **Standard bookmarks**: Downloads, Documents, Pictures, Desktop, Music, Videos, Recycle Bin.

Learn more: [“Navigation”](#navigation), [“Folder panes”](#folder-panes).

#### File list

- **Show in the list**: “Hidden files and folders”, and under it “Protected operating system files” and “System folders and files at drive roots”.
- **Companion files**: “Show a file and its companions as one item”.
- **Changes made by other programs**: “Show right away”; if you clear it, changes appear only after you press `F5`.

Learn more: [“File area”](#file-area), [“Companion files”](#companion-files).

#### View

- “Default view”: Details, Tiles, Icons or Gallery.
- **Photo folders**: “Use Gallery when photos in a folder exceed” a set percentage.

Learn more: [“Views and sorting”](#views-and-sorting), [“Gallery”](#gallery).

#### View / Sizes

Sizes for each view, in pixels:

- **Details**: row height and icon size.
- **Tiles**: tile width, icon size and label font size.
- **Icons**: cell width, icon size, cell margin and label font size.
- **Gallery**: cell width, image size, cell margin and label font size.

A preview next to the fields is drawn with these numbers, and `Ctrl`+wheel in the file area changes the same fields.

Learn more: [“Sizes”](#sizes).

#### View / Gallery

“Background”: light, gray or dark; gray and dark have a brightness setting from 0 to 255.

“Z loupe while the pointer is hidden” sets where the `Z` loupe lands in full screen if the mouse hasn't moved: “On the sharpest spot of the photo” or “On the camera's focus point”.

Learn more: [“Gallery”](#gallery), [“Full screen”](#full-screen).

#### File operations

- **Confirmations**: “Ask before moving to the Recycle Bin” and “Ask before moving”.
- **Name conflicts when copying and moving**: “Skip identical files without asking”.
- **Drag and drop**: “Open a folder when a dragged file hovers over it”.

Learn more: [“Working with files”](#working-with-files), [“Copying and moving”](#copying-and-moving), [“Resolving conflicts”](#resolving-conflicts).

#### File operations / Context menu

Items from other apps (all at once or one by one, with search); Wander's main items; resetting the menu settings.

Learn more: [“Context menu”](#customizing).

#### File operations / Actions

Custom actions and built-in conversions: a table and a card for the selected action.

Learn more: [“Custom actions”](#custom-actions).

#### File operations / Programs

FFmpeg, LibreOffice and Pandoc: whether each program was found, “Browse…” and “Reset”.

Learn more: [“Programs”](#programs).

#### File operations / Ratings

- “Rating file for a photo without a companion”: `.xmp` or `.pp3`.
- “Ask before creating a rating file”.

Learn more: [“Ratings and labels”](#ratings-and-labels).

#### Cache and memory

- **Thumbnail cache on disk**: “Keep thumbnails between launches”, the “Max size, MB” limit (256 out of the box) and “Clear cache”.
- **Memory**: “For images, MB”, a shared ceiling for thumbnails and Preview pane frames; 0 means a sixteenth of the computer's memory.
- **Temporary copies**: “Keep in the system Temp folder”.
- **Icon loading**: “What's on screen first”.

Learn more: [“Data and cache”](#data-and-cache).

#### Keyboard

The main keyboard shortcuts, with search by shortcut or by action; the list is read-only.

Learn more: [“Keyboard shortcuts”](#keyboard-shortcuts).

#### Debug and reset

- **Session log**: “Record keystrokes, clicks and selection” and “Write real file paths”.
- **“Debug” menu**: “Show in the main menu”.
- **Reset settings**: “Reset all settings…”.

Learn more: [“Troubleshooting”](#troubleshooting).

### Data and cache

Wander writes nothing into the system: it keeps everything of its own in one folder, `%LOCALAPPDATA%\Wander`, or, in portable mode, in the `data` folder next to `Wander.exe` (see [“Command line”](#command-line)).
The folder a running Wander uses is shown on the `Data root` line at the start of the session log.

| What | Where in the data folder |
|---|---|
| Settings, window size, bookmarks, expanded branches | `state.json` |
| View and sorting pinned to folders | `folders.json` |
| Thumbnail cache | `thumbs` |
| Session logs | `logs` |
| Crash reports | `crashes` |
| Temporary copies of files from archives | `tmp` |
| Built-in browser data for the Preview pane (HTML, Markdown, PDF) | `WebView2` |

To start from scratch, click “Reset all settings…” in [“Settings” → “Debug and reset”](#debug-and-reset), or delete this folder while Wander is closed.

#### Thumbnail cache

Thumbnails, once made, are stored on disk, so a folder you've seen before opens quickly.
If a file changes or is replaced by another one with the same name, its thumbnail is rebuilt; updating Wander clears the whole cache.
In [“Settings” → “Cache and memory”](#cache-and-memory), you can turn the cache off (“Keep thumbnails between launches”), limit it (256 MB out of the box) and clear it with the “Clear cache” button, which shows the current size next to it.

**Memory for images** is a shared ceiling for thumbnails and Preview pane frames. 0 means a sixteenth of the computer's memory (1 GB with 16 GB).
Limits take effect right away, without a restart.

**“What's on screen first”** loads icons and thumbnails of the visible part before the rest.
You notice it in Details and in a large tree, and hardly at all in Tiles.

#### Temporary copies

A file from an archive that a program opens or the Preview pane shows is extracted to a temporary copy; copies older than a day are deleted at the next startup.
“Keep in the system Temp folder” (in [“Settings” → “Cache and memory”](#cache-and-memory)) puts them where Windows itself cleans them up.
In portable mode, this is on from the start, so that nothing extra is written to the USB stick.
After a restart, the built-in browser's data folder moves there too.

### Command line

You can start Wander with switches, from a shortcut, a terminal or a script:

```
Wander.exe [--folder <folder>] [--portable | --data-dir <folder>]
```

| Switch | What it does |
|---|---|
| `--folder <folder>` | Start in this folder instead of the last or working folder. If the folder doesn't exist, Wander starts as if without the switch |
| `--portable` | Keep settings, logs and the thumbnail cache in the `data` folder next to `Wander.exe` |
| `--data-dir <folder>` | Keep them in the specified folder; this switch takes precedence over `--portable` |

Put a path with spaces in quotes; a switch can also be written with an equals sign: `--folder="D:\My photos"`.
A relative path is resolved from the current folder.

**The environment variable** `WANDER_DATA_DIR` works like `--data-dir` for every launch without switches.
If more than one is set, the first in this list wins: `--data-dir`, `--portable`, `WANDER_DATA_DIR`; if none is set, data is stored in `%LOCALAPPDATA%\Wander`.

**Multiple windows.** Each launch opens a separate window.
Windows that share a data folder save settings in turn, and the last save wins; to keep the windows from getting in each other's way, give the second one its own `--data-dir`.

**Examples.** Open Wander in the terminal's current folder (PowerShell):

```pwsh
& "C:\Apps\Wander\Wander.exe" --folder .
```

USB stick: put a `Wander.cmd` file next to `Wander.exe`, and it will start Wander with its settings on the same stick:

```bat
@start "" "%~dp0Wander.exe" --portable
```

Separate settings and bookmarks for work: a shortcut with this target:

```
"C:\Apps\Wander\Wander.exe" --data-dir "D:\Work\wander" --folder "D:\Work"
```

## About

Wander is free for personal and any noncommercial use: you can use it, study and change its code, and share copies.
Commercial use requires an agreement with the author.
License: [PolyForm Noncommercial 1.0.0](../LICENSE).

Wander is in beta: it works, and the author uses it daily, but there are few users so far, and bugs are possible.
The software is provided “as is”, without warranty.

- [“Reliability and speed”](#reliability-and-speed): how Wander is tested and why it stays fast;
- [“Roadmap”](#roadmap): what's coming next.

What changed in each version is in the [CHANGELOG](CHANGELOG.md), and past versions are on the [releases page](https://github.com/lekta/wander/releases).

### Reliability and speed

Wander works with files, so reliability and stability matter most to it.
Here's what they're built on.

**Files are protected from accidents.** Every file operation comes with a mandatory set: undo with `Ctrl+Z`, an entry in the session log, system folder protection, and, for a destructive one, a confirmation with “Cancel” as the default.
Deleted files go to the Recycle Bin, and so do files replaced while copying; name conflicts are resolved before the first file is touched.
Learn more on the [“Working with files”](#working-with-files) page.

**Testing.**

- **Logic: tests.** The decisions that files depend on (what to do on a name conflict, how to undo an operation, what counts as a companion, how to name the result) live in a core with no interface.
  It's about a third of Wander's code, and it's checked by **over two thousand** automated tests that cover **92%** of its lines.
- **Interface: scenarios.** Before a release, automated scenarios walk through the real Wander window on a set of test folders: thousands of files, RAW, documents and archives in various formats, names in various languages, paths longer than 260 characters, hidden and locked files.
  They copy and undo, search, page through the Preview pane, check selection and focus, and verify the result.
  A long run walks through folders for half an hour, making sure memory doesn't grow.
- **Review: by hand.** What automation can't see (drag and drop with other programs, other programs' menu items, the look) is checked against checklists by a meatbag before every release.
- **Measurements.** Each release's size, startup time and memory use are measured and compared with the previous release: a regression stops the release just like a failed test.

**Speed.**

- A folder appears right away, and the heavy parts load in the background: thumbnails, icons, folder sizes, program signatures.
- Only what's visible on screen is drawn, so a folder of thousands of files scrolls as easily as one of ten.
- RAW shows instantly: both the thumbnail and the Preview pane use the preview embedded in the file, without developing the RAW.
- Neighboring photos are prepared in advance while you look at the current one.
- Thumbnails, once made, are kept in a disk cache, and a folder you've seen before opens faster.
- Long operations run in the background, and the window stays responsive meanwhile.
  Everything that could slow things down is cached or runs asynchronously.

**Openness.**

- The source code is open for reading on [GitHub](https://github.com/lekta/wander).
- The release `Wander.exe` is built on GitHub's servers straight from this code, every build's log is public, and the SHA256 checksum next to the file lets you verify that you downloaded exactly that file.
- Wander doesn't go online: no analytics, no ads, no background updates.
  You send a bug report yourself, if you want to.
- Nothing is written into the system: Wander keeps everything of its own in one folder; see [“Data and cache”](#data-and-cache).
- The author originally wrote the app “for myself”, so it works the way that suits the user, not the publisher.

### Roadmap

What Wander will learn to do next; no order or dates are promised.

- Remapping keyboard shortcuts: for now, the list in Settings only shows them.
- Extended support for 3D formats, a waveform in the audio preview.
- Filtering photos by ISO, shutter speed and sharpness.
- Dark theme: for now, only the Gallery background and the Preview pane can be dark.
- Search by text in `.pdf` without an installed PDF reader.
- Writing to archives, creating archives, entering passwords, searching inside archives.
- Better copying of large files, support for external and network devices.
- Extreme automated tests on a virtual machine.
- Plugin support, splitting the app's monolith into pluggable modules.
- World domination (partial at least, about 70 m² for a start).

## Troubleshooting

Is Wander behaving unexpectedly, crashing or refusing to do something? ~~Kill it with fi~~ This page explains how to find out the details and report the problem.

**The action journal** opens with the ![Action journal](icons/journal.svg) button on the left of the status bar.
It records what happened during the session: which folders you opened and what you did in them, with timestamps.
A message that was replaced by the next one before you had time to read it is there too.

**To report a problem**, use `⋯` → “About Wander” → “Feedback” or [Issues on GitHub](https://github.com/lekta/wander/issues).
It helps to include the steps, what you expected and what happened, and the session log.
Report a vulnerability privately, as described in [SECURITY.md](SECURITY.md).

**If Wander crashes**, it offers a report: a prefilled GitHub issue and an archive in the `crashes` folder (see [“Data and cache”](#data-and-cache)).
**Nothing is sent automatically**: it's up to you whether to send the report.

**The session log** is a technical file, one per launch, in the `logs` folder: version, system, opened folders, operations, errors.
Paths in it are replaced by masks like `<C:\~3fa91c\~0b2e4d.jpg>` that show the drive, depth and extension but not the names, so you can attach the log to a report.
To investigate a bug, turn on “Write real file paths” and “Record keystrokes, clicks and selection” in [“Settings” → “Debug and reset”](#debug-and-reset).

**The “Debug” menu** appears in `⋯` when you turn on “Show in the main menu” on the same page.
Its “Session log” item opens the current session's log; the rest is for checks together with the author.
The same page also has “Reset all settings…” at the bottom.
