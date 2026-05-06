using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace MediaBrowser.Controller.Federation.Signing;

/// <summary>
/// IFederationSigningService.
/// </summary>
public interface IFederationSigningService
{
    /// <summary>
    /// Lazily ensures the keypair for HTTP signatures is generated and returns the current
    /// public-key PEM. A missing keypair is generated in-place and written back to federation
    /// configuration as a side effect, but callers do not need to re-read the config.
    /// </summary>
    /// <returns>The current public-key PEM.</returns>
    public Task<string> EnsureKeypairAsync();

    /// <summary>
    /// Validates an HTTP signature per RFC 9421 and returns the signing actor's URL.
    /// </summary>
    /// <param name="request">The HTTP request to validate.</param>
    /// <returns>The actor URL that signed the request, or <c>null</c> if the signature is missing, malformed, or does not verify.</returns>
    public Task<string?> ValidateAsync(HttpRequest request);

    /// <summary>
    /// Signs an outbound HTTP request per RFC 9421.
    /// </summary>
    /// <param name="request">The HTTP request message to sign.</param>
    /// <returns>The request with Signature and Signature-Input headers added.</returns>
    public Task<HttpRequestMessage> SignAsync(HttpRequestMessage request);
}
