namespace MichiChatbot.Core.Enums;

// Who produced a message. Mirrors the OpenAI-compatible chat wire format (system/user/assistant/tool)
// so persisted rows map 1:1 onto what is sent to the LLM. Explicit values: stored ints must stay stable.
public enum MessageRole
{
    System = 1,
    User = 2,
    Assistant = 3,
    Tool = 4,
}
