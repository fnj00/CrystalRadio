namespace CrystalRadio.Audio;

public sealed class EqualizerBand
{
    public float Frequency { get; set; }
    public float Bandwidth { get; set; } = 0.8f;
    public float Gain { get; set; }
}
