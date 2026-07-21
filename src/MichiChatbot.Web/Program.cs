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

// Liveness + DB reachability in one probe.
app.MapGet("/healthz", async (ChatbotDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "healthy" })
        : Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();
