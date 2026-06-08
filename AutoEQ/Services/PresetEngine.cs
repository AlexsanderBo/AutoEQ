using AutoEQ.Config;
using AutoEQ.Models;

namespace AutoEQ.Services;

/// <summary>
/// Named tuning constants for the dynamic AutoEQ voicing engine.
/// Centralizes every "magic number" so the sonic intent is explicit and unit-testable.
/// </summary>
public static class VoicingCoefficients
{
    // --- Smoothing / movement limits ---
    public const double MaxFeedbackCompensationDb = 4.5;
    public const double FirstDynamicSmoothing = 0.25;
    public const double DynamicSmoothing = 0.10;
    public const double MaxFirstDynamicStepDb = 0.45;
    public const double MaxDynamicStepDb = 0.24;
    public const double MaxPresenceIncreaseStepDb = 0.16;
    public const double MaxTrebleIncreaseStepDb = 0.10;
    public const double MaxAirIncreaseStepDb = 0.12;
    public const double MinAudibleDynamicChangeDb = 0.12;

    // --- Correction gain scaling (target - measured) * factor ---
    public const double BassCorrectionScale = 10.0;
    public const double WarmthCorrectionScale = 12.0;
    public const double VocalCorrectionScale = 8.5;
    public const double BrightCorrectionScale = 9.0;

    // --- Loudness-aware voicing thresholds ---
    public const double LowLevelRmsCeiling = 0.055;
    public const double LowLevelRmsRange = 0.045;
    public const double HighLevelRmsFloor = 0.13;
    public const double HighLevelRmsRange = 0.12;

    // --- Fixed gain: AutoEQ changes filters only, not output volume/headroom. ---
    public const double FixedPreampDb = 0.0;
}


public interface IPresetEngine
{
    IReadOnlyList<string> GetPresetNames();
    EqPreset GetPreset(string name);
    string ChooseStartupPreset(AudioOutputInfo outputInfo, bool nearWallMode, bool nightMode);
    PresetEngine.PresetDecision EvaluatePresetDecision(AudioFeatures features, string currentPresetName, bool autoEqEnabled, bool nearWallMode, bool nightMode);
    PresetEngine.PresetDecision EvaluateDynamicAutoEq(AudioFeatures features, OutputAudioProfile outputProfile, bool autoEqEnabled, bool nearWallMode, bool nightMode, bool loudnessComp = true);
    void ResetDecisionWindow();
    EqPreset BuildDynamicAutoEqPreset(AudioFeatures features, OutputAudioProfile outputProfile, bool nearWallMode, bool nightMode, bool fastAttack = false, bool loudnessComp = true);
    EqPreset BuildNativeAutoEqPreset(NativeAutoEqSnapshot snapshot, OutputAudioProfile outputProfile, bool nearWallMode, bool nightMode, bool fuseWithAutoEQBase = true, bool loudnessComp = true);

    void RememberAppliedCurve(EqPreset preset);
    void RememberNativeAppliedCurve(EqPreset preset);
    double[] GetLastDynamicGains();
}

public sealed class PresetEngine : IPresetEngine
{
    private const double MaxFeedbackCompensationDb = VoicingCoefficients.MaxFeedbackCompensationDb;
    private const double FirstDynamicSmoothing = VoicingCoefficients.FirstDynamicSmoothing;
    private const double DynamicSmoothing = VoicingCoefficients.DynamicSmoothing;
    private const double MaxFirstDynamicStepDb = VoicingCoefficients.MaxFirstDynamicStepDb;
    private const double MaxDynamicStepDb = VoicingCoefficients.MaxDynamicStepDb;
    private const double MinAudibleDynamicChangeDb = VoicingCoefficients.MinAudibleDynamicChangeDb;


    private readonly object _gate = new();
    private readonly IAppLogger? _logger;
    private readonly IReadOnlyDictionary<string, EqPreset> _presets;

    private readonly Queue<string> _recentStates = new();


    private static readonly DynamicVoicing.DynamicBand[] DynamicBands = DynamicVoicing.Bands;

    private readonly double[] _smoothedDynamicGains = new double[DynamicBands.Length];
    private DateTime _lastPresetSwitchUtc = DateTime.MinValue;
    private DateTime _lastDynamicEqUtc = DateTime.MinValue;
    private bool _hasDynamicEq;
    private string _lastDynamicEqText = string.Empty;
    private NativeAppliedBand[] _lastNativeAppliedBands = [];

    public PresetEngine(IAppLogger? logger = null)
    {
        _logger = logger;
        _presets = PresetCatalog.Build();
    }



