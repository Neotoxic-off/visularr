namespace Jellyfin.Api.Models.VisularrDtos;

/// <summary>
/// The synced subtitle, saved as a new external subtitle.
/// </summary>
public class SubtitleSyncResultDto
{
    /// <summary>
    /// Gets or sets the new subtitle's stream index, null when the refresh has not listed it yet.
    /// </summary>
    public int? StreamIndex { get; set; }
}
