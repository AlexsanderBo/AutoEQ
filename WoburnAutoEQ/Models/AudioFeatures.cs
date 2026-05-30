namespace WoburnAutoEQ.Models;

public sealed class AudioFeatures
{
    public double Rms { get; init; }
    public double SubBass { get; init; }
    public double Bass { get; init; }
    public double LowMid { get; init; }
    public double Mid { get; init; }
    public double Presence { get; init; }
    public double Treble { get; init; }
    public double Air { get; init; }
    public string State { get; init; } = "Balanced";
    public string GenreHint { get; init; } = "Unknown";
}