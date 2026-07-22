using System.Text.Json;
using MichiChatbot.Core.Entities;
using MichiChatbot.Infrastructure.Llm;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>
/// COMPOSITE tool — the cost-control pattern from the plan. Instead of letting the model chain
/// get_weather → get_products → get_crowdedness (three rounds, three re-feeds of growing context),
/// this fans out server-side in ONE round and returns a structured shortlist the model only has to
/// phrase. The weather→drink-style mapping mirrors mugshot's weatherTag enum
/// (hot|rainy|cool|humid|any); per-site owner-entered mappings arrive with the portal (phase 4).
/// </summary>
public sealed class SuggestDrinkTool(IHttpClientFactory httpClientFactory) : IChatTool
{
    public string Code => "suggest_drink";

    public ToolDefinition BuildDefinition(Site site) => new()
    {
        Function = new FunctionDefinition
        {
            Name = Code,
            Description = "Suggest what to drink right now. Combines live weather, the current menu and "
                        + "how busy the shop is, and returns a ready-made shortlist. ALWAYS prefer this "
                        + "over calling get_weather/get_products separately when the customer asks for a "
                        + "recommendation ('what should I get?', 'something for this weather').",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    occasion = new
                    {
                        type = "string",
                        description = "Optional context from the customer, e.g. 'working late', 'first visit', 'date'.",
                    },
                },
                required = Array.Empty<string>(),
            },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, Site site, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient(SiteApi.HttpClientName);

        // Server-side fan-out, concurrently — the whole point of the composite.
        var today = SiteApi.SiteNow(site).ToString("yyyy-MM-dd");
        var weatherTask = TryGetAsync(http, site, $"api/weather/?date={today}", ct);
        var productsTask = TryGetAsync(http, site, "api/products/", ct);
        await Task.WhenAll(weatherTask, productsTask);

        var weather = weatherTask.Result;
        var products = productsTask.Result;

        var tag = WeatherTag(weather);

        var drinks = new List<object>();
        if (products is { ValueKind: JsonValueKind.Array } list)
        {
            foreach (var p in list.EnumerateArray())
            {
                if (p.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.False)
                    continue;
                if (!string.Equals(p.GetPropertyOrNull("availability"), "available", StringComparison.OrdinalIgnoreCase))
                    continue;
                var category = p.GetPropertyOrNull("category") ?? "";
                if (!category.Contains("drink", StringComparison.OrdinalIgnoreCase)
                    && !category.Contains("coffee", StringComparison.OrdinalIgnoreCase))
                    continue;

                drinks.Add(new
                {
                    name = p.GetPropertyOrNull("name"),
                    description = p.GetPropertyOrNull("description"),
                    category,
                    matchesWeather = MatchesTag(p, tag),
                });
            }
        }

        var crowd = await new GetCrowdednessTool().ExecuteAsync(default, site, ct);

        var occasion = arguments.ValueKind == JsonValueKind.Object
                       && arguments.TryGetProperty("occasion", out var o)
                       && o.ValueKind == JsonValueKind.String
            ? o.GetString()
            : null;

        return JsonSerializer.Serialize(new
        {
            weatherTag = tag,
            weather = weather is { ValueKind: JsonValueKind.Object } w ? (object)w : "unavailable",
            crowdedness = JsonSerializer.Deserialize<JsonElement>(crowd),
            occasion,
            candidates = drinks,
            guidance = "Pick 1-2 candidates (prefer matchesWeather=true), mention the weather reason "
                     + "briefly, and mention crowdedness only if relevant.",
        }, SiteApi.Json);
    }

    /// <summary>Mirrors mugshot's canonical weatherTag enum: hot | rainy | cool | humid | any.</summary>
    private static string WeatherTag(JsonElement? weather)
    {
        if (weather is not { ValueKind: JsonValueKind.Object } w) return "any";

        double Get(string name) =>
            w.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

        if (Get("rain_chance") >= 50) return "rainy";
        if (Get("feels_like") >= 33 || Get("temp") >= 31) return "hot";
        if (Get("temp") <= 22) return "cool";
        if (Get("humidity") >= 85) return "humid";
        return "any";
    }

    /// <summary>
    /// Keyword heuristic until owners enter per-drink weather tags in the portal: iced/cold drinks
    /// match hot|humid weather, warm/hot drinks match rainy|cool weather.
    /// </summary>
    private static bool MatchesTag(JsonElement product, string tag)
    {
        var text = $"{product.GetPropertyOrNull("name")} {product.GetPropertyOrNull("description")}"
            .ToLowerInvariant();
        var iced = text.Contains("iced") || text.Contains("cold") || text.Contains("frappe");
        return tag switch
        {
            "hot" or "humid" => iced,
            "rainy" or "cool" => !iced,
            _ => true,
        };
    }

    private static async Task<JsonElement?> TryGetAsync(
        HttpClient http, Site site, string pathAndQuery, CancellationToken ct)
    {
        try
        {
            return await SiteApi.GetJsonAsync(http, site, pathAndQuery, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null; // degrade gracefully: a shortlist without weather beats an error
        }
    }
}
