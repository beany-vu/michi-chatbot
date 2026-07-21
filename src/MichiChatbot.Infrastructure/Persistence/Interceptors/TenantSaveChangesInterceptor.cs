using MichiChatbot.Core.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MichiChatbot.Infrastructure.Persistence.Interceptors;

/// <summary>
/// The WRITE half of tenant isolation. Query filters only guard reads, so without this nothing stops
/// a row being written for another tenant. On every SaveChanges this interceptor, for each
/// ITenantScoped entity:
///   • Added    — stamps TenantId from the accessor (if unset), or rejects a row already targeting a
///                different tenant; refuses entirely when there is no active tenant.
///   • Modified/Deleted — rejects any row whose TenantId isn't the active tenant (defence in depth;
///                the read filter should already have prevented loading it).
/// Keying off the same <see cref="ITenantAccessor"/> as the read filter keeps isolation in ONE place.
/// </summary>
public sealed class TenantSaveChangesInterceptor(ITenantAccessor accessor) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enforce(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Enforce(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Enforce(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<ITenantScoped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (accessor.TenantId is not { } activeTenant)
                        throw new InvalidOperationException(
                            $"Cannot persist {entry.Entity.GetType().Name}: no active tenant on the request.");

                    if (entry.Entity.TenantId == Guid.Empty)
                        entry.Entity.TenantId = activeTenant;                 // stamp
                    else if (entry.Entity.TenantId != activeTenant)
                        throw new InvalidOperationException(
                            $"Cross-tenant write blocked: {entry.Entity.GetType().Name} targets tenant "
                            + $"{entry.Entity.TenantId}, active tenant is {activeTenant}.");
                    break;

                case EntityState.Modified:
                case EntityState.Deleted:
                    if (accessor.TenantId is { } current && entry.Entity.TenantId != current)
                        throw new InvalidOperationException(
                            $"Cross-tenant {entry.State} blocked: {entry.Entity.GetType().Name} belongs to "
                            + $"tenant {entry.Entity.TenantId}, active tenant is {current}.");
                    break;
            }
        }
    }
}
