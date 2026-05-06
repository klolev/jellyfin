using System.Linq;
using MediaBrowser.Model.Federation.ActivityStreams;

namespace MediaBrowser.Model.Federation.Library;

/// <summary>
/// Builds ActivityStreams activities from a federation library item.
/// </summary>
public class FederationLibraryItemActivityBuilder
{
    private readonly FederationLibraryItem _item;
    private readonly string _actorUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationLibraryItemActivityBuilder"/> class.
    /// </summary>
    /// <param name="item">The library item.</param>
    /// <param name="actorUrl">The instance actor URL.</param>
    public FederationLibraryItemActivityBuilder(FederationLibraryItem item, string actorUrl)
    {
        _item = item;
        _actorUrl = actorUrl;
    }

    /// <summary>
    /// Builds a Create activity for the library item.
    /// </summary>
    /// <returns>The Create activity.</returns>
    public Create BuildActivity()
    {
        return new Create
        {
            Id = $"{_actorUrl}#item-{_item.SourceId}",
            Actor = _actorUrl,
            Object = BuildVideo()
        };
    }

    /// <summary>
    /// Builds an Update activity for the library item.
    /// </summary>
    /// <returns>The Update activity.</returns>
    public Update BuildUpdateActivity()
    {
        return new Update
        {
            Id = $"{_actorUrl}#update-{_item.SourceId}",
            Actor = _actorUrl,
            Object = BuildVideo()
        };
    }

    /// <summary>
    /// Builds a Delete activity for the library item.
    /// </summary>
    /// <returns>The Delete activity.</returns>
    public Delete BuildDeleteActivity()
    {
        return new Delete
        {
            Id = $"{_actorUrl}#delete-{_item.SourceId}",
            Actor = _actorUrl,
            Object = new Tombstone
            {
                Id = $"{_actorUrl}/Items/{_item.SourceId}",
                FormerType = "Video"
            }
        };
    }

    private Video BuildVideo()
    {
        var tags = _item.ProviderIds
            .Select(kvp => new ActivityStreams.Object
            {
                Type = "PropertyValue",
                Name = kvp.Key,
                Content = kvp.Value
            })
            .ToList();

        tags.Add(new ActivityStreams.Object
        {
            Type = "PropertyValue",
            Name = "MediaType",
            Content = _item.MediaType
        });

        if (_item.Year.HasValue)
        {
            tags.Add(new ActivityStreams.Object
            {
                Type = "PropertyValue",
                Name = "Year",
                Content = _item.Year.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        return new Video
        {
            Id = $"{_actorUrl}/Items/{_item.SourceId}",
            Name = _item.Name,
            Content = _item.Overview,
            Duration = _item.GetIsoDuration(),
            Tag = tags.ToArray()
        };
    }
}
