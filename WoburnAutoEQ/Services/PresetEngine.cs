using WoburnAutoEQ.Config;
using WoburnAutoEQ.Models;

namespace WoburnAutoEQ.Services;

public sealed class PresetEngine
{
    private const double FirstDynamicSmoothing = 0.30;
    private const double DynamicSmoothing = 0.12;
    private const double MaxFirstDynamicStepDb = 0.55;
    private const double MaxDynamicStepDb = 0.32;
    private const double MinAudibleDynamicChangeDb = 0.12;

    private readonly Dictionary<string, EqPreset> _presets;
    private readonly Queue<string> _recentStates = new();
    private readonly double[] _smoothedDynamicGains = new double[DynamicBands.Length];
    private DateTime _lastPresetSwitchUtc = DateTime.MinValue;
    private DateTime _lastDynamicEqUtc = DateTime.MinValue;
    private bool _hasDynamicEq;
    private string _lastDynamicEqText = string.Empty;

    private static readonly DynamicBand[] DynamicBands =
    [
        new(45, 0.70, 0.18),
        new(80, 0.78, 0.20),
        new(125, 0.85, 0.22),
        new(180, 0.90, 0.22),
        new(260, 1.05, 0.24),
        new(400, 1.05, 0.26),
        new(650, 0.95, 0.30),
        new(1000, 0.90, 0.32),
        new(1600, 0.90, 0.34),
        new(2500, 0.95, 0.34),
        new(3800, 1.05, 0.32),
        new(6000, 1.35, 0.28),
        new(8500, 1.45, 0.26),
        new(12000, 0.70, 0.22)
    ];

    private static readonly double[] BaseWoburnGains =
    [
        -0.4,
        -0.6,
        -0.9,
        -1.4,
        -1.1,
        -0.5,
        0.0,
        0.3,
        0.7,
        1.0,
        0.6,
        0.2,
        -0.2,
        0.0
    ];

