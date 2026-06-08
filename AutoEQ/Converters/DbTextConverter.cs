using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace AutoEQ.Converters;

public sealed class DbTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double db = System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture);
        return $"{db:+0.0;-0.0;0.0} dB";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
