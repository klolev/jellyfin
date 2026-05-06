using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Federation.Media;

namespace MediaBrowser.Controller.Federation.Media;

/// <summary>
/// Issues and validates time-bound capability tokens used by the federation media mirror endpoints.
/// The raw token is returned once from <see cref="IssueAsync"/> and must be relayed to the follower
/// immediately — only a SHA-256 hash of the raw bytes is persisted, so a DB leak cannot produce
/// valid tokens.
/// </summary>
public interface IFederationStreamTokenService
{
    /// <summary>
    /// Issues a new streaming token for the given (follower, item) pair.
    /// </summary>
    /// <param name="followerId">The follower receiving the token.</param>
    /// <param name="itemId">The library item this token authorizes.</param>
    /// <param name="ttl">How long the token remains valid from issuance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The issued token and its persisted expiry.</returns>
    Task<IssuedToken> IssueAsync(int followerId, Guid itemId, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>
    /// Validates a raw token for a specific library item.
    /// </summary>
    /// <param name="rawToken">The raw token sent by the caller.</param>
    /// <param name="itemId">The library item the caller is trying to access.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The follower ID this token was issued to if the token is valid, unexpired, and scoped to
    /// <paramref name="itemId"/>; <c>null</c> otherwise.
    /// </returns>
    Task<int?> ValidateAsync(string rawToken, Guid itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes expired token records.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows deleted.</returns>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken);
}
