using System;
using NAudio.Wave;

namespace CrystalRadio.Audio;

public sealed class GainSampleProvider : ISampleProvider
{
    private readonly ISampleProvider sourceProvider;
    private float gain = 1.0f;

    public GainSampleProvider(ISampleProvider sourceProvider)
    {
        this.sourceProvider = sourceProvider;
    }

    public WaveFormat WaveFormat => sourceProvider.WaveFormat;

    public float Gain
    {
        get => gain;
        set => gain = Math.Clamp(value, 0f, 1f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = sourceProvider.Read(buffer, offset, count);

        for (var n = 0; n < samplesRead; n++)
        {
            buffer[offset + n] *= gain;
        }

        return samplesRead;
    }
}
