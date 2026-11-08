using System.Globalization;
using Avalonia.Data.Converters;

namespace Nettle.UI.Controls;

public sealed class BoolToOpacity : IValueConverter
{
    public static readonly BoolToOpacity Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 1.0 : 0.0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>First two characters, upper-cased: a stand-in where a channel has no logo.</summary>
public sealed class Initials : IValueConverter
{
    public static readonly Initials Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string)?.Trim() ?? "";
        return (s.Length > 2 ? s[..2] : s).ToUpperInvariant();
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
