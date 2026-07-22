using System.Text.Json;
using MichiChatbot.Core.Entities;
using MichiChatbot.Infrastructure.Llm;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>
/// One capability the bot can invoke for a site. Tools are generic — every site gets the same
/// registered set, and <see cref="Site.EnabledTools"/> decides which codes are actually offered to
/// the model per request. A tool receives the Site so it can close over the site's BaseUrl,
/// timezone and locale instead of hardcoding any one tenant's details.
/// </summary>
public interface IChatTool
{
    /// <summary>Stable code stored in sites.EnabledTools and used as the wire-format function name.</summary>
    string Code { get; }

    /// <summary>The OpenAI-compatible function definition sent to the model.</summary>
    ToolDefinition BuildDefinition(Site site);

    /// <summary>Executes with model-provided arguments; returns a JSON string fed back as the tool result.</summary>
    Task<string> ExecuteAsync(JsonElement arguments, Site site, CancellationToken ct);
}
