using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Database.Implementations.Entities.Federation;

/// <summary>
/// An entity mapping a locally-ingested BaseItem to its source on a remote federation actor.
/// One BaseItem may have multiple rows, one per remote source (for the same movie hosted by multiple instances).
/// </summary>
public class FederationIngestedItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FederationIngestedItem"/> class.
    /// </summary>
    /// <param name="baseItemId">The local BaseItem ID.</param>
    /// <param name="actorId">The source actor ID.</param>
    /// <param name="sourceId">The item ID as known by the source instance.</param>
    public FederationIngestedItem(Guid baseItemId, int actorId, Guid sourceId)
    {
        BaseItemId = baseItemId;
        ActorId = actorId;
        SourceId = sourceId;
        DateIngested = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the identity of this instance.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; private set; }

    /// <summary>
    /// Gets the local BaseItem ID.
    /// </summary>
    /// <remarks>
    /// Required, FK to BaseItems.
    /// </remarks>
    public Guid BaseItemId { get; private set; }

    /// <summary>
    /// Gets the source actor ID.
    /// </summary>
    /// <remarks>
    /// Required, FK to FederationActor.
    /// </remarks>
    public int ActorId { get; private set; }

    /// <summary>
    /// Gets the actor navigation property.
    /// </summary>
    public FederationActor Actor { get; private set; } = null!;

    /// <summary>
    /// Gets the source item ID as known by the source instance.
    /// </summary>
    /// <remarks>
    /// Required. Combined with ActorId this uniquely identifies a remote item.
    /// </remarks>
    public Guid SourceId { get; private set; }

    /// <summary>
    /// Gets the date this item was ingested. This should be in UTC.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public DateTime DateIngested { get; private set; }
}
