using System.Globalization;
using AutoEQ.Models;

namespace AutoEQ.Services;

/// <summary>
/// Pure, stateless DSP voicing math for the dynamic AutoEQ engine.
/// No smoothing state, no cooldowns, no I/O — everything here is a deterministic
/// function of its inputs, so it is trivially unit-testable. <see cref="PresetEngine"/>
/// owns the time-varying state and calls into these helpers.
/// </summary>
public static class DynamicVoicing
{
    public static readonly DynamicBand[] Bands =
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

    private static readonly double[] BaseAutoEQGains =
    [
        -0.4, -0.6, -0.9, -1.4, -1.1, -0.5, 0.0,
        0.3, 0.7, 1.0, 0.6, 0.2, -0.2, 0.0
    ];

    /// <summary>The 4 macro correction amounts (dB) before they are spread across bands.</summary>
    public readonly record struct CorrectionVector(double Bass, double Warmth, double Vocal, double Bright);

    public readonly record struct TargetCurve(double Bass, double Warmth, double Vocal, double Bright, double ConfidenceFloor);

    public static TargetCurve ResolveTargetCurve(OutputAudioProfile outputProfile, bool nearWallMode, bool nightMode)
    {
        double bass = nightMode ? Math.Min(outputProfile.TargetBass, 0.24) : outputProfile.TargetBass;
        double warmth = nearWallMode ? Math.Min(outputProfile.TargetWarmth, 0.16) : outputProfile.TargetWarmth;
        double vocal = outputProfile.TargetVocal;
        double bright = nightMode ? Math.Min(outputProfile.TargetBright, 0.15) : outputProfile.TargetBright;

        return new TargetCurve(bass, warmth, vocal, bright, nightMode ? 0.42 : 0.35);
    }

    /// <summary>
    /// Computes the bass/warmth/vocal/bright correction vector from measured features
    /// and an output profile's voicing targets. Pure function — easy to unit test.
    /// </summary>
    public static CorrectionVector ComputeCorrection(AudioFeatures features, OutputAudioProfile outputProfile, bool nearWallMode, bool nightMode)
    {
        double bassEnergy = features.SubBass + features.Bass;
        double warmthEnergy = features.LowMid;
        double vocalEnergy = features.Mid + features.Presence;
        double brightEnergy = features.Treble + features.Air;

        TargetCurve target = ResolveTargetCurve(outputProfile, nearWallMode, nightMode);
        double confidence = Math.Clamp(features.Confidence <= 0 ? 0.65 : features.Confidence, target.ConfidenceFloor, 1.0);
        double spectralBrightnessTrim = Math.Clamp((features.SpectralCentroidHz - 3200.0) / 5000.0, 0, 0.35);
        double crestSafetyTrim = Math.Clamp((features.CrestFactorDb - 14.0) / 18.0, 0, 0.25);

        double bass = ClampGain((target.Bass - bassEnergy) * VoicingCoefficients.BassCorrectionScale * confidence, nightMode ? -3.5 : -3.0, 2.0);
        double warmth = ClampGain((target.Warmth - warmthEnergy) * VoicingCoefficients.WarmthCorrectionScale * confidence, -3.0, 1.4);
        double vocal = ClampGain((target.Vocal - vocalEnergy) * VoicingCoefficients.VocalCorrectionScale * confidence, -1.4, 2.6);
        double bright = ClampGain(((target.Bright - brightEnergy) * VoicingCoefficients.BrightCorrectionScale * confidence) - spectralBrightnessTrim - crestSafetyTrim, -3.2, 1.6);

        if (nearWallMode)
        {
            bass -= 0.9;
            warmth -= 0.9;
        }

        if (nightMode)
        {
            bass -= 1.2;
            bright -= 0.7;
            vocal += 0.4;
        }

        ApplyLoudnessAwareVoicing(features.Rms, nightMode, ref bass, ref warmth, ref vocal, ref bright);
        return new CorrectionVector(bass, warmth, vocal, bright);
    }

    /// <summary>Spreads a macro correction vector into the 14 per-band gains.</summary>
    public static double[] SpreadToBands(CorrectionVector c) =>
    [
        c.Bass * 0.55,
        c.Bass * 0.85,
        c.Bass,
        (c.Bass + c.Warmth) * 0.65,
        c.Warmth,
        c.Warmth * 0.55,
        c.Vocal * 0.25,
        c.Vocal * 0.45,
        c.Vocal * 0.75,
        c.Vocal,
        (c.Vocal + c.Bright) * 0.70,
        c.Bright,
        c.Bright * 0.85,
        c.Bright * 0.55
    ];

    public static double[] ResolveBaseGains(bool nearWallMode, bool nightMode)
    {
        double[] gains = BaseAutoEQGains.ToArray();
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

    public static void ApplyLoudnessAwareVoicing(
        double rms,
        bool nightMode,
        ref double bassCorrection,
        ref double warmthCorrection,
        ref double vocalCorrection,
        ref double brightCorrection)
    {
        double lowLevel = Math.Clamp((VoicingCoefficients.LowLevelRmsCeiling - rms) / VoicingCoefficients.LowLevelRmsRange, 0, 1);
        double highLevel = Math.Clamp((rms - VoicingCoefficients.HighLevelRmsFloor) / VoicingCoefficients.HighLevelRmsRange, 0, 1);

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

    public static string BuildEqualizerApoText(double preamp, IReadOnlyList<double> gains, OutputAudioProfile outputProfile)
    {
        var lines = new List<string>
        {
            $"Preamp: {FormatDb(preamp)} dB",
            $"# Output profile: {outputProfile.Name}; type={outputProfile.DeviceType}; {outputProfile.Reason}",
            $"Filter: ON PK Fc 32 Hz Gain {FormatDb(outputProfile.BassSafetyCutDb)} dB Q 0.70"
        };

        for (int i = 0; i < Bands.Length; i++)
        {
            double gain = Math.Abs(gains[i]) < 0.15 ? 0 : gains[i];
            if (gain == 0) continue;

            lines.Add($"Filter: ON PK Fc {Bands[i].FrequencyHz} Hz Gain {FormatDb(gain)} dB Q {Bands[i].Q:0.00}");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public static double ClampGain(double value, double min, double max) => Math.Clamp(value, min, max);

    public static double DbToWeight(double db) => Math.Clamp((db + 9.0) / 18.0, 0, 1);

    public static double ResolveNativeBandQ(int frequencyHz) => frequencyHz switch
    {
        < 80 => 0.70,
        < 250 => 0.85,
        < 2000 => 1.00,
        < 6000 => 1.10,
        _ => 1.25
    };

    public static string FormatDb(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public readonly record struct DynamicBand(int FrequencyHz, double Q, double MaxStepDb);
}
