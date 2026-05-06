using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Database.Implementations.Entities.Federation;

/// <summary>
/// An entity representing a published activity in the outbox.
/// </summary>
public class FederationOutboxActivity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FederationOutboxActivity"/> class.
    /// </summary>
    /// <param name="activityJson">The serialized activity JSON.</param>
    public FederationOutboxActivity(string activityJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(activityJson);

        ActivityJson = activityJson;
        DateCreated = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the identity of this instance.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; private set; }

    /// <summary>
    /// Gets or sets the serialized activity JSON.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public string ActivityJson { get; set; }

    /// <summary>
    /// Gets the date created. This should be in UTC.
    /// </summary>
    /// <remarks>
    /// Required.
    /// </remarks>
    public DateTime DateCreated { get; private set; }
}
