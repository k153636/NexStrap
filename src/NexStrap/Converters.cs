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
        try { return Color.Parse(colorStr); }
        catch { return Colors.Gray; }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class BoolToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var param = parameter?.ToString() ?? "はい,いいえ";
        var parts = param.Split(',');
        return (value is bool b && b) ? parts[0] : (parts.Length > 1 ? parts[1] : string.Empty);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class IntToZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i == 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
