# NZB Kestrel for Linux

Native Qt interface with German, English, Spanish, French, Russian and Arabic (right-to-left layout). Independent NZBIndex search, validation, standard NZB export, progress, cancellation and downloader handoff.

See [English Quick Start](QUICKSTART.md).

## Portable Linux x86-64 build

Extract `NZB-Kestrel-v2.1.0-Linux-x86_64.tar.gz` into a permanent folder, then run `nzb-kestrel/nzb-kestrel`. Keep the entire folder together, including its hidden `_internal` resources. Python and Qt are included. No administrator installation is needed.

Targets Debian 12 / Ubuntu 24.04 or newer on x86-64 (glibc 2.36+). ARM and older distributions need their own build. A graphical desktop and its standard Qt/X11 or Wayland system libraries are required. Minimal distributions may need `libegl1`, `libgl1`, `libxkbcommon-x11-0`, `libxcb-cursor0`, `libxcb-icccm4`, `libxcb-keysyms1`, `libxcb-shape0`, `libxcb-xinerama0`, `libxcb-randr0`, `libxcb-render-util0`, `libxcb-image0`, `libfontconfig1` and `xdg-utils`. Noto fonts are recommended for all six scripts.

Select the language at the top. Paste a link and select **Create NZB**. **Associate browser links** creates a per-user desktop entry and registers `nzblnk:` with `xdg-mime`; keep the application folder in place afterwards. Browser permission prompts and desktop integration depend on your desktop/browser. Each external link opens a new window. Searching starts only after you click the button.

The default downloader uses your desktop's `.nzb` association. Alternatively choose an executable which accepts the NZB path as an argument, or disable automatic opening and import the saved file yourself. Downloader remote-control APIs are not included. The default `.nzb` association is not changed.

Settings live in `$XDG_CONFIG_HOME/nzb-kestrel/settings.json` (normally `~/.config/nzb-kestrel/settings.json`). No links or passwords are saved in settings. Exported NZBs include the archive password when supplied. The NZBLNK search key is sent to NZBIndex. Only NZB metadata is downloaded; your downloader handles content downloads.

Cancellation interrupts processing between network reads; an outstanding read can take up to 20 seconds. Each search has a three-minute deadline. Atomic export requires a destination filesystem supporting hard links (normal Linux filesystems do; some network or FAT filesystems do not). Existing NZBs are never overwritten.

## Build / run from source

From `linux/`, with Python 3.10+:

```sh
python3 -m venv .venv
. .venv/bin/activate
pip install -r requirements.txt
python kestrel.py
QT_QPA_PLATFORM=offscreen python -m unittest -v test_engine test_ui
pyinstaller --noconfirm --name nzb-kestrel --onedir --add-data 'translations.json:.' --add-data 'kestrel.png:.' kestrel.py
```

Or use the included Dockerfile from the repository root:

```sh
docker build -t nzb-kestrel-linux linux
mkdir -p release
docker run --rm -v "$PWD/release:/out" nzb-kestrel-linux
```

The container tests run without accessing the live search service. Desktop/browser handoff must also be checked on the target desktop. The application source is publicly available with no project license granted; see THIRD-PARTY-NOTICES.txt for bundled dependencies.
