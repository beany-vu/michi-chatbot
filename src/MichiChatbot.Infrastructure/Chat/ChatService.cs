using System.Diagnostics;
using System.Text.Json;
using MichiChatbot.Core.Entities;
using MichiChatbot.Core.Enums;
using MichiChatbot.Infrastructure.Llm;
using MichiChatbot.Infrastructure.Persistence;
using MichiChatbot.Infrastructure.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace MichiChatbot.Infrastructure.Chat;

/// <summary>
/// One user turn, end to end: load/create the conversation, rebuild history (the LLM API is
/// stateless — "memory" is us resending it), run the model with the site's enabled tools,
/// persist both message rows, upsert the daily usage rollup, and emit SSE-shaped events through
/// the <paramref name="emit"/> callback along the way.
/// The tool loop itself is now Microsoft.Extensions.AI's UseFunctionInvocation() (see
/// ChatClientFactory) — the hand-rolled version it replaced lives on behind /debug/chat.
/// </summary>
public sealed class ChatService(
    ChatbotDbContext db,
    ChatClientFactory llmFactory,
    ToolRegistry toolRegistry)
{
    /// <summary>History window: only the most recent turns are resent — input tokens grow with history.</summary>
    private const int HistoryWindow = 12;

    /// <summary>Emits one SSE event: ("delta"|"tool"|"done", payload). The endpoint owns the wire writing.</summary>
    public delegate Task EventEmitter(string type, object payload);

    public async Task StreamTurnAsync(
        Site site, string userText, Guid? conversationId, string anonId,
        EventEmitter emit, CancellationToken ct)
    {
        // 1. Load or start the conversation. The tenant/site query filter is already active (the
        //    endpoint set the accessor), so a foreign conversationId simply won't be found. AnonId
        //    must match too: one visitor cannot continue another visitor's conversation.
        Conversation? conversation = null;
        if (conversationId is { } id)
        {
            conversation = await db.Conversations
                .FirstOrDefaultAsync(c => c.Id == id && c.AnonId == anonId, ct);
        }
        conversation ??= db.Conversations.Add(new Conversation
        {
            AnonId = anonId,
            Locale = site.Locale,
        }).Entity;

        // 2. Rebuild the history from persisted rows: newest N, then chronological. Only
        //    user/assistant text turns re-enter history — tool internals were context for ONE
        //    answer, not durable conversation state.
        var history = conversation.Id == Guid.Empty
            ? []
            : (await db.Messages
                .Where(m => m.ConversationId == conversation.Id
                            && (m.Role == MessageRole.User || m.Role == MessageRole.Assistant))
                .OrderByDescending(m => m.Id) // uuid v7 = time-ordered
                .Take(HistoryWindow)
                .AsNoTracking()
                .ToListAsync(ct))
                .OrderBy(m => m.Id)
                .ToList();

        var siteNow = SiteApi.SiteNow(site);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System,
                $"{site.PersonaPrompt}\n\n"
                + $"You are chatting on behalf of {site.Name}. "
                + $"Local date/time: {siteNow:dddd yyyy-MM-dd HH:mm} ({site.Timezone}). "
                + "Use the available tools for live facts (menu, weather, events, busyness) "
                + "instead of guessing; if you don't know and no tool helps, say so honestly."),
        };
        messages.AddRange(history.Select(m => new ChatMessage(
            m.Role == MessageRole.User ? ChatRole.User : ChatRole.Assistant, m.Content)));
        messages.Add(new ChatMessage(ChatRole.User, userText));

        // 3. Persist the user turn immediately — if the LLM call dies we still know what was asked.
        //    The interceptor stamps TenantId/SiteId on both rows (ISiteScoped).
        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = userText,
        });
        conversation.LastMessageAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // 4. One call replaces the hand-rolled loop: UseFunctionInvocation() runs the
        //    send → tool_calls → execute → resend rounds internally and returns when the model
        //    answers with plain content (or the round cap trips).
        var model = llmFactory.ResolveModel(site.Model);
        var chatOptions = new ChatOptions { Tools = toolRegistry.AIToolsFor(site) };
        var stopwatch = Stopwatch.StartNew();

        var response = await llmFactory.GetForModel(model)
            .GetResponseAsync(messages, chatOptions, ct);

        stopwatch.Stop();
        var answer = response.Messages.Count > 0 ? response.Messages[^1].Text : "";

        // The response carries every intermediate message the loop produced; replay the
        // call/result pairs for the SSE `tool` events and the jsonb log. (With the hand-rolled
        // loop we observed calls as they ran; the framework hands them back afterwards.)
        var toolCallLog = new List<object>();
        var callsById = new Dictionary<string, FunctionCallContent>();
        foreach (var content in response.Messages.SelectMany(m => m.Contents))
        {
            if (content is FunctionCallContent call)
            {
                callsById[call.CallId] = call;
                await emit("tool", new
                {
                    name = call.Name,
                    arguments = JsonSerializer.Serialize(call.Arguments),
                });
            }
            else if (content is FunctionResultContent result
                     && callsById.TryGetValue(result.CallId, out var matchedCall))
            {
                toolCallLog.Add(new
                {
                    name = matchedCall.Name,
                    arguments = JsonSerializer.Serialize(matchedCall.Arguments),
                    result = JsonSerializer.Serialize(result.Result),
                });
            }
        }

        var rounds = Math.Max(1, response.Messages.Count(m => m.Role == ChatRole.Assistant));
        var tokensIn = (int)(response.Usage?.InputTokenCount ?? 0);
        var tokensOut = (int)(response.Usage?.OutputTokenCount ?? 0);

        // 5. Stream the answer as delta events. DEV SHORTCUT: the call above is non-streaming
        //    (LM Studio/DashScope streaming of tool-call deltas is the known-flaky area), so we
        //    chunk the finished text — the client-facing SSE contract is already the real one.
        foreach (var chunk in Chunk(answer, 48))
            await emit("delta", new { text = chunk });

        // 6. Persist the assistant turn with its LLM metadata; ToolCalls jsonb records what the
        //    model did this turn (analytics/debugging), even though it never re-enters history.
        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            Role = MessageRole.Assistant,
            Content = answer,
            ToolCalls = toolCallLog.Count > 0 ? JsonSerializer.Serialize(toolCallLog) : null,
            Model = response.ModelId ?? model, // the server reports the model it actually ran
            TokensIn = tokensIn,
            TokensOut = tokensOut,
            LatencyMs = (int)stopwatch.ElapsedMilliseconds,
        });
        conversation.LastMessageAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // 7. Upsert the daily usage rollup — raw SQL because EF has no native upsert and the
        //    (SiteId, Date) natural PK is exactly the ON CONFLICT target it was designed to be.
        //    Raw SQL bypasses the tenant filter AND the write interceptor, so TenantId/SiteId are
        //    stamped explicitly here — deliberate, and the reason this stays in one small method.
        var usageDate = DateOnly.FromDateTime(siteNow.Date);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO usage_daily ("SiteId", "TenantId", "Date", "TokensIn", "TokensOut", "MessageCount")
            VALUES ({site.Id}, {site.TenantId}, {usageDate}, {tokensIn}, {tokensOut}, 1)
            ON CONFLICT ("SiteId", "Date") DO UPDATE SET
                "TokensIn" = usage_daily."TokensIn" + EXCLUDED."TokensIn",
                "TokensOut" = usage_daily."TokensOut" + EXCLUDED."TokensOut",
                "MessageCount" = usage_daily."MessageCount" + 1
            """, ct);

        await emit("done", new
        {
            conversationId = conversation.Id,
            anonId,
            rounds,
            usage = new { inTokens = tokensIn, outTokens = tokensOut },
            latencyMs = (int)stopwatch.ElapsedMilliseconds,
        });
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (var i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }
}
