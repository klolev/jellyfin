using Jellyfin.Database.Implementations.Entities.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the FederationIngestedItem entity.
/// </summary>
public class FederationIngestedItemConfiguration : IEntityTypeConfiguration<FederationIngestedItem>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FederationIngestedItem> builder)
    {
        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.ActorId, e.SourceId }).IsUnique();
        builder.HasIndex(e => e.BaseItemId);
    }
}
