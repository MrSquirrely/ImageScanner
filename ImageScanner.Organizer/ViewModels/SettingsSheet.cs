using System.ComponentModel;

using CommunityToolkit.Mvvm.ComponentModel;

using HandyControl.Controls;

using ImageScanner.Organizer.Views;

namespace ImageScanner.Organizer.ViewModels;

/// <summary>
/// The app settings shown in the Settings window's PropertyGrid. The grid builds its rows from these
/// attributes: Category groups rows, DisplayName labels them and Description explains them.
/// </summary>
public partial class SettingsSheet : ObservableObject {
    public const string BuiltInViewer = "Built-in viewer";
    public const string WindowsDefaultApp = "My computer's default image app";

    // Sources for the drop-down rows (see ChoicePropertyEditor); hidden from the grid itself
    [Browsable(false)]
    public IReadOnlyList<string> ViewerChoices { get; } = [BuiltInViewer, WindowsDefaultApp];

    [Browsable(false)]
    public IReadOnlyList<string> EngineChoices { get; init; } = [];

    [ObservableProperty]
    [property: Category("Appearance"), DisplayName("Dark theme"), Description("Previewed straight away; Cancel restores the current theme.")]
    public partial bool DarkTheme { get; set; }

    [ObservableProperty]
    [property: Category("Viewing"), DisplayName("Open images with"),
               Description("The built-in viewer zooms, pans and browses with the arrow keys. Comics always open in the built-in reader.")]
    [property: Editor(typeof(ChoicePropertyEditor), typeof(PropertyEditorBase)), Choices(nameof(ViewerChoices))]
    public partial string OpenImagesWith { get; set; } = BuiltInViewer;

    [ObservableProperty]
    [property: Category("Library"), DisplayName("Library database"), Description("Changing the library file takes effect after restarting.")]
    [property: Editor(typeof(LibraryPathPropertyEditor), typeof(PropertyEditorBase))]
    public partial string LibraryPath { get; set; } = string.Empty;

    [ObservableProperty]
    [property: Category("Import"), DisplayName("Group comic pages automatically"), Description("Folders of numbered pages become comics when imported.")]
    public partial bool DetectComics { get; set; }

    [ObservableProperty]
    [property: Category("Import"), DisplayName("Skip folders named"), Description("Separate names with ; (for example node_modules; .git).")]
    public partial string IgnoredFolders { get; set; } = string.Empty;

    [ObservableProperty]
    [property: Category("AI tagging"), DisplayName("Tagging engine"), Description("Engines come from plugins; set each one up on the Plugins tab.")]
    [property: Editor(typeof(ChoicePropertyEditor), typeof(PropertyEditorBase)), Choices(nameof(EngineChoices))]
    public partial string? TaggingEngine { get; set; }

    [ObservableProperty]
    [property: Category("AI tagging"), DisplayName("Suggest tags when importing"), Description("New images are sent to the tagging engine after each import.")]
    public partial bool SuggestTagsOnImport { get; set; }

    [ObservableProperty]
    [property: Category("AI tagging"), DisplayName("Apply suggestions immediately"),
               Description("Otherwise suggestions wait for review under \"Needs AI review\".")]
    public partial bool AutoApplySuggestions { get; set; }

    [ObservableProperty]
    [property: Category("AI tagging"), DisplayName("Maximum tags per image"), Description("1 to 50.")]
    public partial int MaxSuggestedTags { get; set; }

    [ObservableProperty]
    [property: Category("AI tagging"), DisplayName("Parallel requests"),
               Description("1 to 16. Local AI handles one image at a time regardless; online services can go faster.")]
    public partial int AiConcurrency { get; set; }

    partial void OnDarkThemeChanged(bool value) => App.ApplyTheme(value);
}

/// <summary>Names the property that lists a <see cref="ChoicePropertyEditor"/> row's options.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ChoicesAttribute(string sourceProperty) : Attribute {
    public string SourceProperty { get; } = sourceProperty;
}
