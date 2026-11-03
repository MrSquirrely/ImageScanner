using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ImageScanner.Organizer.Converters;

public class InverseBoolToVisibilityConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

public class NullToVisibilityConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value == null || value as string == string.Empty ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible when the bound index equals ConverterParameter; used to show one wizard page at a time.
/// </summary>
public class IndexToVisibilityConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index && int.TryParse(parameter?.ToString(), out int expected) && index == expected ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Binds one RadioButton of a pair to a single bool: checked when the value equals ConverterParameter.
/// Only the button being checked writes back, so the group's automatic unchecking can't flip the value.
/// </summary>
public class BoolRadioConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && bool.TryParse(parameter?.ToString(), out bool expected) && flag == expected;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && bool.TryParse(parameter?.ToString(), out bool expected) ? expected : Binding.DoNothing;
}

public class InverseBoolConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
