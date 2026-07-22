using MichiChatbot.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MichiChatbot.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="UsageDaily"/> to the <c>usage_daily</c> table. The one table WITHOUT a uuid
/// surrogate key: its natural key (SiteId, Date) is the identity, the upsert conflict target, and
/// the index that serves "this site's usage over a date range" — three jobs, one composite PK.
/// </summary>
public sealed class UsageDailyConfiguration : IEntityTypeConfiguration<UsageDaily>
{
    public void Configure(EntityTypeBuilder<UsageDaily> builder)
    {
        builder.ToTable("usage_daily");

        // SiteId first: queries are always "one site, many days", so the PK's leading column
        // clusters each site's rows together in the index.
        builder.HasKey(u => new { u.SiteId, u.Date });

        // Money -> one jsonb column, same idiom as plans.Price.
        builder.OwnsOne(u => u.EstimatedCost, cost => cost.ToJson());

        builder.HasOne<Site>()
            .WithMany()
            .HasForeignKey(u => u.SiteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
