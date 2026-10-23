using System.Text.Json.Serialization;

namespace Jellyfin.Api.Models.VisularrDtos;

/// <summary>
/// The hub (linkarr) that manages the users, as stored and as sent; both null when the users are managed here.
/// </summary>
public class ManagedDto
{
    /// <summary>
    /// Gets or sets the hub's name.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the hub's url, an absolute http(s) url.
    /// </summary>
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }
}
