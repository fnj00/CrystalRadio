using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using CrystalRadio.Services;

namespace CrystalRadio.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly IRadioService radioService;

    private string newStationName = string.Empty;
    private string newStationUrl = string.Empty;
    private string newStationGenre = string.Empty;
    private string newStationDescription = string.Empty;
    private string newStationWebsite = string.Empty;

    public ConfigWindow(Plugin plugin, Configuration configuration, IRadioService radioService)
        : base("CrystalRadio Settings###CrystalRadioConfig")
    {
        this.configuration = configuration;
        this.radioService = radioService;

        Size = new Vector2(700, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        if (configuration.IsConfigWindowMovable)
            Flags &= ~ImGuiWindowFlags.NoMove;
        else
            Flags |= ImGuiWindowFlags.NoMove;
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("CrystalRadioConfigTabs"))
            return;

        if (ImGui.BeginTabItem("General"))
        {
            DrawGeneralTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Custom Stations"))
        {
            DrawCustomStationsTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawGeneralTab()
    {
        var movable = configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable config window", ref movable))
        {
            configuration.IsConfigWindowMovable = movable;
            configuration.Save();
        }

        var searchLimit = configuration.DefaultSearchLimit;
        if (ImGui.SliderInt("Default search limit", ref searchLimit, 10, 200))
        {
            configuration.DefaultSearchLimit = searchLimit;
            configuration.Save();
        }

        ImGui.Separator();
        ImGui.TextWrapped("Use /cradio config to open this window directly.");
    }

    private void DrawCustomStationsTab()
    {
        ImGui.TextUnformatted("Add custom station");
        ImGui.InputText("Station name", ref newStationName, 128);
        ImGui.InputText("Stream URL", ref newStationUrl, 512);
        ImGui.InputText("Genre", ref newStationGenre, 128);
        ImGui.InputText("Description", ref newStationDescription, 256);
        ImGui.InputText("Website URL", ref newStationWebsite, 256);

        var canAdd =
            !string.IsNullOrWhiteSpace(newStationName) &&
            Uri.TryCreate(newStationUrl, UriKind.Absolute, out _);

        if (!canAdd)
            ImGui.BeginDisabled();

        if (ImGui.Button("Add custom station"))
        {
            configuration.CustomStations.Add(new CustomStationConfig
            {
                Name = newStationName.Trim(),
                StreamUrl = newStationUrl.Trim(),
                Genre = newStationGenre.Trim(),
                Description = newStationDescription.Trim(),
                WebsiteUrl = newStationWebsite.Trim()
            });

            configuration.Save();
            radioService.ReloadCustomStations();
            _ = radioService.LoadStationsAsync();

            newStationName = string.Empty;
            newStationUrl = string.Empty;
            newStationGenre = string.Empty;
            newStationDescription = string.Empty;
            newStationWebsite = string.Empty;
        }

        if (!canAdd)
            ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.TextUnformatted("Saved custom stations");

        for (var i = 0; i < configuration.CustomStations.Count; i++)
        {
            var station = configuration.CustomStations[i];
            ImGui.PushID(station.Id);

            ImGui.TextUnformatted(station.Name);
            ImGui.TextWrapped(station.StreamUrl);

            if (!string.IsNullOrWhiteSpace(station.Genre))
                ImGui.TextDisabled(station.Genre);

            if (ImGui.Button("Remove"))
            {
                var customId = $"custom:{station.Id}";
                configuration.CustomStations.RemoveAt(i);
                configuration.FavoriteStationIds.RemoveAll(x => x == customId);
                configuration.Save();
                radioService.ReloadCustomStations();
                _ = radioService.LoadStationsAsync();
                ImGui.PopID();
                break;
            }

            ImGui.Separator();
            ImGui.PopID();
        }
    }
}
