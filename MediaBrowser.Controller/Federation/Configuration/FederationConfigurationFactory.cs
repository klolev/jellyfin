using System.Collections.Generic;
using MediaBrowser.Common.Configuration;

namespace MediaBrowser.Controller.Federation.Configuration;

/// <summary>
/// Defines the <see cref="FederationConfigurationFactory" />.
/// </summary>
public class FederationConfigurationFactory : IConfigurationFactory
{
    /// <summary>
    /// The GetConfigurations.
    /// </summary>
    /// <returns>The <see cref="IEnumerable{ConfigurationStore}"/>.</returns>
    public IEnumerable<ConfigurationStore> GetConfigurations()
    {
        return new[]
        {
            new FederationConfigurationStore()
        };
    }
}
