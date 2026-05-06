#pragma warning disable SA1402
#pragma warning disable SA1649

using System.Text.Json.Serialization;

namespace MediaBrowser.Model.Federation.ActivityStreams;

/// <summary>
/// Arrive activity.
/// </summary>
public class Arrive : IntransitiveActivity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Arrive"/> class.
    /// </summary>
    public Arrive()
    {
        Type = "Arrive";
    }
}

/// <summary>
/// Travel activity.
/// </summary>
public class Travel : IntransitiveActivity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Travel"/> class.
    /// </summary>
    public Travel()
    {
        Type = "Travel";
    }
}

/// <summary>
/// Question activity.
/// </summary>
public class Question : IntransitiveActivity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Question"/> class.
    /// </summary>
    public Question()
    {
        Type = "Question";
    }

    /// <summary>
    /// Gets or sets the exclusive answer options.
    /// </summary>
    [JsonPropertyName("oneOf")]
    public Object[]? OneOf { get; set; }

    /// <summary>
    /// Gets or sets the inclusive answer options.
    /// </summary>
    [JsonPropertyName("anyOf")]
    public Object[]? AnyOf { get; set; }

    /// <summary>
    /// Gets or sets whether the question is closed.
    /// </summary>
    [JsonPropertyName("closed")]
    public object? Closed { get; set; }
}
