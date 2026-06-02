using AutoEQ.Services;
using AutoEQ.Models;

namespace AutoEQ.Tests;

public sealed class DspAnalyzerTests
{
    private static readonly OutputAudioProfile TestProfile = new();

    [Fact]
    public void NativeSnapshot_ToPreset_ProducesEqualizerApoFiltersAndTruePeakMetadata()
    {
        var preset = new PresetEngine().BuildNativeAutoEqPreset(new NativeAutoEqSnapshot
        {
            Device = "Realtek Loopback",
            SampleRate = 48_000,
            Channels = 2,
            Rms = 0.08,
            TruePeakDb = -0.2,
            BassDb = -18,
            MidDb = -12,
            TrebleDb = -15,
            Profile = "AutoEQ 3 Clean Warm",
            Confidence = 0.82,
            EqGainsDb = [-2.0, 1.5, 0.05, 4.5],
            BandCentersHz = [80, 1000, 2000, 6000]
        }, TestProfile, nearWallMode: true, nightMode: false);

        Assert.Equal("AutoEQ Live - Native Generic Windows Audio", preset.Name);
        Assert.Equal(-0.2, preset.TruePeakDb);
        Assert.Contains("# Native WASAPI AutoEQ: Realtek Loopback", preset.EqualizerApoText);
        Assert.Contains("Filter: ON PK Fc 80 Hz Gain -2.8 dB Q 0.85", preset.EqualizerApoText);
        Assert.Contains("Filter: ON PK Fc 1000 Hz Gain 1.5 dB Q 1.00", preset.EqualizerApoText);
        Assert.Contains("Filter: ON PK Fc 6000 Hz Gain 3 dB Q 1.25", preset.EqualizerApoText);
        Assert.DoesNotContain("Fc 2000 Hz Gain", preset.EqualizerApoText);
    }

    [Fact]
    public void NativeSnapshot_ToFeatures_MapsNativeProfiles()
    {
        var features = PresetEngine.ToAudioFeatures(new NativeAutoEqSnapshot
        {
            Profile = "Clear Vocal",
            Rms = 0.04,
            BassDb = -20,
            MidDb = -10,
            TrebleDb = -18
        });

        Assert.Equal("Vocal Recessed", features.State);
        Assert.Equal("Clear Vocal", features.GenreHint);
    }

    [Fact]
    public void NativeSnapshot_ToPreset_DampensLikelyApoFeedbackFromNearestPreviousFrequency()
    {
        var engine = new PresetEngine();
        var first = engine.BuildNativeAutoEqPreset(new NativeAutoEqSnapshot
        {
            EqGainsDb = [3.0, -1.0],
            BandCentersHz = [1000, 4000],
            Profile = "AutoEQ 3 Clean Warm"
        }, TestProfile, nearWallMode: false, nightMode: false);
        engine.RememberNativeAppliedCurve(first);

        var second = engine.BuildNativeAutoEqPreset(new NativeAutoEqSnapshot
        {
            EqGainsDb = [-2.2],
            BandCentersHz = [950],
            Profile = "AutoEQ 3 Clean Warm"
        }, TestProfile, nearWallMode: false, nightMode: false);

        Assert.Contains("Filter: ON PK Fc 950 Hz Gain -4.15 dB Q 1.00", second.EqualizerApoText);
    }

    [Fact]
    public void NativeSnapshot_ToPreset_IgnoresFeedbackWhenPreviousFrequencyTooFar()
    {
        var engine = new PresetEngine();
        var first = engine.BuildNativeAutoEqPreset(new NativeAutoEqSnapshot
        {
            EqGainsDb = [3.0],
            BandCentersHz = [1000],
            Profile = "AutoEQ 3 Clean Warm"
        }, TestProfile, nearWallMode: false, nightMode: false);
        engine.RememberNativeAppliedCurve(first);

        var second = engine.BuildNativeAutoEqPreset(new NativeAutoEqSnapshot
        {
            EqGainsDb = [-2.2],
            BandCentersHz = [4000],
            Profile = "AutoEQ 3 Clean Warm"
        }, TestProfile, nearWallMode: false, nightMode: false);

        Assert.Contains("Filter: ON PK Fc 4000 Hz Gain -2.2 dB Q 1.10", second.EqualizerApoText);
    }

