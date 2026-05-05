namespace MediaBrowser.Model.Federation.Peers;

/// <summary>
/// Class Following.
/// </summary>
public class Following
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Following" /> class.
    /// </summary>
    /// <param name="url">The followed actor's url.</param>
    /// <param name="outboxUrl">The followed actor's outbox url.</param>
    /// <param name="publicKey">The followed actor's public key.</param>
    public Following(string url, string outboxUrl, string publicKey)
    {
        Url = url;
        OutboxUrl = outboxUrl;
        PublicKey = publicKey;
    }

    /// <summary>
    /// Gets or sets the followed actor's url.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets the followed actor's outbox url.
    /// </summary>
    public string OutboxUrl { get; set; }

    /// <summary>
    /// Gets or sets the followed actor's public key.
    /// </summary>
    public string PublicKey { get; set; }
}
