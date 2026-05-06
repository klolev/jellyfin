using System.Text.Json.Serialization;

namespace MediaBrowser.Model.Federation.ActivityStreams;

/// <summary>
/// Base ActivityStreams Activity type.
/// </summary>
public class Activity : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Activity"/> class.
    /// </summary>
    public Activity()
    {
        Type = "Activity";
    }

    /// <summary>
    /// Gets or sets the object of the activity.
    /// </summary>
    [JsonPropertyName("object")]
    public Object? Object { get; set; }

    /// <summary>
    /// Gets or sets the target.
    /// </summary>
    [JsonPropertyName("target")]
    public Object? Target { get; set; }

    /// <summary>
    /// Gets or sets the result.
    /// </summary>
    [JsonPropertyName("result")]
    public Object? Result { get; set; }

    /// <summary>
    /// Gets or sets the origin.
    /// </summary>
    [JsonPropertyName("origin")]
    public Object? Origin { get; set; }

    /// <summary>
    /// Gets or sets the instrument.
    /// </summary>
    [JsonPropertyName("instrument")]
    public Object? Instrument { get; set; }
}
