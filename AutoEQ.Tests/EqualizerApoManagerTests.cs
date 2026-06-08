using System.IO;
using AutoEQ.Config;
using AutoEQ.Models;
using AutoEQ.Services;

namespace AutoEQ.Tests;

/// <summary>
/// Integration tests for <see cref="EqualizerApoManager"/> that write to a real
/// temp directory (never the live Equalizer APO install) via an injected
/// <see cref="IAutoEqConfig"/> override. Each test isolates its own folder.
/// </summary>
public sealed class EqualizerApoManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AutoEqConfig _config;
    private readonly EqualizerApoManager _manager;
    private readonly DeviceEqStore _store;

    public EqualizerApoManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "autoeq_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _config = new AutoEqConfig(_tempDir);
        _store = new DeviceEqStore(Path.Combine(_tempDir, "device-eq.json"));
        _manager = new EqualizerApoManager(_config, _store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort temp cleanup */ }
    }

    [Fact]
    public void IsInstalled_TrueWhenConfigDirExists()
    {
        Assert.True(_manager.IsInstalled());
    }

    [Fact]
    public async Task EnsureIncludeLineAsync_AddsIncludeOnce_AndBacksUp()
    {
        await _manager.EnsureIncludeLineAsync();

        string config = await File.ReadAllTextAsync(_config.EqualizerApoMainConfig);
        Assert.Contains("Include: AutoEQ_autoeq.txt", config);

        // Calling again must not duplicate the include line.
        await _manager.EnsureIncludeLineAsync();
        config = await File.ReadAllTextAsync(_config.EqualizerApoMainConfig);
        int count = config.Split("Include: AutoEQ_autoeq.txt").Length - 1;
        Assert.Equal(1, count);

        // A backup is produced on the first mutation.
        Assert.True(File.Exists(_config.EqualizerApoMainConfig + ".bak"));
    }

    [Fact]
    public async Task WriteAutoEQConfigAsync_WritesAtomically_WithHeaderAndBody()
    {
        var preset = new EqPreset
        {
            Name = "Test Preset",
            EqualizerApoText = "Preamp: -3.0 dB\r\nFilter: ON PK Fc 100 Hz Gain 2.0 dB Q 0.70",
            IsDynamic = true
        };

        await _manager.WriteAutoEQConfigAsync(preset.EqualizerApoText, preset, "unit test");

        Assert.True(File.Exists(_config.AutoEqEqFile));
        string body = await File.ReadAllTextAsync(_config.AutoEqEqFile);

        Assert.Contains("# Preset: Test Preset", body);
        Assert.Contains("# Reason:", body);
        Assert.Contains("Filter: ON PK Fc 100 Hz", body);

        // No leftover temp file after the atomic swap.
        Assert.False(File.Exists(_config.AutoEqEqFile + ".tmp"));
    }

    [Fact]
    public async Task WriteAutoEQConfigAsync_DoesNotChangePreamp_WhenTruePeakHot()
    {
        var preset = new EqPreset
        {
            Name = "Hot Peak",
            EqualizerApoText = "Preamp: 0.0 dB\r\nFilter: ON PK Fc 60 Hz Gain 6.0 dB Q 0.70",
            TruePeakDb = 0.5, // above the -1 dBFS ceiling -> guard must engage
            IsDynamic = true
        };

        await _manager.WriteAutoEQConfigAsync(preset.EqualizerApoText, preset);

        string body = await File.ReadAllTextAsync(_config.AutoEqEqFile);
        Assert.DoesNotContain("Safety limiter guard", body);
        Assert.Equal(1, body.Split("Preamp:").Length - 1);
        Assert.Contains("Preamp: 0.0 dB", body);
    }

    [Fact]
    public async Task WriteAutoEQConfigAsync_Overwrites_OnSecondWrite()
    {
        var first = new EqPreset { Name = "First", EqualizerApoText = "Preamp: -3.0 dB" };
        var second = new EqPreset { Name = "Second", EqualizerApoText = "Preamp: -4.0 dB" };

        await _manager.WriteAutoEQConfigAsync(first.EqualizerApoText, first);
        await _manager.WriteAutoEQConfigAsync(second.EqualizerApoText, second);

        string body = await File.ReadAllTextAsync(_config.AutoEqEqFile);
        Assert.Contains("# Preset: Second", body);
        Assert.DoesNotContain("# Preset: First", body);
    }

    [Fact]
    public async Task WriteAutoEQConfigAsync_Throws_WhenConfigDirMissing()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), "autoeq_missing_" + Guid.NewGuid().ToString("N"));
        var manager = new EqualizerApoManager(new AutoEqConfig(missingDir));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => manager.WriteAutoEQConfigAsync("Preamp: -3.0 dB"));
    }

    [Fact]
    public async Task WriteDeviceScopedConfigAsync_AppliesTwoDevices_AndKeepsBlocksIndependent()
    {
        var presetA = new EqPreset { Name = "A", EqualizerApoText = "Preamp: -3.0 dB\r\nFilter: ON PK Fc 60 Hz Gain 1.0 dB Q 0.70" };
        var presetB = new EqPreset { Name = "B", EqualizerApoText = "Preamp: -4.0 dB\r\nFilter: ON PK Fc 1000 Hz Gain -2.0 dB Q 1.00" };

        await _manager.WriteDeviceScopedConfigAsync("{0.0.0.00000000}.{aaa}", "{0.0.0.00000000}.{aaa}", presetA.EqualizerApoText, presetA);
        await _manager.WriteDeviceScopedConfigAsync("{0.0.0.00000000}.{bbb}", "{0.0.0.00000000}.{bbb}", presetB.EqualizerApoText, presetB);

        string body = await File.ReadAllTextAsync(_config.AutoEqEqFile);
        Assert.Contains("Device: {0.0.0.00000000}.{aaa}", body);
        Assert.Contains("Device: {0.0.0.00000000}.{bbb}", body);
        Assert.Contains("Filter: ON PK Fc 60 Hz", body);
        Assert.Contains("Filter: ON PK Fc 1000 Hz", body);

        var updatedA = new EqPreset { Name = "A2", EqualizerApoText = "Preamp: -5.0 dB\r\nFilter: ON PK Fc 200 Hz Gain -1.0 dB Q 0.80" };
        await _manager.WriteDeviceScopedConfigAsync("{0.0.0.00000000}.{aaa}", "{0.0.0.00000000}.{aaa}", updatedA.EqualizerApoText, updatedA);

        body = await File.ReadAllTextAsync(_config.AutoEqEqFile);
        Assert.Contains("# Preset: A2", body);
        Assert.DoesNotContain("# Preset: A\r", body);
        Assert.Contains("# Preset: B", body);
        Assert.Contains("Filter: ON PK Fc 1000 Hz", body);
        Assert.Equal(2, body.Split("Device:").Length - 1);
    }

    [Fact]
    public async Task WriteDeviceScopedConfigAsync_PreservesPreampPerBlock()
    {
        var hot = new EqPreset { Name = "Hot", EqualizerApoText = "Preamp: 0.0 dB\r\nFilter: ON PK Fc 60 Hz Gain 6.0 dB Q 0.70", TruePeakDb = 0.5 };
        var safe = new EqPreset { Name = "Safe", EqualizerApoText = "Preamp: 0.0 dB\r\nFilter: ON PK Fc 1000 Hz Gain 0.5 dB Q 1.00" };

        await _manager.WriteDeviceScopedConfigAsync("hot", "{hot}", hot.EqualizerApoText, hot);
        await _manager.WriteDeviceScopedConfigAsync("safe", "{safe}", safe.EqualizerApoText, safe);

        string body = await File.ReadAllTextAsync(_config.AutoEqEqFile);
        Assert.DoesNotContain("Safety limiter guard", body);
        Assert.Contains("Device: {hot}", body);
        Assert.Contains("Preamp: 0.0 dB", body);
        Assert.Contains("Device: {safe}", body);
        Assert.Equal(2, body.Split("Preamp: 0.0 dB").Length - 1);
    }

    [Fact]
    public async Task ApplyPresetAsync_UsesEndpointId_AsDeviceKeyAndPattern()
    {
        var preset = new EqPreset { Name = "Endpoint", EqualizerApoText = "Preamp: -3.0 dB" };
        var info = new AudioOutputInfo { DefaultDeviceId = "{0.0.0.00000000}.{endpoint}", DefaultDeviceName = "USB DAC" };

        await _manager.ApplyPresetAsync(preset, info, "unit");

        string body = await File.ReadAllTextAsync(_config.AutoEqEqFile);
        Assert.Contains("Device: {0.0.0.00000000}.{endpoint}", body);
        Assert.Equal("{0.0.0.00000000}.{endpoint}", _store.GetAll().Single().DeviceKey);
        Assert.Equal("{0.0.0.00000000}.{endpoint}", _store.GetAll().Single().ApoMatchPattern);
    }

    [Fact]
    public async Task ApplyPresetAsync_FallsBackToNameToken_WhenEndpointIdMissing()
    {
        var preset = new EqPreset { Name = "Fallback", EqualizerApoText = "Preamp: -3.0 dB" };
        var info = new AudioOutputInfo { DefaultDeviceName = "Default (Speakers) USB DAC" };

        await _manager.ApplyPresetAsync(preset, info, "unit");

        string body = await File.ReadAllTextAsync(_config.AutoEqEqFile);
        Assert.Contains("Device: USB DAC", body);
        Assert.Equal("name:USB DAC", _store.GetAll().Single().DeviceKey);
    }
}
