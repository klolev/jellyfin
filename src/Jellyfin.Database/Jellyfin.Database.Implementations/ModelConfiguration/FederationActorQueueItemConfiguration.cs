using Jellyfin.Database.Implementations.Entities.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the <see cref="FederationActorQueueItem"/> entity.
/// </summary>
public class FederationActorQueueItemConfiguration : IEntityTypeConfiguration<FederationActorQueueItem>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FederationActorQueueItem> builder)
    {
        builder.HasOne(e => e.Queue)
            .WithMany()
            .HasForeignKey(e => e.QueueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.QueueId, e.DateCreated });
    }
}
