using MichiChatbot.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MichiChatbot.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="VenueFact"/> to the <c>venue_facts</c> table. ISiteScoped, so the tenant query
/// filter + write interceptor cover it automatically.
/// </summary>
public sealed class VenueFactConfiguration : IEntityTypeConfiguration<VenueFact>
{
    public void Configure(EntityTypeBuilder<VenueFact> builder)
    {
        builder.ToTable("venue_facts");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasValueGenerator<UuidV7ValueGenerator>()
            .ValueGeneratedOnAdd();

        builder.Property(v => v.Code).IsRequired();
        builder.Property(v => v.Value).IsRequired().HasColumnType("jsonb");

        // One fact per (site, code) — the natural key the seeder and get_venue_facts both key off.
        builder.HasIndex(v => new { v.SiteId, v.Code }).IsUnique();

        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(v => v.SiteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
