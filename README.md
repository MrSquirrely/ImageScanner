# ImageScanner

## Overview
ImageScanner is a modular C# solution designed to efficiently scan directories for image files, extract metadata, and maintain an encrypted catalog of discovered assets. The application is separated into a core processing library (`ImageScanner.Core`) and an executable console interface (`ImageScanner.ConsoleApp`) to ensure clean architecture and code reusability.

## Features
* **Advanced Directory Scanning:** Utilizes `DirectoryScanner.cs` with `EnumerationOptions` and custom `HashSet` folder filters for fast, recursive directory traversal.
* **Metadata & Thumbnail Extraction:** Leverages `SixLabors.ImageSharp` to extract EXIF data and image resolutions, alongside automatic WebP thumbnail generation.
* **Encrypted Local Database:** Uses `LiteDB` for secure database connections, supporting bulk inserts and the automatic detection and cleanup of deleted or modified files.
* **Interactive Terminal UI:** Built with `Spectre.Console` (`AnsiConsole.Live`), featuring rainbow ASCII titles, live progress bars, WIP warning panels, and custom non-blocking menus with arrow navigation and idle blinking highlights.
* **Retro Audio Feedback:** Integrates asynchronous retro sound effects (`SoundEffects.cs`) for application startup, menu navigation, task completion, and error handling.
* **Secure Vault:** Manages credentials and sensitive configuration data securely within the `SecureVault.cs` architecture.

## Project Structure
The repository is managed via the `ImageScanner.slnx` solution file and is divided into two primary sub-projects:

### 1. ImageScanner.Core
The C# class library containing the foundational business logic.
* `DirectoryScanner.cs`: Core engine for recursive directory parsing and filtering.
* `ImageRecord.cs`: Entity definition for scanned images, metadata, and EXIF properties.

### 2. ImageScanner.ConsoleApp
The terminal-based user interface and application entry point.
* `Program.cs`: The main execution sequence and UI rendering.
* `SecureVault.cs`: Security and credential management.
* `SoundEffects.cs`: Asynchronous audio feedback system.

