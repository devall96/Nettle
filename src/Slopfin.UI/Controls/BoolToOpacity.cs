using System.Globalization;
using Avalonia.Data.Converters;

namespace Slopfin.UI.Controls;

public sealed class BoolToOpacity : IValueConverter
{
    public static readonly BoolToOpacity Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 1.0 : 0.0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
