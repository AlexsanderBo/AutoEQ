using System.Globalization;
using System.Text.Json;
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
    public void Bands_HighTrebleQ_StaysWideEnoughToAvoidSharpBoosts()
    {
        Assert.All(DynamicVoicing.Bands.Where(band => band.FrequencyHz >= 6000), band =>
            Assert.True(band.Q <= 1.0, $"Band {band.FrequencyHz} Hz should keep Q <= 1.0"));
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
    public void BuildDeviceScopedBlock_UsesEndpointId_WhenPatternProvided()
    {
        var profile = new OutputAudioProfile { Name = "USB DAC", DeviceType = "DAC" };
        double[] gains = new double[DynamicVoicing.Bands.Length];

        string text = DynamicVoicing.BuildDeviceScopedBlock("{0.0.0.00000000}.{abc}", -3.0, gains, profile);

        Assert.StartsWith("Device: {0.0.0.00000000}.{abc}", text);
        Assert.Contains("Preamp: -3 dB", text);
    }

    [Fact]
    public void BuildDeviceScopedBlock_FallsBackToGlobal_WhenPatternMissing()
    {
        var profile = new OutputAudioProfile();
        double[] gains = new double[DynamicVoicing.Bands.Length];

        string text = DynamicVoicing.BuildDeviceScopedBlock(string.Empty, -3.0, gains, profile);

        Assert.DoesNotContain("Device:", text);
        Assert.StartsWith("Preamp:", text);
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
    public void ApplyLoudnessAwareVoicing_PositiveBrightCaps_AreReducedButCutsRemain()
    {
        double dayBass = 0, dayWarmth = 0, dayVocal = 0, dayBright = 9;
        DynamicVoicing.ApplyLoudnessAwareVoicing(rms: 0.08, nightMode: false,
            ref dayBass, ref dayWarmth, ref dayVocal, ref dayBright);
        Assert.Equal(1.0, dayBright);

        double nightBass = 0, nightWarmth = 0, nightVocal = 0, nightBright = 9;
        DynamicVoicing.ApplyLoudnessAwareVoicing(rms: 0.08, nightMode: true,
            ref nightBass, ref nightWarmth, ref nightVocal, ref nightBright);
        Assert.Equal(0.8, nightBright);

        double cutBass = 0, cutWarmth = 0, cutVocal = 0, cutBright = -9;
        DynamicVoicing.ApplyLoudnessAwareVoicing(rms: 0.08, nightMode: false,
            ref cutBass, ref cutWarmth, ref cutVocal, ref cutBright);
        Assert.Equal(-3.8, cutBright);
    }

    [Fact]
    public void ApplyLoudnessAwareVoicing_PositiveWarmthCap_IsReducedButCutRemains()
    {
        double bass = 0, warmth = 9, vocal = 0, bright = 0;
        DynamicVoicing.ApplyLoudnessAwareVoicing(rms: 0.08, nightMode: false,
            ref bass, ref warmth, ref vocal, ref bright);
        Assert.Equal(0.8, warmth);

        double cutBass = 0, cutWarmth = -9, cutVocal = 0, cutBright = 0;
        DynamicVoicing.ApplyLoudnessAwareVoicing(rms: 0.08, nightMode: false,
            ref cutBass, ref cutWarmth, ref cutVocal, ref cutBright);
        Assert.Equal(-3.4, cutWarmth);
    }

    [Fact]
    public void LoudnessAware_TinyMaxBoost_VocalDoesNotExceedProfileCap()
    {
        var profile = new OutputAudioProfile { MaxBoostDb = 0.10 };
        double bass = 0, warmth = 0, vocal = 0, bright = 0;

        DynamicVoicing.ApplyLoudnessAwareVoicing(rms: 0.0, nightMode: false, profile,
            ref bass, ref warmth, ref vocal, ref bright);

        Assert.True(vocal <= profile.MaxBoostDb);
        Assert.True(bright <= profile.MaxBoostDb);
        Assert.Equal(0.10, vocal, 6);
        Assert.Equal(0.10, bright, 6);
    }

    [Fact]
    public void SpreadToBands_TinyMaxBoost_NoBandExceedsProfileCap()
    {
        var profile = new OutputAudioProfile { MaxBoostDb = 0.10 };
        var vector = new DynamicVoicing.CorrectionVector(Bass: 0.10, Warmth: 0.10, Vocal: 0.10, Bright: 0.10);

        double[] gains = DynamicVoicing.SpreadToBands(vector, profile);

        Assert.Equal(0.10, gains[3], 6);
        Assert.Equal(0.10, gains[10], 6);
        Assert.All(gains, gain => Assert.True(gain <= profile.MaxBoostDb));
    }

    [Fact]
    public void SpreadToBands_BassRegionCut_RespectsMaxCutPlusBassSafetyCut()
    {
        var profile = new OutputAudioProfile { MaxCutDb = -6.0, BassSafetyCutDb = -1.5 };
        var vector = new DynamicVoicing.CorrectionVector(Bass: -20.0, Warmth: 0, Vocal: 0, Bright: 0);

        double[] gains = DynamicVoicing.SpreadToBands(vector, profile);

        Assert.All(gains.Take(4), gain => Assert.True(gain >= -4.5));
        Assert.Equal(-4.5, gains[2], 6);
        Assert.Equal(-4.5, gains[3], 6);
    }

    [Fact]
    public void LoudnessAware_NormalProfile_PreservesExistingCaps()
    {
        var profile = new OutputAudioProfile { MaxBoostDb = 3.0 };
        double dayBass = 0, dayWarmth = 0, dayVocal = 9, dayBright = 9;

        DynamicVoicing.ApplyLoudnessAwareVoicing(rms: 0.08, nightMode: false, profile,
            ref dayBass, ref dayWarmth, ref dayVocal, ref dayBright);

        Assert.Equal(3.0, dayVocal);
        Assert.Equal(1.0, dayBright);
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
        var profile = new OutputAudioProfile { TargetBright = 0.16 };
        var normal = new AudioFeatures { Rms = 0.08, Treble = 0.05, Air = 0.03, Confidence = 1.0, SpectralCentroidHz = 1200 };
        var bright = new AudioFeatures { Rms = 0.08, Treble = 0.05, Air = 0.03, Confidence = 1.0, SpectralCentroidHz = 9000 };

        var normalCorrection = DynamicVoicing.ComputeCorrection(normal, profile, nearWallMode: false, nightMode: false);
        var brightCorrection = DynamicVoicing.ComputeCorrection(bright, profile, nearWallMode: false, nightMode: false);

        Assert.True(brightCorrection.Bright < normalCorrection.Bright);
    }

    [Fact]
    public void ComputeCorrection_DarkSource_BrightBoost_DoesNotExceedNewDayCap()
    {
        var profile = new OutputAudioProfile { TargetBright = 0.30 };
        var dark = new AudioFeatures { Rms = 0.08, Treble = 0.0, Air = 0.0, Confidence = 1.0, SpectralCentroidHz = 1200 };

        var correction = DynamicVoicing.ComputeCorrection(dark, profile, nearWallMode: false, nightMode: false);

        Assert.True(correction.Bright <= 0.8);
    }

    [Fact]
    public void ComputeCorrection_LowMidDeficientSource_WarmthBoost_DoesNotExceedNewCap()
    {
        var profile = new OutputAudioProfile { TargetWarmth = 0.25 };
        var leanLowMid = new AudioFeatures { Rms = 0.08, LowMid = 0.0, Confidence = 1.0 };

        var correction = DynamicVoicing.ComputeCorrection(leanLowMid, profile, nearWallMode: false, nightMode: false);

        Assert.True(correction.Warmth <= 0.6);
    }

    [Fact]
    public void ComputeCorrection_BrightAndWarmthNegativeFloors_AreUnchanged()
    {
        var profile = new OutputAudioProfile { TargetBright = 0.0, TargetWarmth = 0.0 };
        var excessive = new AudioFeatures
        {
            Rms = 0.08,
            LowMid = 1.0,
            Treble = 0.6,
            Air = 0.6,
            Confidence = 1.0,
            SpectralCentroidHz = 1200
        };

        var correction = DynamicVoicing.ComputeCorrection(excessive, profile, nearWallMode: false, nightMode: false);

        Assert.Equal(-3.0, correction.Warmth);
        Assert.Equal(-3.2, correction.Bright);
    }

    [Fact]
    public void CompensateFeedback_AppliedBassCut_RestoresDeviceBass()
    {
        double[] measured = [0.55, 0.50, 0.12, 0.16, 0.12, 0.06, 0.04];
        double[] bassCutGains = new double[DynamicVoicing.Bands.Length];
        bassCutGains[0] = -3.0;
        bassCutGains[1] = -3.0;

        double[] compensated = CalibrationService.CompensateFeedback(measured, bassCutGains);

        Assert.True(compensated[0] > measured[0], "negative bass EQ must be added back into measured device bass");
        Assert.True(compensated[1] > measured[1], "negative bass EQ must be added back into measured device bass");
        Assert.True(compensated[0] >= 0.67, "bass-dominant device should remain bass-dominant after compensation");
    }

    [Fact]
    public void CompensateFeedback_AppliedTrebleBoost_ReducesMeasuredBrightness()
    {
        double[] measured = [0.18, 0.17, 0.12, 0.16, 0.12, 0.30, 0.28];
        double[] trebleBoostGains = new double[DynamicVoicing.Bands.Length];
        trebleBoostGains[11] = 3.0;
        trebleBoostGains[12] = 3.0;
        trebleBoostGains[13] = 3.0;

        double[] compensated = CalibrationService.CompensateFeedback(measured, trebleBoostGains);

        Assert.True(compensated[5] < measured[5], "positive treble EQ must be removed from measured brightness");
        Assert.True(compensated[6] < measured[6], "positive air EQ must be removed from measured brightness");
    }

    [Fact]
    public void CompensateFeedback_ExtremeEq_ClampsCompensationToNormalizedBounds()
    {
        double[] measured = [0.50, 0.50, 0.20, 0.20, 0.20, 0.50, 0.50];
        double[] extremeGains = Enumerable.Repeat(24.0, DynamicVoicing.Bands.Length).ToArray();

        double[] compensated = CalibrationService.CompensateFeedback(measured, extremeGains);

        Assert.Equal(0.30, compensated[0], 6);
        Assert.Equal(0.30, compensated[1], 6);
        Assert.Equal(0.00, compensated[2], 6);
        Assert.Equal(0.30, compensated[5], 6);
        Assert.Equal(0.30, compensated[6], 6);
    }

    [Fact]
    public void GetLastDynamicGains_WhenAutoEqOff_ReturnsFlat()
    {
        var engine = new PresetEngine();
        double[] gains = engine.GetLastDynamicGains();

        Assert.Equal(DynamicVoicing.Bands.Length, gains.Length);
        Assert.All(gains, gain => Assert.Equal(0.0, gain));
    }

    [Fact]
    public void CompensateFeedback_FlatLastDynamicGains_DoesNotChangeMeasurement()
    {
        var engine = new PresetEngine();
        double[] measured = [0.25, 0.24, 0.18, 0.22, 0.20, 0.13, 0.10];

        double[] compensated = CalibrationService.CompensateFeedback(measured, engine.GetLastDynamicGains());

        Assert.Equal(measured, compensated);
        Assert.NotSame(measured, compensated);
    }

    [Fact]
    public void DeriveTargetsFromMeasurement_ChronicBassHeavy_LowersBassWithinClamp()
    {
        var basis = new OutputAudioProfile { TargetBass = 0.31, MaxBoostDb = 3.0 };
        double[] bassHeavy = [0.55, 0.50, 0.10, 0.16, 0.12, 0.05, 0.04];

        var target = DynamicVoicing.DeriveTargetsFromMeasurement(bassHeavy, centroidHz: 1600, basis);

        Assert.True(target.Bass < basis.TargetBass);
        Assert.InRange(target.Bass, basis.TargetBass - 0.06, basis.TargetBass + 0.06);
    }

    [Fact]
    public void DeriveTargetsFromMeasurement_DarkMeasurement_RaisesBrightWithinClamp()
    {
        var basis = new OutputAudioProfile { TargetBright = 0.20, MaxBoostDb = 3.0 };
        double[] dark = [0.25, 0.24, 0.18, 0.22, 0.20, 0.04, 0.03];

        var target = DynamicVoicing.DeriveTargetsFromMeasurement(dark, centroidHz: 1200, basis);

        Assert.True(target.Bright > basis.TargetBright);
        Assert.InRange(target.Bright, basis.TargetBright - 0.05, basis.TargetBright + 0.05);
    }

    [Fact]
    public void DeriveTargetsFromMeasurement_DoesNotRequireBoostBeyondMaxBoostOrBreakBassSafetyCut()
    {
        var basis = new OutputAudioProfile
        {
            TargetBass = 0.31,
            TargetBright = 0.20,
            MaxBoostDb = 0.10,
            BassSafetyCutDb = -1.5
        };
        double[] leanAndDark = [0.01, 0.01, 0.12, 0.25, 0.25, 0.01, 0.01];

        var target = DynamicVoicing.DeriveTargetsFromMeasurement(leanAndDark, centroidHz: 800, basis);
        var calibrated = WithCalibration(basis, target);
        var correction = DynamicVoicing.ComputeCorrection(UsableFeatures(SubBass: 0.0, Bass: 0.0, Treble: 0.0, Air: 0.0), calibrated, nearWallMode: false, nightMode: false);

        Assert.True(correction.Bass <= basis.MaxBoostDb);
        Assert.True(correction.Bright <= basis.MaxBoostDb);
        Assert.Equal(-1.5, calibrated.BassSafetyCutDb);

        static OutputAudioProfile WithCalibration(OutputAudioProfile p, DynamicVoicing.TargetCurve t) => new()
        {
            TargetBass = t.Bass,
            TargetWarmth = t.Warmth,
            TargetVocal = t.Vocal,
            TargetBright = t.Bright,
            MaxBoostDb = p.MaxBoostDb,
            MaxCutDb = p.MaxCutDb,
            BassSafetyCutDb = p.BassSafetyCutDb
        };
    }

    [Fact]
    public void Observe_RejectsSilenceClippingAndNoiseFrames()
    {
        var service = new CalibrationService(requiredSamples: 8);

        for (int i = 0; i < 20; i++)
        {
            service.Observe("device", UsableFeatures(Rms: 0.001));
            service.Observe("device", UsableFeatures(Peak: 0.99));
            service.Observe("device", UsableFeatures(SpectralFlatness: 0.90));
        }

        Assert.Equal(0.0, service.GetProgress("device"));
    }

    [Fact]
    public void Observe_StableFrames_RaisesCalibrationReadyAfterRequiredSamples()
    {
        var service = new CalibrationService(requiredSamples: 8);
        CalibrationReadyEventArgs? ready = null;
        service.CalibrationReady += (_, args) => ready = args;

        for (int i = 0; i < 8; i++)
        {
            service.Observe("device", UsableFeatures(
                SubBass: 0.30 + i * 0.001,
                Bass: 0.28 + i * 0.001,
                SpectralCentroidHz: 1800 + i));
        }

        Assert.NotNull(ready);
        Assert.Equal("device", ready.DeviceKey);
        Assert.Equal(8, ready.SampleCount);
        Assert.InRange(ready.MeasuredBandAverage[0], 0.30, 0.304);
        Assert.InRange(ready.MeasuredBandAverage[1], 0.28, 0.284);
        Assert.True(service.GetProgress("device") >= 1.0);
    }

    [Fact]
    public void ApplyCalibration_ThenGetOrCreate_PreservesCalibratedTargets()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autoeq-profiles-{Guid.NewGuid():N}", "output-profiles.json");
        var info = TestOutputInfo("device-1", "USB DAC");
        var store = new OutputAudioProfileStore(path);
        store.GetOrCreate(info, nearWallMode: false, nightMode: false);

        store.ApplyCalibration("device-1", bass: 0.25, warmth: 0.17, vocal: 0.41, bright: 0.23, measuredAverage: [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7], sampleCount: 42);
        OutputAudioProfile profile = store.GetOrCreate(info, nearWallMode: true, nightMode: true);

        Assert.True(profile.IsCalibrated);
        Assert.Equal(0.25, profile.TargetBass);
        Assert.Equal(0.17, profile.TargetWarmth);
        Assert.Equal(0.41, profile.TargetVocal);
        Assert.Equal(0.23, profile.TargetBright);
        Assert.Equal(42, profile.CalibrationSampleCount);
        Assert.Equal([0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7], profile.MeasuredBandAverage);
    }

    [Fact]
    public void ResetCalibration_ReturnsInferredProfileAndClearsIsCalibrated()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autoeq-profiles-{Guid.NewGuid():N}", "output-profiles.json");
        var info = TestOutputInfo("device-1", "Realtek Speakers");
        var store = new OutputAudioProfileStore(path);
        store.GetOrCreate(info, nearWallMode: false, nightMode: false);
        store.ApplyCalibration("device-1", 0.25, 0.17, 0.41, 0.23, [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7], 42);

        OutputAudioProfile reset = store.ResetCalibration(info, nearWallMode: false, nightMode: false);

        Assert.False(reset.IsCalibrated);
        Assert.Equal(0, reset.CalibrationSampleCount);
        Assert.Null(reset.MeasuredBandAverage);
        Assert.Equal(0.30, reset.TargetBass);
        Assert.Equal(0.21, reset.TargetBright);
    }

    [Fact]
    public void OutputAudioProfile_OldJsonMissingCalibrationFields_DefaultsToNotCalibrated()
    {
        string json = """
        {
          "DeviceId": "legacy",
          "Name": "Legacy Device",
          "TargetBass": 0.31,
          "TargetWarmth": 0.19,
          "TargetVocal": 0.39,
          "TargetBright": 0.20
        }
        """;

        OutputAudioProfile? profile = JsonSerializer.Deserialize<OutputAudioProfile>(json);

        Assert.NotNull(profile);
        Assert.False(profile.IsCalibrated);
        Assert.Equal(0, profile.CalibrationSampleCount);
        Assert.Null(profile.MeasuredBandAverage);
    }

    private static AudioFeatures UsableFeatures(
        double Rms = 0.08,
        double Peak = 0.40,
        double SpectralFlatness = 0.25,
        double SpectralCentroidHz = 1800,
        double SubBass = 0.25,
        double Bass = 0.24,
        double LowMid = 0.18,
        double Mid = 0.22,
        double Presence = 0.20,
        double Treble = 0.13,
        double Air = 0.10) => new()
    {
        Rms = Rms,
        Peak = Peak,
        SpectralFlatness = SpectralFlatness,
        SpectralCentroidHz = SpectralCentroidHz,
        Confidence = 1.0,
        SubBass = SubBass,
        Bass = Bass,
        LowMid = LowMid,
        Mid = Mid,
        Presence = Presence,
        Treble = Treble,
        Air = Air
    };

    private static AudioOutputInfo TestOutputInfo(string id, string name) => new()
    {
        DefaultDeviceId = id,
        DefaultDeviceName = name,
        SoundCardName = name,
        MainboardName = "Test Board"
    };
}
