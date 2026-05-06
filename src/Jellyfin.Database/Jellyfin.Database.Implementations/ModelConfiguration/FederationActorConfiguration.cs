using Jellyfin.Database.Implementations.Entities.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the FederationActor entity.
/// </summary>
public class FederationActorConfiguration : IEntityTypeConfiguration<FederationActor>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FederationActor> builder)
    {
        builder.HasIndex(entity => entity.Url).IsUnique();
    }
}
