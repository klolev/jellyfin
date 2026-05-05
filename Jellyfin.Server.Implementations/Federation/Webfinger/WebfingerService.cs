using System;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Webfinger;
using MediaBrowser.Model.Federation.Webfinger;

namespace Jellyfin.Server.Implementations.Federation.Webfinger;

/// <summary>
/// Defines the class.
/// </summary>
public class WebfingerService : IWebfingerService
{
    private readonly IConfigurationManager _configManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebfingerService"/> class.
    /// </summary>
    /// <param name="configManager">The configuration manager.</param>
    public WebfingerService(IConfigurationManager configManager)
    {
        _configManager = configManager;
    }

    /// <summary>
    /// Gets the Webfinger response.
    /// </summary>
    /// <param name="resource">The resource queried.</param>
    /// <returns>The Webfinger response, null if not found.</returns>
    public async Task<WebfingerResponse?> QueryAsync(string resource)
    {
        // Feature gate + startup validator guarantee Enabled, ActorName, and Hostname are all
        // populated by the time this is invoked.
        var config = _configManager.GetFederationConfiguration();
        var expectedResourceName = $"acct:{config.ActorName}@{config.Hostname}";
        if (resource != expectedResourceName)
        {
            return null;
        }

        return new WebfingerResponse(
            resource,
            Array.Empty<string>(),
            new WebfingerResponse.Link[]
            {
                new WebfingerResponse.Link(
                    "self",
                    "application/activity+json",
                    config.ActorURL)
            });
    }
}
