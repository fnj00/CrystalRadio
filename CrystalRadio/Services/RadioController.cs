using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CrystalRadio.Audio;

namespace CrystalRadio.Services;

public class RadioController : IRadioService, IDisposable
{
    private readonly IAudioPlayer _audioPlayer;
    private readonly Configuration _configuration;
    private readonly IcyMetadataService _metadataService = new();
    private readonly List<RadioStation> _stations = new();
    private readonly List<RadioStation> _customStations = new();
    private readonly HashSet<string> _favoriteIds = new();
    private readonly Dictionary<string, RadioStation> _favoriteStations = new();

    private RadioStation? _currentStation;
    private PlaybackState _currentState = PlaybackState.Stopped;
    private float _volume = 0.5f;
    private CancellationTokenSource? _metadataCts;

    public RadioStation? CurrentStation => _currentStation;
    public PlaybackState CurrentState => _currentState;

    public float Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(_volume - value) < 0.0001f)
                return;

            var oldValue = _volume;
            _volume = Math.Clamp(value, 0.0f, 1.0f);
            _audioPlayer.SetVolume(_volume);

            VolumeChanged?.Invoke(this, new VolumeChangedEventArgs
            {
                OldVolume = oldValue,
                NewVolume = _volume
            });
        }
    }

    public IReadOnlyList<RadioStation> Stations => _stations.AsReadOnly();
    public IReadOnlyList<RadioStation> FavoriteStations => _favoriteStations.Values.OrderBy(s => s.Name).ToList().AsReadOnly();
    public IReadOnlyList<RadioStation> CustomStations => _customStations.AsReadOnly();

    public event EventHandler? PlaybackStateChanged;
    public event EventHandler? StationChanged;
    public event EventHandler? VolumeChanged;
    public event EventHandler? ErrorOccurred;
    public event EventHandler? MetadataChanged;

    public RadioController(IAudioPlayer audioPlayer, Configuration configuration)
    {
        _audioPlayer = audioPlayer;
        _configuration = configuration;

        foreach (var id in configuration.FavoriteStationIds)
            _favoriteIds.Add(id);

        ReloadCustomStations();
    }

    public void Dispose()
    {
        StopMetadataReading();
    }

    public void ReloadCustomStations()
    {
        _customStations.Clear();

        foreach (var custom in _configuration.CustomStations)
        {
            if (string.IsNullOrWhiteSpace(custom.Name) || string.IsNullOrWhiteSpace(custom.StreamUrl))
                continue;

            var station = new RadioStation
            {
                Id = $"custom:{custom.Id}",
                Name = custom.Name,
                StreamUrl = custom.StreamUrl,
                Genre = custom.Genre,
                Description = custom.Description,
                WebsiteUrl = string.IsNullOrWhiteSpace(custom.WebsiteUrl) ? null : custom.WebsiteUrl,
                IsFavorite = _favoriteIds.Contains($"custom:{custom.Id}"),
                IsCustom = true,
                Source = "Custom"
            };

            _customStations.Add(station);

            if (station.IsFavorite)
                _favoriteStations[station.Id] = station;
        }
    }

    public async Task LoadStationsAsync()
    {
        try
        {
            ReloadCustomStations();

            var stationTasks = new[]
            {
                LoadStationsFromApi("https://de1.api.radio-browser.info/json/stations/topvote/20"),
                LoadStationsFromApi("https://de1.api.radio-browser.info/json/stations/bytag/jazz?limit=10"),
                LoadStationsFromApi("https://de1.api.radio-browser.info/json/stations/bytag/rock?limit=10"),
                LoadStationsFromApi("https://de1.api.radio-browser.info/json/stations/bytag/classical?limit=10"),
                LoadStationsFromApi("https://de1.api.radio-browser.info/json/stations/bytag/electronic?limit=10")
            };

            var stationLists = await Task.WhenAll(stationTasks);
            var apiStations = stationLists
                .SelectMany(list => list)
                .GroupBy(s => s.Id)
                .Select(g => g.First())
                .Take(100)
                .ToList();

            _stations.Clear();

            foreach (var station in _customStations)
                _stations.Add(station);

            foreach (var station in apiStations)
            {
                station.IsFavorite = _favoriteIds.Contains(station.Id);
                station.Source = "Radio Browser";
                _stations.Add(station);

                if (station.IsFavorite)
                    _favoriteStations[station.Id] = station;
            }

            RefreshFavoriteReferencesFromStations();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs
            {
                Message = $"Failed to load stations: {ex.Message}",
                Exception = ex
            });
            throw;
        }
    }

    private async Task<List<RadioStation>> LoadStationsFromApi(string apiUrl)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetStringAsync(apiUrl);
        return ParseStationsJson(response);
    }

    private List<RadioStation> ParseStationsJson(string json)
    {
        var stations = new List<RadioStation>();

        var stationPattern =
            @"\{[^{}]*""stationuuid"":""([^""]*)""[^{}]*""name"":""([^""]*)""[^{}]*""url_resolved"":""([^""]*)""[^{}]*""tags"":""([^""]*)""[^{}]*""country"":""([^""]*)""[^{}]*""language"":""([^""]*)""[^{}]*(?:""favicon"":""([^""]*)"")?[^{}]*\}";

        var matches = System.Text.RegularExpressions.Regex.Matches(json, stationPattern);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Groups.Count < 7)
                continue;

            var station = new RadioStation
            {
                Id = match.Groups[1].Value,
                Name = System.Web.HttpUtility.HtmlDecode(match.Groups[2].Value),
                StreamUrl = match.Groups[3].Value.Replace(@"\/", "/"),
                Genre = System.Web.HttpUtility.HtmlDecode(match.Groups[4].Value),
                Country = System.Web.HttpUtility.HtmlDecode(match.Groups[5].Value),
                Language = System.Web.HttpUtility.HtmlDecode(match.Groups[6].Value),
                IconUrl = match.Groups.Count > 7 ? match.Groups[7].Value.Replace(@"\/", "/") : null,
                Source = "Radio Browser"
            };

            if (!string.IsNullOrWhiteSpace(station.Id) &&
                !string.IsNullOrWhiteSpace(station.Name) &&
                !string.IsNullOrWhiteSpace(station.StreamUrl))
            {
                stations.Add(station);
            }
        }

        return stations;
    }

    public IEnumerable<RadioStation> SearchStations(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _stations;

        var lowerQuery = query.ToLowerInvariant();

        return _stations.Where(s =>
            s.Name.ToLowerInvariant().Contains(lowerQuery) ||
            s.Genre.ToLowerInvariant().Contains(lowerQuery) ||
            s.Country.ToLowerInvariant().Contains(lowerQuery) ||
            s.Language.ToLowerInvariant().Contains(lowerQuery) ||
            s.Description.ToLowerInvariant().Contains(lowerQuery));
    }

    public async Task<List<RadioStation>> SearchStationsAsync(string query, int limit = 50)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return _stations.Take(limit).ToList();

            var localCustomResults = _customStations.Where(s =>
                    s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.Genre.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            using var httpClient = new HttpClient();
            var encodedQuery = Uri.EscapeDataString(query);
            var apiUrl = $"https://de1.api.radio-browser.info/json/stations/search?name={encodedQuery}&limit={limit}";
            var response = await httpClient.GetStringAsync(apiUrl);
            var stations = ParseStationsJson(response);

            foreach (var station in stations)
            {
                station.IsFavorite = _favoriteIds.Contains(station.Id);

                if (station.IsFavorite)
                    _favoriteStations[station.Id] = station;
            }

            return localCustomResults
                .Concat(stations)
                .GroupBy(s => s.Id)
                .Select(g => g.First())
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs
            {
                Message = $"Search failed: {ex.Message}",
                Exception = ex
            });

            return SearchStations(query).Take(limit).ToList();
        }
    }

    public async Task PlayStationAsync(RadioStation station)
    {
        if (_currentStation?.Id == station.Id && _currentState == PlaybackState.Playing)
            return;

        try
        {
            var previousStation = _currentStation;
            SetPlaybackState(PlaybackState.Loading);
            StopMetadataReading();

            await _audioPlayer.PlayAsync(station.StreamUrl);

            _currentStation = station;
            SetPlaybackState(PlaybackState.Playing);

            StationChanged?.Invoke(this, new StationChangedEventArgs
            {
                PreviousStation = previousStation,
                CurrentStation = _currentStation
            });

            StartMetadataReading();
        }
        catch (Exception ex)
        {
            SetPlaybackState(PlaybackState.Error);
            ErrorOccurred?.Invoke(this, new ErrorEventArgs
            {
                Message = $"Failed to play station {station.Name}: {ex.Message}",
                Exception = ex
            });
        }
    }

    public void Stop()
    {
        if (_currentState == PlaybackState.Stopped)
            return;

        var previousStation = _currentStation;
        _audioPlayer.Stop();
        StopMetadataReading();
        _currentStation = null;
        SetPlaybackState(PlaybackState.Stopped);

        StationChanged?.Invoke(this, new StationChangedEventArgs
        {
            PreviousStation = previousStation,
            CurrentStation = null
        });
    }

    public void Pause()
    {
        if (_currentState != PlaybackState.Playing)
            return;

        _audioPlayer.Pause();
        StopMetadataReading();
        SetPlaybackState(PlaybackState.Paused);
    }

    public void Resume()
    {
        if (_currentState != PlaybackState.Paused)
            return;

        _audioPlayer.Resume();
        SetPlaybackState(PlaybackState.Playing);
        StartMetadataReading();
    }

    public void AddFavorite(RadioStation station)
    {
        if (_favoriteIds.Add(station.Id))
        {
            station.IsFavorite = true;
            _favoriteStations[station.Id] = station;
            _configuration.FavoriteStationIds = _favoriteIds.ToList();
            _configuration.Save();
        }
    }

    public void RemoveFavorite(RadioStation station)
    {
        if (_favoriteIds.Remove(station.Id))
        {
            station.IsFavorite = false;
            _favoriteStations.Remove(station.Id);
            _configuration.FavoriteStationIds = _favoriteIds.ToList();
            _configuration.Save();
        }
    }

    private void RefreshFavoriteReferencesFromStations()
    {
        foreach (var station in _stations)
        {
            if (_favoriteIds.Contains(station.Id))
            {
                station.IsFavorite = true;
                _favoriteStations[station.Id] = station;
            }
        }
    }

    private void SetPlaybackState(PlaybackState newState)
    {
        if (_currentState == newState)
            return;

        var oldState = _currentState;
        _currentState = newState;

        PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs
        {
            OldState = oldState,
            NewState = newState
        });
    }

    private void StartMetadataReading()
    {
        if (_currentStation == null)
            return;

        StopMetadataReading();
        _metadataCts = new CancellationTokenSource();
        _ = ReadMetadataLoopAsync(_metadataCts.Token);
    }

    private void StopMetadataReading()
    {
        _metadataCts?.Cancel();
        _metadataCts?.Dispose();
        _metadataCts = null;
    }

    private async Task ReadMetadataLoopAsync(CancellationToken cancellationToken)
    {
        if (_currentStation == null)
            return;

        var stationId = _currentStation.Id;

        while (!cancellationToken.IsCancellationRequested &&
               _currentStation != null &&
               _currentStation.Id == stationId &&
               (_currentState == PlaybackState.Playing || _currentState == PlaybackState.Loading))
        {
            try
            {
                Plugin.Log.Debug($"[RadioController] Attempting to read metadata for {_currentStation.Name}");

                var streamInfo = await _metadataService.GetIcyMetadataAsync(
                    _currentStation.StreamUrl,
                    10000,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(streamInfo.StreamTitle) &&
                    _currentStation != null &&
                    _currentStation.Id == stationId)
                {
                    if (_currentStation.CurrentTrack != streamInfo.StreamTitle)
                    {
                        _currentStation.CurrentTrack = streamInfo.StreamTitle;
                        _currentStation.LastMetadataUpdate = DateTime.UtcNow;

                        Plugin.Log.Debug($"[RadioController] Updated track: {_currentStation.CurrentTrack}");

                        MetadataChanged?.Invoke(this, new MetadataChangedEventArgs
                        {
                            Station = _currentStation,
                            CurrentTrack = _currentStation.CurrentTrack,
                            UpdateTime = _currentStation.LastMetadataUpdate.Value
                        });
                    }
                }
                else
                {
                    Plugin.Log.Debug($"[RadioController] No stream title found for {_currentStation?.Name}");
                }

                await Task.Delay(30000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (TimeoutException ex)
            {
                Plugin.Log.Debug($"[RadioController] Metadata timeout for {_currentStation?.Name}: {ex.Message}");

                try
                {
                    await Task.Delay(10000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[RadioController] Metadata read error for {_currentStation?.Name}: {ex.Message}");

                try
                {
                    await Task.Delay(15000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
