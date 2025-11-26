using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using CrystalRadio.Services;

namespace CrystalRadio.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly string goatImagePath;
    private readonly Plugin plugin;
    private readonly IRadioService radioService;
    private string searchQuery = string.Empty;
    private string previousSearchQuery = string.Empty;
    private bool stationsLoaded = false;
    private string loadingMessage = "Loading stations...";
    private List<RadioStation> displayedStations = new();
    private bool isSearching = false;
    private DateTime lastSearchTime = DateTime.MinValue;
    private const int SearchDebounceMs = 500;

    public MainWindow(Plugin plugin, string goatImagePath, IRadioService radioService)
        : base("Crystal Radio##MainWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.goatImagePath = goatImagePath;
        this.plugin = plugin;
        this.radioService = radioService;
        
        LoadStations();
    }

    private async void LoadStations()
    {
        try
        {
            await radioService.LoadStationsAsync();
            displayedStations = radioService.Stations.ToList();
            stationsLoaded = true;
        }
        catch (Exception ex)
        {
            loadingMessage = $"Error loading stations: {ex.Message}";
        }
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextUnformatted("🎙️ Crystal Radio");
        ImGui.Separator();

        if (!stationsLoaded)
        {
            ImGui.TextUnformatted(loadingMessage);
            return;
        }

        DrawPlaybackControls();
        ImGui.Spacing();
        DrawVolumeControl();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawStationList();
    }

    private void DrawPlaybackControls()
    {
        var currentStation = radioService.CurrentStation;
        var isPlaying = radioService.CurrentState == PlaybackState.Playing;
        var isPaused = radioService.CurrentState == PlaybackState.Paused;

        ImGui.TextUnformatted("Now Playing:");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0, 1, 0, 1), currentStation?.Name ?? "Nothing");

        ImGui.Spacing();

        if (ImGui.Button("▶ Play", new Vector2(100, 0)) && currentStation != null && !isPlaying)
        {
            if (isPaused)
            {
                radioService.Resume();
            }
            else
            {
                radioService.PlayStationAsync(currentStation);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("⏸ Pause", new Vector2(100, 0)) && isPlaying)
        {
            radioService.Pause();
        }

        ImGui.SameLine();
        if (ImGui.Button("⏹ Stop", new Vector2(100, 0)) && radioService.CurrentState != PlaybackState.Stopped)
        {
            radioService.Stop();
        }
    }

    private void DrawVolumeControl()
    {
        var volume = radioService.Volume;
        ImGui.TextUnformatted("Volume:");
        ImGui.SameLine();
        if (ImGui.SliderFloat("##Volume", ref volume, 0f, 1f, "%.2f"))
        {
            radioService.Volume = volume;
        }
    }

    private void DrawStationList()
    {
        ImGui.TextUnformatted("🔍 Search:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        
        if (ImGui.InputText("##SearchStations", ref searchQuery, 256))
        {
            if (searchQuery != previousSearchQuery)
            {
                lastSearchTime = DateTime.UtcNow;
            }
        }

        if ((DateTime.UtcNow - lastSearchTime).TotalMilliseconds >= SearchDebounceMs && 
            searchQuery != previousSearchQuery && !isSearching)
        {
            previousSearchQuery = searchQuery;
            PerformAsyncSearch(searchQuery);
        }

        ImGui.Spacing();

        var stationCount = displayedStations.Count;
        var statusText = isSearching ? $"Available Stations (Searching...)" : $"Available Stations ({stationCount})";
        ImGui.TextUnformatted(statusText);

        using (var child = ImRaii.Child("StationListChild", Vector2.Zero, true))
        {
            if (child.Success)
            {
                foreach (var station in displayedStations)
                {
                    DrawStationItem(station);
                }
            }
        }
    }

    private async void PerformAsyncSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            displayedStations = radioService.Stations.ToList();
            isSearching = false;
            return;
        }

        isSearching = true;
        try
        {
            var results = await radioService.SearchStationsAsync(query, 100);
            displayedStations = results;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Search error: {ex.Message}");
            displayedStations = new List<RadioStation>();
        }
        finally
        {
            isSearching = false;
        }
    }


    private void DrawStationItem(RadioStation station)
    {
        var isCurrentStation = radioService.CurrentStation?.Id == station.Id;
        var isFavorite = station.IsFavorite;

        ImGui.PushID(station.Id);

        if (isCurrentStation)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.2f, 1f));
        }

        var stationLabel = $"{(isFavorite ? "⭐" : "  ")} {station.Name}";
        if (ImGui.Button(stationLabel, new Vector2(-45, 0)))
        {
            radioService.PlayStationAsync(station);
        }

        if (isCurrentStation)
        {
            ImGui.PopStyleColor();
        }

        ImGui.SameLine();

        if (isFavorite)
        {
            if (ImGui.Button("★##favorite", new Vector2(40, 0)))
            {
                radioService.RemoveFavorite(station);
            }
        }
        else
        {
            if (ImGui.Button("☆##favorite", new Vector2(40, 0)))
            {
                radioService.AddFavorite(station);
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{station.Genre}\n{station.Country} - {station.Language}");
        }

        ImGui.PopID();
    }
}
