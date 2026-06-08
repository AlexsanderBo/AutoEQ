using AutoEQ.Models;

namespace AutoEQ.Services;

public interface ICalibrationService
{
    event EventHandler<CalibrationReadyEventArgs>? CalibrationReady;
    void Observe(string deviceKey, AudioFeatures features, IReadOnlyList<double>? appliedGains = null);
    double GetProgress(string deviceKey);
    void Reset(string deviceKey);
}

public sealed class CalibrationReadyEventArgs : EventArgs
{
    public required string DeviceKey { get; init; }
    public required double[] MeasuredBandAverage { get; init; }
    public required double CentroidAverageHz { get; init; }
    public required int SampleCount { get; init; }
}

public sealed class CalibrationService : ICalibrationService
{
    public const int DefaultRequiredSamples = 180;

    private readonly object _gate = new();
    private readonly int _requiredSamples;
    private readonly Dictionary<string, CalibrationAccumulator> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAppLogger? _logger;

    public event EventHandler<CalibrationReadyEventArgs>? CalibrationReady;

    public CalibrationService(IAppLogger? logger = null, int requiredSamples = DefaultRequiredSamples)
    {
        _logger = logger;
        _requiredSamples = Math.Max(8, requiredSamples);
    }

    public void Observe(string deviceKey, AudioFeatures features, IReadOnlyList<double>? appliedGains = null)
    {
        string key = NormalizeKey(deviceKey);
        CalibrationReadyEventArgs? ready = null;

        lock (_gate)
        {
            if (!IsUsableFrame(features)) return;
            CalibrationAccumulator state = GetState(key);
            if (state.ReadyRaised) return;

            double[] bands = CompensateFeedback(ReadBands(features), appliedGains);
            state.Add(bands, features.SpectralCentroidHz, features.SpectralFlatness);

            if (state.Count >= _requiredSamples && state.IsStable())
            {
                state.ReadyRaised = true;
                ready = new CalibrationReadyEventArgs
                {
                    DeviceKey = key,
                    MeasuredBandAverage = state.Mean.ToArray(),
                    CentroidAverageHz = state.CentroidMean,
                    SampleCount = state.Count
                };
            }
        }

        if (ready is not null)
        {
            _logger?.Decision("Calibration", $"ready device={ready.DeviceKey} samples={ready.SampleCount}");
            CalibrationReady?.Invoke(this, ready);
        }
    }

    public double GetProgress(string deviceKey)
    {
        lock (_gate)
        {
            return _states.TryGetValue(NormalizeKey(deviceKey), out CalibrationAccumulator? state)
                ? Math.Clamp((double)state.Count / _requiredSamples, 0, 1)
                : 0;
        }
    }

    public void Reset(string deviceKey)
    {
        lock (_gate)
        {
            _states.Remove(NormalizeKey(deviceKey));
        }
    }

    private CalibrationAccumulator GetState(string key)
    {
        if (_states.TryGetValue(key, out CalibrationAccumulator? state)) return state;
        state = new CalibrationAccumulator();
        _states[key] = state;
        return state;
    }

    private static bool IsUsableFrame(AudioFeatures f)
    {
        // RMS below 0.015 is near silence/noise floor, so spectrum shape is mostly random capture noise.
        if (f.Rms < 0.015) return false;
        // Very high RMS or near-full-scale peak means clipping/limiter distortion, not device voicing.
        if (f.Rms > 0.28 || f.Peak > 0.98) return false;
        // High spectral flatness indicates broadband noise; music has lower, structured spectrum.
        if (f.SpectralFlatness > 0.72) return false;
        return ReadBands(f).All(v => double.IsFinite(v) && v >= 0);
    }

    private static double[] ReadBands(AudioFeatures f) => [f.SubBass, f.Bass, f.LowMid, f.Mid, f.Presence, f.Treble, f.Air];

    public static double[] CompensateFeedback(double[] bands, IReadOnlyList<double>? appliedGains)
    {
        double[] result = bands.ToArray();
        if (appliedGains is null || appliedGains.Count < 14) return result;

        double[] macroGain =
        [
            Avg(appliedGains, 0, 1),
            Avg(appliedGains, 1, 2),
            Avg(appliedGains, 3, 5),
            Avg(appliedGains, 6, 8),
            Avg(appliedGains, 8, 10),
            Avg(appliedGains, 11, 12),
            Avg(appliedGains, 12, 13)
        ];

        for (int i = 0; i < result.Length; i++)
        {
            double compensation = Math.Clamp(macroGain[i] / 24.0, -0.20, 0.20);
            result[i] = Math.Max(0, result[i] - compensation);
        }

        return result;
    }

    private static double Avg(IReadOnlyList<double> values, int start, int end)
    {
        double sum = 0;
        int count = 0;
        for (int i = start; i <= end && i < values.Count; i++)
        {
            sum += values[i];
            count++;
        }
        return count == 0 ? 0 : sum / count;
    }

    private static string NormalizeKey(string deviceKey) => string.IsNullOrWhiteSpace(deviceKey) ? "global" : deviceKey.Trim();

    private sealed class CalibrationAccumulator
    {
        public int Count { get; private set; }
        public bool ReadyRaised { get; set; }
        public double[] Mean { get; } = new double[7];
        private readonly double[] _m2 = new double[7];
        public double CentroidMean { get; private set; }
        private double _flatnessMean;

        public void Add(double[] bands, double centroidHz, double flatness)
        {
            Count++;
            for (int i = 0; i < Mean.Length; i++)
            {
                double delta = bands[i] - Mean[i];
                Mean[i] += delta / Count;
                _m2[i] += delta * (bands[i] - Mean[i]);
            }
            CentroidMean += (centroidHz - CentroidMean) / Count;
            _flatnessMean += (flatness - _flatnessMean) / Count;
        }

        public bool IsStable()
        {
            if (Count < 2) return false;
            double variance = _m2.Sum() / Math.Max(1, Count - 1);
            return variance < 0.10 && _flatnessMean < 0.60;
        }
    }
}