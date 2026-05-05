using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Signing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using StructuredFieldValues;

namespace Jellyfin.Server.Implementations.Federation.Signing;

/// <summary>
/// Defines the class.
/// </summary>
public class HTTPSignatureService : IFederationSigningService
{
    private const int MaxInboxBodySize = 64 * 1024;
    private static readonly string[] RequiredComponents = { "@method", "@authority", "@path", "content-digest", "content-type" };
    private static readonly string[] BodylessComponents = { "@method", "@authority", "@path" };
    private static readonly TimeSpan SignatureMaxAge = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim KeypairLock = new(1, 1);

    private readonly IHTTPSignatureKeyProvider _keyProvider;
    private readonly IConfigurationManager _configManager;
    private readonly ILogger<HTTPSignatureService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HTTPSignatureService"/> class.
    /// </summary>
    /// <param name="keyProvider">The actor public key provider.</param>
    /// <param name="configManager">The configuration manager.</param>
    /// <param name="logger">The logger.</param>
    public HTTPSignatureService(IHTTPSignatureKeyProvider keyProvider, IConfigurationManager configManager, ILogger<HTTPSignatureService> logger)
    {
        _keyProvider = keyProvider;
        _configManager = configManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> EnsureKeypairAsync()
    {
        var config = _configManager.GetFederationConfiguration();
        if (!string.IsNullOrEmpty(config.PublicKey))
        {
            return config.PublicKey;
        }

        await KeypairLock.WaitAsync().ConfigureAwait(false);
        try
        {
            config = _configManager.GetFederationConfiguration();
            if (!string.IsNullOrEmpty(config.PublicKey))
            {
                return config.PublicKey;
            }

            using var rsa = RSA.Create(2048);
            config.PublicKey = rsa.ExportSubjectPublicKeyInfoPem();
            config.PrivateKey = rsa.ExportRSAPrivateKeyPem();
            _configManager.SaveConfiguration(FederationConfigurationStore.StoreKey, config);
            return config.PublicKey;
        }
        finally
        {
            KeypairLock.Release();
        }
    }

    /// <summary>
    /// Validates an HTTP signature per RFC 9421 and returns the signing actor's URL.
    /// </summary>
    /// <param name="request">The HTTP request to validate.</param>
    /// <returns>The actor URL that signed the request, or <c>null</c> if the signature is missing, malformed, or does not verify.</returns>
    public async Task<string?> ValidateAsync(HttpRequest request)
    {
        var signatureHeader = request.Headers["Signature"];
        var signatureInputHeader = request.Headers["Signature-Input"];

        if (StringValues.IsNullOrEmpty(signatureHeader) || StringValues.IsNullOrEmpty(signatureInputHeader))
        {
            _logger.LogDebug("Signature validation failed: missing Signature or Signature-Input header");
            return null;
        }

        // Parse both headers as RFC 8941 Dictionaries
        if (SfvParser.ParseDictionary(signatureInputHeader.ToString(), out var sigInputDict) != null ||
            SfvParser.ParseDictionary(signatureHeader.ToString(), out var sigDict) != null)
        {
            _logger.LogDebug("Signature validation failed: could not parse structured field headers");
            return null;
        }

        // Take the first matching signature entry
        var label = sigDict.Keys
            .Where(key => sigInputDict.ContainsKey(key))
            .FirstOrDefault();
        if (string.IsNullOrEmpty(label))
        {
            _logger.LogDebug("Signature validation failed: no matching label between Signature and Signature-Input");
            return null;
        }

        var inputItem = sigInputDict[label];
        var sigItem = sigDict[label];

        // Extract signature bytes
        if (sigItem.Value is not ReadOnlyMemory<byte> signatureBytes)
        {
            _logger.LogDebug("Signature validation failed: signature value is not a byte sequence");
            return null;
        }

        // Extract keyid from parameters
        if (!inputItem.Parameters.TryGetValue("keyid", out var keyIdObj) || keyIdObj is not string keyId)
        {
            _logger.LogDebug("Signature validation failed: missing or non-string keyid parameter");
            return null;
        }

        // Extract algorithm if present
        var algorithm = inputItem.Parameters.TryGetValue("alg", out var algObj) && algObj is string alg ? alg : "rsa-pss-sha512";

        // Reject stale signatures: require a `created` parameter within SignatureMaxAge.
        if (!inputItem.Parameters.TryGetValue("created", out var createdObj) || createdObj is not long createdEpoch)
        {
            _logger.LogDebug("Signature validation failed: missing or invalid created parameter");
            return null;
        }

        var createdAt = DateTimeOffset.FromUnixTimeSeconds(createdEpoch);
        var age = DateTimeOffset.UtcNow - createdAt;
        if (age > SignatureMaxAge || age < -SignatureMaxAge)
        {
            _logger.LogDebug("Signature validation failed: signature age {Age} exceeds max {Max} (keyid={KeyId})", age, SignatureMaxAge, keyId);
            return null;
        }

        // Reconstruct the signature base from the component list
        var components = inputItem.Value is IReadOnlyList<ParsedItem> list
            ? list.Select(c => c.Value as string ?? c.Value.ToString() ?? string.Empty).ToList()
            : new List<string>();

        // Reject oversized payloads before reading the body into memory.
        if (request.ContentLength > MaxInboxBodySize)
        {
            _logger.LogWarning("Signature validation failed: Content-Length {Length} exceeds {Max} (keyid={KeyId})", request.ContentLength, MaxInboxBodySize, keyId);
            return null;
        }

        // Read the actual body to determine whether the request is bodyful.
        request.EnableBuffering();
        using var ms = new System.IO.MemoryStream();
        await request.Body.CopyToAsync(ms).ConfigureAwait(false);
        if (ms.Length > MaxInboxBodySize)
        {
            _logger.LogWarning("Signature validation failed: body size {Length} exceeds {Max} (keyid={KeyId})", ms.Length, MaxInboxBodySize, keyId);
            return null;
        }

        request.Body.Position = 0;
        var bodyBytes = ms.ToArray();

        var hasBody = bodyBytes.Length > 0;
        var required = hasBody ? RequiredComponents : BodylessComponents;
        if (!required.All(c => components.Contains(c)))
        {
            _logger.LogDebug("Signature validation failed: missing required components (keyid={KeyId})", keyId);
            return null;
        }

        if (hasBody)
        {
            var contentDigestHeader = request.Headers["content-digest"].ToString();
            if (string.IsNullOrEmpty(contentDigestHeader))
            {
                _logger.LogDebug("Signature validation failed: bodyful request missing content-digest header (keyid={KeyId})", keyId);
                return null;
            }

            if (!VerifyContentDigest(contentDigestHeader, bodyBytes))
            {
                _logger.LogWarning("Signature validation failed: content-digest mismatch (keyid={KeyId})", keyId);
                return null;
            }
        }

        var signingLines = new List<string>();
        foreach (var component in components)
        {
            var value = component switch
            {
                "@method" => request.Method,
                "@authority" => request.Host.ToString(),
                "@path" => request.Path.ToString(),
                "@target-uri" => $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}",
                "@scheme" => request.Scheme,
                "@query" => request.QueryString.ToString(),
                _ => request.Headers[component].ToString()
            };
            signingLines.Add($"\"{component}\": {value}");
        }

        // Reconstruct the raw Signature-Input value without the label for @signature-params
        var rawInput = signatureInputHeader.ToString();
        var labelPrefix = $"{label}=";
        var signatureParams = rawInput.StartsWith(labelPrefix, System.StringComparison.Ordinal)
            ? rawInput[labelPrefix.Length..]
            : rawInput;
        signingLines.Add($"\"@signature-params\": {signatureParams}");

        var signingBase = string.Join('\n', signingLines);

        // Determine algorithm and verify
        var (hashAlgorithm, padding) = algorithm switch
        {
            "rsa-pss-sha256" => (HashAlgorithmName.SHA256, RSASignaturePadding.Pss),
            "rsa-pss-sha512" => (HashAlgorithmName.SHA512, RSASignaturePadding.Pss),
            "rsa-v1_5-sha256" => (HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            _ => (HashAlgorithmName.SHA512, RSASignaturePadding.Pss)
        };

        var actorUrl = keyId.Split('#')[0];
        var publicKey = await _keyProvider.GetPublicKey(actorUrl).ConfigureAwait(false);
        if (publicKey == null)
        {
            _logger.LogWarning("Signature validation failed: could not fetch public key for {ActorUrl}", actorUrl);
            return null;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);
            var verified = rsa.VerifyData(
                System.Text.Encoding.UTF8.GetBytes(signingBase),
                signatureBytes.ToArray(),
                hashAlgorithm,
                padding);
            if (!verified)
            {
                _logger.LogWarning("Signature validation failed: cryptographic verification failed for {ActorUrl}", actorUrl);
            }

            return verified ? actorUrl : null;
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Signature validation failed: cryptographic exception for {ActorUrl}", actorUrl);
            return null;
        }
    }

