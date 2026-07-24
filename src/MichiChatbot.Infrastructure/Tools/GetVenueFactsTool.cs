using System.Text.Json;
using MichiChatbot.Core.Entities;
using MichiChatbot.Core.Enums;
using MichiChatbot.Infrastructure.Llm;
using MichiChatbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>
/// The platform's own venue_facts table (capacity, pricing, amenities, rules, hours, contact) —
/// SQL-seeded for now, a portal CRUD screen arrives in phase 3. Needs <see cref="ChatbotDbContext"/>,
/// which is Scoped — that's why this tool (and <see cref="CreateBookingRequestTool"/>) is registered
/// Scoped in DI rather than Singleton like the pure-HTTP tools: a Singleton can't safely hold a
/// Scoped dependency, and DbContext resolved through it would be disconnected from this request's
/// ambient tenant anyway. See ToolRegistry's own registration in Program.cs for why it moved too.
/// </summary>
public sealed class GetVenueFactsTool(ChatbotDbContext db) : IChatTool
{
    public string Code => "get_venue_facts";

    public ToolDefinition BuildDefinition(Site site) => new()
    {
        Function = new FunctionDefinition
        {
            Name = Code,
            Description = "Get facts about the venue for rental/event enquiries: capacity, pricing, "
                        + "amenities, rules, hours, or contact info. Use it whenever the customer asks "
                        + "about renting the space, hosting an event, or venue-related policies.",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    category = new
                    {
                        type = "string",
                        @enum = new[] { "capacity", "pricing", "amenities", "rules", "hours", "contact" },
                        description = "Optional category filter. Omit for everything.",
                    },
                },
                required = Array.Empty<string>(),
            },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, Site site, Guid conversationId, CancellationToken ct)
    {
        var categoryFilter = arguments.ValueKind == JsonValueKind.Object
                            && arguments.TryGetProperty("category", out var c)
                            && c.ValueKind == JsonValueKind.String
                            && Enum.TryParse<VenueFactCategory>(c.GetString(), ignoreCase: true, out var parsed)
            ? parsed
            : (VenueFactCategory?)null;

        var facts = await db.VenueFacts
            .AsNoTracking()
            .Where(v => v.SiteId == site.Id)
            .Where(v => categoryFilter == null || v.Category == categoryFilter)
            .ToListAsync(ct);

        var result = facts.Select(f => new
        {
            category = f.Category.ToString().ToLowerInvariant(),
            code = f.Code,
            value = JsonSerializer.Deserialize<JsonElement>(f.Value),
        });

        return JsonSerializer.Serialize(result, SiteApi.Json);
    }
}
