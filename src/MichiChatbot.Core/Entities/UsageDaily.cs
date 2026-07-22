using MichiChatbot.Core.Abstractions;
using MichiChatbot.Core.ValueObjects;

namespace MichiChatbot.Core.Entities;

/// <summary>
/// Per-site, per-day usage rollup, upserted by the usage middleware after every LLM turn; drives
/// quota checks and the tenant dashboard. Deliberately has NO surrogate Id: the natural key
/// (SiteId, Date) IS the identity — exactly what the upsert conflicts on — and no other table ever
/// references a usage row, so a uuid would buy nothing.
/// </summary>
public class UsageDaily : ISiteScoped
{
    public Guid SiteId { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>The site-local calendar day this row aggregates (site timezone decides the bucket).</summary>
    public DateOnly Date { get; set; }

    public long TokensIn { get; set; }
    public long TokensOut { get; set; }
    public int MessageCount { get; set; }

    /// <summary>Estimated spend for the day (jsonb). Null when the model has no known price (e.g. LM Studio).</summary>
    public Money? EstimatedCost { get; set; }
}
