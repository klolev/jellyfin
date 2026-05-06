#pragma warning disable SA1402
#pragma warning disable SA1649

using System;
using System.Text.Json.Serialization;

namespace MediaBrowser.Model.Federation.ActivityStreams;

/// <summary>
/// Relationship object type.
/// </summary>
public class Relationship : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Relationship"/> class.
    /// </summary>
    public Relationship()
    {
        Type = "Relationship";
    }

    /// <summary>
    /// Gets or sets the subject.
    /// </summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    /// <summary>
    /// Gets or sets the relationship type.
    /// </summary>
    [JsonPropertyName("relationship")]
    public string? RelationshipType { get; set; }
}

/// <summary>
/// Article object type.
/// </summary>
public class Article : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Article"/> class.
    /// </summary>
    public Article()
    {
        Type = "Article";
    }
}

/// <summary>
/// Document object type.
/// </summary>
public class Document : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Document"/> class.
    /// </summary>
    public Document()
    {
        Type = "Document";
    }
}

/// <summary>
/// Audio object type.
/// </summary>
public class Audio : Document
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Audio"/> class.
    /// </summary>
    public Audio()
    {
        Type = "Audio";
    }
}

/// <summary>
/// Image object type.
/// </summary>
public class Image : Document
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Image"/> class.
    /// </summary>
    public Image()
    {
        Type = "Image";
    }
}

/// <summary>
/// Video object type.
/// </summary>
public class Video : Document
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Video"/> class.
    /// </summary>
    public Video()
    {
        Type = "Video";
    }
}

/// <summary>
/// Note object type.
/// </summary>
public class Note : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Note"/> class.
    /// </summary>
    public Note()
    {
        Type = "Note";
    }
}

/// <summary>
/// Page object type.
/// </summary>
public class Page : Document
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Page"/> class.
    /// </summary>
    public Page()
    {
        Type = "Page";
    }
}

/// <summary>
/// Event object type.
/// </summary>
public class Event : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Event"/> class.
    /// </summary>
    public Event()
    {
        Type = "Event";
    }
}

/// <summary>
/// Place object type.
/// </summary>
public class Place : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Place"/> class.
    /// </summary>
    public Place()
    {
        Type = "Place";
    }

    /// <summary>
    /// Gets or sets the accuracy in percentage.
    /// </summary>
    [JsonPropertyName("accuracy")]
    public float? Accuracy { get; set; }

    /// <summary>
    /// Gets or sets the altitude.
    /// </summary>
    [JsonPropertyName("altitude")]
    public float? Altitude { get; set; }

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    [JsonPropertyName("latitude")]
    public float? Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    [JsonPropertyName("longitude")]
    public float? Longitude { get; set; }

    /// <summary>
    /// Gets or sets the radius.
    /// </summary>
    [JsonPropertyName("radius")]
    public float? Radius { get; set; }

    /// <summary>
    /// Gets or sets the units.
    /// </summary>
    [JsonPropertyName("units")]
    public string? Units { get; set; }
}

/// <summary>
/// Profile object type.
/// </summary>
public class Profile : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Profile"/> class.
    /// </summary>
    public Profile()
    {
        Type = "Profile";
    }

    /// <summary>
    /// Gets or sets what this profile describes.
    /// </summary>
    [JsonPropertyName("describes")]
    public Object? Describes { get; set; }
}

/// <summary>
/// Tombstone object type. Represents a deleted object.
/// </summary>
public class Tombstone : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Tombstone"/> class.
    /// </summary>
    public Tombstone()
    {
        Type = "Tombstone";
    }

    /// <summary>
    /// Gets or sets the former type of the deleted object.
    /// </summary>
    [JsonPropertyName("formerType")]
    public string? FormerType { get; set; }

    /// <summary>
    /// Gets or sets the deletion timestamp.
    /// </summary>
    [JsonPropertyName("deleted")]
    public DateTime? Deleted { get; set; }
}
