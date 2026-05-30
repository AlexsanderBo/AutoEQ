namespace WoburnAutoEQ.Config;

public static class AppConfig
{
    public const string AppName = "WoburnAutoEQ";
    public const string EqualizerApoConfigDir = @"C:\Program Files\EqualizerAPO\config";
    public const string EqualizerApoMainConfig = @"C:\Program Files\EqualizerAPO\config\config.txt";
    public const string WoburnEqFile = @"C:\Program Files\EqualizerAPO\config\woburn_autoeq.txt";
    public const int AnalysisIntervalSeconds = 5;
    public const int DecisionWindowSeconds = 25;
    public const int RecentDetectionCount = 5;
    public const int RequiredStableDetections = 4;
    public const int PresetCooldownSeconds = 45;
    public const int DynamicEqCooldownSeconds = 18;
}