namespace MichiChatbot.Core.Enums;

// Where a conversation came from. Explicit values: the int is what gets stored, so adding or
// reordering members must never change the meaning of existing rows (same rule as TenantStatus).
public enum ConversationChannel
{
    /// <summary>The embeddable chat widget (same-origin or cross-origin embed).</summary>
    Widget = 1,
}
