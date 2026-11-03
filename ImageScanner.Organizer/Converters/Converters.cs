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

public class InverseBoolConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
