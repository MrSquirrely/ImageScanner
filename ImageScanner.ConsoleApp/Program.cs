using System.Diagnostics;

using ImageScanner.Core;

using Spectre.Console;
// ReSharper disable AccessToModifiedClosure
// ReSharper disable RedundantVerbatimStringPrefix
// ReSharper disable SwitchStatementMissingSomeEnumCasesNoDefault

namespace ImageScanner.ConsoleApp;
/// <summary>
/// The main entry point for the Image Scanner console application. This class handles user interaction, configuration, and orchestrates the scanning process.
/// </summary>
internal class Program {
    private static void Main() {
        SoundEffects.PlayStartup();
        PlayIntroAnimation();

        // --- DISCLAIMER PHASE ---
        AnsiConsole.Clear();
        DrawTitle();

        Panel warningPanel = new(
		        "[bold yellow]WORK IN PROGRESS[/]\n" +
		        "[dim]This tool currently extracts and saves EXIF metadata automatically.\n" +
		        "In future updates, this feature may become configurable or be removed entirely.[/]"
	        ) {
		        Border = BoxBorder.Rounded,
		        BorderStyle = Color.Yellow,
		        Padding = new Padding(1, 1, 1, 1)
	        };
        AnsiConsole.Write(warningPanel);

        bool acceptTerms = RunConfirmPrompt("Do you acknowledge this is a Work In Progress and wish to continue?");

        if (!acceptTerms) {
	        SoundEffects.PlayExit();
            AnsiConsole.MarkupLine("\n[red]Initialization cancelled. Exiting Image Scanner.[/]");
	        return; // Instantly exits the application
        }

        // --- WIZARD SETUP PHASE ---
        DrawHeader("", "", "", "");
        string folderToScan = AnsiConsole.Ask<string>("Enter the [green]folder path[/] to scan:");

        DrawHeader(folderToScan, "", "", "");
        AnsiConsole.MarkupLine("[dim italic]Note: Scanning tens of thousands of images may take several minutes.[/]\n");
        string dbPath = AnsiConsole.Ask<string>("Enter the [green]database name[/] (default: ImageDatabase):", "ImageDatabase");

        DrawHeader(folderToScan, dbPath, "", "");
        string pwdStatus;
        string? dbPassword = SecureVault.LoadPassword();

        if (string.IsNullOrEmpty(dbPassword)) {
            dbPassword = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter a [green]password[/] to encrypt the database (leave blank for none):")
                    .AllowEmpty()
                    .Secret());

            if (!string.IsNullOrWhiteSpace(dbPassword)) {
                SecureVault.SavePassword(dbPassword);
                pwdStatus = "Encrypted (New Password Saved)";
            }
            else {
                pwdStatus = "Unencrypted (None)";
            }
        }
        else {
            pwdStatus = "Encrypted (Loaded Securely)";
        }

        DrawHeader(folderToScan, dbPath, pwdStatus, "");
        const string defaultIgnored = "node_modules, .git, obj, bin, AppData, Windows, $Recycle.Bin, System Volume Information";
        string ignoredInput = AnsiConsole.Ask("Enter [green]folders to ignore[/] (comma-separated):", defaultIgnored);

