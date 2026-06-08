using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AutoEQ.Config;
using AutoEQ.Models;
using AutoEQ.Services;

namespace AutoEQ.Linux;

internal sealed class LinuxAudioCapture
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private readonly IDspAnalyzer _analyzer;

    public LinuxAudioCapture(IDspAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public static async Task<string> GetDefaultSinkAsync(CancellationToken cancellationToken = default)
    {
        CommandResult result = await CommandRunner.RunAsync("pactl", ["get-default-sink"], cancellationToken).ConfigureAwait(false);
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

    public async IAsyncEnumerable<AudioFeatures> CaptureFeaturesAsync(
        string sinkName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string monitorSource = sinkName.EndsWith(".monitor", StringComparison.OrdinalIgnoreCase)
            ? sinkName
            : $"{sinkName}.monitor";

        using Process process = StartParec(monitorSource);
        _ = Task.Run(async () =>
        {
            string error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.WriteLine(error.Trim());
            }
        }, cancellationToken);

        byte[] buffer = new byte[8192];
        var samples = new List<float>(SampleRate * Channels * AppConfig.AnalysisIntervalSeconds);
        int requiredSamples = SampleRate * Channels * AppConfig.AnalysisIntervalSeconds;

        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead <= 0) break;

            AppendPcm16(buffer, bytesRead, samples);
            if (samples.Count < requiredSamples) continue;

            float[] snapshot = samples.Take(requiredSamples).ToArray();
            samples.RemoveRange(0, Math.Min(samples.Count, requiredSamples));
            yield return _analyzer.Analyze(snapshot, Channels, SampleRate);
        }
    }

    private static Process StartParec(string monitorSource)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "parec",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            },
            EnableRaisingEvents = true
        };

        process.StartInfo.ArgumentList.Add($"--device={monitorSource}");
        process.StartInfo.ArgumentList.Add("--format=s16le");
        process.StartInfo.ArgumentList.Add($"--rate={SampleRate}");
        process.StartInfo.ArgumentList.Add($"--channels={Channels}");
        process.StartInfo.ArgumentList.Add("--raw");

        try
        {
            process.Start();
            return process;
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new InvalidOperationException("Cannot start 'parec'. On Ubuntu install it with: sudo apt install pulseaudio-utils", ex);
        }
    }

    private static void AppendPcm16(byte[] buffer, int bytesRead, List<float> samples)
    {
        int usableBytes = bytesRead - (bytesRead % 2);
        for (int i = 0; i < usableBytes; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            samples.Add(sample / 32768f);
        }
    }
}
