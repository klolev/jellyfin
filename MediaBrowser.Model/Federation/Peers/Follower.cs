namespace MediaBrowser.Model.Federation.Peers;

/// <summary>
/// Class Follower.
/// </summary>
public class Follower
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Follower" /> class.
    /// </summary>
    /// <param name="url">The follower's url.</param>
    /// <param name="inboxUrl">The follower's inbox url.</param>
    /// <param name="publicKey">The follower's public key.</param>
    public Follower(string url, string inboxUrl, string publicKey)
    {
        Url = url;
        InboxUrl = inboxUrl;
        PublicKey = publicKey;
    }

    /// <summary>
    /// Gets or sets the follower's url.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets the follower's inbox url.
    /// </summary>
    public string InboxUrl { get; set; }

    /// <summary>
    /// Gets or sets the follower's public key.
    /// </summary>
    public string PublicKey { get; set; }
}
