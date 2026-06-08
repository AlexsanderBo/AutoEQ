using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AutoEQ.Converters;

/// <summary>
/// Quy đổi giá trị dB (±6) thành hình học cho fader dọc read-only + màu theo dấu.
/// Track cao 140, tâm (0 dB) ở 70. ConverterParameter chọn đại lượng cần lấy:
///   "thumbTop"   → Canvas.Top của núm (Border 22×22).
///   "fillTop"    → Canvas.Top của dải fill (từ núm tới vạch 0).
///   "fillHeight" → chiều cao dải fill.
///   "sign"       → IBrush: boost xanh, cut đỏ, 0 mờ.
/// </summary>
public sealed class DbFaderConverter : IValueConverter
{
    private const double TrackHeight = 140;
    private const double Center = 70;     // y của 0 dB
    private const double ThumbR = 11;     // nửa cạnh núm 22px
    private const double Travel = Center - ThumbR; // 59: biên trượt từ tâm
    private const double MaxDb = 6;

    private static readonly IBrush BoostBrush = new SolidColorBrush(Color.Parse("#7FD39A"));
    private static readonly IBrush CutBrush = new SolidColorBrush(Color.Parse("#F07854"));
    private static readonly IBrush ZeroBrush = new SolidColorBrush(Color.Parse("#9C7A4E"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double db = System.Convert.ToDouble(value ?? 0, CultureInfo.InvariantCulture);
        double clamped = Math.Clamp(db, -MaxDb, MaxDb);
        // Tâm núm: boost (db>0) đi lên (y nhỏ), cut đi xuống (y lớn).
        double cy = Math.Clamp(Center - (clamped / MaxDb) * Travel, ThumbR, TrackHeight - ThumbR);

        string mode = (parameter as string)?.ToLowerInvariant() ?? "thumbtop";
        return mode switch
        {
            "thumbtop" => cy - ThumbR,
            "filltop" => Math.Min(cy, Center),
            "fillheight" => Math.Abs(Center - cy),
            "sign" => clamped > 0.05 ? BoostBrush : clamped < -0.05 ? CutBrush : ZeroBrush,
            _ => cy - ThumbR,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
