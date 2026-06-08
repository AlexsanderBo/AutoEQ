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
        // 260 Hz: Q 1.05 → 0.80 để nếu có boost warmth thì bù rộng/thoải, tránh tạo bướu ù low-mid.
        new(260, 0.80, 0.24),
        new(400, 1.05, 0.26),
        new(650, 0.95, 0.30),
        new(1000, 0.90, 0.32),
        new(1600, 0.90, 0.34),
        new(2500, 0.95, 0.34),
        // 3800 Hz: Q 1.05 → 0.90; Q hẹp vùng presence với peaking filter dễ nghe gắt, Q ~0.9 chuyển tiếp mượt hơn.
        new(3800, 0.90, 0.32),
        // 6000 Hz: Q 1.35 → 0.90; Q hẹp ở vùng tai nhạy nhất làm boost thành cộng hưởng nhọn/rít.
        new(6000, 0.90, 0.28),
        // 8500 Hz: Q 1.45 → 0.90; Q hẹp ở đầu phổ với peaking filter nghe nhân tạo/gắt, Q ~0.9 thoải hơn.
        new(8500, 0.90, 0.26),
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
    public static CorrectionVector ComputeCorrection(AudioFeatures features, OutputAudioProfile outputProfile, bool nearWallMode, bool nightMode, bool loudnessComp = true)
    {

        double bassEnergy = features.SubBass + features.Bass;
        double warmthEnergy = features.LowMid;
        double vocalEnergy = features.Mid + features.Presence;
        double brightEnergy = features.Treble + features.Air;

        TargetCurve target = ResolveTargetCurve(outputProfile, nearWallMode, nightMode);
        double confidence = Math.Clamp(features.Confidence <= 0 ? 0.65 : features.Confidence, target.ConfidenceFloor, 1.0);
        double spectralBrightnessTrim = Math.Clamp((features.SpectralCentroidHz - 3200.0) / 5000.0, 0, 0.35);
        double crestSafetyTrim = Math.Clamp((features.CrestFactorDb - 14.0) / 18.0, 0, 0.25);

        double bass = ClampProfileGain((target.Bass - bassEnergy) * VoicingCoefficients.BassCorrectionScale * confidence, nightMode ? -3.5 : -3.0, 2.0, outputProfile, includeBassSafetyCut: true);
        // Warmth chỉ siết trần boost 1.4 → 0.6, giữ sàn cut -3.0 để vẫn cắt mạnh vùng ù 150–300 Hz.
        double warmth = ClampProfileGain((target.Warmth - warmthEnergy) * VoicingCoefficients.WarmthCorrectionScale * confidence, -3.0, 0.6, outputProfile);
        double vocal = ClampProfileGain((target.Vocal - vocalEnergy) * VoicingCoefficients.VocalCorrectionScale * confidence, -1.4, 2.6, outputProfile);
        // Bright chỉ siết trần boost 1.6 → 0.8, giữ sàn cut -3.2 để vẫn xử lý treble dư/gắt.
        double bright = ClampProfileGain(((target.Bright - brightEnergy) * VoicingCoefficients.BrightCorrectionScale * confidence) - spectralBrightnessTrim - crestSafetyTrim, -3.2, 0.8, outputProfile);

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

        // App không điều khiển volume Windows; RMS loopback phản ứng theo nội dung bản thu, không phải độ to người nghe.
        // loudnessComp tắt: bỏ qua bù độ to, vẫn giữ clamp trần/sàn an toàn theo profile.
        if (loudnessComp)
        {
            ApplyLoudnessAwareVoicing(features.Rms, nightMode, outputProfile, ref bass, ref warmth, ref vocal, ref bright);
        }
        else
        {
            bass = ClampProfileGain(bass, nightMode ? -4.2 : -3.8, 2.2, outputProfile, includeBassSafetyCut: true);
            warmth = ClampProfileGain(warmth, -3.4, 0.8, outputProfile);
            vocal = ClampProfileGain(vocal, -1.4, 3.0, outputProfile);
            bright = ClampProfileGain(bright, -3.8, nightMode ? 0.8 : 1.0, outputProfile);
        }

        return new CorrectionVector(bass, warmth, vocal, bright);

    }

    public static TargetCurve DeriveTargetsFromMeasurement(double[] measuredBandAverage, double centroidHz, OutputAudioProfile basis)
    {
        if (measuredBandAverage.Length < 7) return new TargetCurve(basis.TargetBass, basis.TargetWarmth, basis.TargetVocal, basis.TargetBright, 0.35);

        double bassMeasured = Safe(measuredBandAverage[0]) + Safe(measuredBandAverage[1]);
        double warmthMeasured = Safe(measuredBandAverage[2]);
        double vocalMeasured = Safe(measuredBandAverage[3]) + Safe(measuredBandAverage[4]);
        double brightMeasured = Safe(measuredBandAverage[5]) + Safe(measuredBandAverage[6]);

        double total = Math.Max(0.001, bassMeasured + warmthMeasured + vocalMeasured + brightMeasured);
        bassMeasured /= total;
        warmthMeasured /= total;
        vocalMeasured /= total;
        brightMeasured /= total;

        double bass = ClampTarget(basis.TargetBass - (bassMeasured - 0.30) * 0.35, basis.TargetBass, 0.06, basis.MaxBoostDb, VoicingCoefficients.BassCorrectionScale);
        double warmth = ClampTarget(basis.TargetWarmth - (warmthMeasured - 0.18) * 0.30, basis.TargetWarmth, 0.04, basis.MaxBoostDb, VoicingCoefficients.WarmthCorrectionScale);
        double vocal = ClampTarget(basis.TargetVocal - (vocalMeasured - 0.40) * 0.25, basis.TargetVocal, 0.05, basis.MaxBoostDb, VoicingCoefficients.VocalCorrectionScale);

        double centroidTrim = double.IsFinite(centroidHz) ? Math.Clamp((2800.0 - centroidHz) / 8000.0, -0.03, 0.03) : 0;
        double bright = ClampTarget(basis.TargetBright - (brightMeasured - 0.20) * 0.30 + centroidTrim, basis.TargetBright, 0.05, basis.MaxBoostDb, VoicingCoefficients.BrightCorrectionScale);

        return new TargetCurve(bass, warmth, vocal, bright, 0.35);
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

    public static double[] SpreadToBands(CorrectionVector c, OutputAudioProfile outputProfile)
    {
        double[] gains = SpreadToBands(c);
        // Clamp từng dải sau khi cộng macro: limiter chỉ chặn đỉnh tổng, clamp này mới chặn boost/cut từng dải theo profile.
        for (int i = 0; i < gains.Length; i++)
        {
            gains[i] = ClampProfileBandGain(gains[i], Bands[i].FrequencyHz, outputProfile);
        }

        return gains;
    }

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
        ref double brightCorrection) =>
        ApplyLoudnessAwareVoicing(rms, nightMode, new OutputAudioProfile(), ref bassCorrection, ref warmthCorrection, ref vocalCorrection, ref brightCorrection);

    public static void ApplyLoudnessAwareVoicing(
        double rms,
        bool nightMode,
        OutputAudioProfile outputProfile,
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

        bassCorrection = ClampProfileGain(bassCorrection, nightMode ? -4.2 : -3.8, 2.2, outputProfile, includeBassSafetyCut: true);
        // Warmth loudness chỉ siết trần boost 1.6 → 0.8, giữ sàn cut -3.4 vì ù cần cắt hơn là bơm.
        warmthCorrection = ClampProfileGain(warmthCorrection, -3.4, 0.8, outputProfile);
        vocalCorrection = ClampProfileGain(vocalCorrection, -1.4, 3.0, outputProfile);
        // Bright loudness chỉ siết trần boost night 1.2 → 0.8, day 1.9 → 1.0; giữ sàn cut -3.8 để vẫn khử gắt.
        brightCorrection = ClampProfileGain(brightCorrection, -3.8, nightMode ? 0.8 : 1.0, outputProfile);
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

    public static string BuildDeviceScopedBlock(string apoPattern, double preamp, IReadOnlyList<double> gains, OutputAudioProfile outputProfile)
    {
        string body = BuildEqualizerApoText(preamp, gains, outputProfile).TrimEnd();
        if (string.IsNullOrWhiteSpace(apoPattern)) return body + Environment.NewLine;

        return $"Device: {apoPattern.Trim()}{Environment.NewLine}" + body + Environment.NewLine;
    }

    public static double ClampGain(double value, double min, double max) => Math.Clamp(value, min, max);

    public static double ClampProfileBandGain(double value, int frequencyHz, OutputAudioProfile outputProfile) =>
        ClampProfileGain(value, outputProfile.MaxCutDb, outputProfile.MaxBoostDb, outputProfile, includeBassSafetyCut: frequencyHz <= 180);

    private static double ClampProfileGain(double value, double min, double max, OutputAudioProfile outputProfile, bool includeBassSafetyCut = false)
    {
        // Trần profile phải nằm ở điểm cuối để mọi nhánh tính correction đều bị chặn bởi giới hạn an toàn.
        double profileMin = outputProfile.MaxCutDb;
        if (includeBassSafetyCut)
        {
            profileMin -= outputProfile.BassSafetyCutDb;
        }

        return ClampGain(value, Math.Max(min, profileMin), Math.Min(max, outputProfile.MaxBoostDb));
    }

    private static double ClampTarget(double value, double basis, double span, double maxBoostDb, double scale)
    {
        double clamped = Math.Clamp(value, basis - span, basis + span);
        double maxAllowedByBoost = basis + Math.Max(0.01, maxBoostDb / Math.Max(1.0, scale));
        return Math.Clamp(clamped, 0.02, Math.Min(0.80, maxAllowedByBoost));
    }

    private static double Safe(double value) => double.IsFinite(value) && value > 0 ? value : 0;

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
