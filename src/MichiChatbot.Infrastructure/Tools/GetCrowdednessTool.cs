using System.Text.Json;
using MichiChatbot.Core.Entities;
using MichiChatbot.Infrastructure.Llm;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>
/// How busy the shop is right now, from a popular-times curve (manually extracted from Google Maps —
/// the plan's data source). PLACEHOLDER DATA: the curve is hardcoded until the portal lets owners
/// enter per-site popular times (phase 4); the tool's shape and site-timezone handling are final.
/// </summary>
public sealed class GetCrowdednessTool : IChatTool
{
    public string Code => "get_crowdedness";

    // Popular-times style occupancy 0-100 per hour (index 0-23), weekday vs weekend. A cafe curve:
    // morning ramp, lunch peak, afternoon lull, evening peak (weekends busier and later).
    private static readonly int[] Weekday =
        [0, 0, 0, 0, 0, 0, 5, 20, 40, 45, 40, 60, 75, 55, 35, 30, 35, 45, 60, 55, 40, 20, 5, 0];
    private static readonly int[] Weekend =
        [0, 0, 0, 0, 0, 0, 0, 10, 30, 50, 65, 75, 85, 80, 65, 55, 60, 70, 85, 90, 70, 40, 10, 0];

    public ToolDefinition BuildDefinition(Site site) => new()
    {
        Function = new FunctionDefinition
        {
            Name = Code,
            Description = "How busy the shop is right now (quiet / moderate / busy / packed), based on "
                        + "typical visit patterns. Use it when the customer asks if it's crowded, wants "
                        + "a quiet time to visit or work, or when timing matters for a suggestion.",
            Parameters = new { type = "object", properties = new { }, required = Array.Empty<string>() },
        },
    };

    public Task<string> ExecuteAsync(JsonElement arguments, Site site, CancellationToken ct)
    {
        var now = SiteApi.SiteNow(site);
        var isWeekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var curve = isWeekend ? Weekend : Weekday;
        var occupancy = curve[now.Hour];

        var level = occupancy switch
        {
            0 => "closed",
            < 35 => "quiet",
            < 60 => "moderate",
            < 80 => "busy",
            _ => "packed",
        };

        // Also surface the next quiet window so the model can answer "when should I come?".
        string? nextQuietHour = null;
        for (var offset = 1; offset <= 12; offset++)
        {
            var future = now.AddHours(offset);
            var futureCurve = future.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? Weekend : Weekday;
            if (futureCurve[future.Hour] is > 0 and < 35)
            {
                nextQuietHour = $"{future.Hour:00}:00";
                break;
            }
        }

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            localTime = now.ToString("yyyy-MM-dd HH:mm"),
            dayType = isWeekend ? "weekend" : "weekday",
            level,
            occupancyPercent = occupancy,
            nextQuietHour,
        }, SiteApi.Json));
    }
}
