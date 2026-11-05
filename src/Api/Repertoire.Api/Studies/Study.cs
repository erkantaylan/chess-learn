namespace Repertoire.Api.Studies;

/// <summary>
/// One saved opening study. Mirrors the table the Python/SQLite backend used, plus an owner, so
/// the JSON contract in docs/API.md is unchanged apart from <c>owned</c>.
/// </summary>
public class Study
{
    public int Id { get; set; }

    /// <summary>
    /// Whose library this study is in — the IdP's <c>sub</c> under the central login, the
    /// configured username under the password login (see <c>StudyOwner</c>). Only the owner lists,
    /// renames, saves over or deletes it; anyone who may read studies at all can open it by id.
    /// Never sent on the wire: the client gets <c>owned</c>, not someone else's subject id.
    /// </summary>
    public string Owner { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string StartFen { get; set; } = string.Empty;

    /// <summary>
    /// The move tree, as raw JSON text held in a Postgres <c>jsonb</c> column. Arbitrary and
    /// opaque: the API never validates or rewrites it, it only walks <c>children</c> to compute
    /// <c>move_count</c> for the list endpoint.
    ///
    /// Kept as a string rather than JsonDocument on purpose — a string needs no value comparer
    /// for EF change tracking and nothing to dispose, and the API layer parses it at the edge.
    /// NB: jsonb normalises (key order is not preserved, whitespace is dropped, duplicate keys
    /// collapse). Semantically identical, not byte-identical — SQLite's TEXT column was.
    /// </summary>
    public string Tree { get; set; } = "null";

    public string Pgn { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
