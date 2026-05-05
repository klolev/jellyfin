using Microsoft.AspNetCore.Authorization;

namespace Jellyfin.Api.Auth.FederationEnabledPolicy;

/// <summary>
/// Authorization requirement asserting that the federation feature is enabled in server
/// configuration. Failures are mapped to 404 by
/// <see cref="FederationEnabledAuthorizationMiddlewareResultHandler"/> rather than the default 403
/// so the federation surface is hidden on instances where the feature is off.
/// </summary>
public class FederationEnabledRequirement : IAuthorizationRequirement
{
}
