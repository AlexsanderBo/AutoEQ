using NAudio.CoreAudioApi;
using NAudio.Wave;
using WoburnAutoEQ.Config;
using WoburnAutoEQ.Models;

namespace WoburnAutoEQ.Services;

public sealed class AudioCaptureService : IDisposable
{
    private readonly DspAnalyzer _analyzer = new();
    private readonly object _lock = new();
    private WasapiLoopbackCapture? _capture;
    private readonly List<float> _samples = new();
    private int _sampleRate;
    private int _channels;
    private bool _isRunning;

    public event EventHandler<AudioFeatures>? FeaturesAvailable;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string>? DeviceChanged;

    public string CurrentDeviceName { get; private set; } = "Unknown";

    public void Start()
    {
        if (_isRunning) return;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            CurrentDeviceName = device.FriendlyName;
            DeviceChanged?.Invoke(this, CurrentDeviceName);

            _capture = new WasapiLoopbackCapture(device);
            _sampleRate = _capture.WaveFormat.SampleRate;
            _channels = _capture.WaveFormat.Channels;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _isRunning = true;
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

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            WaveFormat? format = _capture?.WaveFormat;
            if (format == null) return;

            float[] block = ConvertToFloatSamples(e.Buffer, e.BytesRecorded, format);

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

            if (snapshot != null)
            {
                _ = Task.Run(() =>
                {
                    AudioFeatures features = _analyzer.Analyze(snapshot, _channels, _sampleRate);
                    FeaturesAvailable?.Invoke(this, features);
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
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            float[] result = new float[bytesRecorded / 4];
            Buffer.BlockCopy(buffer, 0, result, 0, bytesRecorded);
            return result;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
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

        throw new NotSupportedException($"Unsupported capture format: {format.Encoding}, {format.BitsPerSample}-bit.");
    }

    public void Dispose()
    {
        Stop();
    }
}