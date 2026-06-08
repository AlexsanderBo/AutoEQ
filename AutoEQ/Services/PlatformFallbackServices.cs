using AutoEQ.Models;

namespace AutoEQ.Services;

internal sealed class NullAudioCaptureService : IAudioCaptureService
{
    private readonly string _message;

    public NullAudioCaptureService(string message)
    {
        _message = message;
    }

    public event EventHandler<AudioFeatures>? FeaturesAvailable { add { } remove { } }
    public event EventHandler<float[]>? WaveformAvailable { add { } remove { } }
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string>? DeviceChanged { add { } remove { } }
    public event EventHandler<CaptureFormatInfo>? FormatChanged { add { } remove { } }

    public string CurrentDeviceName => "Unsupported OS";

    public bool IsRunning => false;

    public void Start() => ErrorOccurred?.Invoke(this, _message);

    public void Stop() { }

    public void Restart() => Start();

    public void Dispose() { }
}

internal sealed class NullSystemVolumeService : ISystemVolumeService
{
    public event EventHandler<SystemVolumeChangedEventArgs>? VolumeChanged { add { } remove { } }

    public AudioOutputInfo GetAudioOutputInfo() => new()
    {
        DefaultDeviceName = Environment.MachineName,
        DefaultDeviceId = Environment.MachineName,
        MainboardName = Environment.MachineName,
        SoundCardName = "Unknown audio backend",
        SoundCardVersion = Environment.OSVersion.Platform.ToString(),
        ActiveOutputNames = [Environment.MachineName]
    };

    public int GetMasterVolumePercent() => 0;

    public bool GetMute() => false;
}

internal sealed class NullDefaultDeviceWatcher : IDefaultDeviceWatcher
{
    public event EventHandler<string>? DefaultRenderDeviceChanged { add { } remove { } }

    public void Start() { }

    public void Dispose() { }
}

internal sealed class NullNativeAutoEqClient : INativeAutoEqClient
{
    public event EventHandler<NativeAutoEqSnapshot>? SnapshotAvailable { add { } remove { } }
    public event EventHandler<string>? ErrorOccurred { add { } remove { } }

    public bool IsAvailable() => false;

    public void StartMonitoring(TimeSpan interval, TimeSpan timeout) { }

    public void StopMonitoring() { }

    public Task<NativeAutoEqSnapshot?> CaptureSnapshotAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => Task.FromResult<NativeAutoEqSnapshot?>(null);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
