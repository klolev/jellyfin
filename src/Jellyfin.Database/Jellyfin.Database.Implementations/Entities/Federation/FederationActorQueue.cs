using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Database.Implementations.Entities.Federation;

/// <summary>
/// Per-actor queue of pending federation interactions (Deliver POSTs and Fetch GETs). One queue
/// per actor; items processed in <c>DateCreated</c> order by the worker. Queue-level backoff
/// applies to all items: if one item fails, the queue's <see cref="NextAttemptAt"/> is bumped and
/// subsequent items wait.
/// </summary>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix — this is an entity representing a queue, not a queue type
public class FederationActorQueue
#pragma warning restore CA1711
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FederationActorQueue"/> class.
    /// </summary>
    /// <param name="actorId">The remote actor this queue belongs to.</param>
    public FederationActorQueue(int actorId)
    {
        ActorId = actorId;
        NextAttemptAt = DateTime.UtcNow;
        AttemptCount = 0;
        DateCreated = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the identity of this instance.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; private set; }

    /// <summary>
    /// Gets the actor ID this queue is bound to.
    /// </summary>
    /// <remarks>
    /// Required, Unique, FK to FederationActor (cascade).
    /// </remarks>
    public int ActorId { get; private set; }

    /// <summary>
    /// Gets the actor navigation property.
    /// </summary>
    public FederationActor Actor { get; private set; } = null!;

    /// <summary>
    /// Gets or sets the UTC time at which the worker should next attempt this queue.
    /// </summary>
    public DateTime NextAttemptAt { get; set; }

    /// <summary>
    /// Gets or sets the number of consecutive failed attempts. Reset to 0 on any success.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Gets the UTC timestamp at which this queue row was created.
    /// </summary>
    public DateTime DateCreated { get; private set; }
}
