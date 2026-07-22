using System.Text.Json;
using MichiChatbot.Core.Entities;
using MichiChatbot.Infrastructure.Llm;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>
/// Current weather at the site, from the site's own public API (mugshot: /api/weather/, cached 2h
/// upstream). The API needs a date; the tool supplies today in the SITE's timezone so the model
/// never has to know about timezones.
/// </summary>
public sealed class GetWeatherTool(IHttpClientFactory httpClientFactory) : IChatTool
{
    public string Code => "get_weather";

    public ToolDefinition BuildDefinition(Site site) => new()
    {
        Function = new FunctionDefinition
        {
            Name = Code,
            Description = "Get the current weather at the shop: temperature (°C), feels-like, "
                        + "description, humidity and rain chance. Use it for anything weather-related "
                        + "or when suggesting hot vs iced drinks.",
            Parameters = new { type = "object", properties = new { }, required = Array.Empty<string>() },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, Site site, CancellationToken ct)
    {
        try
        {
            var http = httpClientFactory.CreateClient(SiteApi.HttpClientName);
            var today = SiteApi.SiteNow(site).ToString("yyyy-MM-dd");
            var weather = await SiteApi.GetJsonAsync(http, site, $"api/weather/?date={today}", ct);
            return weather.GetRawText(); // already small: temp/feels_like/description/humidity/rain_chance
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return SiteApi.Error($"Weather service unavailable: {ex.Message}");
        }
    }
}
