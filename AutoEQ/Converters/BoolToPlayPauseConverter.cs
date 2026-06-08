using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace AutoEQ.Converters;

public sealed class BoolToPlayPauseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool playing && playing ? "⏸" : "▶";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
