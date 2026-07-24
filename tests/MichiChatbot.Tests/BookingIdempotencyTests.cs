using MichiChatbot.Core.Abstractions;
using MichiChatbot.Core.Entities;
using MichiChatbot.Core.Enums;
using MichiChatbot.Core.ValueObjects;
using MichiChatbot.Infrastructure.Persistence;
using MichiChatbot.Infrastructure.Tenancy;
using MichiChatbot.Infrastructure.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MichiChatbot.Tests;

/// <summary>
/// Plan.md's phase-2 verification: "idempotency (same request twice -> one event)". The "one
/// event" outcome only happens if the DB half never creates a second row for the same slot, so
/// that's what this proves — <see cref="CreateBookingRequestTool.FindOrCreateAsync"/> in isolation,
/// no Google Calendar or site API involved. Same real-Postgres, rolled-back-transaction pattern as
/// SiteIsolationTests.
/// </summary>
public sealed class BookingIdempotencyTests
{
    private static readonly string ConnectionString =
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(BookingIdempotencyTests).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build()
            .GetConnectionString("Chatbot")
        ?? throw new InvalidOperationException(
            "No 'Chatbot' connection string. Set it in User Secrets or the ConnectionStrings__Chatbot env var.");

    private static ChatbotDbContext NewContext(ITenantAccessor accessor) =>
        new(new DbContextOptionsBuilder<ChatbotDbContext>().UseNpgsql(ConnectionString).Options, accessor);

    private static async Task<(Guid TenantId, Guid SiteId, Guid ConversationId)> SeedAsync(
        ChatbotDbContext db, AmbientTenantAccessor tenant)
    {
        var plan = new Plan
        {
            Code = $"test-{Guid.NewGuid():N}",
            MonthlyTokenQuota = 1,
            MaxSites = 1,
            MaxMessagesPerDay = 1,
            Price = new Money(0m, "USD"),
        };
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        var t = new Tenant { Name = "T", Slug = $"t-{Guid.NewGuid():N}", PlanId = plan.Id, Status = TenantStatus.Active };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();

        tenant.Set(t.Id);

        var site = new Site
        {
            Slug = "main",
            Name = "Test Site",
            PublicKey = $"pk_test_{Guid.NewGuid():N}",
            BaseUrl = "http://example",
            Locale = "en-US",
            Timezone = "Asia/Manila",
            Model = "qwen-plus",
            PersonaPrompt = "test persona",
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        tenant.Set(t.Id, site.Id);

        var conversation = new Conversation { AnonId = Guid.NewGuid().ToString("N"), Locale = "en-US" };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        return (t.Id, site.Id, conversation.Id);
    }

    [Fact]
    public async Task Same_slot_requested_twice_in_one_conversation_yields_one_row()
    {
        var tenant = new AmbientTenantAccessor();
        await using var db = NewContext(tenant);
        await using var tx = await db.Database.BeginTransactionAsync();

        var (tenantId, siteId, conversationId) = await SeedAsync(db, tenant);
        tenant.Set(tenantId, siteId);
        var site = await db.Sites.FirstAsync(s => s.Id == siteId);

        var date = new DateOnly(2026, 8, 1);
        var start = new TimeOnly(14, 0);
        var end = new TimeOnly(16, 0);

        var (first, firstIsNew) = await CreateBookingRequestTool.FindOrCreateAsync(
            db, site, conversationId, date, start, end, guestCount: 10,
            contactName: "Jamie", contactEmail: "jamie@example.com", contactPhone: null, message: null,
            CancellationToken.None);

        var (second, secondIsNew) = await CreateBookingRequestTool.FindOrCreateAsync(
            db, site, conversationId, date, start, end, guestCount: 10,
            contactName: "Jamie", contactEmail: "jamie@example.com", contactPhone: null, message: null,
            CancellationToken.None);

        Assert.True(firstIsNew);
        Assert.False(secondIsNew);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(await db.BookingRequests.AsNoTracking().ToListAsync());

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Different_slot_same_conversation_yields_a_second_row()
    {
        var tenant = new AmbientTenantAccessor();
        await using var db = NewContext(tenant);
        await using var tx = await db.Database.BeginTransactionAsync();

        var (tenantId, siteId, conversationId) = await SeedAsync(db, tenant);
        tenant.Set(tenantId, siteId);
        var site = await db.Sites.FirstAsync(s => s.Id == siteId);

        var date = new DateOnly(2026, 8, 1);

        await CreateBookingRequestTool.FindOrCreateAsync(
            db, site, conversationId, date, new TimeOnly(14, 0), new TimeOnly(16, 0), 10,
            "Jamie", "jamie@example.com", null, null, CancellationToken.None);

        var (_, secondIsNew) = await CreateBookingRequestTool.FindOrCreateAsync(
            db, site, conversationId, date, new TimeOnly(18, 0), new TimeOnly(20, 0), 10,
            "Jamie", "jamie@example.com", null, null, CancellationToken.None);

        Assert.True(secondIsNew);
        Assert.Equal(2, await db.BookingRequests.AsNoTracking().CountAsync());

        await tx.RollbackAsync();
    }
}
