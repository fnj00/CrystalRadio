using System;
using System.Threading.Tasks;
using NAudio.Wave;

namespace CrystalRadio.Audio;

public class AudioPlayer : IAudioPlayer, IDisposable
{
    private IWavePlayer? _wavePlayer;
    private HttpStreamingPlayer? _streamingPlayer;
    private EqualizerSampleProvider? _equalizer;
    private float _volume = 0.5f;
    private bool _disposed;
    private GainSampleProvider? _gainProvider;

    private readonly EqualizerBand[] _eqBands =
    {
        new() { Frequency = 64f, Bandwidth = 0.8f, Gain = 0f },     // Bass
        new() { Frequency = 250f, Bandwidth = 0.8f, Gain = 0f },    // Low Mid
        new() { Frequency = 1000f, Bandwidth = 0.8f, Gain = 0f },   // Mid
        new() { Frequency = 4000f, Bandwidth = 0.8f, Gain = 0f },   // High Mid
        new() { Frequency = 12000f, Bandwidth = 0.8f, Gain = 0f },  // Treble
    };

    public AudioPlayer()
    {
        InitializePlayer();
    }

    private void InitializePlayer()
    {
        _wavePlayer = new WaveOutEvent();
    }

    public async Task<bool> PlayAsync(string streamUrl)
    {
        try
        {
            StopInternal();

            _streamingPlayer = new HttpStreamingPlayer(streamUrl);

            var sampleProvider = _streamingPlayer.ToSampleProvider();
            _gainProvider = new GainSampleProvider(sampleProvider)
            {
                Gain = _volume
            };

            _equalizer = new EqualizerSampleProvider(_gainProvider, _eqBands);

            _wavePlayer!.Init(_equalizer);

            // Leave output/player volume at unity so we do not touch session/device level volume.
            _wavePlayer.Volume = 1.0f;
            _wavePlayer.Play();

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error playing stream: {ex.Message}");
            return false;
        }
    }

    public void Pause()
    {
        if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Playing)
            _wavePlayer.Pause();
    }

    public void Stop()
    {
        StopInternal();
    }

    public void Resume()
    {
        if (_wavePlayer != null && _wavePlayer.PlaybackState == PlaybackState.Paused)
            _wavePlayer.Play();
    }

    public void SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);

        if (_gainProvider != null)
            _gainProvider.Gain = _volume;

        if (_wavePlayer != null)
            _wavePlayer.Volume = 1.0f;
    }

    public float[] GetEqGains()
    {
        var gains = new float[_eqBands.Length];
        for (var i = 0; i < _eqBands.Length; i++)
            gains[i] = _eqBands[i].Gain;

        return gains;
    }

    public void SetEqGain(int bandIndex, float gainDb)
    {
        if (bandIndex < 0 || bandIndex >= _eqBands.Length)
            return;

        _eqBands[bandIndex].Gain = Math.Clamp(gainDb, -12f, 12f);
        _equalizer?.Update();
    }

    public void ResetEq()
    {
        for (var i = 0; i < _eqBands.Length; i++)
            _eqBands[i].Gain = 0f;

        _equalizer?.Update();
    }

    private void StopInternal()
    {
        _wavePlayer?.Stop();
        _streamingPlayer?.Dispose();
        _streamingPlayer = null;
        _gainProvider = null;
        _equalizer = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopInternal();
        _wavePlayer?.Dispose();
        _wavePlayer = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~AudioPlayer()
    {
        Dispose();
    }
}
