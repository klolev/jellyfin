using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Database.Implementations.Entities.Federation;

/// <summary>
/// An entity representing a time-bound capability token issued to a follower for streaming a
/// specific library item via the federation media mirror endpoints.
/// </summary>
public class FederationStreamToken
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FederationStreamToken"/> class.
    /// </summary>
    /// <param name="tokenHash">The SHA-256 hash of the raw token bytes.</param>
    /// <param name="followerId">The ID of the follower this token was issued to.</param>
    /// <param name="itemId">The library item this token authorizes access to.</param>
    /// <param name="expiresAt">The UTC expiry timestamp.</param>
    public FederationStreamToken(byte[] tokenHash, int followerId, Guid itemId, DateTime expiresAt)
    {
        TokenHash = tokenHash;
        FollowerId = followerId;
        ItemId = itemId;
        ExpiresAt = expiresAt;
        DateCreated = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the identity of this instance.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; private set; }

    /// <summary>
    /// Gets the SHA-256 hash of the raw token.
    /// </summary>
    /// <remarks>
    /// Required, Unique. Raw tokens are never persisted.
    /// </remarks>
#pragma warning disable CA1819 // Properties should not return arrays — EF maps byte[] directly to BLOB
    public byte[] TokenHash { get; private set; }
#pragma warning restore CA1819

    /// <summary>
    /// Gets the follower ID.
    /// </summary>
    /// <remarks>
    /// Required, FK to FederationFollower with cascade delete.
    /// </remarks>
    public int FollowerId { get; private set; }

    /// <summary>
    /// Gets the follower navigation property.
    /// </summary>
    public FederationFollower Follower { get; private set; } = null!;

    /// <summary>
    /// Gets the library item this token authorizes.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public Guid ItemId { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which this token expires.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp at which this token was issued.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public DateTime DateCreated { get; private set; }
}
