using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using AutoEQ.Config;
using AutoEQ.Models;

namespace AutoEQ.Services;

public interface IEqualizerApoManager
{
    bool IsInstalled();
    Task EnsureIncludeLineAsync();
    Task WriteAutoEQConfigAsync(string text, EqPreset? preset = null, string? reason = null);
    Task WriteDeviceScopedConfigAsync(string deviceKey, string apoPattern, string text, EqPreset? preset = null, string? reason = null);
    bool IsLikelyInstalledOnCurrentDevice(AudioOutputInfo? outputInfo);
    Task ApplyPresetAsync(EqPreset preset, string? reason = null);
    Task ApplyPresetAsync(EqPreset preset, AudioOutputInfo? outputInfo, string? reason = null);
    void OpenConfigFolder();
}

public sealed class EqualizerApoManager : IEqualizerApoManager
{
    private readonly IAutoEqConfig _config;
    private readonly IDeviceEqStore _deviceEqStore;
    private readonly IAppLogger? _logger;

    private const string IncludeLine = "Include: AutoEQ_autoeq.txt";

    public EqualizerApoManager(IAutoEqConfig? config = null, IDeviceEqStore? deviceEqStore = null, IAppLogger? logger = null)
    {
        _config = config ?? new AutoEqConfig();
        _logger = logger;
        _deviceEqStore = deviceEqStore ?? new DeviceEqStore(logger);
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
        => await WriteDeviceScopedConfigAsync("global", string.Empty, text, preset, reason).ConfigureAwait(false);

    public async Task WriteDeviceScopedConfigAsync(string deviceKey, string apoPattern, string text, EqPreset? preset = null, string? reason = null)
    {
        if (!Directory.Exists(_config.EqualizerApoConfigDir))
        {
            throw new DirectoryNotFoundException($"Equalizer APO config folder not found: {_config.EqualizerApoConfigDir}");
        }

        string scopedText = EnsureDeviceScope(text, apoPattern);
        string block = BuildBlockBody(scopedText, preset, reason);
        _deviceEqStore.Upsert(deviceKey, apoPattern, block, preset?.Name ?? string.Empty, preset?.TruePeakDb);
        string body = BuildConfigBody(_deviceEqStore.GetAll());
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

    private static string EnsureDeviceScope(string text, string apoPattern)
    {
        string trimmed = text.Trim();
        bool hasDevice = trimmed.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Any(line => line.TrimStart().StartsWith("Device:", StringComparison.OrdinalIgnoreCase));
        if (hasDevice || string.IsNullOrWhiteSpace(apoPattern)) return trimmed;
        return $"Device: {apoPattern.Trim()}{Environment.NewLine}" + trimmed;
    }

    public bool IsLikelyInstalledOnCurrentDevice(AudioOutputInfo? outputInfo)
    {
        if (!IsInstalled() || outputInfo is null) return false;
        string text = $"{outputInfo.DefaultDeviceName} {outputInfo.OutputSummary}";
        return text.Contains("Equalizer APO", StringComparison.OrdinalIgnoreCase) ||
               File.Exists(_config.EqualizerApoMainConfig);
    }

    public async Task ApplyPresetAsync(EqPreset preset, string? reason = null)
        => await ApplyPresetAsync(preset, null, reason).ConfigureAwait(false);

    public async Task ApplyPresetAsync(EqPreset preset, AudioOutputInfo? outputInfo, string? reason = null)
    {
        try
        {
            await EnsureIncludeLineAsync();
            DeviceEqTarget target = ResolveDeviceEqTarget(outputInfo);
            await WriteDeviceScopedConfigAsync(target.DeviceKey, target.ApoPattern, preset.EqualizerApoText, preset, reason).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException("Please run AutoEQ as Administrator once to connect with Equalizer APO.", ex);
        }
    }

    internal DeviceEqTarget ResolveDeviceEqTarget(AudioOutputInfo? outputInfo)
    {
        if (!string.IsNullOrWhiteSpace(outputInfo?.DefaultDeviceId))
        {
            string id = outputInfo.DefaultDeviceId.Trim();
            return new DeviceEqTarget(id, id);
        }

        if (!string.IsNullOrWhiteSpace(outputInfo?.DefaultDeviceName))
        {
            string token = NormalizeDeviceNameToken(outputInfo.DefaultDeviceName);
            if (!string.IsNullOrWhiteSpace(token))
            {
                _logger?.Error($"Equalizer APO Device fallback: DefaultDeviceId missing; using normalized device name token '{token}'. Endpoint ID matching is safer.");
                return new DeviceEqTarget($"name:{token}", token);
            }
        }

        _logger?.Error("Equalizer APO Device fallback: DefaultDeviceId missing and no reliable name token; writing global EQ block.");
        return new DeviceEqTarget("global", string.Empty);
    }

    private static string NormalizeDeviceNameToken(string name)
    {
        string token = Regex.Replace(name, @"^Default\s*\([^)]*\)\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
        token = Regex.Replace(token, @"\s+", " ").Trim();
        return token.Length >= 3 ? token : string.Empty;
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

    private static string BuildBlockBody(string text, EqPreset? preset, string? reason)
    {
        var lines = new List<string>();

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
        return string.Join("\r\n", lines.Where((line, index) => index < lines.Count - 1 || line.Length > 0)).Trim() + "\r\n";
    }

    private static string BuildConfigBody(IReadOnlyList<DeviceEqRecord> records)
    {
        var lines = new List<string>
        {
            "# Generated by AutoEQ. Safe to overwrite.",
            $"# Updated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            string.Empty
        };

        foreach (string text in records.Select(record => record.EqText.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)))
        {
            if (lines.Count > 3) lines.Add(string.Empty);
            lines.Add(text);
        }
        return string.Join("\r\n", lines).TrimEnd() + "\r\n";
    }

    internal readonly record struct DeviceEqTarget(string DeviceKey, string ApoPattern);
}
