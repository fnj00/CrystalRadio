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
    IReadOnlyList<RadioStation> CustomStations { get; }

    Task PlayStationAsync(RadioStation station);
    void Stop();
    void Pause();
    void Resume();

    void AddFavorite(RadioStation station);
    void RemoveFavorite(RadioStation station);

    Task LoadStationsAsync();
    IEnumerable<RadioStation> SearchStations(string query);
    Task<List<RadioStation>> SearchStationsAsync(string query, int limit = 50);
    void ReloadCustomStations();

    event EventHandler? PlaybackStateChanged;
    event EventHandler? StationChanged;
    event EventHandler? VolumeChanged;
    event EventHandler? ErrorOccurred;
    event EventHandler? MetadataChanged;
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

public class MetadataChangedEventArgs : EventArgs
{
    public RadioStation? Station { get; set; }
    public string? CurrentTrack { get; set; }
    public DateTime UpdateTime { get; set; }
}
