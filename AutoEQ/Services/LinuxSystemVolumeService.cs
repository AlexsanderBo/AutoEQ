using System.Text.RegularExpressions;
using AutoEQ.Models;

namespace AutoEQ.Services;

internal sealed partial class LinuxSystemVolumeService : ISystemVolumeService
{
    public event EventHandler<SystemVolumeChangedEventArgs>? VolumeChanged { add { } remove { } }

    public AudioOutputInfo GetAudioOutputInfo()
    {
        try
        {
            string sink = LinuxAudioBackend.GetDefaultSinkAsync().GetAwaiter().GetResult();
            return LinuxAudioBackend.BuildOutputInfo(sink);
        }
        catch
        {
            return new AudioOutputInfo
            {
                DefaultDeviceName = "PipeWire/PulseAudio",
                DefaultDeviceId = "linux-audio",
                MainboardName = Environment.MachineName,
                SoundCardName = "PipeWire/PulseAudio",
                SoundCardVersion = "Linux",
                ActiveOutputNames = ["PipeWire/PulseAudio"]
            };
        }
    }

    public int GetMasterVolumePercent()
    {
        try
        {
            LinuxCommandResult result = LinuxCommandRunner.RunAsync("pactl", ["get-sink-volume", "@DEFAULT_SINK@"]).GetAwaiter().GetResult();
            if (!result.Success) return 0;

            Match match = PercentRegex().Match(result.Output);
            return match.Success && int.TryParse(match.Groups["value"].Value, out int value)
                ? Math.Clamp(value, 0, 150)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    public bool GetMute()
    {
        try
        {
            LinuxCommandResult result = LinuxCommandRunner.RunAsync("pactl", ["get-sink-mute", "@DEFAULT_SINK@"]).GetAwaiter().GetResult();
            return result.Success && result.Output.Contains("yes", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [GeneratedRegex(@"(?<value>\d+)%")]
    private static partial Regex PercentRegex();
}
