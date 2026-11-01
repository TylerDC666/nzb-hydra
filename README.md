<p align="center"><img src="assets/hydra.png" width="170" alt="NZB Hydra"></p>

# NZB Hydra

A Windows and Linux app that turns NZBLNK links into validated NZB files, with a black-and-red interface, live progress, and support for your preferred NZB downloader.

## Features

- Paste NZBLNK links or open them directly from your browser.
- Independent NZBIndex integration, without a Monkey Go dependency.
- Validate subjects, file counts, segment numbers, missing segments, and duplicates.
- Export standard NZB files with title and optional archive-password metadata.
- Live status, progress by processing stage, and cancellation.
- Open results with the Windows default application or a selected executable.
- Save files for manual import or use a downloader's watched folder.
- Per-Monitor V2 DPI support on Windows; native Qt scaling on Linux.
- Six interface languages, including Arabic right-to-left layout.

## Windows requirements

Windows 10 Creators Update or later, or Windows 11, with .NET Framework 4.8 or newer. Searching requires an internet connection. Actual content downloads are handled by a separate NZB downloader and its Usenet connection.

Choose German, English, Spanish, French, Russian or Arabic from the language selector. The choice is saved, and Arabic uses a right-to-left layout. The Windows build and native Qt Linux build are provided separately. See [Linux instructions](linux/README-Linux.md).

## Getting started

See the [English Quick Start Guide](QUICKSTART.md) for Windows and Linux.

1. Download the Windows ZIP from **Releases** and extract it completely.
2. Run `NZB Hydra.exe`. **Keep `NZB Hydra.exe.config` in the same directory.**
3. Paste an NZBLNK link and click **NZB erstellen** (Create NZB).

Click **Browser-Links zuordnen** (Associate browser links) to register the application for `nzblnk:` links in your Windows account. Keep the app in that location after registration. Your browser may ask for permission to open the app. Browser links populate the input field; searching starts when you click **NZB erstellen**.

## Downloader compatibility

**Windows-Standardprogramm** uses the existing Windows `.nzb` file association. **Downloader auswählen** (Choose downloader) passes the NZB path as a single argument to your selected executable. The downloader must support this invocation.

For other NZB-compatible downloaders, disable automatic opening and import the saved NZB manually, or select a watched folder as the destination. Remote control through SABnzbd, NZBGet, or cloud APIs is not implemented.

## Progress

The search stage displays an activity bar. NZB transfer shows a percentage when the response size is known. Validation shows the proportion of files checked. Percentages apply to the current stage, not to the subsequent content download.

## Limitations

NZBIndex is currently the only supported indexer. Direct NNTP searching is not implemented. Posts must be indexed by NZBIndex, and changes to its public web interface may require application updates. Validation does not guarantee that every article is available on a particular Usenet server.

## Local data and network requests

The search key from an NZBLNK link is sent to `nzbindex.com`. The application retrieves the corresponding NZB and processes it locally. It does not download the underlying content from Usenet.

Settings are stored in `%LOCALAPPDATA%\NZBHydra\settings.xml`. The local `start-diagnose.txt` startup log records timestamps, process IDs, argument counts, startup/shutdown status, and exception types when applicable. It does not record full links or passwords. Archive passwords are included in exported NZB metadata.

## Building from source

Run this in PowerShell from the repository directory:

```powershell
.\source\build.ps1
```

The build uses the .NET Framework C# compiler at `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. No NuGet packages are required. The executable and its configuration file are written to `bin`.

Run the offline self-tests:

```powershell
$report = Join-Path $env:TEMP 'NZB-Hydra-Test.txt'
Start-Process -FilePath '.\bin\NZB Hydra.exe' -ArgumentList ('--self-test "' + $report + '"') -Wait
Get-Content $report
```

Tests cover six-language controls, Arabic layout, link parsing, result selection, XML namespaces, segment gaps, metadata, safe filenames, collision-free saving, and cancellation during saving. They do not replace live testing of the external search service.

## Project structure

```text
assets/             Logo and Windows icon
source/NZBHydra.cs   Interface, link parsing, and application entry point
source/Engine.cs     HTTP search, NZB validation, and export
source/Theme.cs      Styling and assembly metadata
source/Localization.cs Six-language Windows translations
linux/              Linux application, tests, and container build
source/Tests.cs      Offline self-tests
source/app.config   Runtime and DPI configuration
source/app.manifest  Windows compatibility manifest
source/build.ps1    Build script
ANLEITUNG.txt        German quick-start guide
QUICKSTART.md        English quick-start guide
```

## Project status

Public repository. No project license is currently granted. The logo was created for this project using AI image generation.
