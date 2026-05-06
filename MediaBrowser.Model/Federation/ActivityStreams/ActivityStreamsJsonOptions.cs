using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaBrowser.Model.Federation.ActivityStreams;

/// <summary>
/// Canonical <see cref="JsonSerializerOptions"/> used for ActivityPub / ActivityStreams JSON. All
/// federation code paths that serialize activities for outbox storage or deserialize them for
/// outbox rendering/delivery should route through <see cref="Default"/> so stored and read-back
/// forms stay in lockstep regardless of any host-wide serializer customization.
/// </summary>
public static class ActivityStreamsJsonOptions
{
    /// <summary>
    /// Gets the canonical options. Do not mutate — use the copy constructor if you need a variant.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = new()
    {
        // Property names come verbatim from [JsonPropertyName] attributes on the model types
        // (e.g. "@context", "type"). A naming policy would ignore the attributes anyway, but
        // pinning to null makes the intent explicit.
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
