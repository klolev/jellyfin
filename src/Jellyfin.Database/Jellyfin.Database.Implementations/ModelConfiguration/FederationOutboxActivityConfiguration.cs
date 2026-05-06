using Jellyfin.Database.Implementations.Entities.Federation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the FederationOutboxActivity entity.
/// </summary>
public class FederationOutboxActivityConfiguration : IEntityTypeConfiguration<FederationOutboxActivity>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<FederationOutboxActivity> builder)
    {
        builder.HasIndex(e => e.DateCreated);
    }
}
