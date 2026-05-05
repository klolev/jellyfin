using System;
using System.Buffers.Text;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using MediaBrowser.Controller.Federation.Media;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Server.Implementations.Federation.Media;

/// <inheritdoc/>
public sealed class FederationStreamTokenService : IFederationStreamTokenService
{
    private const int TokenByteLength = 32;

    private readonly IDbContextFactory<JellyfinDbContext> _dbFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationStreamTokenService"/> class.
    /// </summary>
    /// <param name="dbFactory">Instance of the <see cref="IDbContextFactory{JellyfinDbContext}"/> interface.</param>
    public FederationStreamTokenService(IDbContextFactory<JellyfinDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <inheritdoc/>
    public async Task<string> IssueAsync(int followerId, Guid itemId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var rawBytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var rawToken = Base64Url.EncodeToString(rawBytes);
        var hash = SHA256.HashData(rawBytes);
        var expiresAt = DateTime.UtcNow.Add(ttl);

        var record = new FederationStreamToken(hash, followerId, itemId, expiresAt);

        var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                // Replace any existing token(s) for this (follower, item) pair so the DB stays bounded
                // at one row per pair and a compromised follower can't mint unlimited valid tokens.
                await db.FederationStreamTokens
                    .Where(t => t.FollowerId == followerId && t.ItemId.Equals(itemId))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);

                await db.FederationStreamTokens.AddAsync(record, cancellationToken).ConfigureAwait(false);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return rawToken;
    }

    /// <inheritdoc/>
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.FederationStreamTokens
                .Where(t => t.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<int?> ValidateAsync(string rawToken, Guid itemId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(rawToken))
        {
            return null;
        }

        byte[] rawBytes;
        try
        {
            rawBytes = Base64Url.DecodeFromChars(rawToken.AsSpan());
        }
        catch (FormatException)
        {
            return null;
        }

        if (rawBytes.Length != TokenByteLength)
        {
            return null;
        }

        var hash = SHA256.HashData(rawBytes);
        var now = DateTime.UtcNow;

        var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var record = await db.FederationStreamTokens
                .FirstOrDefaultAsync(
                    t => t.TokenHash == hash && t.ItemId.Equals(itemId) && t.ExpiresAt > now,
                    cancellationToken)
                .ConfigureAwait(false);
            return record?.FollowerId;
        }
    }
}
