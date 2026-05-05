using System;

namespace Jellyfin.Api.Models.FederationDtos;

/// <summary>
/// Response body for <c>POST /Federation/Media/{itemId}/Token</c>.
/// </summary>
public sealed class FederationStreamTokenResponse
{
    /// <summary>
    /// Gets or sets the raw token string. Only ever transmitted here — the server persists a hash.
    /// </summary>
    public required string Token { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp at which the token expires.
    /// </summary>
    public required DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the fully-qualified progressive stream URL for convenience. The same token is
    /// accepted on the HLS and subtitle mirror endpoints.
    /// </summary>
    public required string StreamUrl { get; set; }
}
