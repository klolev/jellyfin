using System.Threading.Tasks;

namespace MediaBrowser.Controller.Federation.Signing;

/// <summary>
/// Provides public keys for HTTP signature verification.
/// </summary>
public interface IHTTPSignatureKeyProvider
{
    /// <summary>
    /// Gets the public key for a given actor URL.
    /// </summary>
    /// <param name="actorUrl">The actor URL.</param>
    /// <returns>The public key PEM string, or null if not found.</returns>
    public Task<string?> GetPublicKey(string actorUrl);
}
