using System.Text.Json;

namespace MichiChatbot.Infrastructure.Llm;

/// <summary>
/// Throwaway tools proving the hand-rolled loop works, not the platform's real tool registry (that's
/// IChatTool, still to come). One zero-argument tool and one with an argument, so both the "no
/// parsing needed" and "parse the model's JSON arguments" paths get exercised.
/// </summary>
public static class DebugTools
{
    public static ToolDefinition GetCurrentTimeDefinition() => new()
    {
        Function = new FunctionDefinition
        {
            Name = "get_current_time",
            Description = "Get the current date and time in UTC.",
            Parameters = new { type = "object", properties = new { }, required = Array.Empty<string>() },
        },
    };

    public static ToolDefinition RollDiceDefinition() => new()
    {
        Function = new FunctionDefinition
        {
            Name = "roll_dice",
            Description = "Roll a die with the given number of sides and return the result.",
            Parameters = new
            {
                type = "object",
                properties = new { sides = new { type = "integer", description = "Number of sides on the die" } },
                required = new[] { "sides" },
            },
        },
    };

    public static string GetCurrentTime(JsonElement _) =>
        JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow.ToString("O") });

    public static string RollDice(JsonElement arguments)
    {
        var sides = arguments.TryGetProperty("sides", out var sidesEl) ? sidesEl.GetInt32() : 6;
        var result = Random.Shared.Next(1, sides + 1);
        return JsonSerializer.Serialize(new { sides, result });
    }
}
