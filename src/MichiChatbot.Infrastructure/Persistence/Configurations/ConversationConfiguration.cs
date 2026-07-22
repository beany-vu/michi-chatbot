using MichiChatbot.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MichiChatbot.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Conversation"/> to the <c>conversations</c> table. ISiteScoped, so the tenant
/// query filter + write interceptor cover it automatically.
/// </summary>
public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasValueGenerator<UuidV7ValueGenerator>()
            .ValueGeneratedOnAdd();

        builder.Property(c => c.Locale).IsRequired();

        // Postgres stamps both on insert (same idiom as tenants.CreatedAt); the app only ever
        // UPDATEs LastMessageAt afterwards.
        builder.Property(c => c.StartedAt)
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();
        builder.Property(c => c.LastMessageAt)
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        // "Recent conversations for this site" is the hot list query (widget history, portal browser).
        builder.HasIndex(c => new { c.SiteId, c.LastMessageAt });

        // Anon history lookup: "conversations for this anon visitor on this site".
        builder.HasIndex(c => new { c.SiteId, c.AnonId });

        // Restrict, like site->tenant: deleting a site with conversations is an explicit,
        // ordered teardown, never an accidental cascade of user data.
        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(c => c.SiteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
