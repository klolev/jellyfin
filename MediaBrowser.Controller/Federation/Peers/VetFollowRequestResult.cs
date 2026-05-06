namespace MediaBrowser.Controller.Federation.Peers;

/// <summary>
/// Defines the result of vetting (accepting or rejecting) an inbound follow request.
/// </summary>
public enum VetFollowRequestResult
{
    /// <summary>
    /// The request was accepted or rejected as specified.
    /// </summary>
    Success,

    /// <summary>
    /// No pending (unresponded) request exists for the given actor — either none was ever made, or
    /// the previous request was already accepted or rejected. The caller should reach for
    /// <c>DELETE /Federation/Admin/Followers</c> if the intent is to remove an accepted follower.
    /// </summary>
    NoPendingRequest
}