    public IReadOnlyList<string> GetPresetNames() => _presets.Keys.ToList();

    public EqPreset GetPreset(string name) => _presets.TryGetValue(name, out var preset)
        ? preset
        : _presets[PresetCatalog.Fallback];


    public string ChooseStartupPreset(AudioOutputInfo outputInfo, bool nearWallMode, bool nightMode)
    {
        if (nightMode) return "Late Night Smooth";

        string deviceText = $"{outputInfo.DefaultDeviceName} {outputInfo.OutputSummary}";
        if (ContainsAny(deviceText, "AutoEQ", "marshall"))
        {
            return nearWallMode ? "Room Tamed Speaker" : "Universal Warm Balance";
        }

        if (ContainsAny(deviceText, "headphone", "headset", "earbuds", "earphone", "tai nghe"))
        {
            return "Pure Device Pass";
        }

        if (ContainsAny(deviceText, "bluetooth", "speaker", "loa"))
        {
            return nearWallMode ? "Room Tamed Speaker" : "Universal Warm Balance";
        }

        return "Universal Warm Balance";
    }

    public PresetDecision EvaluatePresetDecision(
        AudioFeatures features,
        string currentPresetName,
        bool autoEqEnabled,
        bool nearWallMode,
        bool nightMode)
    {
        lock (_gate)
        {
        TrackRecentState(features.State);

        if (!autoEqEnabled)
        {
            return new PresetDecision { Reason = "Auto EQ disabled, keeping current preset." };
        }

        string? stableState = GetStableState();
        if (stableState is null)
        {
            return new PresetDecision { Reason = "State not stable yet, keeping current preset." };
        }

        string targetPresetName = ChoosePresetName(stableState, nearWallMode, nightMode);

        if (string.Equals(currentPresetName, targetPresetName, StringComparison.OrdinalIgnoreCase))
        {
            return new PresetDecision
            {
                Reason = $"Stable state detected: {stableState}. Keeping current preset.",
                StableState = stableState,
                TargetPresetName = targetPresetName
            };
        }

        if ((DateTime.UtcNow - _lastPresetSwitchUtc).TotalSeconds < AppConfig.PresetCooldownSeconds)
        {
            return new PresetDecision
            {
                Reason = "Preset cooldown active, skipping preset switch.",
                StableState = stableState,
                TargetPresetName = targetPresetName
            };
        }

        _lastPresetSwitchUtc = DateTime.UtcNow;
        _logger?.Decision("PresetEngine", $"state={stableState} -> preset={targetPresetName} (nearWall={nearWallMode}, night={nightMode})");
        return new PresetDecision
        {
            ShouldSwitch = true,
            TargetPresetName = targetPresetName,
            Reason = $"Stable state detected: {stableState}. Switching to {targetPresetName}.",
            StableState = stableState
        };
        }
    }


    public PresetDecision EvaluateDynamicAutoEq(
        AudioFeatures features,
        OutputAudioProfile outputProfile,
        bool autoEqEnabled,
        bool nearWallMode,
        bool nightMode,
        bool loudnessComp = true)
    {

        lock (_gate)
        {
        TrackRecentState(features.State);


        if (!autoEqEnabled)
        {
            return new PresetDecision { Reason = "Auto EQ disabled, keeping current preset." };
        }

        string? stableState = GetStableState();
        if (stableState is null)
        {
            return new PresetDecision { Reason = "State not stable yet, waiting before writing dynamic EQ." };
        }

        if (features.Confidence > 0 && features.Confidence < 0.28)
        {
            return new PresetDecision
            {
                Reason = "DSP confidence too low, keeping current curve.",
                StableState = stableState,
                TargetPresetName = "AutoEQ Live - AutoEQ Optimized"
            };
        }

        bool firstWrite = !_hasDynamicEq;
        if (!firstWrite && (DateTime.UtcNow - _lastDynamicEqUtc).TotalSeconds < AppConfig.DynamicEqCooldownSeconds)
        {
            return new PresetDecision
            {
                Reason = "Dynamic EQ cooldown active, keeping current curve.",
                StableState = stableState,
                TargetPresetName = "AutoEQ Live - AutoEQ Optimized"
            };
        }

        EqPreset preset = BuildDynamicAutoEqPreset(features, outputProfile, nearWallMode, nightMode, firstWrite, loudnessComp);
        if (!firstWrite && string.Equals(preset.EqualizerApoText, _lastDynamicEqText, StringComparison.Ordinal))

        {
            return new PresetDecision
            {
                Reason = "Dynamic EQ movement below audible threshold, keeping current curve.",
                StableState = stableState,
                TargetPresetName = preset.Name
            };
        }

        _lastDynamicEqUtc = DateTime.UtcNow;
        _hasDynamicEq = true;
        _lastDynamicEqText = preset.EqualizerApoText;

        _logger?.Decision("PresetEngine", $"dynamic state={stableState} -> {preset.Name} (first={firstWrite}, nearWall={nearWallMode}, night={nightMode})");
        return new PresetDecision

        {
            ShouldSwitch = true,
            TargetPresetName = preset.Name,
            RequestedPreset = preset,
            Reason = firstWrite
                ? $"Stable state detected: {stableState}. Writing first dynamic AutoEQ curve."
                : $"Stable state detected: {stableState}. Refreshing smoothed dynamic AutoEQ curve.",
            StableState = stableState
        };
        }
    }

