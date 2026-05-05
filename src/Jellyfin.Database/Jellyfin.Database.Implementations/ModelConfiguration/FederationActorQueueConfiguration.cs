using Jellyfin.Database.Implementations.Entities.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the <see cref="FederationActorQueue"/> entity.
/// </summary>
public class FederationActorQueueConfiguration : IEntityTypeConfiguration<FederationActorQueue>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FederationActorQueue> builder)
    {
        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.ActorId).IsUnique();
        builder.HasIndex(e => e.NextAttemptAt);
    }
}
