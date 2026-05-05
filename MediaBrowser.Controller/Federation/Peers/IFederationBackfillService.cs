using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities.Federation;

namespace MediaBrowser.Controller.Federation.Peers;

/// <summary>
/// Fetches a remote peer's outbox and feeds each library activity through the ingester.
/// Triggered when a follow request is accepted to snapshot the peer's existing library.
/// </summary>
public interface IFederationBackfillService
{
    /// <summary>
    /// Starts a backfill from the given peer by enqueueing a Fetch item for their outbox URL on
    /// the federation actor queue. The queue worker drives the pagination chain via repeated
    /// calls to <see cref="HandleOutboxResponseAsync"/>, picking up the <c>first</c>/<c>next</c>
    /// refs from each response. Retry/backoff is the queue worker's job — we don't do our own.
    /// </summary>
    /// <param name="source">The peer actor to backfill from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the enqueue operation.</returns>
    Task BackfillFromAsync(FederationActor source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invoked by the federation actor queue worker when a Fetch item for this peer's outbox
    /// returns a successful response. Ingests any inlined activities and enqueues a follow-up
    /// Fetch for the next page if the response carries a <c>first</c> or <c>next</c> ref.
    /// </summary>
    /// <param name="source">The peer actor the response came from.</param>
    /// <param name="responseBody">The raw JSON response body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task HandleOutboxResponseAsync(FederationActor source, string responseBody, CancellationToken cancellationToken);
}
