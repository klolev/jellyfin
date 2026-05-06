using System;
using System.Collections.Generic;

namespace MediaBrowser.Model.Federation.Library;

/// <summary>
/// Represents a library item for federation sharing.
/// </summary>
public class FederationLibraryItem
{
    /// <summary>
    /// Gets or sets the item ID as known by the source instance.
    /// </summary>
    public Guid SourceId { get; set; }

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the overview/description.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the runtime in ticks.
    /// </summary>
    public long? RunTimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the media type (Movie, Series, Episode, etc.).
    /// </summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider IDs (Imdb, Tmdb, Tvdb, etc.).
    /// </summary>
    public Dictionary<string, string> ProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Converts runtime ticks to an ISO 8601 duration string.
    /// </summary>
    /// <returns>The duration string, or null if no runtime.</returns>
    public string? GetIsoDuration()
    {
        if (!RunTimeTicks.HasValue)
        {
            return null;
        }

        var span = TimeSpan.FromTicks(RunTimeTicks.Value);
        return $"PT{(int)span.TotalHours}H{span.Minutes}M{span.Seconds}S";
    }
}
