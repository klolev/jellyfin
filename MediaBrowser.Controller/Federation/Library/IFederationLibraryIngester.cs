using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Federation.ActivityStreams;

namespace MediaBrowser.Controller.Federation.Library;

/// <summary>
/// Ingests remote library activities (Create/Update/Delete) into the local library.
/// Shared code path for live inbox deliveries and outbox backfill.
/// </summary>
public interface IFederationLibraryIngester
{
    /// <summary>
    /// Processes a library activity from a remote actor.
    /// The activity's signature is assumed to already be verified.
    /// The source actor must be in FederationFollowings; otherwise the activity is dropped.
    /// </summary>
    /// <param name="activity">The incoming activity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task IngestAsync(Activity activity, CancellationToken cancellationToken = default);
}
