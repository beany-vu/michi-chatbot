using System.Net.Http.Headers;
using MichiChatbot.Core.Abstractions;
using MichiChatbot.Infrastructure.Llm;
using MichiChatbot.Infrastructure.Persistence;
using MichiChatbot.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Tenant accessor: one mutable holder per request, exposed under both its concrete type (so the
// seeder can Set it) and the ITenantAccessor interface (what the DbContext depends on).
builder.Services.AddScoped<AmbientTenantAccessor>();
builder.Services.AddScoped<ITenantAccessor>(sp => sp.GetRequiredService<AmbientTenantAccessor>());

builder.Services.AddDbContext<ChatbotDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Chatbot")));

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));

builder.Services.AddHttpClient("Llm", (sp, client) =>
{
    var llm = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    // HttpClient combines a relative request URI against BaseAddress by replacing the LAST path
    // segment unless BaseAddress ends in '/' — without the trailing slash, ".../v1" + "chat/completions"
    // silently resolves to ".../chat/completions" (the "v1" gets dropped, not appended).
    var baseUrl = llm.BaseUrl.EndsWith('/') ? llm.BaseUrl : llm.BaseUrl + "/";
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", llm.ApiKey);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<HandRolledToolLoop>();

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

// Throwaway proof-of-mechanism for the hand-rolled tool loop — no site/tenant scoping, no
// persistence, no streaming. Gets replaced by the real /chat/stream endpoint next. The request/
// response shape IS the raw OpenAI-compatible wire format: paste the returned "messages" back in
// as the next request's "messages" to continue the "conversation" (the API itself is stateless).
app.MapPost("/debug/chat", async (DebugChatRequest request, HandRolledToolLoop loop, CancellationToken ct) =>
{
    var (messages, usage, rounds) = await loop.RunAsync(request.Messages, ct);
    return Results.Ok(new DebugChatResponse(messages, usage, rounds));
});

app.Run();

record DebugChatRequest(List<WireMessage> Messages);

record DebugChatResponse(List<WireMessage> Messages, Usage Usage, int Rounds);
