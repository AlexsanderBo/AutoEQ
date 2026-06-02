using System.IO;

namespace AutoEQ.Config;

/// <summary>
/// Injectable configuration surface for AutoEQ. Abstracts away the hard-coded
/// Equalizer APO install location so paths can be overridden in tests and the
/// real install directory can be auto-detected at runtime.
/// </summary>
public interface IAutoEqConfig
{
    string EqualizerApoConfigDir { get; }
    string EqualizerApoMainConfig { get; }
    string AutoEqEqFile { get; }

    int AnalysisIntervalSeconds { get; }
    int DecisionWindowSeconds { get; }
    int RecentDetectionCount { get; }
    int RequiredStableDetections { get; }
    int PresetCooldownSeconds { get; }
    int DynamicEqCooldownSeconds { get; }
}

/// <summary>
/// Default config provider. Tuning values come from <see cref="AppConfig"/>, while
/// the Equalizer APO config directory is auto-detected across the common install
/// locations and falls back to the historical default.
/// </summary>
public sealed class AutoEqConfig : IAutoEqConfig
{
    private const string AutoEqFileName = "AutoEQ_autoeq.txt";
    private const string MainConfigFileName = "config.txt";

    public AutoEqConfig(string? configDirOverride = null)
    {
        EqualizerApoConfigDir = configDirOverride ?? DetectConfigDir();
        EqualizerApoMainConfig = Path.Combine(EqualizerApoConfigDir, MainConfigFileName);
        AutoEqEqFile = Path.Combine(EqualizerApoConfigDir, AutoEqFileName);
    }

    public string EqualizerApoConfigDir { get; }
    public string EqualizerApoMainConfig { get; }
    public string AutoEqEqFile { get; }

    public int AnalysisIntervalSeconds => AppConfig.AnalysisIntervalSeconds;
    public int DecisionWindowSeconds => AppConfig.DecisionWindowSeconds;
    public int RecentDetectionCount => AppConfig.RecentDetectionCount;
    public int RequiredStableDetections => AppConfig.RequiredStableDetections;
    public int PresetCooldownSeconds => AppConfig.PresetCooldownSeconds;
    public int DynamicEqCooldownSeconds => AppConfig.DynamicEqCooldownSeconds;

    private static string DetectConfigDir()
    {
        string[] candidates =
        [
            AppConfig.EqualizerApoConfigDir,
            @"C:\Program Files\EqualizerAPO\config",
            @"C:\Program Files (x86)\EqualizerAPO\config"
        ];

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate)) return candidate;
        }

        return AppConfig.EqualizerApoConfigDir;
    }
}
