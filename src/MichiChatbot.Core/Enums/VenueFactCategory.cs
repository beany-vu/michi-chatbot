namespace MichiChatbot.Core.Enums;

// What kind of venue fact a row holds. Explicit values: the int is what gets stored, so adding or
// reordering members must never change the meaning of existing rows (same rule as TenantStatus).
public enum VenueFactCategory
{
    Capacity = 1,
    Pricing = 2,
    Amenities = 3,
    Rules = 4,
    Hours = 5,
    Contact = 6,
}
