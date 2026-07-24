using System.Net.Http.Headers;
using System.Text.Json;
using MichiChatbot.Core.Abstractions;
using MichiChatbot.Infrastructure.Chat;
using MichiChatbot.Infrastructure.Llm;
using MichiChatbot.Infrastructure.Persistence;
using MichiChatbot.Infrastructure.Tenancy;
using MichiChatbot.Infrastructure.Tools;
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

// Tools call each tenant site's PUBLIC APIs (absolute URLs built from site.BaseUrl), so no BaseAddress.
builder.Services.AddHttpClient(SiteApi.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

// The real chat path speaks to the LLM through Microsoft.Extensions.AI (ChatClientFactory builds
// the pipeline: function-invocation loop → logging → OpenAI-compatible transport). The hand-rolled
// loop stays registered for /debug/chat — the raw wire format, kept visible on purpose.
builder.Services.AddSingleton<ChatClientFactory>();
builder.Services.AddScoped<HandRolledToolLoop>();
builder.Services.AddScoped<ChatService>();

// The tool registry: register every tool once; per request, site.EnabledTools picks the subset the
// model actually sees. Adding a tool = one class + one line here.
builder.Services.AddSingleton<IChatTool, GetWeatherTool>();
builder.Services.AddSingleton<IChatTool, GetProductsTool>();
builder.Services.AddSingleton<IChatTool, GetEventsTool>();
builder.Services.AddSingleton<IChatTool, GetCrowdednessTool>();
builder.Services.AddSingleton<IChatTool, SuggestDrinkTool>();
builder.Services.AddSingleton<ToolRegistry>();

var app = builder.Build();

// Bring the schema up to date and seed reference data on every boot (both idempotent).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChatbotDbContext>();
    await db.Database.MigrateAsync();

    var tenant = scope.ServiceProvider.GetRequiredService<AmbientTenantAccessor>();
    await DbSeeder.SeedAsync(db, tenant);
}

// The bare test page (wwwroot/index.html) — a stand-in for the real embeddable widget.
app.UseDefaultFiles();
app.UseStaticFiles();

// Liveness: deliberately checks NOTHING. It may only fail when a restart would
// help; a dependency check here turns any DB outage into a container kill-loop.
app.MapGet("/livez", () => Results.Ok(new { status = "alive" }));

// Readiness: dependency checks live here. 503 means "stop routing to me", never "restart me".
app.MapGet("/readyz", async (ChatbotDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "not ready" }, statusCode: StatusCodes.Status503ServiceUnavailable));

// The real chat endpoint. Anonymous by design (site customers don't log in); the SITE is
// authenticated via its public key header, and everything after that runs tenant+site scoped.
// SSE events: `tool` (a tool was executed), `delta` (answer text), `done` (ids + usage), `error`.
app.MapPost("/chat/stream", async (
    HttpContext http,
    ChatRequest request,
    ChatbotDbContext db,
    AmbientTenantAccessor tenant,
    ChatService chat,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        await http.Response.WriteAsJsonAsync(new { error = "message is required" }, ct);
        return;
    }

    // Site lookup MUST ignore the tenant query filter: this is the bootstrap step that discovers
    // which tenant the request belongs to — before it, the accessor is empty and the filter would
    // hide every row. The public key is the site's credential.
    var siteKey = http.Request.Headers["X-Site-Key"].FirstOrDefault();
    var site = string.IsNullOrEmpty(siteKey)
        ? null
        : await db.Sites.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.PublicKey == siteKey && s.Active, ct);
    if (site is null)
    {
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await http.Response.WriteAsJsonAsync(new { error = "unknown or inactive site key" }, ct);
        return;
    }

    // From here on, every read and write in this request is scoped to this tenant + site.
    tenant.Set(site.TenantId, site.Id);

    // Anonymous visitor identity: client-minted GUID, echoed back in `done` so the widget can
    // store it (localStorage) and send it on every later request.
    var anonId = http.Request.Headers["X-Anon-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(anonId)) anonId = Guid.NewGuid().ToString("N");

    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";

    async Task Emit(string type, object payload)
    {
        await http.Response.WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(payload)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    try
    {
        await chat.StreamTurnAsync(site, request.Message, request.ConversationId, anonId, Emit, ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Client went away mid-stream — nothing to report.
    }
    catch (Exception ex)
    {
        // Headers are already sent (SSE), so errors must travel as an event, not a status code.
        logger.LogError(ex, "chat turn failed for site {Site}", site.Slug);
        await Emit("error", new { message = "Something went wrong. Please try again." });
    }
});

// Throwaway proof-of-mechanism for the hand-rolled tool loop — no site/tenant scoping, no
// persistence, no streaming. Kept as the raw-wire-format playground: paste the returned "messages"
// back in as the next request's "messages" to continue the "conversation" (the API is stateless).
app.MapPost("/debug/chat", async (
    DebugChatRequest request, HandRolledToolLoop loop, IOptions<LlmOptions> llm, CancellationToken ct) =>
{
    var (messages, usage, rounds) = await loop.RunAsync(
        request.Messages, DebugTools.Definitions(), DebugTools.ExecuteAsync,
        llm.Value.Model, onToolExecuted: null, ct);
    return Results.Ok(new DebugChatResponse(messages, usage, rounds));
});

app.Run();

record ChatRequest(string Message, Guid? ConversationId);

record DebugChatRequest(List<WireMessage> Messages);

record DebugChatResponse(List<WireMessage> Messages, Usage Usage, int Rounds);
