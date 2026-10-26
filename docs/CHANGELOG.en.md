# Changelog

🌐 **English** · [Русский](CHANGELOG.md)

English edition of [CHANGELOG.md](CHANGELOG.md), from version 0.6 on;
earlier versions are described in Russian only. Each version's section here
opens its release notes on GitHub.

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions
follow [SemVer](https://semver.org/). Dates in ISO format.

## [0.6.0-beta] — 2026-10-26

### Added
- **English interface** — follows the Windows language, set in Interface →
  Language. **Site and guide in English**, README in English.
- **Dark theme** — Interface → Color scheme, same as Windows by default.
- **Phone and camera as a folder** in Computer: preview, thumbnails, copy
  to disk; a dropped connection waits and resumes.
- **Flash drives and cards** appear and disappear on their own; **Eject**
  in the drive menu and on a finished operation, works on the first try.
- **Reliable copying**: retry on read errors, temporary name until done,
  no sleep, plain-word reasons. **Checking copies** — File operations.
- **Finished operations** stay in the status bar: result, per-file errors,
  Retry.
- **Computer**: drives in the list with size and fill bar; the panel
  collapses.
- **Your place in a folder** — same file and scroll when you come back;
  rating filter per folder, optional.
- **Search**: by type (`/img`, `/vid`, `/doc`…), by shooting data
  (`f/2.8`, `iso>1600`, `24-70mm`), "Search subfolders" button, "Go to
  location".
- **Zoom in steps** `Ctrl` + wheel — 50–400 % with a minimap; **video** —
  2× and 4×.
- **Video**: what's inside — codec, HDR, tracks, subtitles; **WebM, OGV,
  Ogg, Opus** via the system player; if it won't play — why, and which
  Store extension is needed.
- **What Windows is missing** — for AVIF, JPEG XL, WebP, RAW: the extension
  name and a Store button.
- **Formats**: spreadsheets as a grid (CSV, TSV, XLSX, ODS); 3D — PLY, 3MF,
  FBX and model thumbnails; PSD / PSB; fonts; subtitles, playlists, `.nfo`,
  `.reg` and more code languages as text; OpenDocument, Office, Krita, CBZ
  thumbnails.
- **Preview by content** — files with a wrong or no extension.
- **Bookmarks**: custom names; New folder in the bookmark and drive menu.
- **`Ctrl+Z` into a folder that's gone** — asks where to put things back.
- **Audio**: card line — codec, bitrate, sample rate; Ogg and Opus tags and
  cover.
- **More detailed action journal** — a line per file, reasons, opened
  files.

### Changed
- **Busy files**: `Ctrl+Z` and permanent delete wait; a refusal names the
  file and the program.
- **A photo with sidecars** moves all together or not at all.
- **Review helpers**: new icons in two groups, work without a selected
  photo; **sharpness score** — curved scale, "soft" from 50.
- **Gallery on a dark background** — dark all over; each color scheme has
  its own gallery background.
- **Look**: one background for dialogs, fewer grays, flat table header.
- **A star in the filter bar** — only that rating, `Ctrl` adds.
- **Static GIF** — as a picture (loupe, `Z`, Compare); **text `.doc`** — as
  text; **XLSX, ODS** — as a table.
- **Small things**: search result paths from the search folder; half
  transparent AF frames; RAW tiles from devices rotated; a drive in the
  pane shows volume info only.

### Fixed
- **Copy and move**: documents open in other programs get copied; a failed
  move between drives leaves no second copy; a folder copy that failed
  halfway undoes with `Ctrl+Z`.
- **Ratings** with `.pp3` and `.xmp` show right away; no stray "None"
  label in `.pp3`.
- **Thumbnails**: HEIC, AVIF, JPEG XL, WebP after installing the extension;
  scanned PDF covers not blank; small TortoiseGit icons.
- **WebP zooms** with the loupe and `Z`.
- **Drag and drop**: sidecars don't go to other programs; Wander's window
  doesn't pop up; a folder dragged between panels stays in sight.
- **Full screen**: leaving it lands on the last photo.
- **Small things**: bookmark "⋯" when scrolled sideways; `Ctrl+C` over text
  in the pane copies text; selection rectangle past the list edge;
  sharpness score after `Alt`; "no space" on FAT with space left; an
  unavailable standard bookmark is gray.

[0.6.0-beta]: https://github.com/lekta/wander/releases/tag/v0.6.0
