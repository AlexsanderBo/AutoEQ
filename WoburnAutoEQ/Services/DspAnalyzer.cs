using MathNet.Numerics.IntegralTransforms;
using System.Numerics;
using WoburnAutoEQ.Models;

namespace WoburnAutoEQ.Services;

public sealed class DspAnalyzer
{
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
            if (rms < 0.003)
            {
                return Quiet(rms);
            }

            int fftSize = HighestPowerOfTwo(Math.Min(mono.Length, sampleRate * 4));
            if (fftSize < 1024)
            {
                return Quiet(rms);
            }

            Complex[] bins = new Complex[fftSize];
            int offset = Math.Max(0, mono.Length - fftSize);
            for (int i = 0; i < fftSize; i++)
            {
                double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1)));
                bins[i] = new Complex(mono[offset + i] * window, 0);
            }

            Fourier.Forward(bins, FourierOptions.Matlab);

            double subBass = BandEnergy(bins, sampleRate, fftSize, 20, 60);
            double bass = BandEnergy(bins, sampleRate, fftSize, 60, 120);
            double lowMid = BandEnergy(bins, sampleRate, fftSize, 120, 350);
            double mid = BandEnergy(bins, sampleRate, fftSize, 350, 2000);
            double presence = BandEnergy(bins, sampleRate, fftSize, 2000, 5000);
            double treble = BandEnergy(bins, sampleRate, fftSize, 5000, 10000);
            double air = BandEnergy(bins, sampleRate, fftSize, 10000, 16000);
            double total = new[] { subBass, bass, lowMid, mid, presence, treble, air }.Sum();

            if (total <= double.Epsilon)
            {
                return Quiet(rms);
            }

            var features = new AudioFeatures
            {
                Rms = rms,
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
        catch
        {
            return Quiet();
        }
    }

    private static AudioFeatures WithState(AudioFeatures features, string state, string genreHint) => new()
    {
        Rms = features.Rms,
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
        if (f.Rms < 0.003) return "Quiet";
        if (f.Treble > 0.22 || f.Air > 0.12) return "Harsh Treble";
        if (f.LowMid > 0.24 || f.Bass + f.LowMid > (f.Mid + f.Presence) * 1.35) return "Boomy";
        if (f.Presence < f.Mid * 0.45) return "Vocal Recessed";
        return "Balanced";
    }

    private static string GuessGenre(AudioFeatures f)
    {
        if (f.Rms < 0.003) return "Silence";
        if (f.SubBass + f.Bass > 0.38 && f.Presence + f.Treble > 0.26) return "EDM / Hip-hop energy";
        if (f.Bass + f.LowMid > 0.42 && f.Treble < 0.16) return "Warm pop / R&B";
        if (f.Mid + f.Presence > 0.55 && f.Bass < 0.18) return "Vocal / Acoustic";
        if (f.Presence + f.Treble > 0.42) return "Rock / Bright mix";
        if (f.LowMid + f.Mid > 0.58 && f.Air < 0.08) return "Jazz / Podcast warmth";
        return "Balanced pop";
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

    private static double BandEnergy(Complex[] bins, int sampleRate, int fftSize, double minHz, double maxHz)
    {
        int start = Math.Max(1, (int)Math.Floor(minHz * fftSize / sampleRate));
        int end = Math.Min(fftSize / 2 - 1, (int)Math.Ceiling(maxHz * fftSize / sampleRate));
        if (end <= start) return 0;

        double sum = 0;
        for (int i = start; i <= end; i++)
        {
            double mag = bins[i].Magnitude;
            sum += mag * mag;
        }
        return sum / (end - start + 1);
    }

    private static AudioFeatures Quiet(double rms = 0) => new() { Rms = rms, State = "Quiet", GenreHint = "Silence" };
}