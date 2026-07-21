using MichiChatbot.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MichiChatbot.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Tenant"/> to the <c>tenants</c> table. A Tenant is the TOP of the hierarchy — it is
/// not itself tenant-scoped (it has no TenantId), so it carries no query filter; its child Sites do.
/// </summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasValueGenerator<UuidV7ValueGenerator>()
            .ValueGeneratedOnAdd();

        builder.Property(t => t.Name).IsRequired();

        builder.Property(t => t.Slug).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();

        // Status enum stored as its int value (Active=1, Suspended=2). The explicit numbers you chose
        // make the stored value stable if members are ever reordered. This IS the default mapping;
        // .HasConversion<string>() would trade compactness for readability in raw psql.
        builder.Property(t => t.Status);

        // Let Postgres stamp the creation time so every insert path (seed, app, admin) is consistent.
        builder.Property(t => t.CreatedAt)
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        // Every tenant references a seeded Plan. Restrict: you cannot delete a plan that tenants still
        // point at (reference data outlives no single tenant). No navigation property by design —
        // Core stays a lean POCO graph.
        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(t => t.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
