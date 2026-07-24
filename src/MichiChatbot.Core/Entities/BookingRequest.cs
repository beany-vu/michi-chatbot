using MichiChatbot.Core.Abstractions;
using MichiChatbot.Core.Enums;

namespace MichiChatbot.Core.Entities;

/// <summary>
/// One venue-rental booking request captured from a chat conversation. Created tentative: a
/// Google Calendar event is made ("[TENTATIVE]" summary — service accounts on a personal Google
/// account can't invite attendees, so contact info lives in the event description instead) and the
/// same details are forwarded to the site's own enquiry endpoint so a human follows up.
/// (Date, Start, End) together with (SiteId, ConversationId) are the idempotency key: the same
/// conversation asking to book the same slot twice must produce exactly one row and one event —
/// enforced by the unique index in <c>BookingRequestConfiguration</c>, the DB doing the guarding
/// rather than the app trusting itself to never call this twice.
/// </summary>
public class BookingRequest : ISiteScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public Guid ConversationId { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public int GuestCount { get; set; }

    public required string ContactName { get; set; }
    public required string ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Message { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    /// <summary>Google Calendar event id once the tentative event exists; null until then.</summary>
    public string? GoogleEventId { get; set; }

    /// <summary>Whether this has been POSTed to the site's own enquiry/inbox endpoint.</summary>
    public bool ForwardedToSiteInquiry { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
