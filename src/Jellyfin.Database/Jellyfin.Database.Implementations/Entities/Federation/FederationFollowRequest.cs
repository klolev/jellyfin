using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Database.Implementations.Entities.Federation;

/// <summary>
/// An entity representing a federation follow request, either inbound (a remote actor wants to
/// follow this instance) or outbound (we've requested to follow a remote actor). Uniquely keyed
/// on (ActorId, Type): one pending request per actor per direction.
/// </summary>
public class FederationFollowRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FederationFollowRequest"/> class.
    /// </summary>
    /// <param name="actorId">The actor the request relates to.</param>
    /// <param name="type">The direction of the follow request.</param>
    public FederationFollowRequest(int actorId, FederationFollowRequestType type)
    {
        ActorId = actorId;
        Type = type;
        Responded = false;
        DateCreated = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the identity of this instance.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; private set; }

    /// <summary>
    /// Gets the actor ID this request relates to.
    /// </summary>
    /// <remarks>
    /// Required, FK to FederationActor with cascade delete.
    /// </remarks>
    public int ActorId { get; private set; }

    /// <summary>
    /// Gets the actor navigation property.
    /// </summary>
    public FederationActor Actor { get; private set; } = null!;

    /// <summary>
    /// Gets or sets the direction of the follow request.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public FederationFollowRequestType Type { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the request was responded to.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public bool Responded { get; set; }

    /// <summary>
    /// Gets or sets the date created. This should be in UTC.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public DateTime DateCreated { get; set; }
}
