using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace CrystalRadio.Audio;

public sealed class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider sourceProvider;
    private readonly int channels;
    private readonly int sampleRate;

    private readonly EqualizerBand[] bands;
    private BiQuadFilter[,] filters = null!;

    public EqualizerSampleProvider(ISampleProvider sourceProvider, EqualizerBand[] bands)
    {
        this.sourceProvider = sourceProvider;
        this.bands = bands;
        channels = sourceProvider.WaveFormat.Channels;
        sampleRate = sourceProvider.WaveFormat.SampleRate;

        CreateFilters();
    }

    public WaveFormat WaveFormat => sourceProvider.WaveFormat;

    public EqualizerBand[] Bands => bands;

    public void Update()
    {
        CreateFilters();
    }

    private void CreateFilters()
    {
        filters = new BiQuadFilter[channels, bands.Length];

        for (var bandIndex = 0; bandIndex < bands.Length; bandIndex++)
        {
            var band = bands[bandIndex];

            for (var channel = 0; channel < channels; channel++)
            {
                filters[channel, bandIndex] = CreateFilterForBand(band);
            }
        }
    }

    private BiQuadFilter CreateFilterForBand(EqualizerBand band)
    {
        // Bass and treble feel better as shelves; middle bands as peaking EQ.
        if (Math.Abs(band.Frequency - 64f) < 1f)
        {
            return BiQuadFilter.LowShelf(sampleRate, band.Frequency, 1.0f, band.Gain);
        }

        if (Math.Abs(band.Frequency - 12000f) < 10f)
        {
            return BiQuadFilter.HighShelf(sampleRate, band.Frequency, 1.0f, band.Gain);
        }

        return BiQuadFilter.PeakingEQ(sampleRate, band.Frequency, band.Bandwidth, band.Gain);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = sourceProvider.Read(buffer, offset, count);

        for (var sample = 0; sample < samplesRead; sample++)
        {
            var absoluteIndex = offset + sample;
            var channel = sample % channels;
            var value = buffer[absoluteIndex];

            for (var band = 0; band < bands.Length; band++)
            {
                value = filters[channel, band].Transform(value);
            }

            buffer[absoluteIndex] = value;
        }

        return samplesRead;
    }
}
