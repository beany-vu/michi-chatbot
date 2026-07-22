using MichiChatbot.Core.Abstractions;
using MichiChatbot.Core.Entities;
using MichiChatbot.Core.Enums;
using MichiChatbot.Core.ValueObjects;
using MichiChatbot.Infrastructure.Persistence;
using MichiChatbot.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MichiChatbot.Tests;

/// <summary>
/// The Conversation/Message leak tests owed since the entities were added: the chat endpoint now
/// WRITES these rows, so isolation must be proven one level below sites too — the tenant filter
/// hides other tenants' chat data, and the interceptor stamps/validates SiteId (ISiteScoped).
/// Same pattern as TenantIsolationTests: real Postgres, per-test transaction, rolled back.
/// </summary>
public sealed class SiteIsolationTests
{
    private static readonly string ConnectionString =
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(SiteIsolationTests).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build()
            .GetConnectionString("Chatbot")
        ?? throw new InvalidOperationException(
            "No 'Chatbot' connection string. Set it in User Secrets or the ConnectionStrings__Chatbot env var.");

    private static ChatbotDbContext NewContext(ITenantAccessor accessor) =>
        new(new DbContextOptionsBuilder<ChatbotDbContext>().UseNpgsql(ConnectionString).Options, accessor);

    /// <summary>Seeds two tenants, each with one site, and returns both (tenantId, siteId) pairs.</summary>
    private static async Task<((Guid Tenant, Guid Site) A, (Guid Tenant, Guid Site) B)> SeedAsync(
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

        var tenantA = new Tenant { Name = "A", Slug = $"a-{Guid.NewGuid():N}", PlanId = plan.Id, Status = TenantStatus.Active };
        var tenantB = new Tenant { Name = "B", Slug = $"b-{Guid.NewGuid():N}", PlanId = plan.Id, Status = TenantStatus.Active };
        db.Tenants.AddRange(tenantA, tenantB);
        await db.SaveChangesAsync();

        var siteA = NewSite("a-main");
        tenant.Set(tenantA.Id);
        db.Sites.Add(siteA);
        await db.SaveChangesAsync();

        var siteB = NewSite("b-main");
        tenant.Set(tenantB.Id);
        db.Sites.Add(siteB);
        await db.SaveChangesAsync();

        tenant.Set(null);
        return ((tenantA.Id, siteA.Id), (tenantB.Id, siteB.Id));
    }

    private static Site NewSite(string slug) => new()
    {
        Slug = slug,
        Name = slug,
        PublicKey = $"pk_test_{Guid.NewGuid():N}",
        BaseUrl = "http://example",
        Locale = "en-US",
        Timezone = "UTC",
        Model = "qwen-plus",
        PersonaPrompt = "test persona",
    };

    private static Conversation NewConversation() => new()
    {
        AnonId = Guid.NewGuid().ToString("N"),
        Locale = "en-US",
    };

    [Fact]
    public async Task Conversations_and_messages_are_hidden_from_other_tenants()
    {
        var tenant = new AmbientTenantAccessor();
        await using var db = NewContext(tenant);
        await using var tx = await db.Database.BeginTransactionAsync();

        var (a, b) = await SeedAsync(db, tenant);

        // Tenant A writes a conversation with one message.
        tenant.Set(a.Tenant, a.Site);
        var conversation = NewConversation();
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = "tenant A's private question",
        });
        await db.SaveChangesAsync();

        // Tenant B sees NOTHING — even direct db.Messages queries with no join in sight.
        tenant.Set(b.Tenant, b.Site);
        Assert.Empty(await db.Conversations.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Messages.AsNoTracking().ToListAsync());

        // Tenant A still sees its own.
        tenant.Set(a.Tenant, a.Site);
        Assert.Single(await db.Messages.AsNoTracking().ToListAsync());

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Interceptor_stamps_site_id_on_conversation_and_message()
    {
        var tenant = new AmbientTenantAccessor();
        await using var db = NewContext(tenant);
        await using var tx = await db.Database.BeginTransactionAsync();

        var (a, _) = await SeedAsync(db, tenant);

        tenant.Set(a.Tenant, a.Site);
        var conversation = NewConversation();     // SiteId left as Guid.Empty
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        Assert.Equal(a.Tenant, conversation.TenantId);
        Assert.Equal(a.Site, conversation.SiteId);

        var message = new Message
        {
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = "hello",
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        Assert.Equal(a.Site, message.SiteId);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Interceptor_rejects_message_targeting_another_site()
    {
        var tenant = new AmbientTenantAccessor();
        await using var db = NewContext(tenant);
        await using var tx = await db.Database.BeginTransactionAsync();

        var (a, b) = await SeedAsync(db, tenant);

        tenant.Set(a.Tenant, a.Site);
        var conversation = NewConversation();
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        // Row explicitly claims site B while the active site is A -> cross-site write blocked.
        var rogue = new Message
        {
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = "rogue",
            SiteId = b.Site,
        };
        db.Messages.Add(rogue);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Interceptor_refuses_conversation_with_no_active_site()
    {
        var tenant = new AmbientTenantAccessor();
        await using var db = NewContext(tenant);
        await using var tx = await db.Database.BeginTransactionAsync();

        var (a, _) = await SeedAsync(db, tenant);

        tenant.Set(a.Tenant);                     // tenant set, but NO site
        db.Conversations.Add(NewConversation());

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        await tx.RollbackAsync();
    }
}
