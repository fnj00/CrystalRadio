namespace CrystalRadio.Audio;

using NAudio.Wave;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class HttpStreamingPlayer : IWaveProvider, IDisposable
{
    private HttpClient _httpClient;
    private Stream? _stream;
    private MediaFoundationReader? _decompressedProvider;
    private WaveFormat _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    private bool _disposed = false;
    private CancellationTokenSource? _cancellationTokenSource;

    public WaveFormat WaveFormat => _waveFormat;

    public HttpStreamingPlayer(string url)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        InitializeStream(url).Wait();
    }

    private async Task InitializeStream(string url)
    {
        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _stream = await _httpClient.GetStreamAsync(url);
            
            _decompressedProvider = new MediaFoundationReader(url);
            _waveFormat = _decompressedProvider.WaveFormat;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing stream: {ex.Message}");
            throw;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        if (_decompressedProvider == null)
            return 0;

        return _decompressedProvider.Read(buffer, offset, count);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _stream?.Dispose();
        _decompressedProvider?.Dispose();
        _httpClient?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~HttpStreamingPlayer()
    {
        Dispose();
    }
}

