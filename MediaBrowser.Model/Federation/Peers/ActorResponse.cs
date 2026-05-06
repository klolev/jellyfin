using System.Text.Json.Serialization;

namespace MediaBrowser.Model.Federation.Peers;

/// <summary>
/// Defines an actor response.
/// </summary>
public class ActorResponse
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorResponse"/> class.
    /// </summary>
    public ActorResponse()
    {
        Id = string.Empty;
        Inbox = string.Empty;
        Outbox = string.Empty;
        PublicKey = new PublicKeyResponse(string.Empty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorResponse"/> class.
    /// </summary>
    /// <param name="id">The actor id.</param>
    /// <param name="inbox">The actor inbox.</param>
    /// <param name="outbox">The actor outbox.</param>
    /// <param name="publicKey">The actor public key PEM.</param>
    public ActorResponse(string id, string inbox, string outbox, string publicKey)
    {
        Id = id;
        Inbox = inbox;
        Outbox = outbox;
        PublicKey = new PublicKeyResponse(publicKey);
    }

    /// <summary>
    /// Gets or sets the id of the response.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the inbox url of the response.
    /// </summary>
    [JsonPropertyName("inbox")]
    public string Inbox { get; set; }

    /// <summary>
    /// Gets or sets the outbox url of the response.
    /// </summary>
    [JsonPropertyName("outbox")]
    public string Outbox { get; set; }

    /// <summary>
    /// Gets or sets the public key of the response.
    /// </summary>
    [JsonPropertyName("publicKey")]
    public PublicKeyResponse PublicKey { get; set; }

    /// <summary>
    /// Defines an actor public key response.
    /// </summary>
    public class PublicKeyResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PublicKeyResponse"/> class.
        /// </summary>
        public PublicKeyResponse()
        {
            PublicKeyPem = string.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PublicKeyResponse"/> class.
        /// </summary>
        /// <param name="publicKeyPem">The actor public key.</param>
        public PublicKeyResponse(string publicKeyPem)
        {
            PublicKeyPem = publicKeyPem;
        }

        /// <summary>
        /// Gets or sets the inner public key of the response.
        /// </summary>
        [JsonPropertyName("publicKeyPem")]
        public string PublicKeyPem { get; set; }
    }
}
