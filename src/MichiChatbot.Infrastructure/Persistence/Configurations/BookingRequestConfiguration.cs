using MichiChatbot.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MichiChatbot.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="BookingRequest"/> to the <c>booking_requests</c> table. ISiteScoped, so the
/// tenant query filter + write interceptor cover it automatically.
/// </summary>
public sealed class BookingRequestConfiguration : IEntityTypeConfiguration<BookingRequest>
{
    public void Configure(EntityTypeBuilder<BookingRequest> builder)
    {
        builder.ToTable("booking_requests");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasValueGenerator<UuidV7ValueGenerator>()
            .ValueGeneratedOnAdd();

        builder.Property(b => b.ContactName).IsRequired();
        builder.Property(b => b.ContactEmail).IsRequired();

        // Same numeric-enum rule as tenants.Status: reordering members must never change stored meaning.
        builder.Property(b => b.Status);

        builder.Property(b => b.CreatedAt)
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        // Idempotency key, enforced by Postgres, not trusted to the app: the same conversation
        // asking to book the same slot twice must land on the SAME row, not a duplicate.
        builder.HasIndex(b => new { b.SiteId, b.ConversationId, b.Date, b.Start, b.End }).IsUnique();

        // "What's booked on day X" — the hot lookup check_availability and the future portal share.
        builder.HasIndex(b => new { b.SiteId, b.Date });

        // A booking is meaningless without its conversation -> cascade (same rule as Message).
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(b => b.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(b => b.SiteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
