using System;
using MediaBrowser.Controller.Entities;
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
}
