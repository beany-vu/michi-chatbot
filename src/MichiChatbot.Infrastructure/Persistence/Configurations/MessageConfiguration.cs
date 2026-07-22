using MichiChatbot.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MichiChatbot.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Message"/> to the <c>messages</c> table — the highest-volume table in the system,
/// so its two indexes are chosen for the two real access paths (per-conversation replay, time-window
/// analytics) and nothing else.
/// </summary>
public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasValueGenerator<UuidV7ValueGenerator>()
            .ValueGeneratedOnAdd();

        builder.Property(m => m.Content).IsRequired();

        // Raw tool-call payload as jsonb: written once, queried ad hoc (analytics, debugging) —
        // never joined or filtered in hot paths, so no structured mapping needed.
        builder.Property(m => m.ToolCalls).HasColumnType("jsonb");

        builder.Property(m => m.CreatedAt)
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        // Conversation replay: "all messages of conversation X in order". Id is uuid v7 =
        // time-ordered, so (ConversationId, Id) serves both the filter and the ORDER BY.
        builder.HasIndex(m => new { m.ConversationId, m.Id });

        // Time-window queries (usage rollups, analytics) scan by creation time across conversations.
        builder.HasIndex(m => m.CreatedAt);

        // A message is meaningless without its conversation -> cascade (locked phase-0 decision).
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // SiteId is denormalized for isolation (see ISiteScoped); the FK keeps it honest.
        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(m => m.SiteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
