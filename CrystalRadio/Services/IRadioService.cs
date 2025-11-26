using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CrystalRadio.Services;

public interface IRadioService
{
    RadioStation? CurrentStation { get; }
    PlaybackState CurrentState { get; }
    float Volume { get; set; }
    IReadOnlyList<RadioStation> Stations { get; }
    IReadOnlyList<RadioStation> FavoriteStations { get; }
    
    Task<bool> PlayStationAsync(RadioStation station);
    void Stop();
    void Pause();
    void Resume();
    void AddFavorite(RadioStation station);
    void RemoveFavorite(RadioStation station);
    
    Task LoadStationsAsync();
    
    public IEnumerable<RadioStation> SearchStations(string query);

    public Task<List<RadioStation>> SearchStationsAsync(string query, int limit = 50);
    
    event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;
    event EventHandler<StationChangedEventArgs>? StationChanged;
    event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
    event EventHandler<ErrorEventArgs>? ErrorOccurred;
}


public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
    Loading,
    Error
}

public class PlaybackStateChangedEventArgs : EventArgs
{
    public PlaybackState OldState { get; set; }
    public PlaybackState NewState { get; set; }
}

public class StationChangedEventArgs : EventArgs
{
    public RadioStation? PreviousStation { get; set; }
    public RadioStation? CurrentStation { get; set; }
}

public class VolumeChangedEventArgs : EventArgs
{
    public float OldVolume { get; set; }
    public float NewVolume { get; set; }
}

public class ErrorEventArgs : EventArgs
{
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}
