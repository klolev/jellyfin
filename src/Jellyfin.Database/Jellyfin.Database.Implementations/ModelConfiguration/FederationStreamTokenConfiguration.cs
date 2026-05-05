using Jellyfin.Database.Implementations.Entities.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the FederationStreamToken entity.
/// </summary>
public class FederationStreamTokenConfiguration : IEntityTypeConfiguration<FederationStreamToken>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FederationStreamToken> builder)
    {
        builder.HasOne(e => e.Follower)
            .WithMany()
            .HasForeignKey(e => e.FollowerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.TokenHash).IsUnique();
        builder.HasIndex(e => e.ExpiresAt);
    }
}
