using Jellyfin.Database.Implementations.Entities.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the FederationFollowing entity.
/// </summary>
public class FederationFollowingConfiguration : IEntityTypeConfiguration<FederationFollowing>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FederationFollowing> builder)
    {
        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId);
        builder.HasIndex(e => e.ActorId).IsUnique();
    }
}