    private static bool VerifyContentDigest(string header, byte[] body)
    {
        // Content-Digest is an RFC 8941 Dictionary: each member's key is the algorithm name and
        // its value is a byte-sequence containing the hash. Using SfvParser avoids fragile manual
        // splitting that would break if an algorithm name ever contained '='.
        if (SfvParser.ParseDictionary(header, out var dict) != null || dict.Count == 0)
        {
            return false;
        }

        foreach (var (algorithmName, member) in dict)
        {
            if (member.Value is not ReadOnlyMemory<byte> expectedHash)
            {
                continue;
            }

            byte[] actualHash = algorithmName switch
            {
                "sha-256" => SHA256.HashData(body),
                "sha-512" => SHA512.HashData(body),
                _ => Array.Empty<byte>()
            };

            if (actualHash.Length == 0)
            {
                continue;
            }

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash.Span);
        }

        return false;
    }

    /// <summary>
    /// Signs an outbound HTTP request per RFC 9421.
    /// </summary>
    /// <param name="request">The HTTP request message to sign.</param>
    /// <returns>The request with Signature and Signature-Input headers added.</returns>
    public async Task<HttpRequestMessage> SignAsync(HttpRequestMessage request)
    {
        await EnsureKeypairAsync().ConfigureAwait(false);
        var config = _configManager.GetFederationConfiguration();

        var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI must be set before signing.");
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var keyId = $"{config.ActorURL}#main-key";

        // Determine whether the request has a meaningful body. RFC 9530 allows omitting
        // Content-Digest on bodyless requests; doing so removes the need for callers to
        // synthesize an empty ByteArrayContent just to satisfy signing.
        var hasBody = request.Content is not null;
        byte[] body = Array.Empty<byte>();
        if (hasBody)
        {
            body = await request.Content!.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        // Only bodyful requests carry content-digest / content-type in the signature.
        var components = hasBody && body.Length > 0 ? RequiredComponents : BodylessComponents;

        if (hasBody && body.Length > 0)
        {
            var hash = SHA256.HashData(body);
            request.Content!.Headers.TryAddWithoutValidation("Content-Digest", $"sha-256=:{Convert.ToBase64String(hash)}:");
        }

        // Build the signature base
        var signingLines = new List<string>();
        foreach (var component in components)
        {
            var value = component switch
            {
                "@method" => request.Method.ToString().ToUpperInvariant(),
                "@authority" => uri.Authority,
                "@path" => uri.AbsolutePath,
                "@target-uri" => uri.ToString(),
                "@scheme" => uri.Scheme,
                "@query" => uri.Query,
                _ => request.Headers.TryGetValues(component, out var values)
                    ? string.Join(", ", values)
                    : request.Content?.Headers.TryGetValues(component, out var contentValues) == true
                        ? string.Join(", ", contentValues)
                        : string.Empty
            };
            signingLines.Add($"\"{component}\": {value}");
        }

        // Build the signature-params value
        var componentList = string.Join(" ", components.Select(c => $"\"{c}\""));
        var signatureParams = $"({componentList});created={created};keyid=\"{keyId}\"";
        signingLines.Add($"\"@signature-params\": {signatureParams}");

        var signingBase = string.Join('\n', signingLines);

        // Sign with RSA-PSS-SHA512
        using var rsa = RSA.Create();
        rsa.ImportFromPem(config.PrivateKey);
        var signatureBytes = rsa.SignData(
            Encoding.UTF8.GetBytes(signingBase),
            HashAlgorithmName.SHA512,
            RSASignaturePadding.Pss);

        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        // Add headers
        request.Headers.TryAddWithoutValidation("Signature-Input", $"sig1={signatureParams}");
        request.Headers.TryAddWithoutValidation("Signature", $"sig1=:{signatureBase64}:");

        return request;
    }
}
