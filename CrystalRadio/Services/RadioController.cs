using System;
using System.Net.Http;
using System.Threading;

namespace CrystalRadio.Services;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class RadioController : IRadioService
{
    private RadioStation? _currentStation;
    private PlaybackState _currentState = PlaybackState.Stopped;
    private float _volume = 0.5f;
    private List<RadioStation> _stations = new();
    private HashSet<string> _favorites = new();
    private Dictionary<string, RadioStation> _favoriteStations = new();
    private IAudioPlayer? _audioPlayer;
    private readonly Configuration _configuration;
    private readonly IcyMetadataService _metadataService = new();
    private CancellationTokenSource? _metadataCancellation;

    public RadioStation? CurrentStation => _currentStation;
    public PlaybackState CurrentState => _currentState;

    public float Volume
    {
        get => _volume;
        set
        {
            if (value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(nameof(value), "Volume must be between 0.0 and 1.0");

            var oldVolume = _volume;
            _volume = value;
            _audioPlayer?.SetVolume(value);
            VolumeChanged?.Invoke(this, new VolumeChangedEventArgs { OldVolume = oldVolume, NewVolume = value });
        }
    }

    public IReadOnlyList<RadioStation> Stations => _stations.AsReadOnly();

    public IReadOnlyList<RadioStation> FavoriteStations =>
        _favoriteStations.Values.ToList().AsReadOnly();

    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;
    public event EventHandler<StationChangedEventArgs>? StationChanged;
    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;
    public event EventHandler<MetadataChangedEventArgs>? MetadataChanged;

    public RadioController(IAudioPlayer audioPlayer, Configuration configuration)
    {
        _audioPlayer = audioPlayer;
        _configuration = configuration;
        
        foreach (var favoriteId in _configuration.FavoriteStationIds)
        {
            _favorites.Add(favoriteId);
        }
    }

    public async Task<bool> PlayStationAsync(RadioStation station)
    {
        try
        {
            // Stop metadata reading for previous station
            StopMetadataReading();
            
            SetPlaybackState(PlaybackState.Loading);

            var previousStation = _currentStation;
            _currentStation = station;

            if (_audioPlayer == null)
                throw new InvalidOperationException("Audio player is not initialized");

            var success = await _audioPlayer.PlayAsync(station.StreamUrl);

            if (success)
            {
                SetPlaybackState(PlaybackState.Playing);
                StationChanged?.Invoke(this, new StationChangedEventArgs
                {
                    PreviousStation = previousStation,
                    CurrentStation = station
                });
                
                // Start metadata reading for new station
                StartMetadataReading(station);
                
                return true;
            }
            else
            {
                SetPlaybackState(PlaybackState.Error);
                ErrorOccurred?.Invoke(this, new ErrorEventArgs
                {
                    Message = $"Failed to play station: {station.Name}"
                });
                return false;
            }
        }
        catch (Exception ex)
        {
            SetPlaybackState(PlaybackState.Error);
            ErrorOccurred?.Invoke(this, new ErrorEventArgs
            {
                Message = $"Error playing station: {ex.Message}",
                Exception = ex
            });
            return false;
        }
    }

    public void Stop()
    {
        _audioPlayer?.Stop();
        SetPlaybackState(PlaybackState.Stopped);
        var previousStation = _currentStation;
        _currentStation = null;
        
        // Stop metadata reading when stream stops
        StopMetadataReading();
        
        StationChanged?.Invoke(this, new StationChangedEventArgs
        {
            PreviousStation = previousStation,
            CurrentStation = null
        });
    }

    public void Pause()
    {
        if (_currentState == PlaybackState.Playing)
        {
            _audioPlayer?.Pause();
            SetPlaybackState(PlaybackState.Paused);
        }
    }

    public void Resume()
    {
        if (_currentState == PlaybackState.Paused)
        {
            _audioPlayer?.Resume();
            SetPlaybackState(PlaybackState.Playing);
        }
    }

    public void AddFavorite(RadioStation station)
    {
        if (_favorites.Add(station.Id))
        {
            station.IsFavorite = true;
            _favoriteStations[station.Id] = station;
            _configuration.FavoriteStationIds.Add(station.Id);
            _configuration.Save();
        }
    }

    public void RemoveFavorite(RadioStation station)
    {
        if (_favorites.Remove(station.Id))
        {
            station.IsFavorite = false;
            _favoriteStations.Remove(station.Id);
            _configuration.FavoriteStationIds.Remove(station.Id);
            _configuration.Save();
        }
    }

    public async Task LoadStationsAsync()
    {
        try
        {
            var stations = await FetchFromRadioBrowserAsync();
            _stations = stations.ToList();
            
            foreach (var station in _stations)
            {
                if (_favorites.Contains(station.Id))
                {
                    station.IsFavorite = true;
                    _favoriteStations[station.Id] = station;
                }
            }
            
            var missingFavorites = _favorites.Where(id => !_favoriteStations.ContainsKey(id)).ToList();
            foreach (var favoriteId in missingFavorites)
            {
                try
                {
                    var station = await FetchStationByUuidAsync(favoriteId);
                    if (station != null)
                    {
                        station.IsFavorite = true;
                        _favoriteStations[station.Id] = station;
                    }
                    else
                    {
                        _favorites.Remove(favoriteId);
                        _configuration.FavoriteStationIds.Remove(favoriteId);
                    }
                }
                catch
                {
                    _favorites.Remove(favoriteId);
                    _configuration.FavoriteStationIds.Remove(favoriteId);
                }
            }
            
            if (missingFavorites.Count > 0)
            {
                _configuration.Save();
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs
            {
                Message = "Failed to load stations",
                Exception = ex
            });
        }
    }

    public IEnumerable<RadioStation> SearchStations(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _stations;
            
        var lowerQuery = query.ToLower();
        return _stations.Where(s =>
            s.Name.ToLower().Contains(lowerQuery) ||
            s.Genre.ToLower().Contains(lowerQuery) ||
            s.Country.ToLower().Contains(lowerQuery)
        );
    }

    public async Task<List<RadioStation>> SearchStationsAsync(string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _stations.Take(limit).ToList();

        try
        {
            var results = await FetchFromRadioBrowserAsync(query, limit);
            
            foreach (var station in results)
            {
                if (_favorites.Contains(station.Id))
                {
                    station.IsFavorite = true;
                }
            }
            
            return results;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Error searching stations: {ex.Message}");
            return new List<RadioStation>();
        }
    }

    private void SetPlaybackState(PlaybackState newState)
    {
        if (_currentState != newState)
        {
            var oldState = _currentState;
            _currentState = newState;
            PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs
            {
                OldState = oldState,
                NewState = newState
            });
        }
    }

