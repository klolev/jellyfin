using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using Microsoft.AspNetCore.Authorization;

namespace Jellyfin.Api.Auth.FederationEnabledPolicy;

/// <summary>
/// Authorization handler for <see cref="FederationEnabledRequirement"/>. Succeeds when
/// <see cref="FederationConfiguration.Enabled"/> is <c>true</c>; otherwise leaves the requirement
/// failed so the middleware result handler can map it to 404.
/// </summary>
public class FederationEnabledHandler : AuthorizationHandler<FederationEnabledRequirement>
{
    private readonly IConfigurationManager _configManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationEnabledHandler"/> class.
    /// </summary>
    /// <param name="configManager">Instance of the <see cref="IConfigurationManager"/> interface.</param>
    public FederationEnabledHandler(IConfigurationManager configManager)
    {
        _configManager = configManager;
    }

    /// <inheritdoc/>
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, FederationEnabledRequirement requirement)
    {
        var config = _configManager.GetFederationConfiguration();
        if (config.Enabled)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
