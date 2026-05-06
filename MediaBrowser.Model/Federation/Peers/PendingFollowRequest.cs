using System;

namespace MediaBrowser.Model.Federation.Peers;

/// <summary>
/// A pending federation follow request — either inbound (someone wants to follow this instance) or
/// outbound (this instance has requested to follow a remote actor) depending on which collection it
/// was returned from.
/// </summary>
public class PendingFollowRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PendingFollowRequest"/> class.
    /// </summary>
    /// <param name="actorUrl">The actor URL.</param>
    /// <param name="dateCreated">When the request was recorded, in UTC.</param>
    public PendingFollowRequest(string actorUrl, DateTime dateCreated)
    {
        ActorUrl = actorUrl;
        DateCreated = dateCreated;
    }

    /// <summary>
    /// Gets or sets the actor URL.
    /// </summary>
    public string ActorUrl { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp at which the request was recorded.
    /// </summary>
    public DateTime DateCreated { get; set; }
}
