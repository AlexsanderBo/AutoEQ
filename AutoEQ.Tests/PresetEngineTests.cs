using AutoEQ.Models;
using AutoEQ.Services;

namespace AutoEQ.Tests;

public sealed class PresetEngineTests
{
    private static readonly OutputAudioProfile TestProfile = new();

    [Fact]
    public void Catalog_ContainsAllNamedPresets_AndFallbackResolvable()
    {
        var engine = new PresetEngine();
        var names = engine.GetPresetNames();

        Assert.Contains("Universal Warm Balance", names);
        Assert.Contains("Late Night Smooth", names);
        Assert.Contains("Pure Device Pass", names);

        // Unknown name resolves to the fallback rather than throwing.
        var fallback = engine.GetPreset("does-not-exist");
        Assert.Equal(PresetCatalog.Fallback, fallback.Name);
    }

    [Fact]
    public void GetPreset_IsCaseInsensitive()
    {
        var engine = new PresetEngine();
        Assert.Equal("Bass Control", engine.GetPreset("bass control").Name);
    }

    [Fact]
    public void ChooseStartupPreset_NightMode_AlwaysSmooth()
    {
        var engine = new PresetEngine();
        var info = new AudioOutputInfo { DefaultDeviceName = "Headphones" };

        Assert.Equal("Late Night Smooth", engine.ChooseStartupPreset(info, nearWallMode: false, nightMode: true));
    }

    [Fact]
    public void ChooseStartupPreset_Headphones_PassThrough()
    {
        var engine = new PresetEngine();
        var info = new AudioOutputInfo { DefaultDeviceName = "USB Headset", ActiveOutputNames = ["headphone"] };

        Assert.Equal("Pure Device Pass", engine.ChooseStartupPreset(info, nearWallMode: false, nightMode: false));
    }

    [Fact]
    public void ChooseStartupPreset_NearWallSpeaker_RoomTamed()
    {
        var engine = new PresetEngine();
        var info = new AudioOutputInfo { DefaultDeviceName = "Bluetooth Speaker", ActiveOutputNames = ["speaker"] };

        Assert.Equal("Room Tamed Speaker", engine.ChooseStartupPreset(info, nearWallMode: true, nightMode: false));
    }

    [Fact]
    public void BuildDynamicAutoEqPreset_RememberAppliedCurve_DampensRepeatedSameFeatures()
    {
        var features = new AudioFeatures
        {
            Rms = 0.08,
            Confidence = 0.85,
            SubBass = 0.16,
            Bass = 0.18,
            LowMid = 0.16,
            Mid = 0.20,
            Presence = 0.18,
            Treble = 0.15,
            Air = 0.12,
            State = "Balanced"
        };

        var rememberedEngine = new PresetEngine();
        EqPreset first = rememberedEngine.BuildDynamicAutoEqPreset(features, TestProfile, nearWallMode: false, nightMode: false, fastAttack: true);
        rememberedEngine.RememberAppliedCurve(first);
        EqPreset second = rememberedEngine.BuildDynamicAutoEqPreset(features, TestProfile, nearWallMode: false, nightMode: false, fastAttack: true);

        var baselineEngine = new PresetEngine();
        _ = baselineEngine.BuildDynamicAutoEqPreset(features, TestProfile, nearWallMode: false, nightMode: false, fastAttack: true);
        EqPreset secondWithoutFeedbackRemoval = baselineEngine.BuildDynamicAutoEqPreset(features, TestProfile, nearWallMode: false, nightMode: false, fastAttack: true);

        double firstTotal = SumAbsGains(first);
        double secondTotal = SumAbsGains(second);
        Assert.True(secondTotal <= firstTotal + 0.0001, $"second={secondTotal}, first={firstTotal}");

        var firstBands = ParseGains(first.EqualizerApoText);
        var secondBands = ParseGains(second.EqualizerApoText);
        var noFeedbackBands = ParseGains(secondWithoutFeedbackRemoval.EqualizerApoText);

        foreach (var (frequency, firstGain) in firstBands.Where(pair => pair.Value > VoicingCoefficients.MinAudibleDynamicChangeDb))
        {
            Assert.True(secondBands[frequency] <= noFeedbackBands[frequency] + 0.0001, $"frequency={frequency}");
            Assert.True(Math.Abs(secondBands[frequency]) <= Math.Abs(noFeedbackBands[frequency]) + 0.0001, $"frequency={frequency}");
        }
    }

    [Fact]
    public void BuildDynamicAutoEqPreset_WithoutRememberedCurve_MatchesPreviousEmptyFeedbackState()
    {
        var features = new AudioFeatures
        {
            Rms = 0.09,
            Confidence = 0.9,
            SubBass = 0.22,
            Bass = 0.24,
            LowMid = 0.18,
            Mid = 0.21,
            Presence = 0.24,
            Treble = 0.18,
            Air = 0.14,
            State = "Balanced"
        };

        var engineA = new PresetEngine();
        var engineB = new PresetEngine();

        EqPreset a = engineA.BuildDynamicAutoEqPreset(features, TestProfile, nearWallMode: false, nightMode: false, fastAttack: true);
        EqPreset b = engineB.BuildDynamicAutoEqPreset(features, TestProfile, nearWallMode: false, nightMode: false, fastAttack: true);

        Assert.Equal(a.EqualizerApoText, b.EqualizerApoText);
    }

