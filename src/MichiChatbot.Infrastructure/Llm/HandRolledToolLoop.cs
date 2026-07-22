using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MichiChatbot.Infrastructure.Llm;

/// <summary>
/// Phase-1 centerpiece: the tool-call loop written by hand against the raw HTTP wire format, before
/// Microsoft.Extensions.AI's UseFunctionInvocation() hides all of this. Send messages+tools; if the
/// model asks for a tool call, execute it locally and append a role:"tool" result; repeat until the
/// model answers with plain content or MaxRounds is hit.
/// The loop is tool-source-agnostic: callers pass the definitions and an executor (the real chat
/// endpoint passes the site-filtered ToolRegistry; /debug/chat passes the throwaway DebugTools).
/// </summary>
public class HandRolledToolLoop(
    IOptions<LlmOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<HandRolledToolLoop> logger)
{
    public const int MaxRounds = 6;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Executes one named tool with already-parsed arguments; returns the JSON result string.</summary>
    public delegate Task<string> ToolExecutor(string name, JsonElement arguments, CancellationToken ct);

    /// <summary>Called after each executed tool call — the SSE endpoint streams these as `tool` events.</summary>
    public delegate Task ToolObserver(ToolCall call, string result);

    public async Task<(List<WireMessage> Messages, Usage Usage, int Rounds)> RunAsync(
        List<WireMessage> messages,
        List<ToolDefinition> tools,
        ToolExecutor executeTool,
        string model,
        ToolObserver? onToolExecuted,
        CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient("Llm");
        var totalUsage = new Usage();

        for (var round = 1; round <= MaxRounds; round++)
        {
            var request = new ChatCompletionRequest
            {
                Model = model,
                Messages = messages,
                Tools = tools.Count > 0 ? tools : null,
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
                var result = await ExecuteDefensivelyAsync(executeTool, toolCall, ct);
                messages.Add(new WireMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = result,
                });

                if (onToolExecuted is not null)
                    await onToolExecuted(toolCall, result);
            }
        }

        logger.LogWarning("Hit MaxRounds ({MaxRounds}) without a final answer", MaxRounds);
        return (messages, totalUsage, MaxRounds);
    }

    /// <summary>
    /// The arguments string is MODEL-generated JSON, so it can be malformed. A parse failure goes
    /// back to the model as a JSON error result (it can retry) — never up the stack.
    /// </summary>
    private static async Task<string> ExecuteDefensivelyAsync(
        ToolExecutor executeTool, ToolCall toolCall, CancellationToken ct)
    {
        JsonElement arguments;
        try
        {
            arguments = JsonSerializer.Deserialize<JsonElement>(
                string.IsNullOrWhiteSpace(toolCall.Function.Arguments) ? "{}" : toolCall.Function.Arguments);
        }
        catch (JsonException ex)
        {
            return JsonSerializer.Serialize(new { error = $"Bad arguments JSON: {ex.Message}" });
        }

        return await executeTool(toolCall.Function.Name, arguments, ct);
    }

    /// <summary>The model actually used: env/appsettings override (dev: LM Studio) wins over the site's configured model.</summary>
    public string ResolveModel(string siteModel) =>
        string.IsNullOrWhiteSpace(options.Value.Model) ? siteModel : options.Value.Model;
}
