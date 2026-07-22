using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MichiChatbot.Infrastructure.Llm;

/// <summary>
/// Phase-1 centerpiece: the tool-call loop written by hand against the raw HTTP wire format, before
/// Microsoft.Extensions.AI's UseFunctionInvocation() hides all of this. Send messages+tools; if the
/// model asks for a tool call, execute it locally and append a role:"tool" result; repeat until the
/// model answers with plain content or MaxRounds is hit (same guardrail number planned for the real
/// /chat/stream endpoint).
/// </summary>
public class HandRolledToolLoop(
    IHttpClientFactory httpClientFactory,
    IOptions<LlmOptions> options,
    ILogger<HandRolledToolLoop> logger)
{
    private const int MaxRounds = 6;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly Dictionary<string, Func<JsonElement, string>> ToolExecutors = new()
    {
        ["get_current_time"] = DebugTools.GetCurrentTime,
        ["roll_dice"] = DebugTools.RollDice,
    };

    private static readonly List<ToolDefinition> ToolDefinitions =
    [
        DebugTools.GetCurrentTimeDefinition(),
        DebugTools.RollDiceDefinition(),
    ];

    public async Task<(List<WireMessage> Messages, Usage Usage, int Rounds)> RunAsync(
        List<WireMessage> messages, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient("Llm");
        var totalUsage = new Usage();

        for (var round = 1; round <= MaxRounds; round++)
        {
            var request = new ChatCompletionRequest
            {
                Model = options.Value.Model,
                Messages = messages,
                Tools = ToolDefinitions,
            };

            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            logger.LogInformation("LLM request (round {Round}):\n{Json}", round, requestJson);

            using var httpResponse = await http.PostAsync(
                "chat/completions",
                new StringContent(requestJson, Encoding.UTF8, "application/json"),
                ct);

            var responseJson = await httpResponse.Content.ReadAsStringAsync(ct);
            logger.LogInformation("LLM response (round {Round}):\n{Json}", round, responseJson);

            httpResponse.EnsureSuccessStatusCode();

            var response = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson)
                ?? throw new InvalidOperationException("LLM returned an empty response body.");

            if (response.Usage is { } usage)
            {
                totalUsage.PromptTokens += usage.PromptTokens;
                totalUsage.CompletionTokens += usage.CompletionTokens;
                totalUsage.TotalTokens += usage.TotalTokens;
            }

            var assistantMessage = response.Choices[0].Message;
            messages.Add(assistantMessage);

            if (assistantMessage.ToolCalls is not { Count: > 0 } toolCalls)
            {
                return (messages, totalUsage, round);
            }

            foreach (var toolCall in toolCalls)
            {
                messages.Add(new WireMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = ExecuteTool(toolCall),
                });
            }
        }

        logger.LogWarning("Hit MaxRounds ({MaxRounds}) without a final answer", MaxRounds);
        return (messages, totalUsage, MaxRounds);
    }

    private static string ExecuteTool(ToolCall toolCall)
    {
        if (!ToolExecutors.TryGetValue(toolCall.Function.Name, out var executor))
        {
            return JsonSerializer.Serialize(new { error = $"Unknown tool '{toolCall.Function.Name}'" });
        }

        try
        {
            var arguments = JsonSerializer.Deserialize<JsonElement>(toolCall.Function.Arguments);
            return executor(arguments);
        }
        catch (JsonException ex)
        {
            return JsonSerializer.Serialize(new { error = $"Bad arguments JSON: {ex.Message}" });
        }
    }
}
