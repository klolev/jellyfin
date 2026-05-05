namespace MediaBrowser.Model.Federation.Peers;

/// <summary>
/// Represents a known federation peer actor.
/// </summary>
public class PeerActor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PeerActor" /> class.
    /// </summary>
    /// <param name="url">The actor's URL.</param>
    /// <param name="inboxUrl">The actor's inbox URL.</param>
    /// <param name="outboxUrl">The actor's outbox URL.</param>
    /// <param name="publicKey">The actor's public key.</param>
    public PeerActor(string url, string inboxUrl, string outboxUrl, string publicKey)
    {
        Url = url;
        InboxUrl = inboxUrl;
        OutboxUrl = outboxUrl;
        PublicKey = publicKey;
    }

    /// <summary>
    /// Gets or sets the actor's URL.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets the actor's inbox URL.
    /// </summary>
    public string InboxUrl { get; set; }

    /// <summary>
    /// Gets or sets the actor's outbox URL.
    /// </summary>
    public string OutboxUrl { get; set; }

    /// <summary>
    /// Gets or sets the actor's public key.
    /// </summary>
    public string PublicKey { get; set; }
}
