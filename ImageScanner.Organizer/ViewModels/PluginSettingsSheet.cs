using System.ComponentModel;

using HandyControl.Controls;

using ImageScanner.Organizer.Views;

namespace ImageScanner.Organizer.ViewModels;

/// <summary>
/// Presents a plugin's settings to HandyControls' PropertyGrid. The grid builds its rows from
/// <c>TypeDescriptor.GetProperties(type)</c>, so a type description provider exposes each setting of the sheet
/// that's on screen as a property: the setting's label, help text and category become the row's name,
/// description and group, and <see cref="PluginSettingPropertyEditor"/> shows the setting's own editor.
/// Only one plugin settings dialog is open at a time (it's modal), which is what makes the type-level view work.
/// </summary>
public sealed class PluginSettingsSheet {
    private static PluginSettingsSheet? active;

    static PluginSettingsSheet() => TypeDescriptor.AddProvider(new SheetDescriptionProvider(), typeof(PluginSettingsSheet));

    public PluginSettingsSheet(IReadOnlyList<PluginSettingViewModel> settings) {
        Properties = new PropertyDescriptorCollection([.. settings.Select(setting => new SettingPropertyDescriptor(setting))], readOnly: true);
        active = this;
    }

    internal PropertyDescriptorCollection Properties { get; }

    private sealed class SheetDescriptionProvider() : TypeDescriptionProvider(TypeDescriptor.GetProvider(typeof(object))) {
        public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object? instance) =>
            new SheetDescriptor(instance as PluginSettingsSheet ?? active);
    }

    private sealed class SheetDescriptor(PluginSettingsSheet? sheet) : CustomTypeDescriptor {
        public override PropertyDescriptorCollection GetProperties() => sheet?.Properties ?? PropertyDescriptorCollection.Empty;

        public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();
    }

    /// <summary>A setting as a read-only property whose value is the setting itself; its editor edits the value.</summary>
    private sealed class SettingPropertyDescriptor(PluginSettingViewModel setting) : PropertyDescriptor(setting.Definition.Key, [
        new DisplayNameAttribute(setting.Label),
        new DescriptionAttribute(setting.Description ?? string.Empty),
        new CategoryAttribute(setting.Definition.Category ?? "General"),
        new EditorAttribute(typeof(PluginSettingPropertyEditor), typeof(PropertyEditorBase))
    ]) {
        public override Type ComponentType => typeof(PluginSettingsSheet);
        public override Type PropertyType => typeof(PluginSettingViewModel);
        public override bool IsReadOnly => false;
        public override bool CanResetValue(object component) => false;
        public override object GetValue(object? component) => setting;
        public override void ResetValue(object component) { }
        public override void SetValue(object? component, object? value) { }
        public override bool ShouldSerializeValue(object component) => false;
    }
}
