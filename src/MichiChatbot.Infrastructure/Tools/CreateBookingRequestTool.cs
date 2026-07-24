using System.Text.Json;
using MichiChatbot.Core.Entities;
using MichiChatbot.Infrastructure.Calendar;
using MichiChatbot.Infrastructure.Llm;
using MichiChatbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>
/// Captures a venue-rental booking request: a DB row (source of truth), a "[TENTATIVE]" Google
/// Calendar event, and a forward to the site's own enquiry endpoint so a human follows up. Needs
/// <see cref="ChatbotDbContext"/> — Scoped in DI, same reasoning as <see cref="GetVenueFactsTool"/>.
/// Idempotent: the DB half (<see cref="FindOrCreateAsync"/>) is a separate, directly-testable step
/// so the "same request twice -> one event" guarantee can be unit-tested without touching Google
/// Calendar or the site's real API.
/// </summary>
public sealed class CreateBookingRequestTool(
    ChatbotDbContext db, GoogleCalendarService calendar, IHttpClientFactory httpClientFactory) : IChatTool
{
    public string Code => "create_booking_request";

    public ToolDefinition BuildDefinition(Site site) => new()
    {
        Function = new FunctionDefinition
        {
            Name = Code,
            Description = "Book a venue-rental slot after check_availability has confirmed it's free. "
                        + "Collect all required details from the customer first — never guess contact "
                        + "info. Calling this twice with the same date/time for the same conversation "
                        + "is safe: it returns the existing request instead of duplicating it.",
            Parameters = new
            {
                type = "object",
                properties = new
                {
                    date = new { type = "string", description = "Date, YYYY-MM-DD, local time." },
                    start = new { type = "string", description = "Start time, HH:mm, 24h, local time." },
                    end = new { type = "string", description = "End time, HH:mm, 24h, local time." },
                    guestCount = new { type = "integer", description = "Expected number of guests." },
                    contactName = new { type = "string" },
                    contactEmail = new { type = "string" },
                    contactPhone = new { type = "string", description = "Optional." },
                    eventType = new
                    {
                        type = "string",
                        description = "Optional, e.g. birthday, meeting, workshop, private event, other.",
                    },
                    message = new { type = "string", description = "Optional extra context from the customer." },
                },
                required = new[] { "date", "start", "end", "guestCount", "contactName", "contactEmail" },
            },
        },
    };

    public async Task<string> ExecuteAsync(JsonElement arguments, Site site, Guid conversationId, CancellationToken ct)
    {
        if (!CheckAvailabilityTool.TryParseSlot(arguments, site, out var start, out var end, out var slotError))
            return SiteApi.Error(slotError!);

        if (!TryGetContact(arguments, out var contactName, out var contactEmail, out var guestCount, out var contactError))
            return SiteApi.Error(contactError!);

        var contactPhone = GetOptionalString(arguments, "contactPhone");
        var message = GetOptionalString(arguments, "message");
        var eventType = GetOptionalString(arguments, "eventType") ?? "other";

        var date = DateOnly.FromDateTime(start.DateTime);
        var startTime = TimeOnly.FromDateTime(start.DateTime);
        var endTime = TimeOnly.FromDateTime(end.DateTime);

        var (booking, isNew) = await FindOrCreateAsync(
            db, site, conversationId, date, startTime, endTime, guestCount,
            contactName!, contactEmail!, contactPhone, message, ct);

        if (isNew)
        {
            // Calendar/forwarding failures must not lose the booking row itself — it's already
            // saved; a human can still follow up from the DB even if these side effects failed.
            if (!string.IsNullOrEmpty(site.GoogleCalendarId))
            {
                try
                {
                    booking.GoogleEventId = await calendar.CreateTentativeEventAsync(
                        site.GoogleCalendarId, site.Timezone, start, end,
                        summary: $"Booking request — {contactName} x{guestCount}",
                        description: $"Contact: {contactName} <{contactEmail}>"
                                    + (contactPhone is null ? "" : $", {contactPhone}")
                                    + $"\nGuests: {guestCount}\nRequested via chatbot."
                                    + (message is null ? "" : $"\n\n{message}"),
                        ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Left GoogleEventId null; visible in the response below as calendarConfirmed=false.
                    _ = ex;
                }
            }

            try
            {
                var http = httpClientFactory.CreateClient(SiteApi.HttpClientName);
                await SiteApi.PostJsonAsync(http, site, "api/event-inquiries/", new
                {
                    name = contactName,
                    email = contactEmail,
                    phone = contactPhone,
                    eventDate = date.ToString("yyyy-MM-dd"),
                    eventType,
                    guestCount,
                    // Real endpoint requires a truthy message (empty string fails its `!body.message`
                    // check) — see mugshotcoffee/src/app/api/event-inquiries/route.ts.
                    message = string.IsNullOrWhiteSpace(message)
                        ? "Booking request submitted via the chatbot."
                        : message,
                }, ct);
                booking.ForwardedToSiteInquiry = true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _ = ex;
            }

            await db.SaveChangesAsync(ct);
        }

        return JsonSerializer.Serialize(new
        {
            bookingId = booking.Id,
            status = booking.Status.ToString().ToLowerInvariant(),
            alreadyRequested = !isNew,
            calendarConfirmed = booking.GoogleEventId is not null,
            forwardedToShop = booking.ForwardedToSiteInquiry,
        }, SiteApi.Json);
    }

    /// <summary>
    /// The idempotent DB half, isolated so it's unit-testable without Google Calendar or the site's
    /// API. A unique index on (SiteId, ConversationId, Date, Start, End) is the DB-level backstop
    /// for the race this check-then-insert can't fully close on its own.
    /// </summary>
    public static async Task<(BookingRequest Booking, bool IsNew)> FindOrCreateAsync(
        ChatbotDbContext db, Site site, Guid conversationId,
        DateOnly date, TimeOnly start, TimeOnly end, int guestCount,
        string contactName, string contactEmail, string? contactPhone, string? message,
        CancellationToken ct)
    {
        var existing = await db.BookingRequests.FirstOrDefaultAsync(b =>
            b.SiteId == site.Id && b.ConversationId == conversationId
            && b.Date == date && b.Start == start && b.End == end, ct);
        if (existing is not null)
            return (existing, false);

        var booking = new BookingRequest
        {
            ConversationId = conversationId,
            Date = date,
            Start = start,
            End = end,
            GuestCount = guestCount,
            ContactName = contactName,
            ContactEmail = contactEmail,
            ContactPhone = contactPhone,
            Message = message,
        };
        db.BookingRequests.Add(booking);
        await db.SaveChangesAsync(ct);
        return (booking, true);
    }

    private static bool TryGetContact(
        JsonElement arguments, out string? name, out string? email, out int guestCount, out string? error)
    {
        name = GetOptionalString(arguments, "contactName");
        email = GetOptionalString(arguments, "contactEmail");
        guestCount = arguments.TryGetProperty("guestCount", out var g) && g.ValueKind == JsonValueKind.Number
            ? g.GetInt32()
            : 0;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || guestCount <= 0)
        {
            error = "contactName, contactEmail and a positive guestCount are required.";
            return false;
        }

        error = null;
        return true;
    }

    private static string? GetOptionalString(JsonElement arguments, string property) =>
        arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
