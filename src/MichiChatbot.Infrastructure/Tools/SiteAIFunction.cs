using System.Text.Json;
using MichiChatbot.Core.Entities;
using Microsoft.Extensions.AI;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>
/// Adapter from our IChatTool (hand-written JSON-Schema, per-site behavior) to
/// Microsoft.Extensions.AI's AIFunction, the shape UseFunctionInvocation() knows how to call.
/// AIFunctionFactory.Create() would generate the schema from a C# method signature; our tools
/// carry their own schemas and need the Site threaded through, so we implement AIFunction directly.
/// One instance is bound to one (tool, site) pair for the duration of a request.
/// </summary>
public sealed class SiteAIFunction : AIFunction
{
    private readonly Site _site;
    private readonly ToolRegistry _registry;
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _schema;

    public SiteAIFunction(IChatTool tool, Site site, ToolRegistry registry)
    {
        _site = site;
        _registry = registry;
        var definition = tool.BuildDefinition(site);
        _name = definition.Function.Name;
        _description = definition.Function.Description;
        _schema = JsonSerializer.SerializeToElement(definition.Function.Parameters);
    }

    public override string Name => _name;
    public override string Description => _description;
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // The framework already parsed the model's arguments STRING into a dictionary (the
        // defensive parse the hand-rolled loop did itself); re-materialize it as a JsonElement
        // for our tools. Routing through the registry keeps its contract: failures come back as
        // JSON error strings TO THE MODEL, never as exceptions.
        var args = JsonSerializer.SerializeToElement(arguments);
        var result = await _registry.ExecuteAsync(_name, args, _site, cancellationToken);

        // Tools return JSON strings; hand them back as JsonElement so the framework serializes
        // them as raw JSON instead of a double-encoded quoted string.
        return JsonSerializer.Deserialize<JsonElement>(result);
    }
}
