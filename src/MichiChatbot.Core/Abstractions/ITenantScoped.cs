namespace MichiChatbot.Core.Abstractions;

/// <summary>
/// Marker for any entity that belongs to a tenant. The EF global query filter (reads) and the
/// SaveChanges interceptor (writes) both key off this interface to stamp/validate TenantId, so
/// tenant isolation is enforced in ONE place instead of being re-remembered per entity.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
