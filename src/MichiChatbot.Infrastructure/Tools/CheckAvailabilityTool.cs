using System.Text.Json;
using MichiChatbot.Core.Entities;
using MichiChatbot.Infrastructure.Calendar;
using MichiChatbot.Infrastructure.Llm;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>
/// Google Calendar FreeBusy check on the site's calendar. Pure HTTP (no DbContext), so — unlike
/// <see cref="GetVenueFactsTool"/> / <see cref="CreateBookingRequestTool"/> — this stays Singleton.
/// </summary>
public sealed class CheckAvailabilityTool(GoogleCalendarService calendar) : IChatTool
{
    public string Code => "check_availability";

    public ToolDefinition BuildDefinition(Site site) => new()
    {
        Function = new FunctionDefinition
        {
            Name = Code,
            Description = "Check whether the venue is free on a given date and time range, before "
                        + "offering to book it. Always call this before create_booking_request.",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    date = new { type = "string", description = "Date, YYYY-MM-DD, in the shop's local time." },
                    start = new { type = "string", description = "Start time, HH:mm, 24h, local time." },
                    end = new { type = "string", description = "End time, HH:mm, 24h, local time." },
                },
                required = new[] { "date", "start", "end" },
            },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, Site site, Guid conversationId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(site.GoogleCalendarId))
            return SiteApi.Error("This site has no calendar configured yet.");

        if (!TryParseSlot(arguments, site, out var start, out var end, out var parseError))
            return SiteApi.Error(parseError!);

        try
        {
            var busy = await calendar.GetBusyPeriodsAsync(site.GoogleCalendarId, start, end, ct);
            var available = !AvailabilityChecker.Overlaps(start, end, busy);

            return JsonSerializer.Serialize(new
            {
                available,
                date = start.ToString("yyyy-MM-dd"),
                start = start.ToString("HH:mm"),
                end = end.ToString("HH:mm"),
            }, SiteApi.Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return SiteApi.Error($"Calendar unavailable: {ex.Message}");
        }
    }

    /// <summary>Parses date/start/end model arguments into UTC-offset instants in the site's own timezone.</summary>
    internal static bool TryParseSlot(
        JsonElement arguments, Site site, out DateTimeOffset start, out DateTimeOffset end, out string? error)
    {
        start = default;
        end = default;
        error = null;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(site.Timezone);

        if (!arguments.TryGetProperty("date", out var dateEl) || !DateOnly.TryParse(dateEl.GetString(), out var date)
            || !arguments.TryGetProperty("start", out var startEl) || !TimeOnly.TryParse(startEl.GetString(), out var startTime)
            || !arguments.TryGetProperty("end", out var endEl) || !TimeOnly.TryParse(endEl.GetString(), out var endTime))
        {
            error = "date, start and end are required (date=YYYY-MM-DD, start/end=HH:mm).";
            return false;
        }

        var offset = tz.GetUtcOffset(date.ToDateTime(startTime));
        start = new DateTimeOffset(date.ToDateTime(startTime), offset);
        end = new DateTimeOffset(date.ToDateTime(endTime), offset);

        if (end <= start)
        {
            error = "end must be after start.";
            return false;
        }

        return true;
    }
}
