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
