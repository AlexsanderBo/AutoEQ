using AutoEQ.Models;

namespace AutoEQ.Services;

/// <summary>
/// Static, hand-tuned Equalizer APO preset catalog.
/// Pure data: no decision logic, no state. Lives apart from <see cref="PresetEngine"/>
/// so the orchestration engine stays small and the tunings are easy to audit/test.
/// </summary>
public static class PresetCatalog
{
    public const string Fallback = "Cozy";

    public static IReadOnlyDictionary<string, EqPreset> Build() =>
        CreatePresets().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<EqPreset> CreatePresets()
    {
        yield return Preset("Reference", """
Preamp: 0 dB
Filter: ON PK Fc 180 Hz Gain -1.5 dB Q 1.00
Filter: ON PK Fc 260 Hz Gain -1 dB Q 1.00
Filter: ON PK Fc 2500 Hz Gain 1.5 dB Q 1.00
Filter: ON PK Fc 6000 Hz Gain 0.8 dB Q 0.95
""");
        yield return Preset("Cozy", """
Preamp: 0 dB
Filter: ON PK Fc 160 Hz Gain -2 dB Q 1.0
Filter: ON PK Fc 280 Hz Gain -1.5 dB Q 1.0
Filter: ON PK Fc 2500 Hz Gain 1.5 dB Q 1.0
Filter: ON PK Fc 5000 Hz Gain 1 dB Q 1.0
""");
        yield return Preset("Corner", """
Preamp: 0 dB
Filter: ON PK Fc 90 Hz Gain -1.5 dB Q 0.8
Filter: ON PK Fc 180 Hz Gain -3 dB Q 1.0
Filter: ON PK Fc 320 Hz Gain -1.5 dB Q 1.0
Filter: ON PK Fc 2500 Hz Gain 1.5 dB Q 1.0
""");
        yield return Preset("Tight", """
Preamp: 0 dB
Filter: ON PK Fc 120 Hz Gain -2 dB Q 0.9
Filter: ON PK Fc 200 Hz Gain -3 dB Q 1.0
Filter: ON PK Fc 320 Hz Gain -2 dB Q 1.0
Filter: ON PK Fc 2500 Hz Gain 1 dB Q 1.0
""");
        yield return Preset("Podcast", """
Preamp: 0 dB
Filter: ON PK Fc 220 Hz Gain -1.5 dB Q 1.0
Filter: ON PK Fc 1200 Hz Gain 1 dB Q 1.0
Filter: ON PK Fc 2500 Hz Gain 2 dB Q 1.0
Filter: ON PK Fc 4500 Hz Gain 1 dB Q 1.0
""");
        yield return Preset("Silk", """
Preamp: 0 dB
Filter: ON PK Fc 4500 Hz Gain -1.5 dB Q 1.0
Filter: ON PK Fc 7500 Hz Gain -2.5 dB Q 1.0
Filter: ON PK Fc 10000 Hz Gain -1 dB Q 0.8
""");
        yield return Preset("Midnight", """
Preamp: 0 dB
Filter: ON PK Fc 80 Hz Gain -3 dB Q 0.8
Filter: ON PK Fc 160 Hz Gain -2 dB Q 1.0
Filter: ON PK Fc 2500 Hz Gain 1 dB Q 1.0
Filter: ON PK Fc 8000 Hz Gain -1.5 dB Q 1.0
""");
        yield return Preset("Flat", "Preamp: 0 dB");
    }

    private static EqPreset Preset(string name, string text) => new()
    {
        Name = name,
        EqualizerApoText = text.Trim() + Environment.NewLine
    };
}
