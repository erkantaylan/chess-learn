using System.Text.Json;
using System.Text.Json.Serialization;

namespace Repertoire.Api.Studies;

/// <summary>Full study record. Property names serialise to snake_case (see RepertoireProgram).</summary>
public sealed record StudyRecord(
    int Id,
    string Name,
    string StartFen,
    JsonElement Tree,
    string Pgn,
    string CreatedAt,
    string UpdatedAt);

/// <summary>Listing row: no tree and no pgn, so the list stays cheap.</summary>
public sealed record StudyListRow(
    int Id,
    string Name,
    string CreatedAt,
    string UpdatedAt,
    int MoveCount);

public sealed record StudyCreateRequest(
    string? Name,
    string? StartFen,
    JsonElement? Tree,
    string? Pgn);

/// <summary>
/// Partial update. A property left out of the request body arrives as null and is left alone —
/// which is also why <c>{"tree": null}</c> cannot clear the tree: absent and explicit-null are
/// indistinguishable after binding. Send <c>{"tree": {}}</c> to clear it. Same rule the Python
/// backend documented, kept deliberately.
/// </summary>
public sealed record StudyUpdateRequest(
    string? Name,
    string? StartFen,
    JsonElement? Tree,
    string? Pgn);

public sealed record HealthResponse(bool Ok, int Studies);

public sealed record LoginRequest(string? Username, string? Password);

public sealed record MeResponse(bool Authed, string? User);

internal static class StudyMapping
{
    /// <summary>ISO-8601 UTC, second precision, explicit Z — the format docs/API.md promises.</summary>
    public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>
    /// A move tree nests two JSON levels per ply (the children array, then the child object), so
    /// System.Text.Json's default limit of 64 gives out around move 32 — the seeded Urusov study
    /// alone blows through it. Python's json module allowed ~1000, which is why this only bites
    /// on .NET. 512 covers ~255 plies, past any line anyone will actually enter, while still
    /// bounding a hostile deeply-nested body.
    /// </summary>
    public const int MaxJsonDepth = 512;

    public static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = MaxJsonDepth };

    public static string ToIso(this DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString(TimestampFormat);

    /// <summary>UTC now, truncated to whole seconds so stored and rendered values agree.</summary>
    public static DateTime NowUtc()
    {
        DateTime now = DateTime.UtcNow;
        return new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }

    public static StudyRecord ToRecord(this Study study) =>
        new(study.Id,
            study.Name,
            study.StartFen,
            ParseTree(study.Tree),
            study.Pgn,
            study.CreatedAt.ToIso(),
            study.UpdatedAt.ToIso());

    /// <summary>
    /// Re-parses the stored jsonb text so the tree goes out as JSON rather than a JSON-escaped
    /// string. An empty column reads as null; malformed content is NOT swallowed — Postgres
    /// validates jsonb on write, so a parse failure here means a real bug, and a study that
    /// silently opens empty is a study the next save overwrites with nothing.
    /// </summary>
    public static JsonElement ParseTree(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return NullElement();
        }

        using JsonDocument document = JsonDocument.Parse(raw, DocumentOptions);
        return document.RootElement.Clone();
    }

    private static JsonElement NullElement()
    {
        using JsonDocument document = JsonDocument.Parse("null");
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Number of move nodes in the tree, not counting the root position node. Iterative walk over
    /// `children`, matching the Python implementation node for node.
    /// </summary>
    public static int CountMoves(string? rawTree)
    {
        JsonElement tree = ParseTree(rawTree);
        if (tree.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        int count = -1; // the root does not count as a move

        Stack<JsonElement> stack = new();
        stack.Push(tree);

        while (stack.Count > 0)
        {
            JsonElement node = stack.Pop();
            if (node.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            count++;

            if (node.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in children.EnumerateArray())
                {
                    stack.Push(child);
                }
            }
        }

        return Math.Max(count, 0);
    }
}
