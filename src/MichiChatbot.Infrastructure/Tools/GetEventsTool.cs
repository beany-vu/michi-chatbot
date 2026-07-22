using System.Text.Json;
using MichiChatbot.Core.Entities;
using MichiChatbot.Infrastructure.Llm;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>Upcoming events at the venue (mugshot: art nights, workshops), trimmed like products.</summary>
public sealed class GetEventsTool(IHttpClientFactory httpClientFactory) : IChatTool
{
    public string Code => "get_events";

    public ToolDefinition BuildDefinition(Site site) => new()
    {
        Function = new FunctionDefinition
        {
            Name = Code,
            Description = "List upcoming events at the shop (art sessions, workshops, gatherings) with "
                        + "dates, times and capacity. Use it when the customer asks what's happening, "
                        + "about a specific event, or about visiting on a particular day.",
            Parameters = new { type = "object", properties = new { }, required = Array.Empty<string>() },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, Site site, CancellationToken ct)
    {
        try
        {
            var http = httpClientFactory.CreateClient(SiteApi.HttpClientName);
            var events = await SiteApi.GetJsonAsync(http, site, "api/events/?type=upcoming", ct);

            var trimmed = new List<object>();
            foreach (var e in events.EnumerateArray())
            {
                trimmed.Add(new
                {
                    title = e.GetPropertyOrNull("title"),
                    description = e.GetPropertyOrNull("description"),
                    date = e.GetPropertyOrNull("date"),
                    endDate = e.GetPropertyOrNull("endDate"),
                    startTime = e.GetPropertyOrNull("startTime"),
                    endTime = e.GetPropertyOrNull("endTime"),
                    type = e.GetPropertyOrNull("type"),
                    capacity = e.TryGetProperty("capacity", out var cap) && cap.ValueKind == JsonValueKind.Number
                        ? cap.GetInt32()
                        : (int?)null,
                });
            }

            return JsonSerializer.Serialize(trimmed, SiteApi.Json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return SiteApi.Error($"Events service unavailable: {ex.Message}");
        }
    }
}
