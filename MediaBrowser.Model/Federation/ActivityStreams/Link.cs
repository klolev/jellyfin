using System.Text.Json.Serialization;

namespace MediaBrowser.Model.Federation.ActivityStreams;

/// <summary>
/// ActivityStreams Link type.
/// </summary>
public class Link
{
    /// <summary>
    /// Gets or sets the href.
    /// </summary>
    [JsonPropertyName("href")]
    public string? Href { get; set; }

    /// <summary>
    /// Gets or sets the rel.
    /// </summary>
    [JsonPropertyName("rel")]
    public string? Rel { get; set; }

    /// <summary>
    /// Gets or sets the media type.
    /// </summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the hreflang.
    /// </summary>
    [JsonPropertyName("hreflang")]
    public string? Hreflang { get; set; }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    [JsonPropertyName("height")]
    public int? Height { get; set; }

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    [JsonPropertyName("width")]
    public int? Width { get; set; }
}
