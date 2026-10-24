# Skimmer

**A fast, portable photo browser for folders with tens of thousands of pictures.**
One `.exe`, no installer, no database server, no bloat.

[Русская версия](README.ru.md)

Skimmer looks like a classic file manager: folder tree on the left, thumbnails on the right.
While a folder is open it quietly builds thumbnails for *every* file in it, stores them in a
compact on-disk cache, and the next time you open that folder everything appears instantly.

## Features

- **Explorer-style layout** – folder tree + thumbnail grid, clickable breadcrumb address bar (› lists subfolders, click the empty part to type a path), back / forward / up, drag & drop a folder or photo onto the window.
- **Subfolders as tiles** with a preview of the first photos inside; going *Up* selects the folder you came from.
- **Explorer-style selection** – Ctrl+click, Shift+click, Shift+arrows, Ctrl+A; `Del` sends to the Recycle Bin, `Shift+Del` deletes permanently (Windows asks). Works in the full-screen viewer too, handy for culling.
- **Live updates** – new, deleted, renamed or rewritten photos show up by themselves (a file still being copied gets its thumbnail once it is complete); scroll position and selection stay put.
- **Built for huge folders** – the grid is fully virtual, so 50,000 photos in one folder cost no more than 50.
- **Instant thumbnails from a persistent cache** – open a folder a second time and all previews are already there.
- **Background indexing** – while the app is running it can index folders you choose in *Settings* (with subfolders) and re-scan them periodically, so your cameras' / photo archive folders are always ready.
- **Full-screen viewer** – double-click or `Enter`. Neighbouring photos are pre-decoded, a cached thumbnail is shown immediately, so paging with the arrow keys feels instant.
- **EXIF orientation**, natural sorting (`img2` before `img10`), sort by name / date / size.
- **Portable** – settings (`Skimmer.settings.json`) and cache (`SkimmerCache\`) live next to `Skimmer.exe`. If that folder is read-only, `%LOCALAPPDATA%\Skimmer` is used.
- **Self-cleaning cache** – cache of folders you have not opened for N days (default 30) is deleted automatically, plus a total size cap (default 4 GB).
- **Self-update from GitHub Releases** – a link appears in the status bar when a new version is out; the new exe is downloaded, verified by SHA-256 and swapped in place.
- Dark / light theme (or follow Windows), Russian and English UI (follows Windows language, can be forced in Settings).

Supported formats: JPEG, PNG, BMP, GIF, TIFF, ICO, JPEG XR and – if the corresponding Windows codecs are installed – WebP and HEIC.

## Keys

| Where | Key | Action |
|---|---|---|
| Grid | arrows, `PgUp`/`PgDn`, `Home`/`End` | move selection |
| Grid | `Enter`, double click | open photo / open folder |
| Grid | Ctrl+click, Shift+click, `Shift`+arrows, `Ctrl+A` | multiple selection |
| Grid | `Del` / `Shift+Del` | to Recycle Bin / delete permanently |
| Grid | right click | open / show in Explorer / copy path / copy file |
| Anywhere | `F5` | refresh folder |
| Anywhere | `Alt+←` / `Alt+→` / `Alt+↑` | back / forward / up |
| Anywhere | `Ctrl+L` | focus address bar |
| Viewer | `→` `↓` `PgDn` `Space` / mouse wheel down | next photo |
| Viewer | `←` `↑` `PgUp` `Backspace` / mouse wheel up | previous photo |
| Viewer | `Home` / `End` | first / last photo |
| Viewer | `Z` or middle click | toggle 100 % (drag to pan) |
| Viewer | `I` | toggle info bar |
| Viewer | `Del` / `Shift+Del` | delete the current photo and go to the next one |
| Viewer | `Esc`, `Enter`, double click | close |

You can also start it as `Skimmer.exe "D:\Photos"` or `Skimmer.exe "D:\Photos\img.jpg"` (handy for *Open with*).

## Download

Grab `Skimmer.exe` from [**Releases**](../../releases) (or from the *Actions* artifacts of any commit).
It is fully self-contained (~60 MB): nothing to install. Windows 10 / 11, x64.

## Updates

Skimmer checks `github.com/D3vil787/Skimmer/releases/latest` a few seconds after start and every 6 hours
(can be turned off in *Settings*, there is also *Check now*). When a newer version exists, a link appears in
the status bar. *Update and restart* downloads the new `Skimmer.exe`,
checks it against `SHA256SUMS.txt` from the release, renames the running exe to `*.old`, puts the new one in its
place and restarts. Settings and cache are kept. If the program folder is read-only, the release page is opened instead.

## How the cache works

For every folder Skimmer keeps two files in `SkimmerCache\` (named by a hash of the folder path):

- `*.dat` – the thumbnails (256 px JPEG, quality 80) appended one after another,
- `*.idx` – a small index: file name, modification time, size, position in `.dat`.

On opening a folder the index is read into memory; a thumbnail is valid while the file's name,
modification time and size are unchanged, so edited or replaced photos are regenerated
automatically. Thumbnails of deleted files are dropped when they make up a large part of the
cache. Folders that have not been opened for the configured number of days are removed.

Thumbnails are decoded through Windows Imaging Component with DCT scaling, which is several
times faster than decoding the whole JPEG.

## Build

Requires the .NET 8 SDK on Windows.

```bat
build.bat
```

or

```bat
dotnet publish Skimmer.csproj -c Release -p:PublishSingleFile=true -o out
```

The result is `out\Skimmer.exe`.

**Releases** are automatic: every push to `main` is built by GitHub Actions, and when `<Version>` in `Skimmer.csproj`
has no matching `vX.Y.Z` tag yet, the workflow creates that release with `Skimmer.exe` and
`SHA256SUMS.txt`. So releasing = bumping the version.

## License

[MIT](LICENSE)
