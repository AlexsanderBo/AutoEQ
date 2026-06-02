using NAudio.CoreAudioApi;
using System.Management;
using AutoEQ.Models;

namespace AutoEQ.Services;

public interface ISystemVolumeService
{
    AudioOutputInfo GetAudioOutputInfo();
    int GetMasterVolumePercent();
    void SetMasterVolumePercent(int percent);
}

public sealed class SystemVolumeService : ISystemVolumeService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _renderDevice;

    public AudioOutputInfo GetAudioOutputInfo()
    {
        using MMDevice defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
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
            ActiveOutputNames = outputNames
        };
    }

    private static string ReadMainboardName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (ManagementObject board in searcher.Get().Cast<ManagementObject>())
            {
                string manufacturer = Convert.ToString(board["Manufacturer"])?.Trim() ?? string.Empty;
                string product = Convert.ToString(board["Product"])?.Trim() ?? string.Empty;
                string name = string.Join(" ", new[] { manufacturer, product }.Where(part => !string.IsNullOrWhiteSpace(part)));
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch
        {
            // WMI can be unavailable on trimmed Windows installs; keep the UI usable.
        }

        return "Unknown mainboard";
    }

    private static (string Name, string Version) ReadSoundCardInfo(string fallbackName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_SoundDevice");
            (string Name, string Version)? firstDevice = null;
            foreach (ManagementObject soundDevice in searcher.Get().Cast<ManagementObject>())
            {
                string name = Convert.ToString(soundDevice["Name"])?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;

                string pnpDeviceId = Convert.ToString(soundDevice["PNPDeviceID"])?.Trim() ?? string.Empty;
                string version = ReadSoundDriverVersion(name, pnpDeviceId);
                firstDevice ??= (name, version);
                if (LooksLikeOnboardSound(name)) return (name, version);
            }

            if (firstDevice.HasValue) return firstDevice.Value;
        }
        catch
        {
            // Fall back to the active render endpoint if hardware inventory cannot be read.
        }

        return (string.IsNullOrWhiteSpace(fallbackName) ? "Unknown sound card" : fallbackName, "Unknown version");
    }

    private static string ReadSoundDriverVersion(string deviceName, string pnpDeviceId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceName, DeviceID, DriverVersion FROM Win32_PnPSignedDriver WHERE DeviceClass = 'MEDIA'");
            foreach (ManagementObject driver in searcher.Get().Cast<ManagementObject>())
            {
                string driverDeviceId = Convert.ToString(driver["DeviceID"])?.Trim() ?? string.Empty;
                string driverDeviceName = Convert.ToString(driver["DeviceName"])?.Trim() ?? string.Empty;
                bool sameDeviceId = !string.IsNullOrWhiteSpace(pnpDeviceId) && string.Equals(driverDeviceId, pnpDeviceId, StringComparison.OrdinalIgnoreCase);
                bool sameDeviceName = !string.IsNullOrWhiteSpace(driverDeviceName) && deviceName.Contains(driverDeviceName, StringComparison.OrdinalIgnoreCase);
                if (!sameDeviceId && !sameDeviceName) continue;

                string version = Convert.ToString(driver["DriverVersion"])?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(version)) return version;
            }
        }
        catch
        {
            // Driver version is optional; the hardware name is still enough for the main UI.
        }

        return "Unknown version";
    }

    private static bool LooksLikeOnboardSound(string name)
    {
        string normalized = name.ToLowerInvariant();
        string[] onboardKeywords = { "realtek", "high definition audio", "intel", "amd", "nvidia", "usb audio" };
        return onboardKeywords.Any(normalized.Contains);
    }

    public int GetMasterVolumePercent()
    {
        return (int)Math.Round(GetRenderDevice().AudioEndpointVolume.MasterVolumeLevelScalar * 100);
    }

    public void SetMasterVolumePercent(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        GetRenderDevice().AudioEndpointVolume.MasterVolumeLevelScalar = clamped / 100f;
    }

    private MMDevice GetRenderDevice()
    {
        if (_renderDevice == null || _renderDevice.State != DeviceState.Active)
        {
            _renderDevice?.Dispose();
            _renderDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        return _renderDevice;
    }
}