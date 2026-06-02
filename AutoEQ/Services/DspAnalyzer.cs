using MathNet.Numerics.IntegralTransforms;
using System.Numerics;
using AutoEQ.Models;

namespace AutoEQ.Services;

public interface IDspAnalyzer
{
    AudioFeatures Analyze(float[] interleavedSamples, int channels, int sampleRate);
    event EventHandler<string>? ErrorOccurred;
}

public sealed class DspAnalyzer : IDspAnalyzer
{
    public event EventHandler<string>? ErrorOccurred;

    private const double QuietRmsThreshold = 0.003;
    private const int MinFftSize = 1024;
    private const int PreferredWelchFftSize = 8192;
    private const int MaxAnalysisSeconds = 4;
    private const double ClassifierMargin = 0.02;
    private const double SpectrumFloor = 1e-20;

    public AudioFeatures Analyze(float[] interleavedSamples, int channels, int sampleRate)
    {
        try
        {
            if (interleavedSamples.Length == 0 || channels <= 0 || sampleRate <= 0)
            {
                return Quiet();
            }

            float[] mono = ToMono(interleavedSamples, channels);
            double rms = Math.Sqrt(mono.Select(s => (double)s * s).DefaultIfEmpty(0).Average());
            double peak = mono.Select(s => Math.Abs((double)s)).DefaultIfEmpty(0).Max();
            if (rms < QuietRmsThreshold)
            {
                return Quiet(rms, peak);
            }

            int fftSize = Math.Min(HighestPowerOfTwo(Math.Min(mono.Length, sampleRate * MaxAnalysisSeconds)), PreferredWelchFftSize);
            if (fftSize < MinFftSize)
            {
                return Quiet(rms, peak);
            }

            double[] powerSpectrum = BuildWelchPowerSpectrum(mono, fftSize, sampleRate * MaxAnalysisSeconds);
            double spectralCentroidHz = SpectralCentroid(powerSpectrum, sampleRate, fftSize);
            double spectralRolloffHz = SpectralRolloff(powerSpectrum, sampleRate, fftSize, 0.85);
            double spectralFlatness = SpectralFlatness(powerSpectrum);
            double crestFactorDb = 20.0 * Math.Log10(Math.Max(peak, SpectrumFloor) / Math.Max(rms, SpectrumFloor));
            double dynamicRangeDb = EstimateShortTermDynamicRangeDb(mono, sampleRate);

            double subBass = BandEnergy(powerSpectrum, sampleRate, fftSize, 20, 60);
            double bass = BandEnergy(powerSpectrum, sampleRate, fftSize, 60, 120);
            double lowMid = BandEnergy(powerSpectrum, sampleRate, fftSize, 120, 350);
            double mid = BandEnergy(powerSpectrum, sampleRate, fftSize, 350, 2000);
            double presence = BandEnergy(powerSpectrum, sampleRate, fftSize, 2000, 5000);
            double treble = BandEnergy(powerSpectrum, sampleRate, fftSize, 5000, 10000);
            double air = BandEnergy(powerSpectrum, sampleRate, fftSize, 10000, 16000);
            double total = subBass + bass + lowMid + mid + presence + treble + air;

            if (total <= double.Epsilon)
            {
                return Quiet(rms, peak);
            }

            var features = new AudioFeatures
            {
                Rms = rms,
                Peak = peak,
                CrestFactorDb = crestFactorDb,
                SpectralCentroidHz = spectralCentroidHz,
                SpectralRolloffHz = spectralRolloffHz,
                SpectralFlatness = spectralFlatness,
                DynamicRangeDb = dynamicRangeDb,
                Confidence = EstimateConfidence(rms, peak, fftSize, mono.Length, spectralFlatness),
                SubBass = subBass / total,
                Bass = bass / total,
                LowMid = lowMid / total,
                Mid = mid / total,
                Presence = presence / total,
                Treble = treble / total,
                Air = air / total
            };

            return WithState(features, Classify(features), GuessGenre(features));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"DSP analysis failed: {ex.Message}");
            return Quiet();
        }
    }

    private static AudioFeatures WithState(AudioFeatures features, string state, string genreHint) => new()
    {
        Rms = features.Rms,
        Peak = features.Peak,
        CrestFactorDb = features.CrestFactorDb,
        SpectralCentroidHz = features.SpectralCentroidHz,
        SpectralRolloffHz = features.SpectralRolloffHz,
        SpectralFlatness = features.SpectralFlatness,
        DynamicRangeDb = features.DynamicRangeDb,
        Confidence = features.Confidence,
        SubBass = features.SubBass,
        Bass = features.Bass,
        LowMid = features.LowMid,
        Mid = features.Mid,
        Presence = features.Presence,
        Treble = features.Treble,
        Air = features.Air,
        State = state,
        GenreHint = genreHint
    };

    private static string Classify(AudioFeatures f)
    {
        if (f.Rms < QuietRmsThreshold) return "Quiet";
        double brightTilt = f.Presence + f.Treble + f.Air;
        double lowTilt = f.SubBass + f.Bass + f.LowMid;
        if (f.Treble > 0.22 + ClassifierMargin || f.Air > 0.12 + ClassifierMargin || (brightTilt > 0.48 && f.SpectralCentroidHz > 3600)) return "Harsh Treble";
        if (f.LowMid > 0.24 + ClassifierMargin || lowTilt > (f.Mid + f.Presence) * (1.32 + ClassifierMargin)) return "Boomy";
        if (f.Presence < f.Mid * (0.45 - ClassifierMargin) || (f.Mid + f.Presence < 0.38 && f.Confidence > 0.45)) return "Vocal Recessed";
        return "Balanced";
    }

    private static string GuessGenre(AudioFeatures f)
    {
        if (f.Rms < QuietRmsThreshold) return "Silence";
        if (f.SubBass + f.Bass > 0.38 && f.Presence + f.Treble > 0.26) return "EDM / Hip-hop energy";
        if (f.Bass + f.LowMid > 0.42 && f.Treble < 0.16) return "Warm pop / R&B";
        if (f.Mid + f.Presence > 0.55 && f.Bass < 0.18) return "Vocal / Acoustic";
        if (f.Presence + f.Treble > 0.42) return "Rock / Bright mix";
        if (f.LowMid + f.Mid > 0.58 && f.Air < 0.08) return "Jazz / Podcast warmth";
        return "Balanced pop";
    }

    private static double[] BuildWelchPowerSpectrum(float[] mono, int fftSize, int maxSamples)
    {
        int analysisLength = Math.Min(mono.Length, maxSamples);
        int analysisOffset = Math.Max(0, mono.Length - analysisLength);
        int hopSize = Math.Max(1, fftSize / 2);
        double[] averagePower = new double[fftSize / 2];
        double windowPower = HannWindowPower(fftSize);
        int segmentCount = 0;

        for (int offset = analysisOffset; offset + fftSize <= mono.Length; offset += hopSize)
        {
            Complex[] bins = BuildWindowedFftInput(mono, offset, fftSize);
            Fourier.Forward(bins, FourierOptions.Matlab);

            for (int i = 1; i < averagePower.Length; i++)
            {
                double mag = bins[i].Magnitude;
                averagePower[i] += (mag * mag) / windowPower;
            }

            segmentCount++;
        }

        if (segmentCount == 0)
        {
            return averagePower;
        }

        for (int i = 1; i < averagePower.Length; i++)
        {
            averagePower[i] /= segmentCount;
        }

        return averagePower;
    }

    private static Complex[] BuildWindowedFftInput(float[] mono, int offset, int fftSize)
    {
        Complex[] bins = new Complex[fftSize];

        for (int i = 0; i < fftSize; i++)
        {
            double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1)));
            bins[i] = new Complex(mono[offset + i] * window, 0);
        }

        return bins;
    }

    private static float[] ToMono(float[] samples, int channels)
    {
        int frames = samples.Length / channels;
        float[] mono = new float[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            double sum = 0;
            for (int ch = 0; ch < channels; ch++)
            {
                sum += samples[frame * channels + ch];
            }
            mono[frame] = (float)(sum / channels);
        }
        return mono;
    }

    private static int HighestPowerOfTwo(int value)
    {
        int power = 1;
        while (power * 2 <= value) power *= 2;
        return power;
    }

    private static double BandEnergy(double[] powerSpectrum, int sampleRate, int fftSize, double minHz, double maxHz)
    {
        int start = Math.Max(1, (int)Math.Floor(minHz * fftSize / sampleRate));
        int end = Math.Min(fftSize / 2 - 1, (int)Math.Ceiling(maxHz * fftSize / sampleRate));
        if (end <= start) return 0;

        double sum = 0;
        double weightSum = 0;
        for (int i = start; i <= end; i++)
        {
            double frequencyHz = i * sampleRate / (double)fftSize;
            double logWeight = 1.0 / Math.Max(frequencyHz, 1.0);
            double perceptualWeight = AWeightingPower(frequencyHz);
            double weight = logWeight * perceptualWeight;
            sum += powerSpectrum[i] * weight;
            weightSum += weight;
        }
        return weightSum <= double.Epsilon ? 0 : sum / weightSum;
    }

    private static double HannWindowPower(int fftSize)
    {
        double sum = 0;
        for (int i = 0; i < fftSize; i++)
        {
            double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1)));
            sum += window * window;
        }
        return Math.Max(sum, SpectrumFloor);
    }

    private static double SpectralCentroid(double[] powerSpectrum, int sampleRate, int fftSize)
    {
        double weighted = 0;
        double total = 0;
        for (int i = 1; i < powerSpectrum.Length; i++)
        {
            double frequencyHz = i * sampleRate / (double)fftSize;
            double power = Math.Max(powerSpectrum[i], 0);
            weighted += frequencyHz * power;
            total += power;
        }
        return total <= SpectrumFloor ? 0 : weighted / total;
    }

    private static double SpectralRolloff(double[] powerSpectrum, int sampleRate, int fftSize, double percentile)
    {
        double total = powerSpectrum.Skip(1).Where(double.IsFinite).Sum(p => Math.Max(p, 0));
        if (total <= SpectrumFloor) return 0;

        double target = total * Math.Clamp(percentile, 0, 1);
        double cumulative = 0;
        for (int i = 1; i < powerSpectrum.Length; i++)
        {
            cumulative += Math.Max(powerSpectrum[i], 0);
            if (cumulative >= target) return i * sampleRate / (double)fftSize;
        }
        return (powerSpectrum.Length - 1) * sampleRate / (double)fftSize;
    }

    private static double SpectralFlatness(double[] powerSpectrum)
    {
        double logSum = 0;
        double linearSum = 0;
        int count = 0;
        for (int i = 1; i < powerSpectrum.Length; i++)
        {
            double power = Math.Max(powerSpectrum[i], SpectrumFloor);
            logSum += Math.Log(power);
            linearSum += power;
            count++;
        }
        if (count == 0 || linearSum <= SpectrumFloor) return 0;
        double geometric = Math.Exp(logSum / count);
        double arithmetic = linearSum / count;
        return Math.Clamp(geometric / arithmetic, 0, 1);
    }

    private static double EstimateShortTermDynamicRangeDb(float[] mono, int sampleRate)
    {
        int window = Math.Clamp(sampleRate / 10, 256, Math.Max(256, mono.Length));
        var rmsValues = new List<double>();
        for (int offset = 0; offset + window <= mono.Length; offset += window)
        {
            double sum = 0;
            for (int i = offset; i < offset + window; i++)
            {
                sum += (double)mono[i] * mono[i];
            }
            double rms = Math.Sqrt(sum / window);
            if (rms > QuietRmsThreshold) rmsValues.Add(20.0 * Math.Log10(rms));
        }
        if (rmsValues.Count < 2) return 0;
        rmsValues.Sort();
        double low = rmsValues[(int)Math.Floor((rmsValues.Count - 1) * 0.10)];
        double high = rmsValues[(int)Math.Floor((rmsValues.Count - 1) * 0.90)];
        return Math.Clamp(high - low, 0, 60);
    }

    private static double EstimateConfidence(double rms, double peak, int fftSize, int sampleCount, double spectralFlatness)
    {
        double level = Math.Clamp((rms - QuietRmsThreshold) / 0.08, 0, 1);
        double duration = Math.Clamp(sampleCount / (double)(fftSize * 2), 0, 1);
        double clipPenalty = peak > 0.98 ? 0.45 : 0.0;
        double tonalPenalty = spectralFlatness < 0.0005 ? 0.10 : 0.0;
        return Math.Clamp((level * 0.55) + (duration * 0.35) + 0.10 - clipPenalty - tonalPenalty, 0, 1);
    }

    private static double AWeightingPower(double frequencyHz)
    {
        double f2 = frequencyHz * frequencyHz;
        const double c20 = 20.6 * 20.6;
        const double c107 = 107.7 * 107.7;
        const double c737 = 737.9 * 737.9;
        const double c12200 = 12200.0 * 12200.0;

        double ra = (c12200 * f2 * f2) /
            ((f2 + c20) * Math.Sqrt((f2 + c107) * (f2 + c737)) * (f2 + c12200));
        double db = 20.0 * Math.Log10(Math.Max(ra, double.Epsilon)) + 2.0;
        return Math.Pow(10.0, db / 10.0);
    }

    private static AudioFeatures Quiet(double rms = 0, double peak = 0) => new() { Rms = rms, Peak = peak, State = "Quiet", GenreHint = "Silence", Confidence = 1.0 };
}