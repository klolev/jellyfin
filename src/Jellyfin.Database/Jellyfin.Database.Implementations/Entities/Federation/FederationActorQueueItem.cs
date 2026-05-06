using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Database.Implementations.Entities.Federation;

/// <summary>
/// The kind of a <see cref="FederationActorQueueItem"/>.
/// </summary>
public enum FederationActorQueueItemKind
{
    /// <summary>
    /// A signed POST to the target URL (typically an inbox) with the serialized activity as body.
    /// Success is any 2xx — no response-body processing.
    /// </summary>
    Deliver = 0,

    /// <summary>
    /// A signed GET of the target URL. The response body is handed to the federation-side
    /// fetch-response handler, which may ingest content and/or enqueue follow-up Fetch items
    /// (e.g. next outbox page for backfill).
    /// </summary>
    Fetch = 1
}

/// <summary>
/// An entry in a <see cref="FederationActorQueue"/> representing a single HTTP interaction with the
/// remote actor. Items are processed in <c>DateCreated</c> order; success deletes the item, failure
/// bumps the queue's backoff and leaves remaining items for the next attempt window.
/// </summary>
public class FederationActorQueueItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FederationActorQueueItem"/> class.
    /// </summary>
    /// <param name="queueId">The parent queue ID.</param>
    /// <param name="kind">The kind of interaction.</param>
    /// <param name="targetUrl">The URL to POST to (Deliver) or GET from (Fetch).</param>
    /// <param name="body">Request body for Deliver items; null for Fetch items.</param>
    public FederationActorQueueItem(int queueId, FederationActorQueueItemKind kind, string targetUrl, string? body)
    {
        QueueId = queueId;
        Kind = kind;
        TargetUrl = targetUrl;
        Body = body;
        DateCreated = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the identity of this instance.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; private set; }

    /// <summary>
    /// Gets the parent queue ID.
    /// </summary>
    /// <remarks>
    /// Required, FK to FederationActorQueue (cascade).
    /// </remarks>
    public int QueueId { get; private set; }

    /// <summary>
    /// Gets the parent queue navigation property.
    /// </summary>
    public FederationActorQueue Queue { get; private set; } = null!;

    /// <summary>
    /// Gets the kind of interaction.
    /// </summary>
    public FederationActorQueueItemKind Kind { get; private set; }

    /// <summary>
    /// Gets the target URL.
    /// </summary>
    public string TargetUrl { get; private set; }

    /// <summary>
    /// Gets the request body. Populated for <see cref="FederationActorQueueItemKind.Deliver"/>
    /// items and null for <see cref="FederationActorQueueItemKind.Fetch"/> items.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which this item was enqueued.
    /// </summary>
    public DateTime DateCreated { get; private set; }
}
