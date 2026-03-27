using System;

namespace CrystalRadio.Services;

public class RadioStation
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public string Country { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public bool IsCustom { get; set; }
    public string Source { get; set; } = "Radio Browser";
    public string? CurrentTrack { get; set; }
    public DateTime? LastMetadataUpdate { get; set; }

    public override string ToString() => Name;
}
