using MichiChatbot.Core.Abstractions;

namespace MichiChatbot.Core.Entities;

/// <summary>
/// A single bot deployment belonging to a tenant. Implements ITenantScoped so tenant isolation is
/// enforced automatically (query filter + write interceptor). Everything below the Site in the
/// hierarchy will carry SiteId; the Site itself carries TenantId.
/// Deferred to later phases: DashScopeKeyEncrypted (phase 4, BYO key), PersonaPrompt versioning
/// (phase 3, versioned-as-data). PersonaPrompt is a plain string for now.
/// </summary>
public class Site : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public required string Slug { get; set; }
    public required string Name { get; set; }

    /// <summary>Public widget key (pk_live_...), globally unique. Enforced in Infrastructure.</summary>
    public required string PublicKey { get; set; }

    public required string BaseUrl { get; set; }

    /// <summary>Allowed embed origins; drives CORS + the Origin/Referer check. Postgres text[].</summary>
    public string[] AllowedOrigins { get; set; } = [];

    public required string Locale { get; set; }
    public required string Timezone { get; set; }

    /// <summary>Which LLM model this site uses (config, per-site).</summary>
    public required string Model { get; set; }

    public required string PersonaPrompt { get; set; }

    /// <summary>Codes of the tools enabled for this site; mapped to jsonb.</summary>
    public string[] EnabledTools { get; set; } = [];

    public string? GoogleCalendarId { get; set; }

    public bool Active { get; set; } = true;
}
