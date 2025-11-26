using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using CrystalRadio.Services;
using Dalamud.Interface.Textures.TextureWraps;

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
    private Dictionary<string, IDalamudTextureWrap?> imageCache = new();

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

    public void Dispose()
    {
        foreach (var texture in imageCache.Values.OfType<IDalamudTextureWrap>())
        {
            texture?.Dispose();
        }

        imageCache.Clear();
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Crystal Radio");
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

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();
        
        DrawCurrentStationIcon();
    }

    private void DrawCurrentStationIcon()
    {
        var currentStation = radioService.CurrentStation;
        if (currentStation == null || string.IsNullOrEmpty(currentStation.IconUrl))
        {
            ImGui.Dummy(new Vector2(150, 150));
            return;
        }

        if (!imageCache.ContainsKey(currentStation.IconUrl))
        {
            LoadImageForStation(currentStation);
            ImGui.Dummy(new Vector2(150, 150));
            return;
        }

        if (imageCache.TryGetValue(currentStation.IconUrl, out var texture) && texture != null)
        {
            ImGui.Image(texture.Handle, new Vector2(150, 150));
        }
        else
        {
            ImGui.Dummy(new Vector2(150, 150));
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
        } finally
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

        var isStationHovered = ImGui.IsItemHovered();

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

        if (isStationHovered)
        {
            var tooltipText = $"{station.Genre}\n{station.Country} - {station.Language}";
            ImGui.SetTooltip(tooltipText);
        }

        ImGui.PopID();
    }

    private void LoadImageForStation(RadioStation station)
    {
        if (string.IsNullOrEmpty(station.IconUrl))
            return;

        if (!imageCache.ContainsKey(station.IconUrl))
        {
            LoadImageAsyncForStation(station);
        }
    }

    private async void LoadImageAsyncForStation(RadioStation station)
    {
        if (string.IsNullOrEmpty(station.IconUrl))
            return;

        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = System.TimeSpan.FromSeconds(5);
            var imageData = await httpClient.GetByteArrayAsync(station.IconUrl);

            if (imageData == null || imageData.Length == 0)
            {
                imageCache[station.IconUrl] = null;
                return;
            }

            var texture = await Plugin.TextureProvider.CreateFromImageAsync(
                              new System.ReadOnlyMemory<byte>(imageData),
                              $"CrystalRadio_{station.Id}");

            imageCache[station.IconUrl] = texture;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading icon for {station.Name}: {ex.Message}");
            imageCache[station.IconUrl] = null;
        }
    }
}



