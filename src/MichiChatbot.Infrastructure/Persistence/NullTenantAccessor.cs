using MichiChatbot.Core.Abstractions;

namespace MichiChatbot.Infrastructure.Persistence;

/// <summary>
/// An <see cref="ITenantAccessor"/> that reports NO active tenant. Used where there is no request
/// context: design-time migrations (<c>dotnet ef</c>) and, deliberately, as the "unsafe by default"
/// baseline — with no tenant the read filter matches nothing and the write interceptor refuses to
/// persist tenant-scoped rows. Real request-scoped resolution lives in Web (phase 1/3).
/// </summary>
public sealed class NullTenantAccessor : ITenantAccessor
{
    public static readonly NullTenantAccessor Instance = new();

    public Guid? TenantId => null;
    public Guid? SiteId => null;
}
