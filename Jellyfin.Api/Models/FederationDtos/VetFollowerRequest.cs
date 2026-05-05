namespace Jellyfin.Api.Models.FederationDtos;

/// <summary>
/// Body of <c>PATCH /Federation/Admin/Followers/Requests</c>. Selects whether to accept or reject
/// the pending inbound follow request.
/// </summary>
public sealed class VetFollowerRequest
{
    /// <summary>
    /// Gets or sets a value indicating whether to accept the pending request. <c>false</c> rejects.
    /// </summary>
    public bool Accept { get; set; } = true;
}
