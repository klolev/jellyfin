using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy; // default AuthorizationMiddlewareResultHandler impl lives here
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Api.Auth.FederationEnabledPolicy;

/// <summary>
/// Replaces the default <see cref="IAuthorizationMiddlewareResultHandler"/>. When the evaluated
/// policy includes a <see cref="FederationEnabledRequirement"/> and the federation feature is
/// disabled in configuration, responds with 404 regardless of whether the auth outcome was a
/// Forbid or Challenge — so the surface is indistinguishable from a server that never shipped
/// federation (no 401/403 leak that the feature exists). All other paths fall through to the
/// default handler.
/// </summary>
public sealed class FederationEnabledAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();
    private readonly IConfigurationManager _configManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationEnabledAuthorizationMiddlewareResultHandler"/> class.
    /// </summary>
    /// <param name="configManager">Instance of the <see cref="IConfigurationManager"/> interface.</param>
    public FederationEnabledAuthorizationMiddlewareResultHandler(IConfigurationManager configManager)
    {
        _configManager = configManager;
    }

    /// <inheritdoc/>
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (policy.Requirements.OfType<FederationEnabledRequirement>().Any()
            && !_configManager.GetFederationConfiguration().Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult).ConfigureAwait(false);
    }
}
