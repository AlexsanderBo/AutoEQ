using AutoEQ.Config;

namespace AutoEQ.Services;

internal static class PlatformServiceFactory
{
    public static IAudioCaptureService CreateAudioCapture(IDspAnalyzer analyzer)
    {
        if (OperatingSystem.IsWindows()) return new AudioCaptureService(analyzer);
        if (OperatingSystem.IsLinux()) return new LinuxAudioCaptureService(analyzer);
        return new NullAudioCaptureService("Audio capture is not supported on this OS.");
    }

    public static ISystemVolumeService CreateSystemVolumeService()
    {
        if (OperatingSystem.IsWindows()) return new SystemVolumeService();
        if (OperatingSystem.IsLinux()) return new LinuxSystemVolumeService();
        return new NullSystemVolumeService();
    }

    public static IEqualizerApoManager CreateEqualizerManager(IAutoEqConfig config, IAppLogger logger)
    {
        if (OperatingSystem.IsLinux()) return new PipeWireEqualizerManager(logger);
        return new EqualizerApoManager(config, logger: logger);
    }

    public static INowPlayingService CreateNowPlayingService()
    {
        return OperatingSystem.IsWindows() ? new WindowsAudioSessionNowPlayingService() : new NowPlayingService();
    }

    public static INativeAutoEqClient CreateNativeAutoEqClient()
    {
        return OperatingSystem.IsWindows() ? new NativeWasapiAutoEqClient() : new NullNativeAutoEqClient();
    }

    public static IDefaultDeviceWatcher CreateDefaultDeviceWatcher()
    {
        if (OperatingSystem.IsWindows()) return new DefaultDeviceWatcher();
        if (OperatingSystem.IsLinux()) return new LinuxDefaultDeviceWatcher();
        return new NullDefaultDeviceWatcher();
    }
}
