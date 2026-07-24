namespace MichiChatbot.Core.Enums;

// Explicit values, same rule as TenantStatus/VenueFactCategory: reordering members must never
// change the meaning of a stored row. Confirm/Cancel actions land with the owner portal (phase 3);
// every booking created by the bot today starts and stays Pending until then.
public enum BookingStatus
{
    Pending = 1,
    Confirmed = 2,
    Cancelled = 3,
}
