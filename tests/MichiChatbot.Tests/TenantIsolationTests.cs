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
/// Proves tenant isolation from BOTH sides against a real Postgres database. Each test runs inside a
/// transaction that is rolled back on dispose, so it creates its own throwaway plan/tenants/sites and
/// leaves the shared `chatbot` database untouched.
/// </summary>
public sealed class TenantIsolationTests
{
    // Connection string comes from User Secrets (dev, shared with the Web project's store) or the
    // ConnectionStrings__Chatbot env var (CI) — never hardcoded, so no secret lands in the repo.
    private static readonly string ConnectionString =
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(TenantIsolationTests).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build()
            .GetConnectionString("Chatbot")
        ?? throw new InvalidOperationException(
            "No 'Chatbot' connection string. Set it in User Secrets or the ConnectionStrings__Chatbot env var.");

    private static ChatbotDbContext NewContext(ITenantAccessor accessor) =>
        new(new DbContextOptionsBuilder<ChatbotDbContext>().UseNpgsql(ConnectionString).Options, accessor);

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

    /// <summary>Seeds a rolled-back plan + two tenants and returns their ids.</summary>
    private static async Task<(Guid TenantA, Guid TenantB)> SeedTwoTenantsAsync(ChatbotDbContext db)
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

        var a = new Tenant { Name = "A", Slug = $"a-{Guid.NewGuid():N}", PlanId = plan.Id, Status = TenantStatus.Active };
        var b = new Tenant { Name = "B", Slug = $"b-{Guid.NewGuid():N}", PlanId = plan.Id, Status = TenantStatus.Active };
        db.Tenants.AddRange(a, b);
        await db.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task ReadFilter_hides_other_tenants_sites()
    {
        var tenant = new AmbientTenantAccessor();
        await using var db = NewContext(tenant);
        await using var tx = await db.Database.BeginTransactionAsync();

        var (a, b) = await SeedTwoTenantsAsync(db);

        tenant.Set(a);
        db.Sites.Add(NewSite("a-main"));
        await db.SaveChangesAsync();

        tenant.Set(b);
        db.Sites.Add(NewSite("b-main"));
        await db.SaveChangesAsync();

        // Read as tenant A: the filter must scope SELECTs to A only.
        tenant.Set(a);
        var visible = await db.Sites.AsNoTracking().ToListAsync();

        Assert.Single(visible);
        Assert.Equal("a-main", visible[0].Slug);

        // Sanity: with NO active tenant, the filter matches nothing (safe by default).
        tenant.Set(null);
        Assert.Empty(await db.Sites.AsNoTracking().ToListAsync());

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task WriteInterceptor_rejects_insert_targeting_another_tenant()
    {
        var tenant = new AmbientTenantAccessor();
        await using var db = NewContext(tenant);
        await using var tx = await db.Database.BeginTransactionAsync();

        var (a, b) = await SeedTwoTenantsAsync(db);

        // Active tenant is A, but the row explicitly claims tenant B.
        tenant.Set(a);
        var rogue = NewSite("rogue");
        rogue.TenantId = b;
        db.Sites.Add(rogue);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task WriteInterceptor_stamps_tenant_id_from_the_accessor()
    {
        var tenant = new AmbientTenantAccessor();
        await using var db = NewContext(tenant);
        await using var tx = await db.Database.BeginTransactionAsync();

        var (a, _) = await SeedTwoTenantsAsync(db);

        tenant.Set(a);
        var site = NewSite("unstamped");   // TenantId left as Guid.Empty
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        Assert.Equal(a, site.TenantId);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task WriteInterceptor_refuses_scoped_insert_with_no_active_tenant()
    {
        var tenant = new AmbientTenantAccessor();   // never Set -> no tenant
        await using var db = NewContext(tenant);
        await using var tx = await db.Database.BeginTransactionAsync();

        db.Sites.Add(NewSite("orphan"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        await tx.RollbackAsync();
    }
}
