# NZB Kestrel — Quick Start

## 1. Download and launch

Download the package for your system from https://github.com/TylerDC666/nzb-kestrel/releases/latest.

**Windows:** Extract the Windows ZIP completely and run `NZB Kestrel.exe`. Keep `NZB Kestrel.exe.config` beside it. Requires Windows 10 Creators Update or later / Windows 11 and .NET Framework 4.8 or newer.

**Linux x86-64:** Extract the Linux `.tar.gz` archive and run `nzb-kestrel/nzb-kestrel`. Keep the entire folder, including `_internal`. Python and Qt are included. Targets Debian 12 / Ubuntu 24.04 or newer (glibc 2.36+). See `README-Linux.md` in the package for desktop libraries and troubleshooting requirements.

## 2. Choose English

Use the language selector at the top of the window and choose **English**. Your choice is saved. German, Spanish, French, Russian and Arabic are also available.

## 3. Create an NZB

1. Paste a complete `nzblnk:` link into the link field, or click **Paste from clipboard**.
2. Click **Browse** to choose where the NZB should be saved.
3. Choose your downloader, or leave the default application selected. Enable **Send to downloader after saving** if desired.
4. Click **Create NZB**. The status and progress bar show searching, transfer and validation.
5. Open the resulting NZB in your downloader. If automatic opening is unavailable, import the saved file manually or save into the downloader's watched folder.

Progress refers to NZB creation. Your downloader handles the actual content download and shows its own progress. A separate Usenet account may be required by your downloader.

## 4. Open browser links with Kestrel

Click **Associate browser links** once. Keep the application in the same location afterwards. Confirm your browser's request to open NZB Kestrel if prompted. Browser links fill the input field; click **Create NZB** to start searching. On Linux, this requires `xdg-utils` and a compatible desktop environment.

## Troubleshooting

- **No result:** NZBIndex is the only supported search service; the post may not be indexed or complete.
- **Downloader does not open:** Select a downloader executable which accepts an NZB file path, configure your system's `.nzb` association, or import the file manually.
- **Linux does not start:** Check the system requirements and libraries in `README-Linux.md`. This package is for x86-64, not ARM.
- **Cancel:** Click **Cancel**. On Linux, an outstanding network read may take up to 20 seconds to stop.

The search key is sent to NZBIndex. Exported NZBs include an archive password when the link supplies one. Settings do not store a link or password history. Validation does not guarantee article availability on your Usenet server.

## Upgrading

Existing settings from the former NZB Hydra application are imported automatically on first launch if no NZB Kestrel settings exist. Click **Associate browser links** again to point browser links at NZB Kestrel.
