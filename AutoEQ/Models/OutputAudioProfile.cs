namespace AutoEQ.Models;

using System;

public sealed class OutputAudioProfile
{
    public string DeviceId { get; init; } = string.Empty;
    public string Name { get; init; } = "Generic Windows Audio";
    public string DeviceType { get; init; } = "Unknown";
    public string SoundCardName { get; init; } = "Unknown sound card";
    public string MainboardName { get; init; } = "Unknown mainboard";
    public string Reason { get; init; } = "Default safe profile";
    public double TargetBass { get; init; } = 0.31;
    public double TargetWarmth { get; init; } = 0.19;
    public double TargetVocal { get; init; } = 0.39;
    public double TargetBright { get; init; } = 0.20;
    public double MaxBoostDb { get; init; } = 3.0;
    public double MaxCutDb { get; init; } = -6.0;
    public double BassSafetyCutDb { get; init; } = -1.5;
    public bool PreferSpeakerVoicing { get; init; } = true;
    public DateTime LastSeenUtc { get; init; } = DateTime.UtcNow;
}