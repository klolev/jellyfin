using System;
using System.Text.Json.Serialization;

namespace MediaBrowser.Model.Federation.ActivityStreams;

/// <summary>
/// Base ActivityStreams Object type.
/// </summary>
public class Object
{
    /// <summary>
    /// Gets the JSON-LD context.
    /// </summary>
    [JsonPropertyName("@context")]
    public string Context { get; } = "https://www.w3.org/ns/activitystreams";

    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the object type.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the actor. Formally an <see cref="Activity"/>-only field in AS2.0, but lifted
    /// to the base so nested inbound activities (e.g. the inner <c>Follow</c> of an
    /// <c>Undo{Follow}</c>) can surface their signer to upstream validation without a re-parse.
    /// <see cref="Activity"/> shadows this with <c>new</c> to keep its own serializer binding.
    /// </summary>
    [JsonPropertyName("actor")]
    public string? Actor { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the content.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// Gets or sets the summary.
    /// </summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>
    /// Gets or sets the media type.
    /// </summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    /// <summary>
    /// Gets or sets the URL.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the published timestamp.
    /// </summary>
    [JsonPropertyName("published")]
    public DateTime? Published { get; set; }

    /// <summary>
    /// Gets or sets the updated timestamp.
    /// </summary>
    [JsonPropertyName("updated")]
    public DateTime? Updated { get; set; }

    /// <summary>
    /// Gets or sets the attributed to reference.
    /// </summary>
    [JsonPropertyName("attributedTo")]
    public string? AttributedTo { get; set; }

    /// <summary>
    /// Gets or sets the to recipients.
    /// </summary>
    [JsonPropertyName("to")]
    public string[]? To { get; set; }

    /// <summary>
    /// Gets or sets the cc recipients.
    /// </summary>
    [JsonPropertyName("cc")]
    public string[]? Cc { get; set; }

    /// <summary>
    /// Gets or sets the in-reply-to reference.
    /// </summary>
    [JsonPropertyName("inReplyTo")]
    public string? InReplyTo { get; set; }

    /// <summary>
    /// Gets or sets the tags.
    /// </summary>
    [JsonPropertyName("tag")]
    public Object[]? Tag { get; set; }

    /// <summary>
    /// Gets or sets the attachments.
    /// </summary>
    [JsonPropertyName("attachment")]
    public Object[]? Attachment { get; set; }

    /// <summary>
    /// Gets or sets the icon.
    /// </summary>
    [JsonPropertyName("icon")]
    public Object? Icon { get; set; }

    /// <summary>
    /// Gets or sets the image.
    /// </summary>
    [JsonPropertyName("image")]
    public Object? Image { get; set; }

    /// <summary>
    /// Gets or sets the start time.
    /// </summary>
    [JsonPropertyName("startTime")]
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Gets or sets the end time.
    /// </summary>
    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Gets or sets the duration.
    /// </summary>
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    /// <summary>
    /// Gets or sets the replies collection.
    /// </summary>
    [JsonPropertyName("replies")]
    public string? Replies { get; set; }
}