    private void TrackRecentState(string state)

    {
        _recentStates.Enqueue(state);
        while (_recentStates.Count > AppConfig.RecentDetectionCount)
        {
            _recentStates.Dequeue();
        }
    }

    private string? GetStableState()
    {
        if (_recentStates.Count < AppConfig.RecentDetectionCount) return null;

        return _recentStates
            .GroupBy(state => state, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= AppConfig.RequiredStableDetections)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    private static string ChoosePresetName(string stableState, bool nearWallMode, bool nightMode)
    {
        if (nightMode) return "Late Night Smooth";
        if (nearWallMode && IsState(stableState, "Balanced", "Quiet")) return "Room Tamed Speaker";

        return stableState switch
        {
            "Boomy" => "Bass Control",
            "Vocal Recessed" => "Vocal Focus",
            "Harsh Treble" => "Treble Softener",
            "Quiet" => "Universal Warm Balance",
            _ => "Universal Warm Balance"
        };
    }

    private static bool IsState(string state, params string[] names) =>
        names.Any(name => string.Equals(state, name, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static double ResolveMaxIncreaseStepDb(int frequencyHz, double defaultStepDb) => frequencyHz switch
    {
        >= 6000 and < 10000 => Math.Min(defaultStepDb, VoicingCoefficients.MaxTrebleIncreaseStepDb),
        >= 10000 => Math.Min(defaultStepDb, VoicingCoefficients.MaxAirIncreaseStepDb),
        >= 2500 => Math.Min(defaultStepDb, VoicingCoefficients.MaxPresenceIncreaseStepDb),
        _ => defaultStepDb
    };

    public void ResetDecisionWindow() => _recentStates.Clear();

    public double[] GetLastDynamicGains()
    {
        lock (_gate)
        {
            return _smoothedDynamicGains.ToArray();
        }
    }

    public EqPreset BuildDynamicAutoEqPreset(AudioFeatures features, OutputAudioProfile outputProfile, bool nearWallMode, bool nightMode, bool fastAttack = false, bool loudnessComp = true)
    {
        // Macro correction + band spread now live in the pure DynamicVoicing math.
        // The engine only owns time-varying state (smoothing / stepping / preamp).
        DynamicVoicing.CorrectionVector correction =
            DynamicVoicing.ComputeCorrection(features, outputProfile, nearWallMode, nightMode, loudnessComp);

        double[] dynamicGains = DynamicVoicing.SpreadToBands(correction, outputProfile);

        double[] baseGains = ResolveBaseGains(nearWallMode, nightMode);
        double[] targetGains = new double[dynamicGains.Length];
        for (int i = 0; i < targetGains.Length; i++)
        {
            targetGains[i] = baseGains[i] + dynamicGains[i];
        }

        for (int i = 0; i < targetGains.Length; i++)
        {
            // Lý tưởng là đo lỗi dư: S_source = S_measured - EQ_applied.
            // Vì AudioFeatures là tỉ lệ năng lượng band đã chuẩn hoá và A-weighted, không phải dB tuyệt đối,
            // nên chỉ bù ở gain cuối theo band để engine không đuổi theo chính EQ vừa ghi.
            targetGains[i] = RemoveLikelyApoFeedback(targetGains[i], DynamicBands[i].FrequencyHz);
        }

        double smoothing = fastAttack ? FirstDynamicSmoothing : DynamicSmoothing;
        for (int i = 0; i < targetGains.Length; i++)
        {
            double limited = DynamicVoicing.ClampProfileBandGain(targetGains[i], DynamicBands[i].FrequencyHz, outputProfile);
            double previous = _hasDynamicEq ? _smoothedDynamicGains[i] : 0.0;
            double blended = previous + ((limited - previous) * smoothing);
            double delta = blended - previous;
            double maxStepDb = fastAttack ? MaxFirstDynamicStepDb : Math.Min(MaxDynamicStepDb, DynamicBands[i].MaxStepDb);
            double maxIncreaseStepDb = ResolveMaxIncreaseStepDb(DynamicBands[i].FrequencyHz, maxStepDb);
            double stepped = previous + Math.Clamp(delta, -maxStepDb, maxIncreaseStepDb);
            _smoothedDynamicGains[i] = Math.Abs(stepped) < MinAudibleDynamicChangeDb ? 0 : stepped;
        }

        string text = BuildEqualizerApoText(VoicingCoefficients.FixedPreampDb, _smoothedDynamicGains, outputProfile);

        return new EqPreset
        {
            Name = $"AutoEQ Live - {outputProfile.Name}",
            EqualizerApoText = text,
            IsDynamic = true
        };
    }

    public EqPreset BuildNativeAutoEqPreset(NativeAutoEqSnapshot snapshot, OutputAudioProfile outputProfile, bool nearWallMode, bool nightMode, bool fuseWithAutoEQBase = true, bool loudnessComp = true)
    {
        int count = Math.Min(snapshot.EqGainsDb.Length, snapshot.BandCentersHz.Length);
        if (count == 0)
        {
            return BuildDynamicAutoEqPreset(ToAudioFeatures(snapshot), outputProfile, nearWallMode, nightMode, fastAttack: true, loudnessComp: loudnessComp);
        }


        double[] nativeGains = new double[count];
        for (int i = 0; i < count; i++)
        {
            double gain = Math.Clamp(snapshot.EqGainsDb[i], outputProfile.MaxCutDb, nightMode ? Math.Min(outputProfile.MaxBoostDb, 1.8) : outputProfile.MaxBoostDb);
            double frequencyHz = Math.Clamp(snapshot.BandCentersHz[i], 20, 20000);
            gain = RemoveLikelyApoFeedback(gain, frequencyHz);
            if (nearWallMode && snapshot.BandCentersHz[i] <= 300) gain -= 0.8;
            if (nightMode && snapshot.BandCentersHz[i] <= 160) gain -= 1.0;
            nativeGains[i] = gain;
        }

        double? truePeakDb = double.IsFinite(snapshot.TruePeakDb) ? snapshot.TruePeakDb : null;

        var lines = new List<string>
        {
            $"Preamp: {FormatDb(VoicingCoefficients.FixedPreampDb)} dB",
            $"# Native WASAPI AutoEQ: {snapshot.Device}; confidence={snapshot.Confidence:0.00}; true_peak_db={(truePeakDb?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")}",
            $"Filter: ON PK Fc 32 Hz Gain {FormatDb(outputProfile.BassSafetyCutDb)} dB Q 0.70"
        };

        for (int i = 0; i < count; i++)
        {
            double gain = Math.Abs(nativeGains[i]) < MinAudibleDynamicChangeDb ? 0 : nativeGains[i];
            if (gain == 0) continue;

            int frequency = (int)Math.Round(Math.Clamp(snapshot.BandCentersHz[i], 20, 20000));
            double q = ResolveNativeBandQ(frequency);
            lines.Add($"Filter: ON PK Fc {frequency} Hz Gain {FormatDb(gain)} dB Q {q:0.00}");
        }

        return new EqPreset
        {
            Name = fuseWithAutoEQBase ? $"AutoEQ Live - Native {outputProfile.Name}" : "AutoEQ Live - Native WASAPI Direct",
            EqualizerApoText = string.Join(Environment.NewLine, lines) + Environment.NewLine,
            IsDynamic = true,
            TruePeakDb = truePeakDb
        };
    }

    private double RemoveLikelyApoFeedback(double proposedGain, double frequencyHz)
    {
        NativeAppliedBand? nearest = FindNearestAppliedBand(frequencyHz);
        if (nearest is null) return proposedGain;

        double previousApplied = nearest.Value.GainDb;
        if (Math.Abs(previousApplied) < MinAudibleDynamicChangeDb) return proposedGain;

        // WASAPI loopback thường thấy tín hiệu sau Equalizer APO, nên gain mới có thể là phản ứng với EQ cũ.
        // Trừ một phần gain đã áp giúp đo gần lỗi dư hơn mà không giả vờ phổ đã chuẩn hoá là dB tuyệt đối.
        double feedback = Math.Clamp(previousApplied * 0.65, -MaxFeedbackCompensationDb, MaxFeedbackCompensationDb);
        return proposedGain - feedback;
    }

    private NativeAppliedBand? FindNearestAppliedBand(double frequencyHz)
    {
        if (_lastNativeAppliedBands.Length == 0) return null;

        NativeAppliedBand nearest = _lastNativeAppliedBands
            .OrderBy(band => Math.Abs(Math.Log2(Math.Max(band.FrequencyHz, 1) / Math.Max(frequencyHz, 1))))
            .First();

        double octaveDistance = Math.Abs(Math.Log2(Math.Max(nearest.FrequencyHz, 1) / Math.Max(frequencyHz, 1)));
        return octaveDistance <= 0.5 ? nearest : null;
    }

    public void RememberAppliedCurve(EqPreset preset)
    {
        EqGainsSnapshot parsed = EqGainsSnapshot.Parse(preset.EqualizerApoText);
        _lastNativeAppliedBands = parsed.Bands
            .Where(band => band.FrequencyHz != 32)
            .ToArray();
    }

    public void RememberNativeAppliedCurve(EqPreset preset) => RememberAppliedCurve(preset);

    public static AudioFeatures ToAudioFeatures(NativeAutoEqSnapshot snapshot)
    {
        double bass = DbToWeight(snapshot.BassDb);
        double mid = DbToWeight(snapshot.MidDb);
        double treble = DbToWeight(snapshot.TrebleDb);
        string state = snapshot.Profile switch
        {
            "boomy" => "Boomy",
            "vocal" => "Vocal Recessed",
            "bright" or "harsh" => "Harsh Treble",
            "Near Wall Less Boom" => "Boomy",
            "Clear Vocal" => "Vocal Recessed",
            "AutoEQ 3 Clean Warm" => "Balanced",
            _ when snapshot.Rms < 0.01 => "Quiet",
            _ => "Balanced"
        };

        return new AudioFeatures
        {
            Rms = snapshot.Rms,
            Confidence = snapshot.Confidence <= 0 ? 0.65 : Math.Clamp(snapshot.Confidence, 0, 1),
            SubBass = bass * 0.45,
            Bass = bass * 0.55,
            LowMid = mid * 0.45,
            Mid = mid * 0.55,
            Presence = treble * 0.35,
            Treble = treble * 0.45,
            Air = treble * 0.20,
            State = state,
            GenreHint = string.IsNullOrWhiteSpace(snapshot.Profile) ? "Native WASAPI" : snapshot.Profile
        };
    }

    public sealed class PresetDecision
    {
        public bool ShouldSwitch { get; init; }
        public string TargetPresetName { get; init; } = "";
        public string Reason { get; init; } = "";
        public string StableState { get; init; } = "";
        public EqPreset? RequestedPreset { get; init; }
    }

    // --- Thin delegators to the pure DSP math in DynamicVoicing ---
    private static string BuildEqualizerApoText(double preamp, IReadOnlyList<double> gains, OutputAudioProfile outputProfile)
        => DynamicVoicing.BuildEqualizerApoText(preamp, gains, outputProfile);

    private static double ClampGain(double value, double min, double max) => DynamicVoicing.ClampGain(value, min, max);

    private static double DbToWeight(double db) => DynamicVoicing.DbToWeight(db);

    private static double ResolveNativeBandQ(int frequencyHz) => DynamicVoicing.ResolveNativeBandQ(frequencyHz);

    private static string FormatDb(double value) => DynamicVoicing.FormatDb(value);

    private static double[] ResolveBaseGains(bool nearWallMode, bool nightMode) => DynamicVoicing.ResolveBaseGains(nearWallMode, nightMode);

    private sealed class EqGainsSnapshot

    {
        private static readonly System.Text.RegularExpressions.Regex FilterRegex = new(@"Filter:\s*ON\s+PK\s+Fc\s+(?<freq>\d+)\s+Hz\s+Gain\s+(?<gain>[-+]?\d+(?:\.\d+)?)\s+dB", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        public NativeAppliedBand[] Bands { get; init; } = [];

        public static EqGainsSnapshot Parse(string text) => new()
        {
            Bands = FilterRegex.Matches(text)
                .Select(match => new NativeAppliedBand(
                    double.Parse(match.Groups["freq"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(match.Groups["gain"].Value, System.Globalization.CultureInfo.InvariantCulture)))
                .ToArray()
        };
    }

    private readonly record struct NativeAppliedBand(double FrequencyHz, double GainDb);

}












