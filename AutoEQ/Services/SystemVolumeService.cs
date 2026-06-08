using System.Management;
using NAudio.CoreAudioApi;
using AutoEQ.Models;

namespace AutoEQ.Services;

public interface ISystemVolumeService
{
    AudioOutputInfo GetAudioOutputInfo();
    int GetMasterVolumePercent();
    bool GetMute();
    event EventHandler<SystemVolumeChangedEventArgs>? VolumeChanged;
}

public sealed class SystemVolumeChangedEventArgs : EventArgs
{
    public SystemVolumeChangedEventArgs(int volumePercent, bool isMuted, string deviceId, string deviceName)
    {
        VolumePercent = volumePercent;
        IsMuted = isMuted;
        DeviceId = deviceId;
        DeviceName = deviceName;
    }

    public int VolumePercent { get; }
    public bool IsMuted { get; }
    public string DeviceId { get; }
    public string DeviceName { get; }
}

public sealed class SystemVolumeService : ISystemVolumeService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _gate = new();
    private MMDevice? _renderDevice;
    private AudioEndpointVolumeNotificationDelegate? _volumeNotification;

    public event EventHandler<SystemVolumeChangedEventArgs>? VolumeChanged;

    public AudioOutputInfo GetAudioOutputInfo()
    {
        using MMDevice defaultDevice = GetWindowsDefaultRenderEndpoint(_enumerator);
        MMDeviceCollection activeOutputs = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        string[] outputNames = activeOutputs
            .Select(device => device.FriendlyName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        (string soundCardName, string soundCardVersion) = ReadSoundCardInfo(defaultDevice.FriendlyName);

        return new AudioOutputInfo
        {
            DefaultDeviceName = defaultDevice.FriendlyName,
            DefaultDeviceId = defaultDevice.ID,
            MainboardName = ReadMainboardName(),
            SoundCardName = soundCardName,
            SoundCardVersion = soundCardVersion,
            Role = "Console",
            ActiveOutputNames = outputNames
        };
    }

    internal static MMDevice GetWindowsDefaultRenderEndpoint(MMDeviceEnumerator enumerator)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        }
        catch
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
    }

    private static string ReadMainboardName()
    {
        // WMI Win32_BaseBoard cho hãng + model mainboard thật; fallback về tên máy nếu thất bại.
        if (!OperatingSystem.IsWindows())
            return string.IsNullOrWhiteSpace(Environment.MachineName) ? "Unknown machine" : Environment.MachineName;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (ManagementObject board in searcher.Get())
            {
                string manufacturer = (board["Manufacturer"] as string)?.Trim() ?? "";
                string product = (board["Product"] as string)?.Trim() ?? "";
                string name = $"{manufacturer} {product}".Trim();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch
        {
            // WMI có thể bị chặn/không khả dụng; rơi xuống fallback.
        }

        return string.IsNullOrWhiteSpace(Environment.MachineName) ? "Unknown machine" : Environment.MachineName;
    }

    private static (string Name, string Version) ReadSoundCardInfo(string fallbackName)
    {
        // Win32_SoundDevice cho tên sound card; driver version lấy từ Win32_PnPSignedDriver khớp theo tên.
        if (!OperatingSystem.IsWindows())
            return (string.IsNullOrWhiteSpace(fallbackName) ? "Unknown sound card" : fallbackName, "Windows endpoint");

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, ProductName FROM Win32_SoundDevice");
            foreach (ManagementObject sound in searcher.Get())
            {
                string name = (sound["ProductName"] as string)?.Trim()
                              ?? (sound["Name"] as string)?.Trim()
                              ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;

                string version = ReadDriverVersion(name);
                return (name, string.IsNullOrWhiteSpace(version) ? "driver n/a" : version);
            }
        }
        catch
        {
            // WMI không khả dụng; dùng tên endpoint làm fallback.
        }

        return (string.IsNullOrWhiteSpace(fallbackName) ? "Unknown sound card" : fallbackName, "Windows endpoint");
    }

    private static string ReadDriverVersion(string deviceName)
    {
        if (!OperatingSystem.IsWindows()) return "";

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion FROM Win32_PnPSignedDriver WHERE DeviceClass = 'MEDIA'");
            foreach (ManagementObject driver in searcher.Get())
            {
                string driverDevice = (driver["DeviceName"] as string)?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(driverDevice)) continue;

                // Khớp lỏng: tên thiết bị WMI sound thường trùng/chứa tên driver MEDIA.
                if (driverDevice.Contains(deviceName, StringComparison.OrdinalIgnoreCase)
                    || deviceName.Contains(driverDevice, StringComparison.OrdinalIgnoreCase))
                {
                    string version = (driver["DriverVersion"] as string)?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(version)) return $"v{version}";
                }
            }
        }
        catch
        {
            // Bỏ qua, trả rỗng để caller dùng nhãn mặc định.
        }

        return "";
    }

    public int GetMasterVolumePercent()
    {
        return (int)Math.Round(GetRenderDevice().AudioEndpointVolume.MasterVolumeLevelScalar * 100);
    }

    public bool GetMute()
    {
        return GetRenderDevice().AudioEndpointVolume.Mute;
    }

    private MMDevice GetRenderDevice()
    {
        lock (_gate)
        {
            MMDevice defaultDevice = GetWindowsDefaultRenderEndpoint(_enumerator);
            bool mustSwap = _renderDevice == null
                || _renderDevice.State != DeviceState.Active
                || !string.Equals(_renderDevice.ID, defaultDevice.ID, StringComparison.OrdinalIgnoreCase);

            if (mustSwap)
            {
                // Vì default endpoint có thể đổi khi app đang chạy, bỏ callback cũ rồi đăng ký lại cho thiết bị mới.
                UnregisterVolumeCallback();
                _renderDevice?.Dispose();
                _renderDevice = defaultDevice;
                RegisterVolumeCallback(_renderDevice);
            }
            else
            {
                defaultDevice.Dispose();
            }

            return _renderDevice ?? throw new InvalidOperationException("Không lấy được default render audio endpoint.");
        }
    }

    private void RegisterVolumeCallback(MMDevice device)
    {
        _volumeNotification = data =>
        {
            int percent = (int)Math.Round(data.MasterVolume * 100);
            VolumeChanged?.Invoke(this, new SystemVolumeChangedEventArgs(percent, data.Muted, device.ID, device.FriendlyName));
        };
        device.AudioEndpointVolume.OnVolumeNotification += _volumeNotification;
    }

    private void UnregisterVolumeCallback()
    {
        if (_renderDevice is null || _volumeNotification is null) return;

        try
        {
            _renderDevice.AudioEndpointVolume.OnVolumeNotification -= _volumeNotification;
        }
        catch
        {
            // Vì endpoint có thể đã biến mất, bỏ qua để app tiếp tục bám default mới.
        }

        _volumeNotification = null;
    }
}
