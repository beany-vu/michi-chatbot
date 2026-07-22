using MichiChatbot.Core.Abstractions;
using MichiChatbot.Core.Enums;

namespace MichiChatbot.Core.Entities;

/// <summary>
/// One turn in a conversation (system/user/assistant/tool). Carries its own SiteId/TenantId even
/// though the conversation already knows them: isolation must hold on direct `db.Messages` queries
/// without anyone remembering a join (see ISiteScoped).
/// LLM metadata (Model, tokens, latency) is nullable — user rows have none of it.
/// Deferred: PersonaPromptVersion (phase 3 prompt versioning; null will mean "pre-versioning").
/// </summary>
public class Message : ISiteScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public Guid ConversationId { get; set; }

    public MessageRole Role { get; set; }

    public required string Content { get; set; }

    /// <summary>Raw tool-call payload for assistant turns that invoked tools (jsonb; null otherwise).</summary>
    public string? ToolCalls { get; set; }

    /// <summary>Which model produced this turn (assistant rows only) — per-site config can change over time.</summary>
    public string? Model { get; set; }

    public int? TokensIn { get; set; }
    public int? TokensOut { get; set; }
    public int? LatencyMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
