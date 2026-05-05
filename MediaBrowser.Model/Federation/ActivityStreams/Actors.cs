#pragma warning disable SA1402
#pragma warning disable SA1649

using System.Text.Json.Serialization;

namespace MediaBrowser.Model.Federation.ActivityStreams;

/// <summary>
/// Base actor type with common actor properties.
/// </summary>
public class Actor : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Actor"/> class.
    /// </summary>
    public Actor()
    {
        Type = "Actor";
    }

    /// <summary>
    /// Gets or sets the preferred username.
    /// </summary>
    [JsonPropertyName("preferredUsername")]
    public string? PreferredUsername { get; set; }

    /// <summary>
    /// Gets or sets the inbox URL.
    /// </summary>
    [JsonPropertyName("inbox")]
    public string? Inbox { get; set; }

    /// <summary>
    /// Gets or sets the outbox URL.
    /// </summary>
    [JsonPropertyName("outbox")]
    public string? Outbox { get; set; }

    /// <summary>
    /// Gets or sets the followers collection URL.
    /// </summary>
    [JsonPropertyName("followers")]
    public string? Followers { get; set; }

    /// <summary>
    /// Gets or sets the following collection URL.
    /// </summary>
    [JsonPropertyName("following")]
    public string? Following { get; set; }

    /// <summary>
    /// Gets or sets the public key.
    /// </summary>
    [JsonPropertyName("publicKey")]
    public PublicKey? PublicKey { get; set; }
}

/// <summary>
/// Public key object for actor documents.
/// </summary>
public class PublicKey
{
    /// <summary>
    /// Gets or sets the key ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the owner actor URL.
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>
    /// Gets or sets the public key PEM.
    /// </summary>
    [JsonPropertyName("publicKeyPem")]
    public string? PublicKeyPem { get; set; }
}

/// <summary>
/// Application actor type.
/// </summary>
public class Application : Actor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Application"/> class.
    /// </summary>
    public Application()
    {
        Type = "Application";
    }
}

/// <summary>
/// Group actor type.
/// </summary>
public class Group : Actor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Group"/> class.
    /// </summary>
    public Group()
    {
        Type = "Group";
    }
}

/// <summary>
/// Organization actor type.
/// </summary>
public class Organization : Actor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Organization"/> class.
    /// </summary>
    public Organization()
    {
        Type = "Organization";
    }
}

/// <summary>
/// Person actor type.
/// </summary>
public class Person : Actor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Person"/> class.
    /// </summary>
    public Person()
    {
        Type = "Person";
    }
}

/// <summary>
/// Service actor type.
/// </summary>
public class Service : Actor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Service"/> class.
    /// </summary>
    public Service()
    {
        Type = "Service";
    }
}
