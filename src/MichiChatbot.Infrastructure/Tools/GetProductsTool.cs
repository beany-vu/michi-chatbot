using System.Text.Json;
using MichiChatbot.Core.Entities;
using MichiChatbot.Infrastructure.Llm;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>
/// The site's product catalog (mugshot: drinks, seasonal specials, beans, merch). The upstream
/// response carries images/ids/display fields the model doesn't need — trimmed here because every
/// byte of a tool result is paid input tokens on all remaining rounds.
/// </summary>
public sealed class GetProductsTool(IHttpClientFactory httpClientFactory) : IChatTool
{
    public string Code => "get_products";

    public ToolDefinition BuildDefinition(Site site) => new()
    {
        Function = new FunctionDefinition
        {
            Name = Code,
            Description = "List the products the shop sells (drinks, seasonal specials, coffee beans, "
                        + "merchandise) with prices and availability. Use it whenever the customer asks "
                        + "what's on the menu, prices, or whether something is available.",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    category = new
                    {
                        type = "string",
                        description = "Optional category filter, e.g. 'seasonal-drink' or 'coffee-bean' "
                                     + "(matches loosely, so 'coffee' or 'drink' also work). Omit for everything.",
                    },
                },
                required = Array.Empty<string>(),
            },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, Site site, Guid conversationId, CancellationToken ct)
    {
        try
        {
            var http = httpClientFactory.CreateClient(SiteApi.HttpClientName);
            var products = await SiteApi.GetJsonAsync(http, site, "api/products/", ct);

            string? category = arguments.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;

            var trimmed = new List<object>();
            foreach (var p in products.EnumerateArray())
            {
                if (p.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.False)
                    continue;
                var productCategory = p.GetPropertyOrNull("category");
                // Substring match, not exact — the model's category GUESS ("coffee") won't always
                // match the catalog's real slug ("coffee-bean") exactly, and unlike venue_facts'
                // fixed 6-value taxonomy, this list is mugshot's own live, growing catalog, not
                // something to hardcode an enum against. Found via the phase-3 eval harness's
                // tool-argument logging: "coffee" filtered out every real product, silently.
                if (category is not null && (productCategory is null
                        || (!productCategory.Contains(category, StringComparison.OrdinalIgnoreCase)
                            && !category.Contains(productCategory, StringComparison.OrdinalIgnoreCase))))
                    continue;

                trimmed.Add(new
                {
                    name = p.GetPropertyOrNull("name"),
                    description = p.GetPropertyOrNull("description"),
                    category = productCategory,
                    price = p.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Number
                        ? price.GetDecimal()
                        : (decimal?)null,
                    availability = p.GetPropertyOrNull("availability"),
                    featured = p.TryGetProperty("featured", out var f) && f.ValueKind == JsonValueKind.True,
                });
            }

            return JsonSerializer.Serialize(trimmed, SiteApi.Json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return SiteApi.Error($"Product catalog unavailable: {ex.Message}");
        }
    }
}

internal static class JsonElementExtensions
{
    public static string? GetPropertyOrNull(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
