using Jellyfin.Database.Implementations.Entities.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the FederationFollowRequest entity.
/// </summary>
public class FederationFollowRequestConfiguration : IEntityTypeConfiguration<FederationFollowRequest>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FederationFollowRequest> builder)
    {
        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(entity => new { entity.ActorId, entity.Type }).IsUnique();
        builder.HasIndex(entity => entity.Type);
    }
}
