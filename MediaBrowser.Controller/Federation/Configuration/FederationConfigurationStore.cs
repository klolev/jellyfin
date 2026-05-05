using System;
using MediaBrowser.Common.Configuration;

namespace MediaBrowser.Controller.Federation.Configuration;

/// <summary>
/// A configuration that stores federation settings.
/// </summary>
public class FederationConfigurationStore : ConfigurationStore, IValidatingConfiguration
{
    /// <summary>
    /// The name of the configuration in the storage.
    /// </summary>
    public const string StoreKey = "federation";

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationConfigurationStore"/> class.
    /// </summary>
    public FederationConfigurationStore()
    {
        ConfigurationType = typeof(FederationConfiguration);
        Key = StoreKey;
    }

    /// <inheritdoc/>
    public void Validate(object oldConfig, object newConfig)
    {
        ArgumentNullException.ThrowIfNull(newConfig);
        FederationConfigurationValidation.ThrowIfInvalid((FederationConfiguration)newConfig);
    }
}
