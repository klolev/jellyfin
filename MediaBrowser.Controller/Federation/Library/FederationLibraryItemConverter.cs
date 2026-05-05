using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Federation.Library;

namespace MediaBrowser.Controller.Federation.Library;

/// <summary>
/// Converts between BaseItem and FederationLibraryItem.
/// </summary>
public static class FederationLibraryItemConverter
{
    /// <summary>
    /// Converts a BaseItem to a FederationLibraryItem for outbound federation.
    /// </summary>
    /// <param name="item">The base item.</param>
    /// <returns>The federation library item.</returns>
    public static FederationLibraryItem FromBaseItem(BaseItem item)
    {
        return new FederationLibraryItem
        {
            SourceId = item.Id,
            Name = item.Name,
            Overview = item.Overview,
            Year = item.ProductionYear,
            RunTimeTicks = item.RunTimeTicks,
            MediaType = item.GetType().Name,
            ProviderIds = new(item.ProviderIds, StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Creates a new BaseItem from a FederationLibraryItem for inbound federation.
    /// </summary>
    /// <param name="source">The federation library item.</param>
    /// <returns>The new base item, or null if the media type is unsupported.</returns>
    public static BaseItem? ToBaseItem(FederationLibraryItem source)
    {
        var item = source.MediaType switch
        {
            nameof(Movie) => (BaseItem)new Movie(),
            nameof(Series) => new Series(),
            nameof(Episode) => new Episode(),
            nameof(Season) => new Season(),
            _ => null
        };

        if (item == null)
        {
            return null;
        }

        item.Name = source.Name;
        item.Overview = source.Overview;
        item.ProductionYear = source.Year;
        item.RunTimeTicks = source.RunTimeTicks;

        foreach (var kvp in source.ProviderIds)
        {
            item.ProviderIds[kvp.Key] = kvp.Value;
        }

        return item;
    }
}
