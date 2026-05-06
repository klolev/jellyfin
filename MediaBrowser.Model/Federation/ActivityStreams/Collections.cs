#pragma warning disable SA1402
#pragma warning disable SA1649

using System.Text.Json.Serialization;

namespace MediaBrowser.Model.Federation.ActivityStreams;

/// <summary>
/// ActivityStreams Collection type.
/// </summary>
public class Collection : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Collection"/> class.
    /// </summary>
    public Collection()
    {
        Type = "Collection";
    }

    /// <summary>
    /// Gets or sets the total number of items.
    /// </summary>
    [JsonPropertyName("totalItems")]
    public int? TotalItems { get; set; }

    /// <summary>
    /// Gets or sets the current page reference.
    /// </summary>
    [JsonPropertyName("current")]
    public string? Current { get; set; }

    /// <summary>
    /// Gets or sets the first page reference.
    /// </summary>
    [JsonPropertyName("first")]
    public string? First { get; set; }

    /// <summary>
    /// Gets or sets the last page reference.
    /// </summary>
    [JsonPropertyName("last")]
    public string? Last { get; set; }

    /// <summary>
    /// Gets or sets the items.
    /// </summary>
    [JsonPropertyName("items")]
    public Object[]? Items { get; set; }
}

/// <summary>
/// ActivityStreams OrderedCollection type.
/// </summary>
public class OrderedCollection : Object
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrderedCollection"/> class.
    /// </summary>
    public OrderedCollection()
    {
        Type = "OrderedCollection";
    }

    /// <summary>
    /// Gets or sets the total number of items.
    /// </summary>
    [JsonPropertyName("totalItems")]
    public int? TotalItems { get; set; }

    /// <summary>
    /// Gets or sets the current page reference.
    /// </summary>
    [JsonPropertyName("current")]
    public string? Current { get; set; }

    /// <summary>
    /// Gets or sets the first page reference.
    /// </summary>
    [JsonPropertyName("first")]
    public string? First { get; set; }

    /// <summary>
    /// Gets or sets the last page reference.
    /// </summary>
    [JsonPropertyName("last")]
    public string? Last { get; set; }

    /// <summary>
    /// Gets or sets the ordered items.
    /// </summary>
    [JsonPropertyName("orderedItems")]
    public Object[]? OrderedItems { get; set; }
}

/// <summary>
/// ActivityStreams CollectionPage type.
/// </summary>
public class CollectionPage : Collection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionPage"/> class.
    /// </summary>
    public CollectionPage()
    {
        Type = "CollectionPage";
    }

    /// <summary>
    /// Gets or sets the parent collection reference.
    /// </summary>
    [JsonPropertyName("partOf")]
    public string? PartOf { get; set; }

    /// <summary>
    /// Gets or sets the next page reference.
    /// </summary>
    [JsonPropertyName("next")]
    public string? Next { get; set; }

    /// <summary>
    /// Gets or sets the previous page reference.
    /// </summary>
    [JsonPropertyName("prev")]
    public string? Prev { get; set; }
}

/// <summary>
/// ActivityStreams OrderedCollectionPage type.
/// </summary>
public class OrderedCollectionPage : OrderedCollection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrderedCollectionPage"/> class.
    /// </summary>
    public OrderedCollectionPage()
    {
        Type = "OrderedCollectionPage";
    }

    /// <summary>
    /// Gets or sets the parent collection reference.
    /// </summary>
    [JsonPropertyName("partOf")]
    public string? PartOf { get; set; }

    /// <summary>
    /// Gets or sets the next page reference.
    /// </summary>
    [JsonPropertyName("next")]
    public string? Next { get; set; }

    /// <summary>
    /// Gets or sets the previous page reference.
    /// </summary>
    [JsonPropertyName("prev")]
    public string? Prev { get; set; }

    /// <summary>
    /// Gets or sets the start index.
    /// </summary>
    [JsonPropertyName("startIndex")]
    public int? StartIndex { get; set; }
}
