using System.Text.Json;
using System.IO;
using AutoEQ.Models;

namespace AutoEQ.Services;

public interface IOutputProfileStore
{
    OutputAudioProfile GetOrCreate(AudioOutputInfo info, bool nearWallMode, bool nightMode);
    IReadOnlyList<OutputAudioProfile> GetAll();
    OutputAudioProfile ApplyCalibration(string deviceId, double bass, double warmth, double vocal, double bright, double[] measuredAverage, int sampleCount);
    OutputAudioProfile ResetCalibration(AudioOutputInfo info, bool nearWallMode, bool nightMode);
}

public sealed class OutputAudioProfileStore : IOutputProfileStore
{
    private readonly string _path;
    private readonly IAppLogger? _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<string, OutputAudioProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _loaded;

    public OutputAudioProfileStore(IAppLogger? logger = null)
    {
        _logger = logger;
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoEQ");
        _path = Path.Combine(root, "output-profiles.json");
    }

    // Test/override hook.
    internal OutputAudioProfileStore(string path, IAppLogger? logger = null)
    {
        _logger = logger;
        _path = path;
    }


    public OutputAudioProfile GetOrCreate(AudioOutputInfo info, bool nearWallMode, bool nightMode)
    {
        lock (_gate)
        {
            EnsureLoaded();
            string key = MakeKey(info.DefaultDeviceId, info.DefaultDeviceName);
            OutputAudioProfile inferred = InferProfile(info, nearWallMode, nightMode);

            _profiles[key] = _profiles.TryGetValue(key, out OutputAudioProfile? existing)
                ? MergeExisting(existing, inferred)
                : inferred;

            Save();
            return _profiles[key];
        }
    }

