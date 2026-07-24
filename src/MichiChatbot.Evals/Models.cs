using System.Text.Json.Serialization;

namespace MichiChatbot.Evals;

/// <summary>
/// One golden-set question. ExpectedFacts/MustNotClaim are checked by the judge against the bot's
/// actual answer — MustNotClaim exists specifically for hallucination guardrails (e.g. venue
/// pricing, where no real number is published and the bot must say so, not invent one).
/// ExpectedTool is null for questions where no particular tool is required.
/// </summary>
public sealed record GoldenQuestion(
    string Id,
    string Category,
    string Question,
    List<string> ExpectedFacts,
    List<string> MustNotClaim,
    string? ExpectedTool);

/// <summary>One adversarial prompt for the red-team suite. ExpectedBehavior is what "safe" looks like.</summary>
public sealed record RedTeamPrompt(
    string Id,
    string Category,
    string Prompt,
    string ExpectedBehavior);

/// <summary>One tool invocation observed in a turn — name AND arguments, so a report can actually
/// show what the model asked for (name alone hid the menu-tool-use finding's real cause).</summary>
public sealed record ToolCallSeen(string Name, string Arguments);

/// <summary>What actually happened when a question/prompt was sent to the live bot.</summary>
public sealed record ChatTurnResult(
    string Answer,
    List<ToolCallSeen> ToolsCalled,
    Guid ConversationId);

/// <summary>Judge's verdict on a golden-set answer. Schema-constrained via response_format below.</summary>
public sealed record GoldenVerdict(
    [property: JsonPropertyName("grounded")] bool Grounded,
    [property: JsonPropertyName("correctToolUse")] bool CorrectToolUse,
    [property: JsonPropertyName("helpful")] int Helpful,
    [property: JsonPropertyName("reasoning")] string Reasoning);

/// <summary>Judge's verdict on a red-team attempt.</summary>
public sealed record RedTeamVerdict(
    [property: JsonPropertyName("refused")] bool Refused,
    [property: JsonPropertyName("reasoning")] string Reasoning);

public sealed record GoldenResult(GoldenQuestion Question, ChatTurnResult Turn, GoldenVerdict Verdict);

/// <summary>ActuallySafe overrides the judge: a detected leak always means unsafe, regardless of
/// what the judge concluded — see LeakDetector's doc comment for why this exists.</summary>
public sealed record RedTeamResult(RedTeamPrompt Prompt, ChatTurnResult Turn, RedTeamVerdict Verdict, bool LeakDetected)
{
    public bool ActuallySafe => Verdict.Refused && !LeakDetected;
}
