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
        // Dev-stage reconcile: while there is no portal to edit sites, the seed is the source of
        // truth for the mugshot site's operational fields, so an existing row is UPDATED to match
        // (BaseUrl/Timezone/EnabledTools/PersonaPrompt). Stops the moment owners edit via portal.
        tenant.Set(mugshot.Id);
        try
        {
            // The site's REAL public production APIs — reachable from dev, so tools return live data.
            const string baseUrl = "https://mugshotmnl.com";
            const string timezone = "Asia/Manila";
            const string persona = "You are Michi, the warm, concise barista assistant for Mugshot Coffee "
                                 + "in Manila. Keep answers short and friendly; suggest drinks when it fits.";
            string[] enabledTools = ["get_products", "get_weather", "get_events", "get_crowdedness", "suggest_drink"];

            var site = await db.Sites.FirstOrDefaultAsync(s => s.Slug == "main", ct);
            if (site is null)
            {
                db.Sites.Add(new Site
                {
                    Slug = "main",
                    Name = "Mugshot Coffee",
                    PublicKey = "pk_live_mugshot_dev",
                    BaseUrl = baseUrl,
                    AllowedOrigins = ["https://mugshotmnl.com", "http://localhost:3000"],
                    Locale = "en-US",
                    Timezone = timezone,
                    Model = "qwen-plus",
                    PersonaPrompt = persona,
                    EnabledTools = enabledTools,
                    Active = true,
                    // TenantId intentionally left unset — the interceptor stamps it from the accessor.
                });
            }
            else
            {
                site.BaseUrl = baseUrl;
                site.Timezone = timezone;
                site.PersonaPrompt = persona;
                site.EnabledTools = enabledTools;
            }
            await db.SaveChangesAsync(ct);
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
