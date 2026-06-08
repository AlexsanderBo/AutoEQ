using NAudio.CoreAudioApi;
using NAudio.Wave;
using AutoEQ.Config;
using AutoEQ.Models;

namespace AutoEQ.Services;

public readonly record struct CaptureFormatInfo(int SampleRate, int BitsPerSample, int Channels);

public interface IAudioCaptureService : IDisposable
{
    event EventHandler<AudioFeatures>? FeaturesAvailable;
    event EventHandler<float[]>? WaveformAvailable;
    event EventHandler<string>? ErrorOccurred;
    event EventHandler<string>? DeviceChanged;
    event EventHandler<CaptureFormatInfo>? FormatChanged;
    string CurrentDeviceName { get; }
    bool IsRunning { get; }
    void Start();
    void Stop();
    void Restart();
}



public sealed class AudioCaptureService : IAudioCaptureService
{
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly IDspAnalyzer _analyzer;
    private readonly object _lock = new();
    private WasapiLoopbackCapture? _capture;
    private readonly List<float> _samples = new();
    private int _sampleRate;
    private int _channels;
    private bool _isRunning;
    private int _analysisInProgress;
    private int _restarting;


    public AudioCaptureService(IDspAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public event EventHandler<AudioFeatures>? FeaturesAvailable;
    public event EventHandler<float[]>? WaveformAvailable;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string>? DeviceChanged;
    public event EventHandler<CaptureFormatInfo>? FormatChanged;

    public IDspAnalyzer Analyzer => _analyzer;

    public string CurrentDeviceName { get; private set; } = "Unknown";

    public bool IsRunning => _isRunning;


    public void Start()
    {
        if (_isRunning) return;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using MMDevice device = SystemVolumeService.GetWindowsDefaultRenderEndpoint(enumerator);
            CurrentDeviceName = device.FriendlyName;
            DeviceChanged?.Invoke(this, CurrentDeviceName);

            _capture = new WasapiLoopbackCapture(device);
            _sampleRate = _capture.WaveFormat.SampleRate;
            _channels = _capture.WaveFormat.Channels;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _isRunning = true;
            FormatChanged?.Invoke(this, new CaptureFormatInfo(_sampleRate, _capture.WaveFormat.BitsPerSample, _channels));

        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Could not start WASAPI loopback capture: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        try
        {
            _isRunning = false;
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.StopRecording();
                _capture.Dispose();
                _capture = null;
            }

            lock (_lock)
            {
                _samples.Clear();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Could not stop capture cleanly: {ex.Message}");
        }
    }

    public void Restart()
    {
        // Default render endpoint đã đổi: dừng capture cũ rồi bám thiết bị mới.
        // Guard chống Restart chồng nhau khi đổi device liên tục (watcher đã debounce,
        // đây là lớp bảo vệ cuối tránh race Stop/Start).
        if (Interlocked.Exchange(ref _restarting, 1) == 1) return;
        try
        {
            Stop();
            Start();
        }
        finally
        {
            Interlocked.Exchange(ref _restarting, 0);
        }
    }


    private void OnDataAvailable(object? sender, WaveInEventArgs e)

    {
        try
        {
            WaveFormat? format = _capture?.WaveFormat;
            if (format == null) return;

            float[] block = ConvertToFloatSamples(e.Buffer, e.BytesRecorded, format);
            WaveformAvailable?.Invoke(this, BuildWaveform(block, format.Channels, 96));

            float[]? snapshot = null;
            lock (_lock)
            {
                _samples.AddRange(block);
                int required = _sampleRate * _channels * AppConfig.AnalysisIntervalSeconds;
                if (_samples.Count >= required)
                {
                    snapshot = _samples.Take(required).ToArray();
                    _samples.RemoveRange(0, required);
                }
            }

            if (snapshot != null && Interlocked.CompareExchange(ref _analysisInProgress, 1, 0) == 0)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        AudioFeatures features = _analyzer.Analyze(snapshot, _channels, _sampleRate);
                        FeaturesAvailable?.Invoke(this, features);
                    }
                    catch (Exception ex)
                    {
                        ErrorOccurred?.Invoke(this, $"Audio analysis failed: {ex.Message}");
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _analysisInProgress, 0);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Audio analysis failed: {ex.Message}");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            ErrorOccurred?.Invoke(this, $"Capture stopped: {e.Exception.Message}");
        }
    }

    private static float[] ConvertToFloatSamples(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        WaveFormatEncoding encoding = NormalizeEncoding(format);

        if (bytesRecorded <= 0 || format.BitsPerSample <= 0)
        {
            return Array.Empty<float>();
        }

        if (encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            float[] result = new float[bytesRecorded / 4];
            Buffer.BlockCopy(buffer, 0, result, 0, bytesRecorded);
            return result;
        }

        if (encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            int count = bytesRecorded / 2;
            float[] result = new float[count];
            for (int i = 0; i < count; i++)
            {
                short sample = BitConverter.ToInt16(buffer, i * 2);
                result[i] = sample / 32768f;
            }
            return result;
        }

        if (encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 24)
        {
            int count = bytesRecorded / 3;
            float[] result = new float[count];
            for (int i = 0; i < count; i++)
            {
                int offset = i * 3;
                int sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((sample & 0x800000) != 0) sample |= unchecked((int)0xFF000000);
                result[i] = sample / 8388608f;
            }
            return result;
        }

        if (encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 32)
        {
            int count = bytesRecorded / 4;
            float[] result = new float[count];
            for (int i = 0; i < count; i++)
            {
                int sample = BitConverter.ToInt32(buffer, i * 4);
                result[i] = sample / 2147483648f;
            }
            return result;
        }

        throw new NotSupportedException($"Unsupported capture format: {format.Encoding}, {format.BitsPerSample}-bit.");
    }

    private static WaveFormatEncoding NormalizeEncoding(WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Extensible || format is not WaveFormatExtensible extensible)
        {
            return format.Encoding;
        }

        if (extensible.SubFormat == IeeeFloatSubFormat)
        {
            return WaveFormatEncoding.IeeeFloat;
        }

        if (extensible.SubFormat == PcmSubFormat)
        {
            return WaveFormatEncoding.Pcm;
        }

        return format.Encoding;
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
                for (int ch = 0; ch < channels; ch++)
                {
                    mono += samples[offset + ch];
                }

                sum += Math.Abs(mono / channels);
                count++;
            }

            waveform[i] = count == 0 ? 0 : (float)Math.Clamp(sum / count * 3.2, 0, 1);
        }

        return waveform;
    }

    public void Dispose()
    {
        Stop();
    }
}