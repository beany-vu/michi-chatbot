using MichiChatbot.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MichiChatbot.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Site"/> to the <c>sites</c> table. Site is the first ITenantScoped entity, so the
/// global query filter (in <see cref="ChatbotDbContext"/>) and the write interceptor both apply to it.
/// </summary>
public sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.ToTable("sites");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasValueGenerator<UuidV7ValueGenerator>()
            .ValueGeneratedOnAdd();

        builder.Property(s => s.Name).IsRequired();
        builder.Property(s => s.BaseUrl).IsRequired();
        builder.Property(s => s.Locale).IsRequired();
        builder.Property(s => s.Timezone).IsRequired();
        builder.Property(s => s.Model).IsRequired();
        builder.Property(s => s.PersonaPrompt).IsRequired();

        // Public widget key (pk_live_...): a credential, so unique across ALL sites forever — including
        // inactive ones. A plain unique constraint (not a partial/active-only index) is what a
        // credential wants: it must never be reissued.
        builder.Property(s => s.PublicKey).IsRequired();
        builder.HasIndex(s => s.PublicKey).IsUnique();

        // Slug is unique WITHIN a tenant, not globally — two tenants may both have a "main" site.
        builder.Property(s => s.Slug).IsRequired();
        builder.HasIndex(s => new { s.TenantId, s.Slug }).IsUnique();

        // Both are flat string lists in phase 0 -> native Postgres text[] (queryable, indexable, zero
        // serialization). AllowedOrigins drives the CORS/Origin check; EnabledTools filters the tool
        // registry. (Plan pencilled EnabledTools as jsonb; deferred until a tool needs structured
        // config — YAGNI.)
        builder.Property(s => s.AllowedOrigins).HasColumnType("text[]");
        builder.Property(s => s.EnabledTools).HasColumnType("text[]");

        builder.Property(s => s.Active).HasDefaultValue(true);

        // Site belongs to exactly one Tenant. Restrict: a tenant with live sites can't be deleted out
        // from under them; teardown is an explicit, ordered admin action.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