    [Fact]
    public void BuildDynamicAutoEqPreset_LeavesPreampAtUnity()
    {
        var engine = new PresetEngine();
        EqPreset preset = engine.BuildDynamicAutoEqPreset(StableFeatures(), TestProfile, nearWallMode: false, nightMode: false, fastAttack: true);

        Assert.StartsWith("Preamp: 0 dB", preset.EqualizerApoText);
    }

    [Fact]
    public void BuildNativeAutoEqPreset_LeavesPreampAtUnity()
    {
        var engine = new PresetEngine();
        EqPreset preset = engine.BuildNativeAutoEqPreset(new NativeAutoEqSnapshot
        {
            EqGainsDb = [2.0, -1.0],
            BandCentersHz = [1000, 6000],
            TruePeakDb = 0.5
        }, TestProfile, nearWallMode: false, nightMode: false);

        Assert.StartsWith("Preamp: 0 dB", preset.EqualizerApoText);
    }

    // --- EvaluateDynamicAutoEq: cổng quyết định ghi EQ động (logic nối vào OnFeatures) ---

    private static AudioFeatures StableFeatures(string state = "Balanced", double confidence = 0.9) => new()
    {
        Rms = 0.09,
        Confidence = confidence,
        SubBass = 0.22,
        Bass = 0.24,
        LowMid = 0.18,
        Mid = 0.21,
        Presence = 0.24,
        Treble = 0.18,
        Air = 0.14,
        State = state
    };

    // Đẩy đủ frame cùng state để vào cửa sổ ổn định rồi trả quyết định lần cuối.
    private static PresetEngine.PresetDecision FeedStable(PresetEngine engine, AudioFeatures features, int times)
    {
        PresetEngine.PresetDecision decision = new();
        for (int i = 0; i < times; i++)
            decision = engine.EvaluateDynamicAutoEq(features, TestProfile, autoEqEnabled: true, nearWallMode: false, nightMode: false);
        return decision;
    }

    [Fact]
    public void EvaluateDynamicAutoEq_Disabled_NoSwitch()
    {
        var engine = new PresetEngine();
        PresetEngine.PresetDecision decision = engine.EvaluateDynamicAutoEq(
            StableFeatures(), TestProfile, autoEqEnabled: false, nearWallMode: false, nightMode: false);

        Assert.False(decision.ShouldSwitch);
        Assert.Null(decision.RequestedPreset);
    }

    [Fact]
    public void EvaluateDynamicAutoEq_UnstableState_WaitsBeforeWriting()
    {
        var engine = new PresetEngine();
        // Chỉ 3 frame (< RequiredStableDetections=4) -> chưa ổn định.
        PresetEngine.PresetDecision decision = FeedStable(engine, StableFeatures(), times: 3);

        Assert.False(decision.ShouldSwitch);
        Assert.Null(decision.RequestedPreset);
    }

    [Fact]
    public void EvaluateDynamicAutoEq_LowConfidence_KeepsCurve()
    {
        var engine = new PresetEngine();
        PresetEngine.PresetDecision decision = FeedStable(engine, StableFeatures(confidence: 0.2), times: 5);

        Assert.False(decision.ShouldSwitch);
        Assert.Null(decision.RequestedPreset);
    }

    [Fact]
    public void EvaluateDynamicAutoEq_StableHighConfidence_WritesFirstCurve()
    {
        var engine = new PresetEngine();
        PresetEngine.PresetDecision decision = FeedStable(engine, StableFeatures(), times: 5);

        Assert.True(decision.ShouldSwitch);
        Assert.NotNull(decision.RequestedPreset);
        Assert.False(string.IsNullOrWhiteSpace(decision.RequestedPreset!.EqualizerApoText));
    }

    [Fact]
    public void EvaluateDynamicAutoEq_RepeatedIdenticalCurve_SkipsRedundantWrite()
    {
        var engine = new PresetEngine();
        AudioFeatures features = StableFeatures();

        PresetEngine.PresetDecision first = FeedStable(engine, features, times: 5);
        Assert.True(first.ShouldSwitch);

        // Feed lại đúng features y hệt -> curve trùng -> không ghi đè dư thừa.
        PresetEngine.PresetDecision second = engine.EvaluateDynamicAutoEq(
            features, TestProfile, autoEqEnabled: true, nearWallMode: false, nightMode: false);

        Assert.False(second.ShouldSwitch);
        Assert.Null(second.RequestedPreset);
    }

    private static double SumAbsGains(EqPreset preset)
        => ParseGains(preset.EqualizerApoText).Values.Sum(Math.Abs);

    private static Dictionary<double, double> ParseGains(string text)
        => System.Text.RegularExpressions.Regex.Matches(text, @"Filter:\s*ON\s+PK\s+Fc\s+(?<freq>\d+)\s+Hz\s+Gain\s+(?<gain>[-+]?\d+(?:\.\d+)?)\s+dB", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ToDictionary(
                match => double.Parse(match.Groups["freq"].Value, System.Globalization.CultureInfo.InvariantCulture),
                match => double.Parse(match.Groups["gain"].Value, System.Globalization.CultureInfo.InvariantCulture));
}
