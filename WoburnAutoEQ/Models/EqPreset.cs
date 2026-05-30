namespace WoburnAutoEQ.Models;

public sealed class EqPreset
{
    public string Name { get; init; } = "";
    public string EqualizerApoText { get; init; } = "";
    public bool IsDynamic { get; init; }
}