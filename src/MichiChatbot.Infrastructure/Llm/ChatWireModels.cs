using System.Text.Json.Serialization;

namespace MichiChatbot.Infrastructure.Llm;

// Raw OpenAI-compatible chat-completions wire format, hand-rolled on purpose: phase-1 learning is
// writing this once before Microsoft.Extensions.AI's UseFunctionInvocation() hides it behind AIFunction.

public class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required List<WireMessage> Messages { get; set; }

    [JsonPropertyName("tools")]
    public List<ToolDefinition>? Tools { get; set; }
}

public class WireMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; } // "system" | "user" | "assistant" | "tool"

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<ToolCall>? ToolCalls { get; set; }

    /// <summary>Only set on role:"tool" messages — links a result back to the call that requested it.</summary>
    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }
}

public class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public required FunctionDefinition Function { get; set; }
}

public class FunctionDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("parameters")]
    public required object Parameters { get; set; } // raw JSON Schema object
}

public class ToolCall
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public required ToolCallFunction Function { get; set; }
}

public class ToolCallFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// The model emits this as a JSON-encoded STRING, not a nested object — it built the string
    /// itself, so it can come back malformed and must be parsed defensively.
    /// </summary>
    [JsonPropertyName("arguments")]
    public required string Arguments { get; set; }
}

public class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public required List<Choice> Choices { get; set; }

    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }
}

public class Choice
{
    [JsonPropertyName("message")]
    public required WireMessage Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public class Usage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