    [Fact]
    public void DynamicPreset_WithRememberedCurve_DoesNotAmplifyOwnFeedback()
    {
        var features = FeedbackTestFeatures();
        var profile = TestProfile;
        var firstEngine = new PresetEngine();
        EqPreset first = firstEngine.BuildDynamicAutoEqPreset(features, profile, nearWallMode: false, nightMode: false, fastAttack: true);
        firstEngine.RememberAppliedCurve(first);

        EqPreset second = firstEngine.BuildDynamicAutoEqPreset(features, profile, nearWallMode: false, nightMode: false, fastAttack: true);

        var noFeedbackEngine = new PresetEngine();
        _ = noFeedbackEngine.BuildDynamicAutoEqPreset(features, profile, nearWallMode: false, nightMode: false, fastAttack: true);
        EqPreset secondWithoutRemember = noFeedbackEngine.BuildDynamicAutoEqPreset(features, profile, nearWallMode: false, nightMode: false, fastAttack: true);

        Dictionary<int, double> firstGains = ParseGains(first);
        Dictionary<int, double> secondGains = ParseGains(second);
        Dictionary<int, double> noFeedbackGains = ParseGains(secondWithoutRemember);

        Assert.True(SumAbs(secondGains) <= SumAbs(firstGains) + 0.001);
        foreach ((int frequency, double firstGain) in firstGains.Where(pair => pair.Value > VoicingCoefficients.MinAudibleDynamicChangeDb))
        {
            Assert.True(Math.Abs(secondGains.GetValueOrDefault(frequency)) <= Math.Abs(noFeedbackGains.GetValueOrDefault(frequency)) + 0.001);
        }
    }

    [Fact]
    public void DynamicPreset_WithoutRememberedCurve_MatchesPreviousBehavior()
    {
        var features = FeedbackTestFeatures();
        var profile = TestProfile;

        EqPreset first = new PresetEngine().BuildDynamicAutoEqPreset(features, profile, nearWallMode: false, nightMode: false, fastAttack: true);
        EqPreset second = new PresetEngine().BuildDynamicAutoEqPreset(features, profile, nearWallMode: false, nightMode: false, fastAttack: true);

        Assert.Equal(first.EqualizerApoText, second.EqualizerApoText);
    }

    [Fact]
    public void Analyze_WithEmptyInput_ReturnsQuietSilence()
    {
        var result = new DspAnalyzer().Analyze([], channels: 2, sampleRate: 48_000);

        Assert.Equal("Quiet", result.State);
        Assert.Equal("Silence", result.GenreHint);
        Assert.Equal(0, result.Rms);
    }

    [Fact]
    public void Analyze_WithInvalidFormat_ReturnsQuietSilence()
    {
        var result = new DspAnalyzer().Analyze([0.5f, -0.5f], channels: 0, sampleRate: 48_000);

        Assert.Equal("Quiet", result.State);
        Assert.Equal("Silence", result.GenreHint);
    }

    [Fact]
    public void Analyze_WithVeryLowLevelInput_ReturnsQuietButKeepsRms()
    {
        float[] samples = Enumerable.Repeat(0.001f, 4_096).ToArray();

        var result = new DspAnalyzer().Analyze(samples, channels: 1, sampleRate: 48_000);

        Assert.Equal("Quiet", result.State);
        Assert.Equal("Silence", result.GenreHint);
        Assert.InRange(result.Rms, 0.0009, 0.0011);
    }

