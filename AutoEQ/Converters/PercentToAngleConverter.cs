using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace AutoEQ.Converters;

public sealed class PercentToAngleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double percent = System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture);
        return -140 + Math.Clamp(percent, 0, 100) * 2.8;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
