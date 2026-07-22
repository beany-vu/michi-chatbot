using MichiChatbot.Core.Abstractions;
using MichiChatbot.Core.Enums;

namespace MichiChatbot.Core.Entities;

/// <summary>
/// One chat session on a site. Identity is EITHER a signed-in user (UserId, phase 3) OR an anonymous
/// visitor (AnonId minted by the widget); both nullable because a conversation has exactly one of
/// them until `/chat/claim` re-parents anon history onto a user.
/// Deferred: SentimentScore/SentimentLabel (phase 5, Python analytics fills them — same YAGNI knife
/// as the billing columns).
/// </summary>
public class Conversation : ISiteScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }

    /// <summary>Signed-in end user (FK to Identity in phase 3; plain Guid until then).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Anonymous visitor id (GUID minted by the widget, cookie/localStorage + X-Anon-Id).</summary>
    public string? AnonId { get; set; }

    public ConversationChannel Channel { get; set; } = ConversationChannel.Widget;

    public required string Locale { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Bumped on every message; drives "recent conversations" queries without joining messages.</summary>
    public DateTimeOffset LastMessageAt { get; set; }
}
