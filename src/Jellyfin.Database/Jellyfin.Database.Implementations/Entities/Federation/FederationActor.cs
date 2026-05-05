using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Database.Implementations.Entities.Federation;

/// <summary>
/// An entity representing a known Federation actor.
/// </summary>
public class FederationActor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FederationActor"/> class.
    /// </summary>
    /// <param name="url">The actor URL.</param>
    /// <param name="inboxUrl">The actor's inbox URL.</param>
    /// <param name="outboxUrl">The actor's outbox URL.</param>
    /// <param name="publicKey">The actor's public key PEM.</param>
    public FederationActor(string url, string inboxUrl, string outboxUrl, string publicKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        ArgumentException.ThrowIfNullOrEmpty(inboxUrl);
        ArgumentException.ThrowIfNullOrEmpty(outboxUrl);
        ArgumentException.ThrowIfNullOrEmpty(publicKey);

        Url = url;
        InboxUrl = inboxUrl;
        OutboxUrl = outboxUrl;
        PublicKey = publicKey;
        DateCreated = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the identity of this instance.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; private set; }

    /// <summary>
    /// Gets or sets the actor's URL.
    /// </summary>
    /// <remarks>
    /// Required, Unique, Max length = 2048.
    /// </remarks>
    [MaxLength(2048)]
    [StringLength(2048)]
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets the actor's inbox URL.
    /// </summary>
    /// <remarks>
    /// Required, Max length = 2048.
    /// </remarks>
    [MaxLength(2048)]
    [StringLength(2048)]
    public string InboxUrl { get; set; }

    /// <summary>
    /// Gets or sets the actor's outbox URL.
    /// </summary>
    /// <remarks>
    /// Required, Max length = 2048.
    /// </remarks>
    [MaxLength(2048)]
    [StringLength(2048)]
    public string OutboxUrl { get; set; }

    /// <summary>
    /// Gets or sets the actor's public key PEM.
    /// </summary>
    /// <remarks>
    /// Required, Max length = 2048.
    /// </remarks>
    [MaxLength(2048)]
    [StringLength(2048)]
    public string PublicKey { get; set; }

    /// <summary>
    /// Gets the date created. This should be in UTC.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public DateTime DateCreated { get; private set; }
}
