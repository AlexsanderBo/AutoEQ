namespace AutoEQ.Models;

public sealed class AudioOutputInfo
{
    public string DefaultDeviceName { get; init; } = "Unknown";
    public string DefaultDeviceId { get; init; } = "";
    public string MainboardName { get; init; } = "Unknown mainboard";
    public string SoundCardName { get; init; } = "Unknown sound card";
    public string SoundCardVersion { get; init; } = "Unknown version";
    public string DataFlow { get; init; } = "Render";
    public string Role { get; init; } = "Console";
    public IReadOnlyList<string> ActiveOutputNames { get; init; } = Array.Empty<string>();

    public string OutputSummary => DefaultDeviceName;
}