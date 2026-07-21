namespace MichiChatbot.Core.Abstractions;

/// <summary>
/// The "who is the current tenant?" for one request. Implemented in Web (resolved from the site
/// public key or the signed-in user's claims) and consumed by the DbContext's query filter +
/// SaveChanges interceptor. Nullable because some contexts (startup, seeding, truly anonymous)
/// have no tenant yet — callers that require one must check.
/// </summary>
public interface ITenantAccessor
{
    Guid? TenantId { get; }
    Guid? SiteId { get; }
}