### 3. PixShelf (`ImageScanner.Organizer`, WPF + HandyControls)
**PixShelf** is a desktop image organizer built on the `ImageScanner.Core.Library` catalog, using the [HandyControls](https://www.nuget.org/packages/HandyControls) control library. Import, Rescan, AI tag and the Tools menu sit in the title bar, with Settings at its right.
* **Import** a folder or a whole drive. Rescans skip unchanged files, refresh changed ones, drop deleted ones and never lose your tags.
* **Search** with free text plus filters: `tag:beach -tag:people artist:"stanley lau" cat:Wallpapers comic:"Space Saga" ext:png folder:Downloads rating>=4 is:fav is:untagged is:pending is:comic is:noartist`. The search box suggests completions for the word you're typing: tags, artists, categories and the `is:` keywords.
* **Tags, artists and categories** with rename/merge, bulk editing of multiple selected images, favorites and 0–5 star ratings. Artists have their own sidebar section and their own chips in the details panel.
* **Comics**: folders of numbered pages (or folders named "Chapter 3", "Vol. 2", "#12"…) are grouped automatically; you can also group any selection by hand. The reader supports two-page spreads, right-to-left order, fit-to-width and page reordering. **Convert to category** (a comic's right-click menu, or the button next to *Read* on a page) puts every page in a new or existing category and removes the comic grouping unless you choose to keep it.
* **AI tag suggestions** from a local vision model or Claude, on import or on demand. Suggestions are queued under *Needs AI review* where you accept them per tag or all at once, or they can be applied automatically.
* **Artist detection**: file names from Danbooru/Gelbooru (`…_drawn_by_<artist>__…`) and DeviantArt (`…_by_<artist>_d1a2b3c`) give the artist directly. Otherwise the AI looks for a signature, watermark or credit in the image; it doesn't guess from style. Detected artists are suggestions like tags.
* **Settings** are a PropertyGrid grouped by category; select a row to see what it does.
* **Plugins**: see below.

Run it with `dotnet run --project ImageScanner.Organizer`. On first launch a setup wizard picks the theme, library location, AI tagging, how images open (built-in viewer or the Windows default app) and which plugins to enable; rerun it from Settings or with `--first-run`. Pass `--library <file.db>` to open a different catalog for one session. Importing a whole drive asks for confirmation first, with an extra warning for the Windows system drive. Settings, the default library and per-plugin data live in `%LOCALAPPDATA%\ImageScanner\Organizer`.

To build a release that runs without .NET installed, run `dotnet publish ImageScanner.Organizer -c Release -r win-x64 --self-contained -o publish\PixShelf` (with `sixlabors.lic` in the repository root). The bundled plugins are copied into `publish\PixShelf\Plugins`, and their hashes are compiled in, so they still show as official.

AI tagging comes from plugins; two ship with the app and are chosen in the setup wizard or under **Settings → General → AI tagging**:

* **Local AI tagging** (`ImageScanner.Plugins.LocalTagger`, the default): runs a vision model on your PC through [KoboldCpp](https://github.com/LostRuins/koboldcpp), following the approach of [ImageIndexer](https://github.com/jabberjabberjabber/ImageIndexer). Free and private. Start KoboldCpp with a vision model (for example a Qwen 3.5 or Gemma 3 GGUF plus its mmproj vision projector; without the mmproj the model can't see images, and the plugin stops with an explanation instead of making up descriptions), or set the path to `koboldcpp.exe` and a `.kcpps` config or a `.gguf` model (its mmproj is picked up from the same folder if you don't choose one) in the plugin's settings so it starts on demand and stops when the app closes. Any OpenAI-compatible vision server also works (llama.cpp, LM Studio, Ollama at `http://localhost:11434`). Each image gets a tag prompt ("Write a list of booru-style tags for this image.", with one-click presets for e621 and Danbooru tags) and optionally a caption prompt; both are editable in the plugin's settings. Tags are cleaned up (underscores become spaces, at most four words, "x and y" split, optional singularizing).
* **Claude AI tagging** (`ImageScanner.Plugins.Claude`): cloud tagging with Anthropic's Claude. Add an API key in the plugin's settings (stored encrypted with Windows DPAPI) or set `ANTHROPIC_API_KEY`. Each image is one paid request.

Two more plugins are download only: install them from **Settings → Marketplace** (see below). Their projects set `<PluginDistribution>Marketplace</PluginDistribution>`, so they are built with the solution but not shipped inside the app.

* **Duplicate finder** (`ImageScanner.Plugins.Duplicates`): *Tools → Find duplicate images…* (or right-click → *Find duplicates of this image*) fingerprints the library with [DuplaImage.Lib](https://github.com/MrSquirrely/DuplaImage.Lib)'s perceptual hashes (difference, DCT/pHash, median or average) and groups images above a similarity threshold, so resized, re-saved and lightly edited copies are found too. Tick the copies to remove, or use *Select all but the best* (the most pixels, then the biggest file). Selected files go to the Recycle Bin and leave the library. Hashes are cached, so later scans only read new or changed files.
* **Batch converter** (`ImageScanner.Plugins.Converter`): right-click → *Convert images…* (or *Tools → Batch convert images…*) converts the selected images to AVIF (default), WebP, JPEG XL, JPEG or PNG with [Magick.NET](https://github.com/dlemstra/Magick.NET). Choose the quality and where to save (a `converted` folder next to each image by default; originals are never changed). When it's done it lists each file's original and new size and the total saved, for example "756 KB → 288 KB · 468 KB saved (62% smaller)". *Only keep files that got smaller* discards conversions that didn't help.


### 4. Plugins
Reference `ImageScanner.Plugins.Abstractions` and implement `IPlugin`. In `Initialize(IPluginHost host)` register any of:

| Extension point | Shows up as |
|---|---|
| `ITagSuggestionProvider` | A tagging engine choice in Settings → General |
| `IImageAction` | An item in the gallery's right-click menu |
| `IImportProcessor` | A step that runs on every newly imported image |
| `IPluginCommand` | An item in the Tools menu |

Implement `IConfigurablePlugin` to get a generated settings dialog: a PropertyGrid with one row per setting (text, multi-line text with preset buttons, secret, number, toggle, choice, file or folder with *Browse…*), grouped by each setting's optional `Category`. Plugins may target `net10.0-windows` and show their own WPF windows; they pick up the app's theme, and HandyControls should be referenced compile-only (`ExcludeAssets="runtime"`) because the app provides it. A tagging plugin should also implement `ITagProviderPlugin` so the setup wizard can offer it, and throw `TagProviderException` (with `isConfigurationError: true` for problems like a bad key or a stopped server, which stop the batch). Plugins get the live `ImageLibrary`, the current selection, notifications and a private data folder through `IPluginHost`.

Install a plugin by copying its build output to `Plugins\<PluginName>\<PluginName>.dll` next to the app or under `%LOCALAPPDATA%\ImageScanner\Organizer\Plugins`. Each plugin loads in its own `AssemblyLoadContext`, so it can ship its own dependencies. The bundled plugins are working examples. A plugin project imports `build\Plugin.targets`, which references the contracts and copies the build output (with its dependencies) into the app's Plugins folder; add a `ProjectReference` with `ReferenceOutputAssembly="false"` from the app so it is built with it.

**Official vs third-party.** Plugin cards show an *Official* badge for plugins made by the PixShelf team and *Third-party* for everything else. A build step (`GenerateOfficialPluginHashes` in `ImageScanner.Organizer.csproj`) compiles the SHA-256 of each bundled plugin's DLL into the app, and a plugin counts as official only if its file matches one of those hashes. The name or author a plugin reports makes no difference, and a modified official DLL shows as third-party. Build the app after changing a bundled plugin so its new hash is recorded.

**Marketplace.** *Settings → Marketplace* lists plugins shared in this repository's [GitHub Discussions](https://github.com/MrSquirrely/ImageScanner/discussions). Every Discussions category whose name ends in "Plugins" is a marketplace category and shows up as a filter:

| Category | Slug | For |
|---|---|---|
| 🤖 AI Tagging Plugins | `ai-tagging-plugins` | `ITagSuggestionProvider` |
| 🖼️ Image Action Plugins | `image-action-plugins` | `IImageAction` |
| 📥 Import Plugins | `import-plugins` | `IImportProcessor` |
| 🧰 Tool Plugins | `tool-plugins` | `IPluginCommand` |
| 🧩 Other Plugins | `other-plugins` | anything else |

Each category has a form in `.github/DISCUSSION_TEMPLATE/<slug>.yml` that asks for the plugin's name, ID, version, description, download URL (an HTTPS link to a .zip, such as a GitHub release asset), the zip's SHA-256 and optional source link. The app reads those fields, so keep the labels unchanged. To publish an update, edit the post's version, link and hash.

Installing downloads the zip, refuses it unless the SHA-256 matches the post, and stages it in `%LOCALAPPDATA%\ImageScanner\Organizer\Plugins.pending`; it moves into `Plugins` the next time PixShelf starts, because loaded plugins keep their DLLs locked. A newer version in the post shows as *Update* (this also works for the included tagging plugins: when the same plugin is installed twice, the higher version is loaded). Posts by the repository owner, members and collaborators carry a *Team* badge, and plugins installed from them count as official. The app reads Discussions anonymously through the GitHub API (60 requests an hour) and caches the list for 10 minutes.

**Uninstalling.** Downloaded plugins have an *Uninstall* button on their card (Settings → Plugins) and in the Marketplace. The plugin keeps running until PixShelf restarts, then its folder is deleted; *Undo* takes it back until then. Its settings are kept for a later reinstall. The included tagging plugins can't be uninstalled, only switched off.

**Publishing the PixShelf plugins.** Every plugin project builds a zip with `dotnet build -c Release` (in `artifacts\plugins`). `tools\Publish-Plugins.ps1` builds them, uploads the zips to a GitHub release and creates or updates one discussion per plugin from `tools\marketplace-plugins.json` (name, ID, version, category, description). It needs the GitHub CLI signed in (`winget install GitHub.cli`, then `gh auth login`) and the Discussions categories above; `-DryRun` builds the zips and prints the posts without touching GitHub. Bump the version in that file and in the plugin's `Version` before publishing an update.

A tagging plugin can return `TagSuggestion.Artists`; `TagSuggestionRequest.KnownArtist` carries the artist from the file name when the app already knows it, so the plugin can skip its own detection.

## Getting Started
To run the application locally, ensure you have the .NET SDK installed for your environment.

1. Clone the repository to your local machine.
2. Navigate to the root directory containing `ImageScanner.slnx`.
3. Build the solution using the .NET CLI:
   ```bash
   dotnet build
   ```
4. Execute the console application:
   ```bash
   dotnet run --project ImageScanner.ConsoleApp/ImageScanner.ConsoleApp.csproj
   ```

## License
This project is governed by the terms outlined in the `LICENSE.txt` file located in the root directory.
