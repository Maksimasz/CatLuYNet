# CatLu YNet

[Русский](README.md) | [English](README.en.md) | [Lietuvių](README.lt.md) | [עברית](README.he.md)

<img src="Assets/CatLuNet2.png" alt="CatLu YNet" width="96">

**YouTube audio and local MP3 files in one Windows player.**

CatLu YNet plays audio from YouTube videos and playlists without video or a browser. Create your own playlists and optionally save music to your disk.

## Download

**[Latest release: Windows installer and portable ZIP](https://github.com/Maksimasz/CatLuYNet/releases/latest)**

- Windows x64 is required. Release packages include .NET and the MP3 tools.
- To install, download and run `CatLuYNet-…-Setup.exe`.
- For the portable version, extract the entire ZIP and run `YouTubeRadio.exe`. Keep the archive's folder structure intact.

## Features

- Add individual YouTube videos or entire playlists.
- Create, rename and edit personal playlists; remove and reorder tracks.
- Import and export one selected playlist as JSON independently of other playlists.
- Optionally download one track or an entire playlist as MP3.
- Choose the download folder and MP3 quality; the default is best-quality VBR.
- Local files take priority during playback and crossfade. If a file is missing, the player uses YouTube.
- Previous, play, pause, stop, next and volume controls.
- Themes, Russian, English, Hebrew and Lithuanian interfaces, clock, track information and a mini player.
- Update checks at startup and every 24 hours.

## Getting started

1. Paste a YouTube link into the top field and save it. Select a personal playlist for a single video, or name the new playlist when adding a YouTube playlist.
2. Click a track to play it. Use `✎` to edit a playlist and `⚙` to open settings.
3. For a local copy, enable the MP3 download checkbox when adding a link, or use the download buttons in the playlist editor.
4. Choose a destination. A subfolder named after the playlist is created there. The download window shows the full path, and the chosen location is remembered.
5. Set MP3 quality under `⚙`: best-quality VBR, 320, 256, 192 or 128 kbps.

Existing downloads are not overwritten. Quality changes apply to new downloads; the result is limited by the YouTube source quality. Cancelling keeps completed MP3 files. YouTube playback and downloads require internet access; saved MP3 files can be played offline. Unavailable videos cannot be downloaded.

## Playlists and data

Export one selected playlist to JSON from settings. Import adds it as a separate playlist, even if another has the same name. Audio files are not included in JSON. A link containing `v=…` adds one video, even if it also contains `list` and `index`.

Data is stored in `%LOCALAPPDATA%\CatLuYNet`. Backup files with the `.bak` extension are kept beside the library and settings files. Removing a track from a playlist does not delete its downloaded MP3.

## Development

[Running from source, checks and installer builds — Russian](docs/BUILD.md).

Application code is available under the [MIT license](LICENSE). Third-party components retain their own licenses.
