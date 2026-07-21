using MichiChatbot.Core.ValueObjects;

namespace MichiChatbot.Core.Entities;

/// <summary>
/// A subscription plan (seeded, global — not owned by any tenant). Quotas are stored as DATA, not
/// hardcoded, so billing can bolt on later without code changes. Tenants reference a plan by PlanId.
/// </summary>
public class Plan
{
    public Guid Id { get; set; }

    /// <summary>Canonical code, never a display string: "free" | "starter" | "pro".</summary>
    public required string Code { get; set; }

    public long MonthlyTokenQuota { get; set; }
    public int MaxSites { get; set; }
    public int MaxMessagesPerDay { get; set; }

    /// <summary>Price as {amount, currency}; mapped to a jsonb column in Infrastructure.</summary>
    public required Money Price { get; set; }
}
