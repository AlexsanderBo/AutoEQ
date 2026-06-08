using System.ComponentModel;
using System.Diagnostics;
using AutoEQ.Config;
using AutoEQ.Models;

namespace AutoEQ.Services;

internal sealed class LinuxAudioCaptureService : IAudioCaptureService
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private readonly IDspAnalyzer _analyzer;
    private readonly object _gate = new();
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private Process? _process;
    private bool _isRunning;

    public LinuxAudioCaptureService(IDspAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public event EventHandler<AudioFeatures>? FeaturesAvailable;
    public event EventHandler<float[]>? WaveformAvailable;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string>? DeviceChanged;
    public event EventHandler<CaptureFormatInfo>? FormatChanged;

    public string CurrentDeviceName { get; private set; } = "PipeWire/PulseAudio";

    public bool IsRunning => _isRunning;

    public void Start()
    {
        lock (_gate)
        {
            if (_captureTask is { IsCompleted: false }) return;
            _captureCts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoopAsync(_captureCts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Process? process;
        lock (_gate)
        {
            cts = _captureCts;
            process = _process;
            _captureCts = null;
            _process = null;
        }

        cts?.Cancel();
        TryKill(process);
        cts?.Dispose();
        _isRunning = false;
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        var samples = new List<float>(SampleRate * Channels * AppConfig.AnalysisIntervalSeconds);
        int requiredSamples = SampleRate * Channels * AppConfig.AnalysisIntervalSeconds;
        byte[] buffer = new byte[8192];

        try
        {
            string sink = await LinuxAudioBackend.GetDefaultSinkAsync(cancellationToken).ConfigureAwait(false);
            CurrentDeviceName = sink;
            DeviceChanged?.Invoke(this, sink);
            FormatChanged?.Invoke(this, new CaptureFormatInfo(SampleRate, 16, Channels));

            using Process process = StartParec(sink);
            lock (_gate) _process = process;
            _isRunning = true;

            _ = Task.Run(async () =>
            {
                string error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(error)) ErrorOccurred?.Invoke(this, error.Trim());
            }, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead <= 0) break;

                float[] block = ConvertPcm16(buffer, bytesRead);
                WaveformAvailable?.Invoke(this, BuildWaveform(block, Channels, 96));
                samples.AddRange(block);

                if (samples.Count < requiredSamples) continue;

                float[] snapshot = samples.Take(requiredSamples).ToArray();
                samples.RemoveRange(0, Math.Min(samples.Count, requiredSamples));

                try
                {
                    AudioFeatures features = _analyzer.Analyze(snapshot, Channels, SampleRate);
                    FeaturesAvailable?.Invoke(this, features);
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Linux audio analysis failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            _isRunning = false;
        }
    }

    private static Process StartParec(string sinkName)
    {
        string monitorSource = sinkName.EndsWith(".monitor", StringComparison.OrdinalIgnoreCase)
            ? sinkName
            : $"{sinkName}.monitor";

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

    private static float[] ConvertPcm16(byte[] buffer, int bytesRead)
    {
        int usableBytes = bytesRead - (bytesRead % 2);
        float[] result = new float[usableBytes / 2];
        for (int i = 0; i < usableBytes; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            result[i / 2] = sample / 32768f;
        }

        return result;
    }

    private static float[] BuildWaveform(float[] samples, int channels, int points)
    {
        if (samples.Length == 0 || channels <= 0) return Array.Empty<float>();

        float[] waveform = new float[points];
        int frames = samples.Length / channels;
        int framesPerPoint = Math.Max(1, frames / points);

        for (int i = 0; i < points; i++)
        {
            int startFrame = i * framesPerPoint;
            int endFrame = Math.Min(frames, startFrame + framesPerPoint);
            if (startFrame >= endFrame) break;

            double sum = 0;
            int count = 0;
            for (int frame = startFrame; frame < endFrame; frame++)
            {
                int offset = frame * channels;
                double mono = 0;
                for (int ch = 0; ch < channels; ch++) mono += samples[offset + ch];
                sum += Math.Abs(mono / channels);
                count++;
            }

            waveform[i] = count == 0 ? 0 : (float)Math.Clamp(sum / count * 3.2, 0, 1);
        }

        return waveform;
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    public void Dispose() => Stop();
}
