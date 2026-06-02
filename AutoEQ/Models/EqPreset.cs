namespace AutoEQ.Models;

public sealed class EqPreset
{
    public string Name { get; init; } = "";
    public string EqualizerApoText { get; init; } = "";
    public double? TruePeakDb { get; init; }
    public bool IsDynamic { get; init; }
}