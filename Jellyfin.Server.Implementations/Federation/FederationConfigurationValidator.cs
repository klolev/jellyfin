using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Server.Implementations.Federation;

/// <summary>
/// Startup validator for the federation configuration. On <c>StartAsync</c> reads the persisted
/// configuration and throws via <see cref="FederationConfigurationValidation.ThrowIfInvalid"/> if
/// federation is enabled with required fields missing, hard-failing server startup. Runtime saves
/// are caught separately by <see cref="FederationConfigurationStore"/> implementing
/// <c>IValidatingConfiguration</c>.
/// </summary>
public sealed class FederationConfigurationValidator : IHostedService
{
    private readonly IConfigurationManager _configManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationConfigurationValidator"/> class.
    /// </summary>
    /// <param name="configManager">Instance of the <see cref="IConfigurationManager"/> interface.</param>
    public FederationConfigurationValidator(IConfigurationManager configManager)
    {
        _configManager = configManager;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        FederationConfigurationValidation.ThrowIfInvalid(_configManager.GetFederationConfiguration());
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
