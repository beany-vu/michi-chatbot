using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace MichiChatbot.Infrastructure.Persistence;

/// <summary>
/// Generates primary keys as UUID v7 on the app side (not in Postgres).
///
/// Why override EF's default? For a Guid key EF Core already plugs in a
/// <c>SequentialGuidValueGenerator</c>, but its byte layout sorts well for SQL Server, NOT for
/// Postgres. UUID v7 is time-ordered in the standard big-endian layout Postgres compares, so inserts
/// land at the B-tree's right edge (dense, hot page) instead of scattering like random v4 keys —
/// which is the whole PK-locality decision from the phase-0 diary, now expressed as one line of config.
///
/// App-generated (not a DB default) keeps v4's virtues — the key exists before the round-trip, so it
/// can be referenced/logged pre-insert — while recovering write locality.
/// </summary>
public sealed class UuidV7ValueGenerator : ValueGenerator<Guid>
{
    // The value is the real key, not a placeholder EF must reconcile after INSERT.
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
}
