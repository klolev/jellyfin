using System;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Federation.Media;

/// <summary>
/// Provides the internal "federation-service" user identity that stands in for follower-initiated
/// requests inside the stage-1 streaming pipeline. The row exists only in the local user table with
/// <c>InvalidAuthProvider</c> assigned, so no external login flow can resolve to it — federation
/// mirror endpoints synthesize a <see cref="System.Security.Claims.ClaimsPrincipal"/> from this id
/// after token validation, purely for the duration of the mirror call.
/// </summary>
public interface IFederationServiceUserProvider
{
    /// <summary>
    /// Gets or lazily creates the federation service user, returning its id.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The federation service user id.</returns>
    Task<Guid> GetOrCreateAsync(CancellationToken cancellationToken);
}
