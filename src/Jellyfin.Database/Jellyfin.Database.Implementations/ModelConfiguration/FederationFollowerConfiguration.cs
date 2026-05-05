using Jellyfin.Database.Implementations.Entities.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the FederationFollower entity.
/// </summary>
public class FederationFollowerConfiguration : IEntityTypeConfiguration<FederationFollower>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FederationFollower> builder)
    {
        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId);
        builder.HasIndex(e => e.ActorId).IsUnique();
    }
}
