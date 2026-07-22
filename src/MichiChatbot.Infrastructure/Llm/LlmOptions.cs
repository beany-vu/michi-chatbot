namespace MichiChatbot.Infrastructure.Llm;

/// <summary>
/// Endpoint/key/model for the OpenAI-compatible chat-completions API. Same shape serves LM Studio
/// (dev) and DashScope (prod) — swapping providers is a config change, never a code change.
/// </summary>
public class LlmOptions
{
    public const string SectionName = "Llm";

    public required string BaseUrl { get; set; }

    /// <summary>Per-machine: whatever is actually loaded in LM Studio, or the DashScope model id in prod.</summary>
    public string Model { get; set; } = "";

    /// <summary>LM Studio ignores this. DashScope needs a real key via User Secrets/env — never here.</summary>
    public string ApiKey { get; set; } = "lm-studio";
}
