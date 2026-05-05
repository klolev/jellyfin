using System;
using System.Collections.Generic;

namespace MediaBrowser.Controller.Federation.Configuration;

/// <summary>
/// Validation rules for <see cref="FederationConfiguration"/>. Used by both the store (to reject
/// invalid saves at runtime) and a startup hosted service (to hard-fail the server if an invalid
/// configuration is loaded from disk). Single source of truth for what "valid" means.
/// </summary>
public static class FederationConfigurationValidation
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if federation is enabled with required
    /// fields missing. A disabled configuration is always valid regardless of other fields.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when federation is enabled and required fields are missing.</exception>
    public static void ThrowIfInvalid(FederationConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.Enabled)
        {
            return;
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(config.Hostname))
        {
            missing.Add(nameof(FederationConfiguration.Hostname));
        }

        if (string.IsNullOrWhiteSpace(config.ActorName))
        {
            missing.Add(nameof(FederationConfiguration.ActorName));
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Federation is enabled but required field(s) are empty: {string.Join(", ", missing)}. Either complete the configuration or disable federation.");
        }
    }
}