    [Fact]
    public void Analyze_WithBassTone_ClassifiesAsBoomyOrBalancedAndNormalizesBands()
    {
        float[] samples = GenerateInterleavedSine(frequencyHz: 90, seconds: 1, channels: 2, sampleRate: 48_000, amplitude: 0.2);

        var result = new DspAnalyzer().Analyze(samples, channels: 2, sampleRate: 48_000);

        Assert.NotEqual("Quiet", result.State);
        Assert.True(result.Bass > result.Treble);
        Assert.True(result.Peak > result.Rms);
        Assert.InRange(result.Confidence, 0, 1);
        AssertBandSumCloseToOne(result.SubBass + result.Bass + result.LowMid + result.Mid + result.Presence + result.Treble + result.Air);
    }

    [Fact]
    public void Analyze_WithTrebleTone_ClassifiesAsHarshTreble()
    {
        float[] samples = GenerateInterleavedSine(frequencyHz: 8_000, seconds: 1, channels: 2, sampleRate: 48_000, amplitude: 0.2);

        var result = new DspAnalyzer().Analyze(samples, channels: 2, sampleRate: 48_000);

        Assert.Equal("Harsh Treble", result.State);
        Assert.True(result.Treble > result.Bass);
        Assert.True(result.SpectralCentroidHz > 7000);
        Assert.True(result.SpectralRolloffHz > 7000);
        AssertBandSumCloseToOne(result.SubBass + result.Bass + result.LowMid + result.Mid + result.Presence + result.Treble + result.Air);
    }

    [Fact]
    public void Analyze_WithWhiteNoise_ReportsHighSpectralFlatness()
    {
        var random = new Random(1234);
        float[] samples = Enumerable.Range(0, 48_000)
            .Select(_ => (float)((random.NextDouble() * 2.0 - 1.0) * 0.08))
            .ToArray();

        var result = new DspAnalyzer().Analyze(samples, channels: 1, sampleRate: 48_000);

        Assert.InRange(result.SpectralFlatness, 0.2, 1.0);
        Assert.InRange(result.Confidence, 0, 1);
    }

    [Fact]
    public void Analyze_WithClippedInput_LowersConfidence()
    {
        float[] clipped = Enumerable.Range(0, 48_000)
            .Select(i => i % 2 == 0 ? 1.0f : -1.0f)
            .ToArray();

        var result = new DspAnalyzer().Analyze(clipped, channels: 1, sampleRate: 48_000);

        Assert.True(result.Peak >= 1.0);
        Assert.True(result.Confidence < 0.6);
    }

    private static float[] GenerateInterleavedSine(double frequencyHz, double seconds, int channels, int sampleRate, double amplitude)
    {
        int frames = (int)(seconds * sampleRate);
        float[] samples = new float[frames * channels];

        for (int frame = 0; frame < frames; frame++)
        {
            float sample = (float)(Math.Sin(2 * Math.PI * frequencyHz * frame / sampleRate) * amplitude);
            for (int channel = 0; channel < channels; channel++)
            {
                samples[frame * channels + channel] = sample;
            }
        }

        return samples;
    }

    private static void AssertBandSumCloseToOne(double sum)
    {
        Assert.InRange(sum, 0.999, 1.001);
    }

    private static AudioFeatures FeedbackTestFeatures() => new()
    {
        Rms = 0.06,
        Confidence = 0.9,
        SubBass = 0.11,
        Bass = 0.10,
        LowMid = 0.15,
        Mid = 0.10,
        Presence = 0.09,
        Treble = 0.08,
        Air = 0.04,
        SpectralCentroidHz = 2100,
        CrestFactorDb = 9,
        State = "Balanced"
    };

    private static Dictionary<int, double> ParseGains(EqPreset preset)
    {
        var result = new Dictionary<int, double>();
        var regex = new System.Text.RegularExpressions.Regex(@"Filter:\s*ON\s+PK\s+Fc\s+(?<freq>\d+)\s+Hz\s+Gain\s+(?<gain>[-+]?\d+(?:\.\d+)?)\s+dB", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match match in regex.Matches(preset.EqualizerApoText))
        {
            int frequency = int.Parse(match.Groups["freq"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (frequency == 32) continue;
            result[frequency] = double.Parse(match.Groups["gain"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static double SumAbs(Dictionary<int, double> gains) => gains.Values.Sum(Math.Abs);
}