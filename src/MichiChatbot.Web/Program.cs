using MichiChatbot.Core.Abstractions;
using MichiChatbot.Infrastructure.Persistence;
using MichiChatbot.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Tenant accessor: one mutable holder per request, exposed under both its concrete type (so the
// seeder can Set it) and the ITenantAccessor interface (what the DbContext depends on).
builder.Services.AddScoped<AmbientTenantAccessor>();
builder.Services.AddScoped<ITenantAccessor>(sp => sp.GetRequiredService<AmbientTenantAccessor>());

builder.Services.AddDbContext<ChatbotDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Chatbot")));

var app = builder.Build();

// Bring the schema up to date and seed reference data on every boot (both idempotent).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatbotDbContext>();
    await db.Database.MigrateAsync();

    var tenant = scope.ServiceProvider.GetRequiredService<AmbientTenantAccessor>();
    await DbSeeder.SeedAsync(db, tenant);
}

// Liveness: deliberately checks NOTHING. It may only fail when a restart would
// help; a dependency check here turns any DB outage into a container kill-loop.
app.MapGet("/livez", () => Results.Ok(new { status = "alive" }));

// Readiness: dependency checks live here. 503 means "stop routing to me", never "restart me".
app.MapGet("/readyz", async (ChatbotDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "not ready" }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();