    public PresetEngine()
    {
        _presets = CreatePresets().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetPresetNames() => _presets.Keys.ToList();

    public EqPreset GetPreset(string name) => _presets.TryGetValue(name, out var preset)
        ? preset
        : _presets["Universal Warm Balance"];

    public string ChooseStartupPreset(AudioOutputInfo outputInfo, bool nearWallMode, bool nightMode)
    {
        if (nightMode) return "Late Night Smooth";

        string deviceText = $"{outputInfo.DefaultDeviceName} {outputInfo.OutputSummary}";
        if (ContainsAny(deviceText, "woburn", "marshall"))
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
        return new PresetDecision
        {
            ShouldSwitch = true,
            TargetPresetName = targetPresetName,
            Reason = $"Stable state detected: {stableState}. Switching to {targetPresetName}.",
            StableState = stableState
        };
    }

    public PresetDecision EvaluateDynamicAutoEq(
        AudioFeatures features,
        bool autoEqEnabled,
        bool nearWallMode,
        bool nightMode)
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

        bool firstWrite = !_hasDynamicEq;
        if (!firstWrite && (DateTime.UtcNow - _lastDynamicEqUtc).TotalSeconds < AppConfig.DynamicEqCooldownSeconds)
        {
            return new PresetDecision
            {
                Reason = "Dynamic EQ cooldown active, keeping current curve.",
                StableState = stableState,
                TargetPresetName = "AutoEQ Live - Woburn Optimized"
            };
        }

        EqPreset preset = BuildDynamicAutoEqPreset(features, nearWallMode, nightMode, firstWrite);
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

    public void ResetDecisionWindow() => _recentStates.Clear();

    public EqPreset BuildDynamicAutoEqPreset(AudioFeatures features, bool nearWallMode, bool nightMode, bool fastAttack = false)
    {
        double bassEnergy = features.SubBass + features.Bass;
        double warmthEnergy = features.LowMid;
        double vocalEnergy = features.Mid + features.Presence;
        double brightEnergy = features.Treble + features.Air;

        double targetBass = nightMode ? 0.24 : 0.31;
        double targetWarmth = nearWallMode ? 0.16 : 0.19;
        double targetVocal = 0.39;
        double targetBright = nightMode ? 0.15 : 0.20;

        double bassCorrection = ClampGain((targetBass - bassEnergy) * 10.0, nightMode ? -3.5 : -3.0, 2.0);
        double warmthCorrection = ClampGain((targetWarmth - warmthEnergy) * 12.0, -3.0, 1.4);
        double vocalCorrection = ClampGain((targetVocal - vocalEnergy) * 8.5, -1.4, 2.6);
        double brightCorrection = ClampGain((targetBright - brightEnergy) * 9.0, -3.2, 1.6);

        if (nearWallMode)
        {
            bassCorrection -= 0.9;
            warmthCorrection -= 0.9;
        }

        if (nightMode)
        {
            bassCorrection -= 1.2;
            brightCorrection -= 0.7;
            vocalCorrection += 0.4;
        }

        ApplyLoudnessAwareVoicing(features.Rms, nightMode, ref bassCorrection, ref warmthCorrection, ref vocalCorrection, ref brightCorrection);

        double[] dynamicGains =
        [
            bassCorrection * 0.55,
            bassCorrection * 0.85,
            bassCorrection,
            (bassCorrection + warmthCorrection) * 0.65,
            warmthCorrection,
            warmthCorrection * 0.55,
            vocalCorrection * 0.25,
            vocalCorrection * 0.45,
            vocalCorrection * 0.75,
            vocalCorrection,
            (vocalCorrection + brightCorrection) * 0.70,
            brightCorrection,
            brightCorrection * 0.85,
            brightCorrection * 0.55
        ];

        double[] baseGains = ResolveBaseGains(nearWallMode, nightMode);
        double[] targetGains = new double[dynamicGains.Length];
        for (int i = 0; i < targetGains.Length; i++)
        {
            targetGains[i] = baseGains[i] + dynamicGains[i];
        }

        double smoothing = fastAttack ? FirstDynamicSmoothing : DynamicSmoothing;
        for (int i = 0; i < targetGains.Length; i++)
        {
            double limited = ClampGain(targetGains[i], -6.0, 3.2);
            double previous = _hasDynamicEq ? _smoothedDynamicGains[i] : 0.0;
            double blended = previous + ((limited - previous) * smoothing);
            double maxStepDb = fastAttack ? MaxFirstDynamicStepDb : Math.Min(MaxDynamicStepDb, DynamicBands[i].MaxStepDb);
            double stepped = previous + Math.Clamp(blended - previous, -maxStepDb, maxStepDb);
            _smoothedDynamicGains[i] = Math.Abs(stepped) < MinAudibleDynamicChangeDb ? 0 : stepped;
        }

        double positiveBoostBudget = _smoothedDynamicGains.Where(gain => gain > 0).Sum(gain => gain * 0.35);
        double maxBoost = Math.Max(0, _smoothedDynamicGains.Max());
        double preamp = nightMode
            ? -6.0
            : Math.Clamp(-3.5 - maxBoost * 0.65 - positiveBoostBudget, -7.5, -3.5);
        string text = BuildEqualizerApoText(preamp, _smoothedDynamicGains);

        return new EqPreset
        {
            Name = "AutoEQ Live - Woburn Optimized",
            EqualizerApoText = text,
            IsDynamic = true
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

    private static string BuildEqualizerApoText(double preamp, IReadOnlyList<double> gains)
    {
        var lines = new List<string>
        {
            $"Preamp: {FormatDb(preamp)} dB",
            "# Sub-bass protection for RCA speaker headroom",
            "Filter: ON PK Fc 32 Hz Gain -1.5 dB Q 0.70"
        };

        for (int i = 0; i < DynamicBands.Length; i++)
        {
            double gain = Math.Abs(gains[i]) < 0.15 ? 0 : gains[i];
            if (gain == 0) continue;

            lines.Add($"Filter: ON PK Fc {DynamicBands[i].FrequencyHz} Hz Gain {FormatDb(gain)} dB Q {DynamicBands[i].Q:0.00}");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static double ClampGain(double value, double min, double max) => Math.Clamp(value, min, max);

    private static string FormatDb(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static void ApplyLoudnessAwareVoicing(
        double rms,
        bool nightMode,
        ref double bassCorrection,
        ref double warmthCorrection,
        ref double vocalCorrection,
        ref double brightCorrection)
    {
        double lowLevel = Math.Clamp((0.055 - rms) / 0.045, 0, 1);
        double highLevel = Math.Clamp((rms - 0.13) / 0.12, 0, 1);

        if (lowLevel > 0)
        {
            vocalCorrection += 0.45 * lowLevel;
            brightCorrection += (nightMode ? 0.18 : 0.35) * lowLevel;
            warmthCorrection += 0.12 * lowLevel;
        }

        if (highLevel > 0)
        {
            bassCorrection -= 0.75 * highLevel;
            warmthCorrection -= 0.25 * highLevel;
            brightCorrection -= 0.65 * highLevel;
        }

        bassCorrection = ClampGain(bassCorrection, nightMode ? -4.2 : -3.8, 2.2);
        warmthCorrection = ClampGain(warmthCorrection, -3.4, 1.6);
        vocalCorrection = ClampGain(vocalCorrection, -1.4, 3.0);
        brightCorrection = ClampGain(brightCorrection, -3.8, nightMode ? 1.2 : 1.9);
    }

    private static double[] ResolveBaseGains(bool nearWallMode, bool nightMode)
    {
        double[] gains = BaseWoburnGains.ToArray();
        if (nearWallMode)
        {
            gains[1] -= 0.5;
            gains[2] -= 0.6;
            gains[3] -= 0.8;
            gains[4] -= 0.5;
        }

        if (nightMode)
        {
            gains[0] -= 0.6;
            gains[1] -= 0.9;
            gains[2] -= 0.5;
            gains[11] -= 0.5;
            gains[12] -= 0.7;
            gains[13] -= 0.4;
        }

        return gains;
    }

    private readonly record struct DynamicBand(int FrequencyHz, double Q, double MaxStepDb);

    private static IEnumerable<EqPreset> CreatePresets()
    {
        yield return Preset("AutoEQ Live - Woburn Optimized", """
Preamp: -3 dB
Filter: ON PK Fc 180 Hz Gain -1.5 dB Q 1.00
Filter: ON PK Fc 260 Hz Gain -1 dB Q 1.00
Filter: ON PK Fc 2500 Hz Gain 1.5 dB Q 1.00
Filter: ON PK Fc 6000 Hz Gain 0.8 dB Q 0.95
""");
        yield return Preset("Universal Warm Balance", """
Preamp: -3 dB
Filter: ON PK Fc 160 Hz Gain -2 dB Q 1.0
Filter: ON PK Fc 280 Hz Gain -1.5 dB Q 1.0
Filter: ON PK Fc 2500 Hz Gain 1.5 dB Q 1.0
Filter: ON PK Fc 5000 Hz Gain 1 dB Q 1.0
""");
        yield return Preset("Room Tamed Speaker", """
Preamp: -4 dB
Filter: ON PK Fc 90 Hz Gain -1.5 dB Q 0.8
Filter: ON PK Fc 180 Hz Gain -3 dB Q 1.0
Filter: ON PK Fc 320 Hz Gain -1.5 dB Q 1.0
Filter: ON PK Fc 2500 Hz Gain 1.5 dB Q 1.0
""");
        yield return Preset("Bass Control", """
Preamp: -4 dB
Filter: ON PK Fc 120 Hz Gain -2 dB Q 0.9
Filter: ON PK Fc 200 Hz Gain -3 dB Q 1.0
Filter: ON PK Fc 320 Hz Gain -2 dB Q 1.0
Filter: ON PK Fc 2500 Hz Gain 1 dB Q 1.0
""");
        yield return Preset("Vocal Focus", """
Preamp: -3 dB
Filter: ON PK Fc 220 Hz Gain -1.5 dB Q 1.0
Filter: ON PK Fc 1200 Hz Gain 1 dB Q 1.0
Filter: ON PK Fc 2500 Hz Gain 2 dB Q 1.0
Filter: ON PK Fc 4500 Hz Gain 1 dB Q 1.0
""");
        yield return Preset("Treble Softener", """
Preamp: -3 dB
Filter: ON PK Fc 4500 Hz Gain -1.5 dB Q 1.0
Filter: ON PK Fc 7500 Hz Gain -2.5 dB Q 1.0
Filter: ON PK Fc 10000 Hz Gain -1 dB Q 0.8
""");
        yield return Preset("Late Night Smooth", """
Preamp: -6 dB
Filter: ON PK Fc 80 Hz Gain -3 dB Q 0.8
Filter: ON PK Fc 160 Hz Gain -2 dB Q 1.0
Filter: ON PK Fc 2500 Hz Gain 1 dB Q 1.0
Filter: ON PK Fc 8000 Hz Gain -1.5 dB Q 1.0
""");
        yield return Preset("Pure Device Pass", "Preamp: 0 dB");
    }

    private static EqPreset Preset(string name, string text) => new()
    {
        Name = name,
        EqualizerApoText = text.Trim() + Environment.NewLine
    };
}