    public IReadOnlyList<OutputAudioProfile> GetAll()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _profiles.Values.OrderBy(p => p.Name).ToArray();
        }
    }

    public OutputAudioProfile ApplyCalibration(string deviceId, double bass, double warmth, double vocal, double bright, double[] measuredAverage, int sampleCount)
    {
        lock (_gate)
        {
            EnsureLoaded();
            string key = string.IsNullOrWhiteSpace(deviceId) ? "global" : deviceId.Trim();
            if (!_profiles.TryGetValue(key, out OutputAudioProfile? existing))
            {
                existing = new OutputAudioProfile { DeviceId = key, Name = key };
            }

            OutputAudioProfile calibrated = CopyWithCalibration(existing, bass, warmth, vocal, bright, measuredAverage, sampleCount);
            _profiles[key] = calibrated;
            Save();
            return calibrated;
        }
    }

    public OutputAudioProfile ResetCalibration(AudioOutputInfo info, bool nearWallMode, bool nightMode)
    {
        lock (_gate)
        {
            EnsureLoaded();
            string key = MakeKey(info.DefaultDeviceId, info.DefaultDeviceName);
            OutputAudioProfile inferred = InferProfile(info, nearWallMode, nightMode);
            _profiles[key] = inferred;
            Save();
            return inferred;
        }
    }


    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(_path)) return;

        try
        {
            ProfileFile? file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(_path), _jsonOptions);
            foreach (OutputAudioProfile profile in file?.Profiles ?? [])
            {
                _profiles[MakeKey(profile.DeviceId, profile.Name)] = profile;
            }
        }
        catch (Exception ex)
        {
            _logger?.Error($"Output profile store: corrupt config at {_path}, starting fresh. {ex.Message}");
            _profiles.Clear();
        }
    }

    private void Save()
    {
        string dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(new ProfileFile { Profiles = _profiles.Values.OrderBy(p => p.Name).ToArray() }, _jsonOptions);
        string tmp = _path + ".tmp";

        try
        {
            File.WriteAllText(tmp, json);
            if (File.Exists(_path))
            {
                File.Replace(tmp, _path, null);
            }
            else
            {
                File.Move(tmp, _path);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error($"Output profile store: failed to save {_path}. {ex.Message}");
            TryDelete(tmp);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }


    private static OutputAudioProfile MergeExisting(OutputAudioProfile existing, OutputAudioProfile inferred) => new()
    {
        DeviceId = inferred.DeviceId,
        Name = inferred.Name,
        DeviceType = inferred.DeviceType,
        SoundCardName = inferred.SoundCardName,
        MainboardName = inferred.MainboardName,
        Reason = inferred.Reason,
        TargetBass = existing.TargetBass,
        TargetWarmth = existing.TargetWarmth,
        TargetVocal = existing.TargetVocal,
        TargetBright = existing.TargetBright,
        MaxBoostDb = existing.MaxBoostDb,
        MaxCutDb = existing.MaxCutDb,
        BassSafetyCutDb = existing.BassSafetyCutDb,
        PreferSpeakerVoicing = existing.PreferSpeakerVoicing,
        IsCalibrated = existing.IsCalibrated,
        CalibratedUtc = existing.CalibratedUtc,
        CalibrationSampleCount = existing.CalibrationSampleCount,
        MeasuredBandAverage = existing.MeasuredBandAverage,
        LastSeenUtc = DateTime.UtcNow
    };

    private static OutputAudioProfile CopyWithCalibration(OutputAudioProfile existing, double bass, double warmth, double vocal, double bright, double[] measuredAverage, int sampleCount) => new()
    {
        DeviceId = existing.DeviceId,
        Name = existing.Name,
        DeviceType = existing.DeviceType,
        SoundCardName = existing.SoundCardName,
        MainboardName = existing.MainboardName,
        Reason = existing.Reason,
        TargetBass = Clamp01(bass),
        TargetWarmth = Clamp01(warmth),
        TargetVocal = Clamp01(vocal),
        TargetBright = Clamp01(bright),
        MaxBoostDb = existing.MaxBoostDb,
        MaxCutDb = existing.MaxCutDb,
        BassSafetyCutDb = existing.BassSafetyCutDb,
        PreferSpeakerVoicing = existing.PreferSpeakerVoicing,
        IsCalibrated = true,
        CalibratedUtc = DateTime.UtcNow,
        CalibrationSampleCount = Math.Max(0, sampleCount),
        MeasuredBandAverage = measuredAverage.ToArray(),
        LastSeenUtc = DateTime.UtcNow
    };

    private static OutputAudioProfile InferProfile(AudioOutputInfo info, bool nearWallMode, bool nightMode)
    {
        string text = $"{info.MainboardName} {info.SoundCardName} {info.DefaultDeviceName} {info.OutputSummary}";
        string type = ResolveType(text);

        if (type == "Headphone") return WithValues(Base(info, type, "Headphone detected: safe low-boost profile"), targetBass: 0.28, targetWarmth: 0.20, targetVocal: 0.38, targetBright: nightMode ? 0.14 : 0.18, maxBoost: 1.5, bassSafety: 0, speakerVoicing: false);
        if (type == "Bluetooth") return WithValues(Base(info, type, "Bluetooth output: codec-safe profile"), targetBass: nearWallMode ? 0.24 : 0.28, targetWarmth: 0.17, targetVocal: 0.40, targetBright: nightMode ? 0.14 : 0.19, maxBoost: 2.0, bassSafety: -1.8, speakerVoicing: true);
        if (type is "Digital" or "DAC") return WithValues(Base(info, type, "Clean digital/DAC output: minimal correction profile"), targetBass: 0.30, targetWarmth: 0.18, targetVocal: 0.40, targetBright: nightMode ? 0.15 : 0.20, maxBoost: 2.2, bassSafety: -1.0, speakerVoicing: true);
        if (type == "Speaker") return WithValues(Base(info, type, "Speaker output: tame low-mid, keep vocal clear"), targetBass: nearWallMode ? 0.25 : 0.30, targetWarmth: nearWallMode ? 0.15 : 0.18, targetVocal: 0.42, targetBright: nightMode ? 0.15 : 0.21, maxBoost: nightMode ? 1.8 : 2.8, bassSafety: nearWallMode ? -2.2 : -1.5, speakerVoicing: true);
        return Base(info, type, "Generic safe output profile");
    }

    private static OutputAudioProfile Base(AudioOutputInfo info, string type, string reason) => new()
    {
        DeviceId = info.DefaultDeviceId,
        Name = string.IsNullOrWhiteSpace(info.DefaultDeviceName) ? "Generic Windows Audio" : info.DefaultDeviceName,
        DeviceType = type,
        SoundCardName = info.SoundCardName,
        MainboardName = info.MainboardName,
        Reason = reason,
        LastSeenUtc = DateTime.UtcNow
    };

    private static OutputAudioProfile WithValues(OutputAudioProfile profile, double targetBass, double targetWarmth, double targetVocal, double targetBright, double maxBoost, double bassSafety, bool speakerVoicing) => new()
    {
        DeviceId = profile.DeviceId,
        Name = profile.Name,
        DeviceType = profile.DeviceType,
        SoundCardName = profile.SoundCardName,
        MainboardName = profile.MainboardName,
        Reason = profile.Reason,
        TargetBass = targetBass,
        TargetWarmth = targetWarmth,
        TargetVocal = targetVocal,
        TargetBright = targetBright,
        MaxBoostDb = maxBoost,
        MaxCutDb = profile.MaxCutDb,
        BassSafetyCutDb = bassSafety,
        PreferSpeakerVoicing = speakerVoicing,
        LastSeenUtc = DateTime.UtcNow
    };

    private static string ResolveType(string value)
    {
        if (ContainsAny(value, "headphone", "headset", "earbud", "earphone", "tai nghe")) return "Headphone";
        if (ContainsAny(value, "bluetooth", "marshall", "AutoEQ")) return "Bluetooth";
        if (ContainsAny(value, "spdif", "s/pdif", "digital")) return "Digital";
        if (ContainsAny(value, "dac", "usb audio")) return "DAC";
        if (ContainsAny(value, "speaker", "loa", "realtek")) return "Speaker";
        return "Unknown";
    }

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static double Clamp01(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.02, 0.80) : 0.20;
    private static string MakeKey(string id, string name) => string.IsNullOrWhiteSpace(id) ? name : id;
    private sealed class ProfileFile { public IReadOnlyList<OutputAudioProfile> Profiles { get; init; } = []; }
}