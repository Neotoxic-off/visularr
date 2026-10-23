using System.ComponentModel.DataAnnotations;

namespace Jellyfin.Api.Models.VisularrDtos;

/// <summary>
/// A subtitle to align to the video's audio.
/// </summary>
public class SubtitleSyncDto
{
    /// <summary>
    /// Gets or sets the media source the subtitle belongs to.
    /// </summary>
    [Required]
    public string MediaSourceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the subtitle's stream index.
    /// </summary>
    [Required]
    public int StreamIndex { get; set; }
}
