using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NexStrap;

public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var param = parameter?.ToString() ?? "#FFFFFF,#888888";
        var parts = param.Split(',');
        var colorStr = (value is bool b && b) ? parts[0] : (parts.Length > 1 ? parts[1] : "#888888");
        try { return new SolidColorBrush(Color.Parse(colorStr)); }
        catch { return new SolidColorBrush(Colors.Gray); }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class BoolToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var param = parameter?.ToString() ?? "Yes,No";
        var parts = param.Split(',');
        return (value is bool b && b) ? parts[0] : (parts.Length > 1 ? parts[1] : string.Empty);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class IntToZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isZero = value is int i && i == 0;
        return parameter?.ToString() == "invert" ? !isZero : isZero;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class StringToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return null;
        try { return new Avalonia.Media.Imaging.Bitmap(path); }
        catch { return null; }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class SecondsToTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int seconds || seconds <= 0) return null;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
            : $"{ts.Minutes}m";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
