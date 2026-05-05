using MediaBrowser.Common.Configuration;

namespace MediaBrowser.Controller.Federation.Configuration;

/// <summary>
/// Defines the <see cref="FederationConfigurationExtensions" />.
/// </summary>
public static class FederationConfigurationExtensions
{
    /// <summary>
    /// Retrieves the ActivityPub configuration.
    /// </summary>
    /// <param name="config">The <see cref="IConfigurationManager"/>.</param>
    /// <returns>The <see cref="FederationConfiguration"/>.</returns>
    public static FederationConfiguration GetFederationConfiguration(this IConfigurationManager config)
    {
        return config.GetConfiguration<FederationConfiguration>(FederationConfigurationStore.StoreKey);
    }
}
