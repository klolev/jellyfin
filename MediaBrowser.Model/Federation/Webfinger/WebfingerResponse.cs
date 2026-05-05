using System.Text.Json.Serialization;

namespace MediaBrowser.Model.Federation.Webfinger;

/// <summary>
/// Defines a Webfinger response.
/// </summary>
public class WebfingerResponse
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebfingerResponse"/> class.
    /// </summary>
    /// <param name="subject">The resource URI being queried (ie: "acct:user@example.com").</param>
    /// <param name="aliases">The aliases of the resource.</param>
    /// <param name="links">The list of related resources.</param>
    public WebfingerResponse(string subject, string[] aliases, Link[] links)
    {
        Subject = subject;
        Aliases = aliases;
        Links = links;
    }

    /// <summary>
    /// Gets or sets the subject of the response.
    /// </summary>
    [JsonPropertyName("subject")]
    public string Subject { get; set; }

    /// <summary>
    /// Gets or sets the aliases list of the response.
    /// </summary>
    [JsonPropertyName("aliases")]
    public string[] Aliases { get; set; }

    /// <summary>
    /// Gets or sets the links list of the response.
    /// </summary>
    [JsonPropertyName("links")]
    public Link[] Links { get; set; }

    /// <summary>
    /// Defines a Webfinger link list item.
    /// </summary>
    public class Link
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Link"/> class.
        /// </summary>
        /// <param name="relation">The link relation.</param>
        /// <param name="type">The link content type.</param>
        /// <param name="href">The link url.</param>
        public Link(string relation, string type, string href)
        {
            Relation = relation;
            Type = type;
            Href = href;
        }

        /// <summary>
        /// Gets or sets the relation of the link.
        /// </summary>
        [JsonPropertyName("rel")]
        public string Relation { get; set; }

        /// <summary>
        /// Gets or sets the content type of the link.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the URL of the link.
        /// </summary>
        [JsonPropertyName("href")]
        public string Href { get; set; }
    }
}
