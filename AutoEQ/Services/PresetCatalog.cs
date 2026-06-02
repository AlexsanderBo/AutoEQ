using AutoEQ.Models;

namespace AutoEQ.Services;

/// <summary>
/// Static, hand-tuned Equalizer APO preset catalog.
/// Pure data: no decision logic, no state. Lives apart from <see cref="PresetEngine"/>
/// so the orchestration engine stays small and the tunings are easy to audit/test.
/// </summary>
public static class PresetCatalog
{
    public const string Fallback = "Universal Warm Balance";

    public static IReadOnlyDictionary<string, EqPreset> Build() =>
        CreatePresets().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<EqPreset> CreatePresets()
    {
        yield return Preset("AutoEQ Live - AutoEQ Optimized", """
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
