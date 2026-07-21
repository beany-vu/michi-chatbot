using MichiChatbot.Core.Abstractions;

namespace MichiChatbot.Infrastructure.Tenancy;

/// <summary>
/// A mutable, request-scoped <see cref="ITenantAccessor"/> — a holder whose value is set once per
/// request (phase 1: from the site public key or the signed-in user's claims). It starts empty, so
/// until something sets it, reads see nothing and tenant-scoped writes are refused — safe by default.
///
/// Phase 0 uses it in exactly one place besides the (empty) request path: the seeder sets it to the
/// tenant being seeded so a Site (ITenantScoped) can be written under the correct tenant.
/// </summary>
public sealed class AmbientTenantAccessor : ITenantAccessor
{
    public Guid? TenantId { get; private set; }
    public Guid? SiteId { get; private set; }

    public void Set(Guid? tenantId, Guid? siteId = null)
    {
        TenantId = tenantId;
        SiteId = siteId;
    }
}
