namespace Jellyfin.Database.Implementations.Entities.Federation;

/// <summary>
/// The direction of a follow request.
/// </summary>
public enum FederationFollowRequestType
{
    /// <summary>
    /// A remote actor wants to follow this instance.
    /// </summary>
    Follower = 0,

    /// <summary>
    /// This instance wants to follow a remote actor.
    /// </summary>
    Following = 1
}
