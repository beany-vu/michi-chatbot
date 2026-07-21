using MichiChatbot.Core.Enums;

namespace MichiChatbot.Core.Entities;

/// <summary>
/// Top of the hierarchy: Tenant -> Sites -> everything else.
/// Plain domain class (POCO) — no EF/ASP.NET here; Infrastructure maps it to the `tenants` table.
/// Billing fields (BillingEmail, StripeCustomerId) are deferred to the billing phase (see plan.md).
/// </summary>
public class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    // FK to Plan. Navigation property added when the relationship is configured (Infrastructure).
    public Guid PlanId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
