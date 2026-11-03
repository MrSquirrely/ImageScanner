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

### 3. ImageScanner.Organizer (WPF + HandyControl)
A desktop image organizer built on the `ImageScanner.Core.Library` catalog.
* **Import** a folder or a whole drive. Rescans skip unchanged files, refresh changed ones, drop deleted ones and never lose your tags.
* **Search** with free text plus filters: `tag:beach -tag:people cat:Wallpapers comic:"Space Saga" ext:png folder:Downloads rating>=4 is:fav is:untagged is:pending is:comic`.
* **Tags and categories** with rename/merge, bulk editing of multiple selected images, favorites and 0–5 star ratings.
* **Comics**: folders of numbered pages (or folders named "Chapter 3", "Vol. 2", "#12"…) are grouped automatically; you can also group any selection by hand. The reader supports two-page spreads, right-to-left order, fit-to-width and page reordering.
* **AI tag suggestions** from a local vision model or Claude, on import or on demand. Suggestions are queued under *Needs AI review* where you accept them per tag or all at once, or they can be applied automatically.
* **Plugins**: see below.

Run it with `dotnet run --project ImageScanner.Organizer`. On first launch a setup wizard picks the theme, library location, AI tagging, how images open (built-in viewer or the Windows default app) and which plugins to enable; rerun it from Settings or with `--first-run`. Pass `--library <file.db>` to open a different catalog for one session. Importing a whole drive asks for confirmation first, with an extra warning for the Windows system drive. Settings, the default library and per-plugin data live in `%LOCALAPPDATA%\ImageScanner\Organizer`.

AI tagging comes from plugins; two ship with the app and are chosen in the setup wizard or under **Settings → AI tagging**:

* **Local AI tagging** (`ImageScanner.Plugins.LocalTagger`, the default): runs a vision model on your PC through [KoboldCpp](https://github.com/LostRuins/koboldcpp), following the approach of [ImageIndexer](https://github.com/jabberjabberjabber/ImageIndexer). Free and private. Start KoboldCpp with a vision model (for example a Qwen 3.5 or Gemma 3 GGUF plus its mmproj vision projector; without the mmproj the model can't see images, and the plugin stops with an explanation instead of making up descriptions), or set the path to `koboldcpp.exe` and a `.kcpps` config or a `.gguf` model (its mmproj is picked up from the same folder if you don't choose one) in the plugin's settings so it starts on demand and stops when the app closes. Any OpenAI-compatible vision server also works (llama.cpp, LM Studio, Ollama at `http://localhost:11434`). Each image gets a tag prompt ("Write a list of booru-style tags for this image.", with one-click presets for e621 and Danbooru tags) and optionally a caption prompt; both are editable in the plugin's settings. Tags are cleaned up (underscores become spaces, at most four words, "x and y" split, optional singularizing).
* **Claude AI tagging** (`ImageScanner.Plugins.Claude`): cloud tagging with Anthropic's Claude. Add an API key in the plugin's settings (stored encrypted with Windows DPAPI) or set `ANTHROPIC_API_KEY`. Each image is one paid request.


### 4. Plugins
Reference `ImageScanner.Plugins.Abstractions` and implement `IPlugin`. In `Initialize(IPluginHost host)` register any of:

| Extension point | Shows up as |
|---|---|
| `ITagSuggestionProvider` | An AI provider choice in Settings → AI tagging |
| `IImageAction` | An item in the gallery's right-click menu |
| `IImportProcessor` | A step that runs on every newly imported image |
| `IPluginCommand` | An item in the Tools menu |

Implement `IConfigurablePlugin` to get a generated settings form (text, secret, number, toggle, choice). A tagging plugin should also implement `ITagProviderPlugin` so the setup wizard can offer it, and throw `TagProviderException` (with `isConfigurationError: true` for problems like a bad key or a stopped server, which stop the batch). Plugins get the live `ImageLibrary`, the current selection, notifications and a private data folder through `IPluginHost`.

Install a plugin by copying its build output to `Plugins\<PluginName>\<PluginName>.dll` next to the app or under `%LOCALAPPDATA%\ImageScanner\Organizer\Plugins`. Each plugin loads in its own `AssemblyLoadContext`, so it can ship its own dependencies. The bundled plugins are working examples. A plugin project imports `buildPlugin.targets`, which references the contracts and copies the build output (with its dependencies) into the app's Plugins folder; add a `ProjectReference` with `ReferenceOutputAssembly="false"` from the app so it is built with it.

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
