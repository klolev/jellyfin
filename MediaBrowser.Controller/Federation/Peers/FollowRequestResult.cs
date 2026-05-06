namespace MediaBrowser.Controller.Federation.Peers;

/// <summary>
/// Defines the result of sending an outbound follow request.
/// </summary>
public enum FollowRequestResult
{
    /// <summary>
    /// The follow request was queued for delivery.
    /// </summary>
    Success,

    /// <summary>
    /// The remote actor document could not be fetched.
    /// </summary>
    ActorFetchFailed,

    /// <summary>
    /// This instance already follows (or is pending following) the target actor.
    /// </summary>
    AlreadyFollowing
}
