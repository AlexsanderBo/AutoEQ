using System.Diagnostics;
using System.IO;
using AutoEQ.Config;
using AutoEQ.Models;

namespace AutoEQ.Services;

public interface IEqualizerApoManager
{
    bool IsInstalled();
    Task EnsureIncludeLineAsync();
    Task WriteAutoEQConfigAsync(string text, EqPreset? preset = null, string? reason = null);
    bool IsLikelyInstalledOnCurrentDevice(AudioOutputInfo? outputInfo);
    Task ApplyPresetAsync(EqPreset preset, string? reason = null);
    void OpenConfigFolder();
}

public sealed class EqualizerApoManager : IEqualizerApoManager
{
    private readonly IAutoEqConfig _config;

    private const string IncludeLine = "Include: AutoEQ_autoeq.txt";
    private const double SafeTruePeakCeilingDb = -1.0;

    public EqualizerApoManager(IAutoEqConfig? config = null)
    {
        _config = config ?? new AutoEqConfig();
    }

    public bool IsInstalled() => Directory.Exists(_config.EqualizerApoConfigDir);

    public async Task EnsureIncludeLineAsync()
    {
        if (!Directory.Exists(_config.EqualizerApoConfigDir))
        {
            throw new DirectoryNotFoundException($"Equalizer APO config folder not found: {_config.EqualizerApoConfigDir}");
        }

        if (!File.Exists(_config.EqualizerApoMainConfig))
        {
            await File.WriteAllTextAsync(_config.EqualizerApoMainConfig, string.Empty);
        }

        string current = await File.ReadAllTextAsync(_config.EqualizerApoMainConfig);
        bool exists = current.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Any(line => string.Equals(line.Trim(), IncludeLine, StringComparison.OrdinalIgnoreCase));
        if (exists) return;

        string backupPath = _config.EqualizerApoMainConfig + ".bak";
        File.Copy(_config.EqualizerApoMainConfig, backupPath, overwrite: true);
        string separator = current.EndsWith('\n') || current.Length == 0 ? string.Empty : Environment.NewLine;
        await File.WriteAllTextAsync(_config.EqualizerApoMainConfig, current + separator + IncludeLine + Environment.NewLine);
    }

    public async Task WriteAutoEQConfigAsync(string text, EqPreset? preset = null, string? reason = null)
    {
        if (!Directory.Exists(_config.EqualizerApoConfigDir))
        {
            throw new DirectoryNotFoundException($"Equalizer APO config folder not found: {_config.EqualizerApoConfigDir}");
        }

        string safeText = AddLimiterGuard(text, preset?.TruePeakDb, out string limiterReason);
        string? mergedReason = string.IsNullOrWhiteSpace(limiterReason)
            ? reason
            : string.IsNullOrWhiteSpace(reason) ? limiterReason : $"{reason}; {limiterReason}";
        string body = BuildConfigBody(safeText, preset, mergedReason);
        string tempPath = _config.AutoEqEqFile + ".tmp";
        await File.WriteAllTextAsync(tempPath, body);

        if (File.Exists(_config.AutoEqEqFile))
        {
            File.Replace(tempPath, _config.AutoEqEqFile, null);
        }
        else
        {
            File.Move(tempPath, _config.AutoEqEqFile);
        }
    }

    private static string AddLimiterGuard(string text, double? truePeakDb, out string reason)
    {
        double preampDb = 0;
        double positiveBoostDb = 0;
        foreach (string rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase))
            {
                preampDb = TryParseFirstDb(line, preampDb);
            }
            else if (line.StartsWith("Filter:", StringComparison.OrdinalIgnoreCase) && line.Contains(" Gain ", StringComparison.OrdinalIgnoreCase))
            {
                double gain = TryParseGainDb(line);
                if (gain > 0) positiveBoostDb += gain * 0.35;
            }
        }

        double estimatedPeakDb = truePeakDb.HasValue
            ? preampDb + truePeakDb.Value
            : preampDb + positiveBoostDb;
        if (estimatedPeakDb <= SafeTruePeakCeilingDb)
        {
            reason = string.Empty;
            return text;
        }

        double extraHeadroomDb = estimatedPeakDb - SafeTruePeakCeilingDb;
        string peakSource = truePeakDb.HasValue ? "true peak" : "estimated peak";
        reason = $"Limiter guard added {extraHeadroomDb:0.##} dB headroom; {peakSource} {estimatedPeakDb:0.##} dBFS.";
        string adjustedText = AdjustFirstPreamp(text, preampDb - extraHeadroomDb);
        return $"# Safety limiter guard: {peakSource} {estimatedPeakDb:0.##} dBFS, ceiling {SafeTruePeakCeilingDb:0.##} dBFS{Environment.NewLine}" + adjustedText;
    }

    private static string AdjustFirstPreamp(string text, double adjustedPreampDb)
    {
        string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase)) continue;

            lines[i] = $"Preamp: {adjustedPreampDb:0.##} dB";
            return string.Join(Environment.NewLine, lines);
        }

        return $"Preamp: {adjustedPreampDb:0.##} dB{Environment.NewLine}" + text;
    }

    public bool IsLikelyInstalledOnCurrentDevice(AudioOutputInfo? outputInfo)
    {
        if (!IsInstalled() || outputInfo is null) return false;
        string text = $"{outputInfo.DefaultDeviceName} {outputInfo.OutputSummary}";
        return text.Contains("Equalizer APO", StringComparison.OrdinalIgnoreCase) ||
               File.Exists(_config.EqualizerApoMainConfig);
    }

    private static double TryParseFirstDb(string line, double fallback)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            if (double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)) return value;
        }
        return fallback;
    }

    private static double TryParseGainDb(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "Gain", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(parts[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }
        }
        return 0;
    }

    public async Task ApplyPresetAsync(EqPreset preset, string? reason = null)
    {
        try
        {
            await EnsureIncludeLineAsync();
            await WriteAutoEQConfigAsync(preset.EqualizerApoText, preset, reason);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Please run AutoEQ as Administrator once to connect with Equalizer APO.", ex);
        }
    }

    public void OpenConfigFolder()
    {
        if (!Directory.Exists(_config.EqualizerApoConfigDir))
        {
            throw new DirectoryNotFoundException($"Equalizer APO config folder not found: {_config.EqualizerApoConfigDir}");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _config.EqualizerApoConfigDir,
            UseShellExecute = true
        });
    }

    private static string BuildConfigBody(string text, EqPreset? preset, string? reason)
    {
        var lines = new List<string>
        {
            "# Generated by AutoEQ. Safe to overwrite.",
            $"# Updated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
        };

        if (preset is not null)
        {
            lines.Add($"# Preset: {preset.Name}");
            lines.Add($"# Type: {(preset.IsDynamic ? "Dynamic AutoEQ" : "Static preset")}");
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            lines.Add($"# Reason: {reason}");
        }

        lines.Add(string.Empty);
        lines.Add(text.Trim());
        return string.Join("\r\n", lines) + "\r\n";
    }
}