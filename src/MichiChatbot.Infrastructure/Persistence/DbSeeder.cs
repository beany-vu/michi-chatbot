using MichiChatbot.Core.Entities;
using MichiChatbot.Core.Enums;
using MichiChatbot.Core.ValueObjects;
using MichiChatbot.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace MichiChatbot.Infrastructure.Persistence;

/// <summary>
/// Idempotent boot-time seed: plans (global reference data), the mugshot tenant, and its first site.
/// Every step checks-then-inserts by natural key (Code / Slug / PublicKey), so running it on every
/// boot is safe. Runs AFTER Database.Migrate() so the schema exists.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(
        ChatbotDbContext db, AmbientTenantAccessor tenant, CancellationToken ct = default)
    {
        var free = await EnsurePlanAsync(db, "free", monthlyTokens: 100_000, maxSites: 1,
            maxMessagesPerDay: 200, price: new Money(0m, "USD"), ct);
        await EnsurePlanAsync(db, "starter", monthlyTokens: 2_000_000, maxSites: 3,
            maxMessagesPerDay: 2_000, price: new Money(29m, "USD"), ct);
        await EnsurePlanAsync(db, "pro", monthlyTokens: 10_000_000, maxSites: 10,
            maxMessagesPerDay: 20_000, price: new Money(99m, "USD"), ct);
        await db.SaveChangesAsync(ct);

        // Mugshot tenant (Tenant is not tenant-scoped, so no ambient tenant needed for this write).
        var mugshot = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == "mugshot", ct);
        if (mugshot is null)
        {
            mugshot = new Tenant
            {
                Name = "Mugshot Coffee",
                Slug = "mugshot",
                PlanId = free.Id,
                Status = TenantStatus.Active,
            };
            db.Tenants.Add(mugshot);
            await db.SaveChangesAsync(ct);
        }

        // Mugshot site IS tenant-scoped: set the ambient tenant so the read filter scopes the existence
        // check AND the write interceptor stamps TenantId. Cleared again afterwards.
        tenant.Set(mugshot.Id);
        try
        {
            var siteExists = await db.Sites.AnyAsync(s => s.Slug == "main", ct);
            if (!siteExists)
            {
                db.Sites.Add(new Site
                {
                    Slug = "main",
                    Name = "Mugshot Coffee",
                    PublicKey = "pk_live_mugshot_dev",
                    BaseUrl = "http://app:3000",
                    AllowedOrigins = ["https://mugshotcoffee.example", "http://localhost:3000"],
                    Locale = "en-US",
                    Timezone = "America/Los_Angeles",
                    Model = "qwen-plus",
                    PersonaPrompt = "You are a warm, concise barista assistant for Mugshot Coffee.",
                    EnabledTools = ["get_products", "get_weather", "get_crowdedness", "suggest_drink"],
                    Active = true,
                    // TenantId intentionally left unset — the interceptor stamps it from the accessor.
                });
                await db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            tenant.Set(null);
        }
    }

    private static async Task<Plan> EnsurePlanAsync(
        ChatbotDbContext db, string code, long monthlyTokens, int maxSites,
        int maxMessagesPerDay, Money price, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Code == code, ct);
        if (plan is not null) return plan;

        plan = new Plan
        {
            Code = code,
            MonthlyTokenQuota = monthlyTokens,
            MaxSites = maxSites,
            MaxMessagesPerDay = maxMessagesPerDay,
            Price = price,
        };
        db.Plans.Add(plan);
        return plan;
    }
}
