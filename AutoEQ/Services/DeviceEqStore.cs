using System.IO;
using System.Text.Json;

namespace AutoEQ.Services;

public interface IDeviceEqStore
{
    void Upsert(string deviceKey, string apoPattern, string eqText, string presetName, double? truePeakDb);
    IReadOnlyList<DeviceEqRecord> GetAll();
    void Remove(string deviceKey);
}

public sealed record DeviceEqRecord(
    string DeviceKey,
    string ApoMatchPattern,
    string EqText,
    string PresetName,
    double? TruePeakDb,
    DateTime UpdatedUtc);

public sealed class DeviceEqStore : IDeviceEqStore
{
    private const int MaxBlocks = 12;
    private readonly string _path;
    private readonly IAppLogger? _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<string, DeviceEqRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _loaded;

    public DeviceEqStore(IAppLogger? logger = null)
    {
        _logger = logger;
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoEQ");
        _path = Path.Combine(root, "device-eq.json");
    }

    public DeviceEqStore(string path, IAppLogger? logger = null)
    {
        _logger = logger;
        _path = path;
    }

    public void Upsert(string deviceKey, string apoPattern, string eqText, string presetName, double? truePeakDb)
    {
        lock (_gate)
        {
            EnsureLoaded();
            string key = string.IsNullOrWhiteSpace(deviceKey) ? "global" : deviceKey.Trim();
            _records[key] = new DeviceEqRecord(key, apoPattern.Trim(), eqText.Trim(), presetName.Trim(), truePeakDb, DateTime.UtcNow);
            TrimOldestIfNeeded();
            Save();
        }
    }

    public IReadOnlyList<DeviceEqRecord> GetAll()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _records.Values.OrderBy(record => record.UpdatedUtc).ToArray();
        }
    }

    public void Remove(string deviceKey)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_records.Remove(string.IsNullOrWhiteSpace(deviceKey) ? "global" : deviceKey.Trim())) Save();
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(_path)) return;

        try
        {
            DeviceEqFile? file = JsonSerializer.Deserialize<DeviceEqFile>(File.ReadAllText(_path), _jsonOptions);
            foreach (DeviceEqRecord record in file?.Records ?? [])
            {
                if (string.IsNullOrWhiteSpace(record.DeviceKey)) continue;
                _records[record.DeviceKey] = record;
            }
        }
        catch (Exception ex)
        {
            _logger?.Error($"Device EQ store: corrupt config at {_path}, starting fresh. {ex.Message}");
            _records.Clear();
        }
    }

    private void TrimOldestIfNeeded()
    {
        foreach (string key in _records.Values.OrderByDescending(record => record.UpdatedUtc).Skip(MaxBlocks).Select(record => record.DeviceKey).ToArray())
        {
            _records.Remove(key);
        }
    }

    private void Save()
    {
        string dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(new DeviceEqFile { Records = _records.Values.OrderBy(record => record.UpdatedUtc).ToArray() }, _jsonOptions);
        string tmp = _path + ".tmp";

        try
        {
            File.WriteAllText(tmp, json);
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }
        catch (Exception ex)
        {
            _logger?.Error($"Device EQ store: failed to save {_path}. {ex.Message}");
            TryDelete(tmp);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private sealed class DeviceEqFile { public IReadOnlyList<DeviceEqRecord> Records { get; init; } = []; }
}