using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Database.Implementations.Entities.Federation;

/// <summary>
/// An entity representing an actor that follows this instance.
/// </summary>
public class FederationFollower
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FederationFollower"/> class.
    /// </summary>
    /// <param name="actorId">The ID of the actor.</param>
    public FederationFollower(int actorId)
    {
        ActorId = actorId;
        DateCreated = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the identity of this instance.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; private set; }

    /// <summary>
    /// Gets the actor ID.
    /// </summary>
    /// <remarks>
    /// Required, Unique, FK to FederationActor.
    /// </remarks>
    public int ActorId { get; private set; }

    /// <summary>
    /// Gets the actor navigation property.
    /// </summary>
    public FederationActor Actor { get; private set; } = null!;

    /// <summary>
    /// Gets the date created. This should be in UTC.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public DateTime DateCreated { get; private set; }
}
