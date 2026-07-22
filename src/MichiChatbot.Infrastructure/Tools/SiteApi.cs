using System.Text.Json;
using MichiChatbot.Core.Entities;

namespace MichiChatbot.Infrastructure.Tools;

/// <summary>
/// Shared plumbing for tools that call a site's public REST API. Tool results are paid input
/// tokens (they re-enter the model as context every remaining round), so helpers here TRIM
/// upstream responses down to the fields the model actually needs.
/// </summary>
public static class SiteApi
{
    public const string HttpClientName = "SiteApi";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// GETs <paramref name="pathAndQuery"/> under the site's BaseUrl. The mugshot Next.js API
    /// 308-redirects paths without a trailing slash, so callers pass paths like
    /// <c>api/products/</c> (slash BEFORE the query string).
    /// </summary>
    public static async Task<JsonElement> GetJsonAsync(
        HttpClient http, Site site, string pathAndQuery, CancellationToken ct)
    {
        var baseUrl = site.BaseUrl.EndsWith('/') ? site.BaseUrl : site.BaseUrl + "/";
        using var response = await http.GetAsync(baseUrl + pathAndQuery, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    /// <summary>Error results go back to the MODEL as JSON, not up the stack — it can apologize or retry.</summary>
    public static string Error(string message) =>
        JsonSerializer.Serialize(new { error = message }, Json);

    /// <summary>"Now" in the site's local timezone — every date-ish tool argument buckets by site time.</summary>
    public static DateTimeOffset SiteNow(Site site) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(site.Timezone));
}
