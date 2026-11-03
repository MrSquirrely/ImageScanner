using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

using HandyControl.Controls;

using ImageScanner.Organizer.ViewModels;

using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace ImageScanner.Organizer.Views;

/// <summary>
/// A drop-down row for the settings PropertyGrid; the options come from the property named by <see cref="ChoicesAttribute"/>.
/// </summary>
public sealed class ChoicePropertyEditor : PropertyEditorBase {
    public override FrameworkElement CreateElement(PropertyItem propertyItem) {
        ComboBox comboBox = new() { IsEnabled = !propertyItem.IsReadOnly };
        AutomationProperties.SetName(comboBox, propertyItem.DisplayName);
        // HandyControls leaves PropertyItem.PropertyDescriptor unset; Value is the object being edited
        object? target = propertyItem.Value;
        if (target != null
            && TypeDescriptor.GetProperties(target)[propertyItem.PropertyName]?.Attributes[typeof(ChoicesAttribute)] is ChoicesAttribute choices
            && TypeDescriptor.GetProperties(target)[choices.SourceProperty]?.GetValue(target) is IEnumerable options) {
            comboBox.ItemsSource = options;
        }

        return comboBox;
    }

    public override DependencyProperty GetDependencyProperty() => Selector.SelectedItemProperty;
}

/// <summary>
/// A plugin setting row: shows the setting's own editor (text, presets, Browse…, password, switch or choice) from
/// PluginSettingEditorTemplate. That template binds to the setting directly, so the grid's binding isn't used.
/// </summary>
public sealed class PluginSettingPropertyEditor : PropertyEditorBase {
    public override FrameworkElement CreateElement(PropertyItem propertyItem) => new ContentPresenter {
        Content = propertyItem.Value is { } sheet ? TypeDescriptor.GetProperties(sheet)[propertyItem.PropertyName]?.GetValue(sheet) : null,
        ContentTemplate = (DataTemplate)Application.Current.FindResource("PluginSettingEditorTemplate")
    };

    public override void CreateBinding(PropertyItem propertyItem, DependencyObject element) {
    }

    public override DependencyProperty GetDependencyProperty() => ContentPresenter.ContentProperty;
}

/// <summary>
/// The library file row: the path plus a Change… button that picks (or names) a .db file.
/// </summary>
public sealed class LibraryPathPropertyEditor : PropertyEditorBase {
    public override FrameworkElement CreateElement(PropertyItem propertyItem) => new PathPicker { Label = propertyItem.DisplayName };

    public override DependencyProperty GetDependencyProperty() => PathPicker.PathProperty;

    public override BindingMode GetBindingMode(PropertyItem propertyItem) => BindingMode.TwoWay;
}

public sealed class PathPicker : DockPanel {
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path), typeof(string), typeof(PathPicker), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string Path {
        get => (string)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public string Label { get; init; } = "File";

    public PathPicker() {
        Button change = new() { Content = "Change…", Margin = new Thickness(6, 0, 0, 0) };
        SetDock(change, Dock.Right);
        change.Click += (_, _) => Browse();

        TextBox path = new() { IsReadOnly = true };
        path.SetBinding(TextBox.TextProperty, new Binding(nameof(Path)) { Source = this, Mode = BindingMode.OneWay });
        Loaded += (_, _) => AutomationProperties.SetName(path, Label);

        Children.Add(change);
        Children.Add(path);
    }

    private void Browse() {
        Microsoft.Win32.SaveFileDialog dialog = new() {
            Title = "Choose library database",
            Filter = "Library database (*.db)|*.db",
            FileName = System.IO.Path.GetFileName(Path),
            InitialDirectory = System.IO.Path.GetDirectoryName(Path),
            OverwritePrompt = false
        };

        if (dialog.ShowDialog() == true) {
            Path = dialog.FileName;
        }
    }
}
