using System.Text;
using System.Text.Json;

namespace MichiChatbot.Evals;

/// <summary>
/// Talks to the REAL /chat/stream endpoint exactly like a real client — same SSE contract the
/// widget uses, no shortcuts. Verifying the eval harness against a fake/in-process path would
/// only prove the harness works, not that the product does.
/// </summary>
public sealed class BotClient(HttpClient http, string siteKey)
{
    public async Task<ChatTurnResult> SendAsync(string message, CancellationToken ct)
    {
        var anonId = $"eval-{Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/chat/stream")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { message }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Site-Key", siteKey);
        request.Headers.Add("X-Anon-Id", anonId);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var answer = new StringBuilder();
        var tools = new List<ToolCallSeen>();
        var conversationId = Guid.Empty;
        string? eventType = null;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventType = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var data = line["data: ".Length..];
                var json = JsonDocument.Parse(data).RootElement;
                switch (eventType)
                {
                    case "delta":
                        answer.Append(json.GetProperty("text").GetString());
                        break;
                    case "tool":
                        tools.Add(new ToolCallSeen(
                            json.GetProperty("name").GetString()!,
                            json.GetProperty("arguments").GetString() ?? ""));
                        break;
                    case "done":
                        conversationId = json.GetProperty("conversationId").GetGuid();
                        break;
                    case "error":
                        throw new InvalidOperationException(
                            $"Bot returned an error event: {json.GetProperty("message").GetString()}");
                }
            }
        }

        return new ChatTurnResult(answer.ToString(), tools, conversationId);
    }
}