    private async Task<List<RadioStation>> FetchFromRadioBrowserAsync()
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "CrystalRadio/1.0");
        
        var stations = new List<RadioStation>();
        
        try
        {
            var response = await httpClient.GetAsync("https://de1.api.radio-browser.info/json/stations/topclick?limit=100");
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var radioBrowserStations = System.Text.Json.JsonSerializer.Deserialize<List<RadioBrowserStation>>(json);
            
            if (radioBrowserStations != null)
            {
                foreach (var rbStation in radioBrowserStations)
                {
                    if (!string.IsNullOrEmpty(rbStation.url_resolved) || !string.IsNullOrEmpty(rbStation.url))
                    {
                        stations.Add(new RadioStation
                        {
                            Id = rbStation.stationuuid ?? Guid.NewGuid().ToString(),
                            Name = rbStation.name ?? "Unknown",
                            StreamUrl = rbStation.url_resolved ?? rbStation.url ?? string.Empty,
                            Genre = rbStation.tags ?? string.Empty,
                            Description = $"{rbStation.country ?? "Unknown"} - {rbStation.language ?? "Unknown"}",
                            Country = rbStation.country ?? string.Empty,
                            Language = rbStation.language ?? string.Empty,
                            IconUrl = rbStation.favicon,
                            WebsiteUrl = rbStation.homepage
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Error fetching from Radio Browser API: {ex.Message}");
            throw;
        }
        
        return stations;
    }

    private async Task<List<RadioStation>> FetchFromRadioBrowserAsync(string query, int limit)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "CrystalRadio/1.0");
        
        var stations = new List<RadioStation>();
        
        try
        {
            var encodedQuery = System.Net.WebUtility.UrlEncode(query);
            var url = $"https://de1.api.radio-browser.info/json/stations/search?name={encodedQuery}&limit={limit}";
            
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var radioBrowserStations = System.Text.Json.JsonSerializer.Deserialize<List<RadioBrowserStation>>(json);
            
            if (radioBrowserStations != null)
            {
                foreach (var rbStation in radioBrowserStations)
                {
                    if (!string.IsNullOrEmpty(rbStation.url_resolved) || !string.IsNullOrEmpty(rbStation.url))
                    {
                        stations.Add(new RadioStation
                        {
                            Id = rbStation.stationuuid ?? Guid.NewGuid().ToString(),
                            Name = rbStation.name ?? "Unknown",
                            StreamUrl = rbStation.url_resolved ?? rbStation.url ?? string.Empty,
                            Genre = rbStation.tags ?? string.Empty,
                            Description = $"{rbStation.country ?? "Unknown"} - {rbStation.language ?? "Unknown"}",
                            Country = rbStation.country ?? string.Empty,
                            Language = rbStation.language ?? string.Empty,
                            IconUrl = rbStation.favicon,
                            WebsiteUrl = rbStation.homepage
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Error searching Radio Browser API: {ex.Message}");
            throw;
        }
        
        return stations;
    }

    private async Task<RadioStation?> FetchStationByUuidAsync(string uuid)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "CrystalRadio/1.0");
        
        try
        {
            var url = $"https://de1.api.radio-browser.info/json/stations/byuuid/{uuid}";
            
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var radioBrowserStations = System.Text.Json.JsonSerializer.Deserialize<List<RadioBrowserStation>>(json);
            
            if (radioBrowserStations != null && radioBrowserStations.Count > 0)
            {
                var rbStation = radioBrowserStations[0];
                if (!string.IsNullOrEmpty(rbStation.url_resolved) || !string.IsNullOrEmpty(rbStation.url))
                {
                    return new RadioStation
                    {
                        Id = rbStation.stationuuid ?? uuid,
                        Name = rbStation.name ?? "Unknown",
                        StreamUrl = rbStation.url_resolved ?? rbStation.url ?? string.Empty,
                        Genre = rbStation.tags ?? string.Empty,
                        Description = $"{rbStation.country ?? "Unknown"} - {rbStation.language ?? "Unknown"}",
                        Country = rbStation.country ?? string.Empty,
                        Language = rbStation.language ?? string.Empty,
                        IconUrl = rbStation.favicon,
                        WebsiteUrl = rbStation.homepage
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Error fetching station by UUID: {ex.Message}");
        }
        
        return null;
    }

    private void StartMetadataReading(RadioStation station)
    {
        // Cancel any existing metadata reading
        StopMetadataReading();
        
        // Create new cancellation token
        _metadataCancellation = new CancellationTokenSource();
        
        // Start metadata reading in background
        _ = ReadMetadataAsync(station, _metadataCancellation.Token);
    }

    private void StopMetadataReading()
    {
        if (_metadataCancellation != null)
        {
            _metadataCancellation.Cancel();
            _metadataCancellation.Dispose();
            _metadataCancellation = null;
        }
    }

    private async Task ReadMetadataAsync(RadioStation station, CancellationToken cancellationToken)
    {
        Plugin.Log.Info($"[ICY Metadata] Starting metadata reading for: {station.Name}");
        string? lastTrack = null;
        bool firstRun = true;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Plugin.Log.Debug($"[ICY Metadata] Polling metadata from: {station.StreamUrl}");
                
                // Fetch current metadata
                var streamInfo = await _metadataService.GetIcyMetadataAsync(
                    station.StreamUrl,
                    timeout: 5000,
                    cancellationToken: cancellationToken);

                Plugin.Log.Debug($"[ICY Metadata] Response - IcyMetaInt: {streamInfo.IcyMetaInt}, StreamTitle: {streamInfo.StreamTitle ?? "(null)"}");

                // Update on first run or when metadata changes
                if (streamInfo.StreamTitle != null && (firstRun || streamInfo.StreamTitle != lastTrack))
                {
                    lastTrack = streamInfo.StreamTitle;
                    station.CurrentTrack = streamInfo.StreamTitle;
                    station.LastMetadataUpdate = DateTime.UtcNow;
                    Plugin.Log.Info($"[ICY Metadata] Track updated: {streamInfo.StreamTitle}");
                    
                    // Fire event to notify UI
                    MetadataChanged?.Invoke(this, new MetadataChangedEventArgs
                    {
                        Station = station,
                        CurrentTrack = streamInfo.StreamTitle,
                        UpdateTime = DateTime.UtcNow
                    });
                    
                    firstRun = false;
                }
                else
                {
                    Plugin.Log.Debug($"[ICY Metadata] No new metadata (current: {streamInfo.StreamTitle ?? "(null)"}, last: {lastTrack ?? "(none)"})");
                    firstRun = false;
                }
                
                // Wait before next poll (10 seconds)
                await Task.Delay(10000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping - exit loop
                Plugin.Log.Debug($"[ICY Metadata] Metadata reading cancelled for: {station.Name}");
                break;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[ICY Metadata] Error reading metadata: {ex.Message}");
                
                // Wait before retrying on error
                try
                {
                    await Task.Delay(10000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}

public class RadioBrowserStation
{
    public string? stationuuid { get; set; }
    public string? name { get; set; }
    public string? url { get; set; }
    public string? url_resolved { get; set; }
    public string? homepage { get; set; }
    public string? favicon { get; set; }
    public string? tags { get; set; }
    public string? country { get; set; }
    public string? language { get; set; }
}

public interface IAudioPlayer
{
    Task<bool> PlayAsync(string streamUrl);
    void Stop();
    void Pause();
    void Resume();
    void SetVolume(float volume);
}
