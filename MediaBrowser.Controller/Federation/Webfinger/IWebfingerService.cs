using System.Threading.Tasks;
using MediaBrowser.Model.Federation.Webfinger;

namespace MediaBrowser.Controller.Federation.Webfinger;

/// <summary>
/// IWebfingerService.
/// </summary>
public interface IWebfingerService
{
    /// <summary>
    /// Gets the Webfinger response.
    /// </summary>
    /// <param name="resource">The resource queried.</param>
    /// <returns>The Webfinger response, null if not found.</returns>
    public Task<WebfingerResponse?> QueryAsync(string resource);
}
