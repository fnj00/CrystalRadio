using System;

namespace CrystalRadio.Audio;

using NAudio.Wave;
using CrystalRadio.Services;
using System.Threading.Tasks;

public class AudioPlayer : IAudioPlayer, IDisposable
{
    private IWavePlayer? _wavePlayer;
    private HttpStreamingPlayer? _streamingPlayer;
    private float _volume = 0.5f;
    private bool _disposed = false;

    public AudioPlayer()
    {
        InitializePlayer();
    }

    private void InitializePlayer()
    {
        _wavePlayer = new WaveOutEvent();
        _wavePlayer.Volume = _volume;
    }

    public async Task<bool> PlayAsync(string streamUrl)
    {
        try
        {
            StopInternal();

            _streamingPlayer = new HttpStreamingPlayer(streamUrl);
            _wavePlayer!.Init(_streamingPlayer);
            _wavePlayer.Play();

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error playing stream: {ex.Message}");
            return false;
        }
    }

    public void Stop()
    {
        StopInternal();
    }

    public void Pause()
    {
        if (_wavePlayer != null && _wavePlayer.PlaybackState == NAudio.Wave.PlaybackState.Playing)
        {
            _wavePlayer.Pause();
        }
    }

    public void Resume()
    {
        if (_wavePlayer != null && _wavePlayer.PlaybackState == NAudio.Wave.PlaybackState.Paused)
        {
            _wavePlayer.Play();
        }
    }

    public void SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);
        if (_wavePlayer != null)
        {
            _wavePlayer.Volume = _volume;
        }
    }

    private void StopInternal()
    {
        if (_wavePlayer != null)
        {
            _wavePlayer.Stop();
        }

        _streamingPlayer?.Dispose();
        _streamingPlayer = null;
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
