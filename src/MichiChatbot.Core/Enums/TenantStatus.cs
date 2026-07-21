namespace MichiChatbot.Core.Enums;

// An enum = a fixed set of named choices. A tenant is either running or paused.
// From the plan schema, tenants.Status is Active / Suspended.
public enum TenantStatus
{
    // TODO: add the two members: Active and Suspended.
    //
    // Decision point — explicit numbers or not?
    //   Bare `Active, Suspended` makes them 0 and 1 automatically.
    //   Assigning `Active = 1, Suspended = 2` is safer ONCE rows exist in the DB:
    //   the number is what gets stored, so if you later reorder or insert a member,
    //   explicit values stop the stored ints from silently meaning something else.
    // Your call — pick one and write the members here.
    Active = 1,
    Suspended = 2
}
