using MichiChatbot.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MichiChatbot.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Plan"/> to the <c>plans</c> table. Plans are global reference data (no tenant),
/// so this entity is NOT ITenantScoped and gets no query filter.
/// </summary>
public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasValueGenerator<UuidV7ValueGenerator>()
            .ValueGeneratedOnAdd();

        // Canonical code ("free"/"starter"/"pro"), unique forever — it is how tenants reference a plan.
        builder.Property(p => p.Code).IsRequired();
        builder.HasIndex(p => p.Code).IsUnique();

        // Money is a value object; store it as one jsonb column ({ "Amount": .., "Currency": ".." })
        // rather than two scalar columns — it always travels together and is never queried piecewise.
        builder.OwnsOne(p => p.Price, price => price.ToJson());
    }
}
