using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace MichiChatbot.Infrastructure.Calendar;

/// <summary>
/// Thin wrapper over the Google Calendar API for one platform-wide service account (every tenant's
/// calendar is shared with this same account during onboarding; see plan.md phase 2). Built once —
/// there is only one credential for the whole app, unlike <c>ChatClientFactory</c>'s per-model
/// caching, so no factory is warranted here (Factory pays for itself when there's something to
/// cache per key; here there's exactly one key).
/// </summary>
public sealed class GoogleCalendarService
{
    private readonly CalendarService _calendar;

    public GoogleCalendarService(IOptions<GoogleCalendarOptions> options)
    {
        var credential = GoogleCredential
            .FromFile(options.Value.CredentialsPath)
            .CreateScoped(CalendarService.Scope.Calendar);

        _calendar = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "michi-chatbot",
        });
    }

    /// <summary>Busy periods on <paramref name="calendarId"/> within [windowStart, windowEnd).</summary>
    public async Task<IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)>> GetBusyPeriodsAsync(
        string calendarId, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken ct)
    {
        var request = new FreeBusyRequest
        {
            TimeMinDateTimeOffset = windowStart,
            TimeMaxDateTimeOffset = windowEnd,
            Items = [new FreeBusyRequestItem { Id = calendarId }],
        };
        var response = await _calendar.Freebusy.Query(request).ExecuteAsync(ct);

        if (!response.Calendars.TryGetValue(calendarId, out var calendar) || calendar.Busy is null)
            return [];

        return calendar.Busy
            .Where(b => b.StartDateTimeOffset.HasValue && b.EndDateTimeOffset.HasValue)
            .Select(b => (b.StartDateTimeOffset!.Value, b.EndDateTimeOffset!.Value))
            .ToList();
    }

    /// <summary>
    /// Creates a "[TENTATIVE]" event with no attendees (a service account on a non-Workspace
    /// calendar can't invite Gmail attendees — contact info goes in the description instead).
    /// Returns the new event's id.
    /// </summary>
    public async Task<string> CreateTentativeEventAsync(
        string calendarId, string timezone, DateTimeOffset start, DateTimeOffset end,
        string summary, string description, CancellationToken ct)
    {
        var newEvent = new Event
        {
            Summary = $"[TENTATIVE] {summary}",
            Description = description,
            Start = new EventDateTime { DateTimeDateTimeOffset = start, TimeZone = timezone },
            End = new EventDateTime { DateTimeDateTimeOffset = end, TimeZone = timezone },
        };

        var created = await _calendar.Events.Insert(newEvent, calendarId).ExecuteAsync(ct);
        return created.Id;
    }
}
