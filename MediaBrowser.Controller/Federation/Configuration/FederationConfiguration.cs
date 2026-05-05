namespace MediaBrowser.Controller.Federation.Configuration;

/// <summary>
/// Defines the <see cref="FederationConfiguration" />.
/// </summary>
public class FederationConfiguration
{
    /// <summary>
    /// Gets or sets the instance's public key for HTTP Signatures.
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the instance's private key for HTTP Signatures.
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the instance's actor name for ActivityPub.
    /// </summary>
    public string ActorName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the public hostname for this instance (e.g. "my-instance.com").
    /// </summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether or not federation is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets the base URL.
    /// </summary>
    public string BaseURL => $"https://{Hostname}";

    /// <summary>
    /// Gets the actor URL.
    /// </summary>
    public string ActorURL => $"{BaseURL}/Federation/Actor";

    /// <summary>
    /// Gets the inbox URL.
    /// </summary>
    public string InboxURL => $"{BaseURL}/Federation/Inbox";

    /// <summary>
    /// Gets the outbox URL.
    /// </summary>
    public string OutboxURL => $"{BaseURL}/Federation/Outbox";
}