        IEnumerable<string> ignoredFolders = ignoredInput
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => folder.Trim());

        DirectoryScanner scanner = new(dbPath, folderToScan, ignoredFolders, dbPassword);

        // --- DASHBOARD MENU LOOP ---
        bool exitRequested = false;
        while (!exitRequested) {
            DrawHeader(folderToScan, dbPath, pwdStatus, ignoredInput);
			
            string[] menuOptions = [
	            "Run Fast Scan (Metadata Only)",
	            "Run Deep Scan (Generate Thumbnails)",
	            "View Database Stats",
	            "Exit"
            ];

            // Use the custom interactive menu
            string choice = RunCustomMenu(menuOptions);

            switch (choice) {
                case "Run Fast Scan (Metadata Only)":
	                SoundEffects.PlaySelect();
                    ExecuteScan(scanner, folderToScan, generateThumbnails: false);
                    break;

                case "Run Deep Scan (Generate Thumbnails)":
	                SoundEffects.PlaySelect();
                    ExecuteScan(scanner, folderToScan, generateThumbnails: true);
                    break;

                case "View Database Stats":
	                SoundEffects.PlaySelect();
                    (int TotalImages, int TotalThumbnails) stats = scanner.GetStats();
                    Table liveTable = new Table().Border(TableBorder.Rounded);
                    liveTable.AddColumn("Metric");
                    liveTable.AddColumn("Count");

                    AnsiConsole.Live(liveTable)
                        .Start(ctx => {
	                        const int animationSteps = 25;
	                        for (int step = 0; step <= animationSteps; step++) {
                                float progress = (float)step / animationSteps;
                                float easedProgress = 1.0f - ((1.0f - progress) * (1.0f - progress));

                                int currentImages = (int)(stats.TotalImages * easedProgress);
                                int currentThumbnails = (int)(stats.TotalThumbnails * easedProgress);

                                Table updatedTable = new Table().Border(TableBorder.Rounded);
                                updatedTable.AddColumn("Metric");
                                updatedTable.AddColumn("Count");
                                updatedTable.AddRow("Total Images Indexed", $"[green]{currentImages:N0}[/]");
                                updatedTable.AddRow("Images with Thumbnails", $"[cyan]{currentThumbnails:N0}[/]");

                                ctx.UpdateTarget(updatedTable);
                                 Thread.Sleep(30);
                            }
                        });

	                // Pause so the UI doesn't wipe immediately
	                AnsiConsole.MarkupLine("\nPress [yellow]any key[/] to return to the menu...");
	                while (Console.KeyAvailable) {
		                Console.ReadKey(true);
	                }
	                Console.ReadKey(true);
	                break;

                case "Exit":
	                SoundEffects.PlaySelect(); // Play the selection sound for picking "Exit"
	                bool confirmExit = RunConfirmPrompt("Are you sure you want to exit Image Scanner?");
	                if (confirmExit) {
		                AnsiConsole.MarkupLine("\n[yellow]Exiting Image Scanner. Goodbye![/]");
		                SoundEffects.PlayExit();
                        exitRequested = true;
	                }
	                break;
            }
        }
    }

    // --- HELPER METHODS ---
    /// <summary>
    /// Draws the ASCII art title of the application with color formatting using Spectre.Console.
    /// </summary>
    private static void DrawTitle() {
	    AnsiConsole.MarkupLine(@"[bold red]  ___                             ____                                 [/]");
	    AnsiConsole.MarkupLine(@"[bold yellow] |_ _|_ __ ___   __ _  __ _  ___ / ___|  ___ __ _ _ __  _ __   ___ _ __ [/]");
	    AnsiConsole.MarkupLine(@"[bold green]  | || '_ ` _ \ / _` |/ _` |/ _ \\___ \ / __/ _` | '_ \| '_ \ / _ \ '__|[/]");
	    AnsiConsole.MarkupLine(@"[bold cyan]  | || | | | | | (_| | (_| |  __/ ___) | (_| (_| | | | | | | |  __/ |   [/]");
	    AnsiConsole.MarkupLine(@"[bold blue] |___|_| |_| |_|\__,_|\__, |\___||____/ \___\__,_|_| |_|_| |_|\___|_|   [/]");
	    AnsiConsole.MarkupLine(@"[bold magenta]                      |___/                                             [/]");
	    Console.WriteLine();
    }
    /// <summary>
    /// Draws the header section of the console application, including the title and a configuration table if applicable.
    /// </summary>
    /// <param name="folder">The target folder path.</param>
    /// <param name="db">The database name.</param>
    /// <param name="pwdStatus">The password status.</param>
    /// <param name="ignored">The list of ignored folders.</param>
    private static void DrawHeader(string folder, string db, string pwdStatus, string ignored) {
	    AnsiConsole.Clear();
	    DrawTitle();

	    // Only draw the Configuration Table if we have started filling it out
	    if (string.IsNullOrEmpty(folder)) {
		    return;
	    }

	    Table table = new Table().Border(TableBorder.Rounded).Title("[bold cyan]Current Configuration[/]");
	    table.AddColumn("Setting");
	    table.AddColumn("Value");

	    table.AddRow("Target Folder", $"[yellow]{folder}[/]");

	    if (!string.IsNullOrEmpty(db))
		    table.AddRow("Database Name", $"[yellow]{db}[/]");

	    if (!string.IsNullOrEmpty(pwdStatus))
		    table.AddRow("Database Security", $"[yellow]{pwdStatus}[/]");

	    if (!string.IsNullOrEmpty(ignored))
		    table.AddRow("Ignored Folders", $"[yellow]{ignored}[/]");

	    AnsiConsole.Write(table);
	    Console.WriteLine();
    }
    /// <summary>
    /// Plays an animated introduction sequence for the application title using color transitions and fade-in effects.
    /// </summary>
    private static void PlayIntroAnimation() {
        string[] titleLines = [
            @"  ___                             ____                                 ",
            @" |_ _|_ __ ___   __ _  __ _  ___ / ___|  ___ __ _ _ __  _ __   ___ _ __ ",
            @"  | || '_ ` _ \ / _` |/ _` |/ _ \\___ \ / __/ _` | '_ \| '_ \ / _ \ '__|",
            @"  | || | | | | | (_| | (_| |  __/ ___) | (_| (_| | | | | | | |  __/ |   ",
            @" |___|_| |_| |_|\__,_|\__, |\___||____/ \___\__,_|_| |_|_| |_|\___|_|   ",
            @"                      |___/                                             "
        ];

        Color[] baseColors = [Color.Red, Color.Yellow, Color.Green, Color.Cyan, Color.Blue, Color.Magenta];

        AnsiConsole.Live(new Markup(""))
            .Start(ctx => {
                const int totalFrames = 25;
                const int fadeFrames = 15;

                for (int frame = 0; frame < totalFrames; frame++) {
                    List<string> renderedLines = [];
                    float fadeFactor = Math.Min(1.0f, (float)frame / fadeFrames);

                    for (int i = 0; i < titleLines.Length; i++) {
                        Color baseColor = baseColors[(i + frame) % baseColors.Length];
                        byte r = (byte)(baseColor.R * fadeFactor);
                        byte g = (byte)(baseColor.G * fadeFactor);
                        byte b = (byte)(baseColor.B * fadeFactor);
                        string hexColor = $"#{r:X2}{g:X2}{b:X2}";

                        renderedLines.Add($"[{hexColor} bold]{titleLines[i]}[/]");
                    }

                    ctx.UpdateTarget(new Markup(string.Join("\n", renderedLines)));
                    Thread.Sleep(70);
                }
            });
    }
    /// <summary>
    /// Executes the scanning process using the provided DirectoryScanner instance, displaying progress and handling completion or errors.
    /// </summary>
    /// <param name="scanner">The directory scanner instance.</param>
    /// <param name="folderToScan">The folder to scan for images.</param>
    /// <param name="generateThumbnails">Indicates whether to generate thumbnails.</param>
    private static void ExecuteScan(DirectoryScanner scanner, string folderToScan, bool generateThumbnails) {
        Stopwatch stopwatch = new();
        try {
            stopwatch.Start();

            AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new ElapsedTimeColumn(), new SpinnerColumn())
                .Start(ctx => {
                    ProgressTask countTask = ctx.AddTask("[yellow]Calculating total files...[/]");
                    countTask.IsIndeterminate = true;

                    int totalFiles = scanner.CountImageFiles(folderToScan);
                    countTask.StopTask();

                    if (totalFiles == 0) {
                        AnsiConsole.MarkupLine("\n[yellow]No supported images found in the specified directory.[/]");
                        return;
                    }

                    string taskDescription = totalFiles > 10000
                        ? $"[green]Saving {totalFiles:N0} images...[/] [dim](This will take a while)[/]"
                        : $"[green]Saving {totalFiles:N0} images to LiteDB...[/]";

                    ProgressTask processTask = ctx.AddTask(taskDescription, maxValue: totalFiles);

                    IProgress<int> progress = new Progress<int>(currentCount => {
                        processTask.Value = currentCount;
                    });

                    scanner.ScanAndSave(progress, generateThumbnails);
                });

            stopwatch.Stop();

            SoundEffects.PlayComplete();

            AnsiConsole.MarkupLine("\n[bold green]✔ Scan Complete![/]");
            Table table = new();
            table.AddColumn("Metric"); table.AddColumn("Value");
            table.AddRow("Target Directory", $"[yellow]{folderToScan}[/]");
            table.AddRow("Time Elapsed", $"[yellow]{stopwatch.Elapsed.TotalSeconds:F2} seconds[/]");
            AnsiConsole.Write(table);
        }
        catch (Exception ex) {
	        SoundEffects.PlayError();
            AnsiConsole.MarkupLine($"\n[bold red]An error occurred:[/] {ex.Message}");
        }

        // Pause so the UI doesn't wipe immediately
        AnsiConsole.MarkupLine("\nPress [yellow]any key[/] to return to the menu...");
        while (Console.KeyAvailable) {
	        Console.ReadKey(true);
        }
        Console.ReadKey(true);
    }
    /// <summary>
    /// Runs a custom interactive menu in the console, allowing the user to navigate options using arrow keys and select an option with Enter. The selected option is returned as a string.
    /// </summary>
    /// <param name="options">The list of options to display.</param>
    /// <returns>The selected option as a string.</returns>
    private static string RunCustomMenu(string[] options) {
        int selectedIndex = 0;
        string finalSelection = string.Empty;

        Console.CursorVisible = false;
        AnsiConsole.MarkupLine("[bold cyan]What would you like to do?[/]\n");

        DateTime lastBlink = DateTime.Now;
        bool blinkState = true;

        // Use Spectre's Live context to handle the redraws without scrolling bugs
        AnsiConsole.Live(new Markup(""))
            .Start(ctx => {
                while (string.IsNullOrEmpty(finalSelection)) {
                    if ((DateTime.Now - lastBlink).TotalMilliseconds > 600) {
                        blinkState = !blinkState;
                        lastBlink = DateTime.Now;
                    }

                    // Build the menu in memory as a single string
                    List<string> menuLines = [];
                    for (int i = 0; i < options.Length; i++) {
                        string paddedOption = options[i].PadRight(40);

                        if (i == selectedIndex) {
                            menuLines.Add(blinkState ? $"[bold black on cyan] > {paddedOption} [/]" : $"[bold white on cyan] > {paddedOption} [/]");
                        }
                        else {
                            menuLines.Add($"   [dim]{paddedOption} [/]");
                        }
                    }

                    // Send the entire block to Spectre to redraw safely
                    ctx.UpdateTarget(new Markup(string.Join("\n", menuLines)));

                    if (Console.KeyAvailable) {
                        ConsoleKeyInfo keyInfo = Console.ReadKey(true);

                        blinkState = true;
                        lastBlink = DateTime.Now;

                        switch (keyInfo.Key) {
	                        case ConsoleKey.UpArrow: {
		                        SoundEffects.PlayMove();
		                        selectedIndex--;
		                        if (selectedIndex < 0) selectedIndex = options.Length - 1;
		                        break;
	                        }
	                        case ConsoleKey.DownArrow: {
		                        SoundEffects.PlayMove();
		                        selectedIndex++;
		                        if (selectedIndex >= options.Length) selectedIndex = 0;
		                        break;
	                        }
	                        case ConsoleKey.Enter:
		                        finalSelection = options[selectedIndex];
		                        break;
                        }
                    }
                    else {
                        Thread.Sleep(30);
                    }
                }
            });

        Console.CursorVisible = true;
        return finalSelection;
    }
    /// <summary>
    /// Runs a confirmation prompt in the console, allowing the user to select "Yes" or "No" using arrow keys and confirm with Enter. Returns true for "Yes" and false for "No".
    /// </summary>
    /// <param name="promptText">The text to display in the confirmation prompt.</param>
    /// <returns></returns>
    private static bool RunConfirmPrompt(string promptText) {
        string[] options = ["Yes", "No"];
        int selectedIndex = 1; // Default to "No" to prevent accidental exits
        bool? finalSelection = null;

        Console.CursorVisible = false;
        AnsiConsole.MarkupLine($"\n[bold yellow]{promptText}[/]\n");

        DateTime lastBlink = DateTime.Now;
        bool blinkState = true;

        AnsiConsole.Live(new Markup(""))
            .Start(ctx => {
                while (finalSelection == null) {
                    if ((DateTime.Now - lastBlink).TotalMilliseconds > 600) {
                        blinkState = !blinkState;
                        lastBlink = DateTime.Now;
                    }

                    List<string> menuLines = [];
                    for (int i = 0; i < options.Length; i++) {
                        string paddedOption = options[i].PadRight(20);

                        if (i == selectedIndex) {
	                        menuLines.Add(blinkState
		                                      ? $"[bold black on yellow] > {paddedOption} [/]"
		                                      : $"[bold white on yellow] > {paddedOption} [/]");
                        }
                        else {
                            menuLines.Add($"   [dim]{paddedOption} [/]");
                        }
                    }

                    ctx.UpdateTarget(new Markup(string.Join("\n", menuLines)));

                    if (Console.KeyAvailable) {
                        ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                        blinkState = true;
                        lastBlink = DateTime.Now;

                        switch (keyInfo.Key) {
	                        // Allow up/down OR left/right for quick Yes/No navigation
	                        case ConsoleKey.UpArrow:
	                        case ConsoleKey.LeftArrow: {
		                        SoundEffects.PlayMove();
		                        selectedIndex--;
		                        if (selectedIndex < 0) selectedIndex = options.Length - 1;
		                        break;
	                        }
	                        case ConsoleKey.DownArrow:
	                        case ConsoleKey.RightArrow: {
		                        SoundEffects.PlayMove();
		                        selectedIndex++;
		                        if (selectedIndex >= options.Length) selectedIndex = 0;
		                        break;
	                        }
	                        case ConsoleKey.Enter:
		                        SoundEffects.PlaySelect();
		                        finalSelection = (selectedIndex == 0); // 0 is "Yes", 1 is "No"
		                        break;
                        }
                    }
                    else {
                        Thread.Sleep(30);
                    }
                }
            });

        Console.CursorVisible = true;
        return (finalSelection != null) && finalSelection.Value;
    }
}