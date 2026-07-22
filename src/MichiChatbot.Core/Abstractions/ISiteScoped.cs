namespace MichiChatbot.Core.Abstractions;

/// <summary>
/// Marker for entities that belong to one specific SITE. Extends ITenantScoped because everything
/// site-scoped is inside that site's tenant, so these entities automatically get the tenant query
/// filter and write guard; the SaveChanges interceptor additionally stamps/validates SiteId.
/// SiteId is deliberately duplicated onto every such row (rather than reached via joins): in a
/// multi-tenant schema a forgotten join is a data leak, a duplicated column is just bytes.
/// </summary>
public interface ISiteScoped : ITenantScoped
{
    Guid SiteId { get; set; }
}
