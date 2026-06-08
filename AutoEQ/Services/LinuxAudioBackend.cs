using AutoEQ.Models;

namespace AutoEQ.Services;

internal static class LinuxAudioBackend
{
    public static async Task<string> GetDefaultSinkAsync(CancellationToken cancellationToken = default)
    {
        LinuxCommandResult result = await LinuxCommandRunner.RunAsync("pactl", ["get-default-sink"], cancellationToken).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            throw new InvalidOperationException("Cannot read the default PulseAudio/PipeWire sink. Install pulseaudio-utils and make sure PipeWire/PulseAudio is running.");
        }

        return result.Output.Trim();
    }

    public static AudioOutputInfo BuildOutputInfo(string defaultSink) => new()
    {
        DefaultDeviceName = defaultSink,
        DefaultDeviceId = defaultSink,
        MainboardName = Environment.MachineName,
        SoundCardName = "PipeWire/PulseAudio",
        SoundCardVersion = "Linux",
        Role = "Console",
        ActiveOutputNames = [defaultSink]
    };
}
