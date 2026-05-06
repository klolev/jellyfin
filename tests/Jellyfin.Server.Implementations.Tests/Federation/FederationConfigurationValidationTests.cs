using System;
using MediaBrowser.Controller.Federation.Configuration;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Federation;

/// <summary>
/// Unit tests for <see cref="FederationConfigurationValidation.ThrowIfInvalid"/>. This is the
/// single source of truth for what "valid" means — used by both the startup validator and the
/// <c>IValidatingConfiguration</c> hook on <see cref="FederationConfigurationStore"/>.
/// </summary>
public class FederationConfigurationValidationTests
{
    [Fact]
    public void Disabled_Default_IsValid()
    {
        FederationConfigurationValidation.ThrowIfInvalid(new FederationConfiguration());
    }

    [Fact]
    public void Disabled_MissingFields_IsValid()
    {
        // Disabled config: even empty strings are allowed. This is the upgrade-from-pre-federation
        // path — an install that never touched federation has all defaults and must still boot.
        FederationConfigurationValidation.ThrowIfInvalid(new FederationConfiguration
        {
            Enabled = false,
            Hostname = string.Empty,
            ActorName = string.Empty
        });
    }

    [Fact]
    public void Enabled_AllRequiredFields_IsValid()
    {
        FederationConfigurationValidation.ThrowIfInvalid(new FederationConfiguration
        {
            Enabled = true,
            Hostname = "example.com",
            ActorName = "jellyfin"
        });
    }

    [Fact]
    public void Enabled_MissingHostname_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FederationConfigurationValidation.ThrowIfInvalid(new FederationConfiguration
            {
                Enabled = true,
                Hostname = string.Empty,
                ActorName = "jellyfin"
            }));

        Assert.Contains(nameof(FederationConfiguration.Hostname), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_MissingActorName_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FederationConfigurationValidation.ThrowIfInvalid(new FederationConfiguration
            {
                Enabled = true,
                Hostname = "example.com",
                ActorName = string.Empty
            }));

        Assert.Contains(nameof(FederationConfiguration.ActorName), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_MissingBothRequiredFields_ThrowsWithBothNames()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FederationConfigurationValidation.ThrowIfInvalid(new FederationConfiguration
            {
                Enabled = true,
                Hostname = string.Empty,
                ActorName = string.Empty
            }));

        Assert.Contains(nameof(FederationConfiguration.Hostname), ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(FederationConfiguration.ActorName), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_WhitespaceOnlyHostname_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FederationConfigurationValidation.ThrowIfInvalid(new FederationConfiguration
            {
                Enabled = true,
                Hostname = "   ",
                ActorName = "jellyfin"
            }));
    }

    [Fact]
    public void Enabled_KeypairMissing_IsValid()
    {
        // Keypair fields are not required — signing service generates them lazily on first use.
        FederationConfigurationValidation.ThrowIfInvalid(new FederationConfiguration
        {
            Enabled = true,
            Hostname = "example.com",
            ActorName = "jellyfin",
            PublicKey = string.Empty,
            PrivateKey = string.Empty
        });
    }

    [Fact]
    public void NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => FederationConfigurationValidation.ThrowIfInvalid(null!));
    }

    [Fact]
    public void Store_ValidateWrapper_DelegatesToSharedRule()
    {
        var store = new FederationConfigurationStore();

        // Happy path: no throw.
        store.Validate(
            new FederationConfiguration(),
            new FederationConfiguration
            {
                Enabled = true,
                Hostname = "example.com",
                ActorName = "jellyfin"
            });

        // Sad path: bubble up the same exception the standalone rule raises.
        Assert.Throws<InvalidOperationException>(() =>
            store.Validate(
                new FederationConfiguration(),
                new FederationConfiguration
                {
                    Enabled = true,
                    Hostname = string.Empty,
                    ActorName = "jellyfin"
                }));
    }

    [Fact]
    public void Store_ValidateWrapper_NullNewConfig_Throws()
    {
        var store = new FederationConfigurationStore();
        Assert.Throws<ArgumentNullException>(() => store.Validate(new FederationConfiguration(), null!));
    }
}
