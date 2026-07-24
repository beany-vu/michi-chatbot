using System.Text.Json;
using MichiChatbot.Core.Entities;
using MichiChatbot.Infrastructure.Llm;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>
/// All registered tools, keyed by code. Per request the SITE decides what the model sees:
/// <see cref="DefinitionsFor"/> intersects the registry with site.EnabledTools, so enabling a
/// tool for a site is a data change (portal, later), not a deploy.
/// </summary>
public sealed class ToolRegistry(IEnumerable<IChatTool> tools)
{
    private readonly Dictionary<string, IChatTool> _byCode =
        tools.ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);

    public List<ToolDefinition> DefinitionsFor(Site site) =>
        site.EnabledTools
            .Where(_byCode.ContainsKey)
            .Select(code => _byCode[code].BuildDefinition(site))
            .ToList();

    /// <summary>The same enabled-tools intersection, as Microsoft.Extensions.AI AIFunctions.</summary>
    public List<Microsoft.Extensions.AI.AITool> AIToolsFor(Site site) =>
        site.EnabledTools
            .Where(_byCode.ContainsKey)
            .Select(Microsoft.Extensions.AI.AITool (code) => new SiteAIFunction(_byCode[code], site, this))
            .ToList();

    /// <summary>
    /// Executes a model-requested call. Unknown tools and execution failures return JSON error
    /// strings TO THE MODEL (it can recover in the next round) instead of failing the request.
    /// </summary>
    public async Task<string> ExecuteAsync(
        string code, JsonElement arguments, Site site, CancellationToken ct)
    {
        if (!_byCode.TryGetValue(code, out var tool) || !site.EnabledTools.Contains(tool.Code))
            return JsonSerializer.Serialize(new { error = $"Unknown tool '{code}'" });

        try
        {
            return await tool.ExecuteAsync(arguments, site, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { error = $"Tool '{code}' failed: {ex.Message}" });
        }
    }
}
