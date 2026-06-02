using System.Globalization;
using AutoEQ.Models;
using AutoEQ.Services;

namespace AutoEQ.Tests;

public sealed class DynamicVoicingTests
{
    [Theory]
    [InlineData(50, 0.70)]
    [InlineData(200, 0.85)]
    [InlineData(1000, 1.00)]
    [InlineData(4000, 1.10)]
    [InlineData(9000, 1.25)]
    public void ResolveNativeBandQ_PicksExpectedQ(int frequencyHz, double expectedQ)
    {
        Assert.Equal(expectedQ, DynamicVoicing.ResolveNativeBandQ(frequencyHz));
    }

    [Theory]
    [InlineData(-9.0, 0.0)]
    [InlineData(9.0, 1.0)]
    [InlineData(0.0, 0.5)]
    [InlineData(-100.0, 0.0)]  // clamped low
    [InlineData(100.0, 1.0)]   // clamped high
    public void DbToWeight_MapsAndClampsToUnitRange(double db, double expected)
    {
        Assert.Equal(expected, DynamicVoicing.DbToWeight(db), 3);
    }

    [Theory]
    [InlineData(5.0, -3.0, 3.0, 3.0)]
    [InlineData(-5.0, -3.0, 3.0, -3.0)]
    [InlineData(1.5, -3.0, 3.0, 1.5)]
    public void ClampGain_RespectsBounds(double value, double min, double max, double expected)
    {
        Assert.Equal(expected, DynamicVoicing.ClampGain(value, min, max));
    }

    [Fact]
    public void FormatDb_UsesInvariantCulture()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // comma decimal
            Assert.Equal("-1.5", DynamicVoicing.FormatDb(-1.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public void Bands_HasFourteenAscendingFrequencies()
    {
        Assert.Equal(14, DynamicVoicing.Bands.Length);
        for (int i = 1; i < DynamicVoicing.Bands.Length; i++)
        {
            Assert.True(DynamicVoicing.Bands[i].FrequencyHz > DynamicVoicing.Bands[i - 1].FrequencyHz,
                $"Band {i} should be higher freq than {i - 1}");
        }
    }

    [Fact]
    public void SpreadToBands_ReturnsOneGainPerBand()
    {
        var vector = new DynamicVoicing.CorrectionVector(Bass: 2, Warmth: -1, Vocal: 1, Bright: -0.5);
        double[] gains = DynamicVoicing.SpreadToBands(vector);
        Assert.Equal(DynamicVoicing.Bands.Length, gains.Length);
    }

    [Fact]
    public void BuildEqualizerApoText_AlwaysEmitsPreampAndBassSafetyCut()
    {
        var profile = new OutputAudioProfile();
        double[] silentGains = new double[DynamicVoicing.Bands.Length]; // all zero -> skipped
        string text = DynamicVoicing.BuildEqualizerApoText(-3.0, silentGains, profile);

        Assert.Contains("Preamp:", text);
        Assert.Contains("Fc 32 Hz", text); // bass safety cut always present
    }

    [Fact]
    public void BuildEqualizerApoText_SkipsInaudibleBands()
    {
        var profile = new OutputAudioProfile();
        double[] gains = new double[DynamicVoicing.Bands.Length];
        gains[2] = 0.05;  // below 0.15 threshold -> skipped
        gains[5] = 2.0;   // audible -> emitted
        string text = DynamicVoicing.BuildEqualizerApoText(-3.0, gains, profile);

        Assert.DoesNotContain($"Fc {DynamicVoicing.Bands[2].FrequencyHz} Hz", text);
        Assert.Contains($"Fc {DynamicVoicing.Bands[5].FrequencyHz} Hz", text);
    }

    [Fact]
    public void ResolveBaseGains_NearWall_CutsLowMids()
    {
        double[] flat = DynamicVoicing.ResolveBaseGains(nearWallMode: false, nightMode: false);
        double[] wall = DynamicVoicing.ResolveBaseGains(nearWallMode: true, nightMode: false);

        Assert.True(wall[3] < flat[3], "near-wall should cut the 180 Hz region further");
    }

    [Fact]
    public void ApplyLoudnessAwareVoicing_LowLevel_LiftsVocalAndBright()
    {
        double bass = 0, warmth = 0, vocal = 0, bright = 0;
        DynamicVoicing.ApplyLoudnessAwareVoicing(rms: 0.0, nightMode: false,
            ref bass, ref warmth, ref vocal, ref bright);

        Assert.True(vocal > 0, "quiet playback should lift vocal");
        Assert.True(bright > 0, "quiet playback should lift bright");
    }

    [Fact]
    public void ApplyLoudnessAwareVoicing_HighLevel_TrimsBass()
    {
        double bass = 0, warmth = 0, vocal = 0, bright = 0;
        DynamicVoicing.ApplyLoudnessAwareVoicing(rms: 0.30, nightMode: false,
            ref bass, ref warmth, ref vocal, ref bright);

        Assert.True(bass < 0, "loud playback should trim bass");
    }

    [Fact]
    public void ResolveTargetCurve_NightMode_LimitsBassAndBrightTargets()
    {
        var profile = new OutputAudioProfile { TargetBass = 0.40, TargetBright = 0.30 };

        var target = DynamicVoicing.ResolveTargetCurve(profile, nearWallMode: false, nightMode: true);

        Assert.Equal(0.24, target.Bass);
        Assert.Equal(0.15, target.Bright);
        Assert.True(target.ConfidenceFloor >= 0.4);
    }

    [Fact]
    public void ComputeCorrection_HighCentroid_TrimsBrightBoost()
    {
        var profile = new OutputAudioProfile { TargetBright = 0.22 };
        var normal = new AudioFeatures { Rms = 0.08, Treble = 0.05, Air = 0.03, Confidence = 1.0, SpectralCentroidHz = 1200 };
        var bright = new AudioFeatures { Rms = 0.08, Treble = 0.05, Air = 0.03, Confidence = 1.0, SpectralCentroidHz = 9000 };

        var normalCorrection = DynamicVoicing.ComputeCorrection(normal, profile, nearWallMode: false, nightMode: false);
        var brightCorrection = DynamicVoicing.ComputeCorrection(bright, profile, nearWallMode: false, nightMode: false);

        Assert.True(brightCorrection.Bright < normalCorrection.Bright);
    }
}
