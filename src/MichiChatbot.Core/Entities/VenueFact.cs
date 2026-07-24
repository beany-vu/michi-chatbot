using MichiChatbot.Core.Abstractions;
using MichiChatbot.Core.Enums;

namespace MichiChatbot.Core.Entities;

/// <summary>
/// One fact about a venue the bot can answer from (capacity, pricing, amenities, rules, hours,
/// contact) instead of guessing. SQL-seeded for phase 2; a portal CRUD screen arrives in phase 3.
/// Value is jsonb so a fact can be a single value or a small structured shape (e.g. hours per day)
/// without a schema change per category.
/// </summary>
public class VenueFact : ISiteScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }

    public VenueFactCategory Category { get; set; }

    /// <summary>Stable identifier within a site, e.g. "max-guests", "hourly-rate", "wifi".</summary>
    public required string Code { get; set; }

    /// <summary>jsonb — a scalar or small object, whatever the fact needs.</summary>
    public required string Value { get; set; }
}
