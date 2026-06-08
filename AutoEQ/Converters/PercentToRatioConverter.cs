using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace AutoEQ.Converters;

public sealed class PercentToRatioConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double percent = System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture);
        return Math.Clamp(percent / 100d, 0, 1);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
