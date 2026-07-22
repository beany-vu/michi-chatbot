using System.Reflection;
using MichiChatbot.Core.Abstractions;
using MichiChatbot.Core.Entities;
using MichiChatbot.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace MichiChatbot.Infrastructure.Persistence;

/// <summary>
/// The EF Core unit of work for the <c>chatbot</c> database. Tenant isolation is enforced here in one
/// place, both sides keyed off the injected <see cref="ITenantAccessor"/>:
///   • READS  — a global query filter on every ITenantScoped entity (added by reflection in
///              OnModelCreating), so all LINQ is silently scoped to the active tenant.
///   • WRITES — a <see cref="TenantSaveChangesInterceptor"/> added in OnConfiguring, so the same guard
///              travels with the context everywhere it is constructed (web, seeding, tests).
/// </summary>
public sealed class ChatbotDbContext(
    DbContextOptions<ChatbotDbContext> options,
    ITenantAccessor tenantAccessor) : DbContext(options)
{
    private readonly ITenantAccessor _tenant = tenantAccessor;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<UsageDaily> UsageDailies => Set<UsageDaily>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Bind the write-side guard to the SAME accessor as the read filter. Registering it here (not
        // only in DI) means no code path can construct this context without tenant write protection.
        optionsBuilder.AddInterceptors(new TenantSaveChangesInterceptor(_tenant));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Apply the tenant read filter to EVERY entity that implements ITenantScoped, so adding a new
        // scoped entity later needs no extra wiring — it is filtered automatically.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                typeof(ChatbotDbContext)
                    .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [modelBuilder]);
            }
        }
    }

    /// <summary>
    /// Adds <c>WHERE TenantId = @activeTenant</c> to <typeparamref name="TEntity"/>. When the accessor
    /// has no tenant the comparison is against NULL and matches nothing — reads are safe by default.
    /// </summary>
    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => e.TenantId == _tenant.TenantId);
    }
}
