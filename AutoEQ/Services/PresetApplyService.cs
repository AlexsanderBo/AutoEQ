using System.Text.RegularExpressions;
using AutoEQ.Models;

namespace AutoEQ.Services;

public interface IPresetApplyService : IDisposable
{
    string LastAppliedPresetName { get; }
    Task<AutoEqResult> ApplyAsync(EqPreset preset, string reason, CancellationToken cancellationToken = default);
}

public sealed class PresetApplyService : IPresetApplyService
{
    private readonly IEqualizerApoManager _apoManager;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private string _lastAppliedPresetText = string.Empty;
    private CancellationTokenSource? _smoothPresetApplyCts;
    private const int SmoothApplySteps = 14;
    private const int SmoothApplyStepDelayMs = 300;

    public PresetApplyService(IEqualizerApoManager apoManager)
    {
        _apoManager = apoManager;
    }

    public string LastAppliedPresetName { get; private set; } = string.Empty;

    public async Task<AutoEqResult> ApplyAsync(EqPreset preset, string reason, CancellationToken cancellationToken = default)
    {
        if (!preset.IsDynamic && string.Equals(LastAppliedPresetName, preset.Name, StringComparison.OrdinalIgnoreCase))
        {
            return AutoEqResult.Ok("Preset already applied.");
        }

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!preset.IsDynamic && string.Equals(LastAppliedPresetName, preset.Name, StringComparison.OrdinalIgnoreCase))
            {
                return AutoEqResult.Ok("Preset already applied.");
            }

            if (!string.IsNullOrWhiteSpace(_lastAppliedPresetText))
            {
                await ApplyPresetSmoothlyAsync(preset, reason, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _smoothPresetApplyCts?.Cancel();
                await _apoManager.ApplyPresetAsync(preset, reason).ConfigureAwait(false);
            }

            LastAppliedPresetName = preset.Name;
            _lastAppliedPresetText = preset.EqualizerApoText;
            return AutoEqResult.Ok($"Applied preset '{preset.Name}'.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AutoEqResult.Fail("Preset apply canceled.", "preset_apply_canceled");
        }
        catch (Exception ex)
        {
            return AutoEqResult.FromException(ex, "preset_apply_failed");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task ApplyPresetSmoothlyAsync(EqPreset preset, string reason, CancellationToken cancellationToken)
    {
        _smoothPresetApplyCts?.Cancel();
        _smoothPresetApplyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken linkedToken = _smoothPresetApplyCts.Token;

        EqCurve from = EqCurve.Parse(_lastAppliedPresetText);
        EqCurve to = EqCurve.Parse(preset.EqualizerApoText);
        await _apoManager.EnsureIncludeLineAsync().ConfigureAwait(false);
        for (int step = 1; step <= SmoothApplySteps; step++)
        {
            linkedToken.ThrowIfCancellationRequested();
            double t = SmootherStep(step / (double)SmoothApplySteps);
            EqCurve curve = EqCurve.Interpolate(from, to, t);
            var stepPreset = new EqPreset
            {
                Name = $"{preset.Name} ({step}/{SmoothApplySteps})",
                EqualizerApoText = curve.ToEqualizerApoText(),
                IsDynamic = true
            };

            await _apoManager.WriteAutoEQConfigAsync(stepPreset.EqualizerApoText, stepPreset, reason).ConfigureAwait(false);
            if (step < SmoothApplySteps)
            {
                await Task.Delay(SmoothApplyStepDelayMs, linkedToken).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _smoothPresetApplyCts?.Cancel();
        _smoothPresetApplyCts?.Dispose();
        _semaphore.Dispose();
    }

    private static double SmootherStep(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private sealed class EqCurve
    {
        private static readonly Regex PreampRegex = new(@"Preamp:\s*(?<gain>[-+]?\d+(?:\.\d+)?)\s*dB", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FilterRegex = new(@"Filter:\s*ON\s+PK\s+Fc\s+(?<freq>\d+)\s+Hz\s+Gain\s+(?<gain>[-+]?\d+(?:\.\d+)?)\s+dB\s+Q\s+(?<q>[-+]?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public double Preamp { get; private set; }
        public Dictionary<int, EqBand> Bands { get; init; } = new();

        public static EqCurve Parse(string text)
        {
            var curve = new EqCurve();
            Match preampMatch = PreampRegex.Match(text);
            if (preampMatch.Success)
            {
                curve.Preamp = ParseInvariant(preampMatch.Groups["gain"].Value);
            }

            foreach (Match match in FilterRegex.Matches(text))
            {
                int frequency = int.Parse(match.Groups["freq"].Value, System.Globalization.CultureInfo.InvariantCulture);
                curve.Bands[frequency] = new EqBand(
                    frequency,
                    ParseInvariant(match.Groups["gain"].Value),
                    ParseInvariant(match.Groups["q"].Value));
            }

            return curve;
        }

        public static EqCurve Interpolate(EqCurve from, EqCurve to, double t)
        {
            var result = new EqCurve
            {
                Preamp = Lerp(from.Preamp, to.Preamp, t)
            };

            foreach (int frequency in from.Bands.Keys.Concat(to.Bands.Keys).Distinct().OrderBy(freq => freq))
            {
                EqBand? start = from.Bands.GetValueOrDefault(frequency);
                EqBand? end = to.Bands.GetValueOrDefault(frequency);
                double gain = Lerp(start?.GainDb ?? 0, end?.GainDb ?? 0, t);
                double q = Lerp(start?.Q ?? end?.Q ?? 1.0, end?.Q ?? start?.Q ?? 1.0, t);
                if (Math.Abs(gain) < 0.05) continue;

                result.Bands[frequency] = new EqBand(frequency, gain, q);
            }

            return result;
        }

        public string ToEqualizerApoText()
        {
            var lines = new List<string> { $"Preamp: {FormatInvariant(Preamp)} dB" };
            lines.AddRange(Bands.Values
                .OrderBy(band => band.FrequencyHz)
                .Select(band => $"Filter: ON PK Fc {band.FrequencyHz} Hz Gain {FormatInvariant(band.GainDb)} dB Q {FormatInvariant(band.Q)}"));

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static double Lerp(double from, double to, double t) => from + ((to - from) * t);
        private static double ParseInvariant(string value) => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        private static string FormatInvariant(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record EqBand(int FrequencyHz, double GainDb, double Q);
}