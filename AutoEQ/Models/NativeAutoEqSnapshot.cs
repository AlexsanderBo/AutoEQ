namespace AutoEQ.Models;

public sealed class NativeAutoEqSnapshot
{
    public string Device { get; init; } = "";
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public double Rms { get; init; }
    public double TruePeakDb { get; init; } = double.NaN;
    public double BassDb { get; init; }
    public double MidDb { get; init; }
    public double TrebleDb { get; init; }
    public string Profile { get; init; } = "";
    public double Confidence { get; init; }
    public double[] EqGainsDb { get; init; } = [];
    public double[] BandCentersHz { get; init; } = [];
}