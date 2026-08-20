using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Repertoire.Api.Studies;

public static class StudyEndpoints
{
    private const int NameMaxLength = 200;

    public static IEndpointRouteBuilder MapStudyEndpoints(this IEndpointRouteBuilder routes)
    {
        // /api/health stays anonymous: the frontend probes it to decide whether a server-backed
        // library exists at all, before anyone has signed in.
        routes.MapGet("/health", async (StudyDbContext db, CancellationToken ct) =>
                  Results.Ok(new HealthResponse(true, await db.Studies.CountAsync(ct))))
              .AllowAnonymous()
              .WithName("Health");

        RouteGroupBuilder studies = routes.MapGroup("/studies").RequireAuthorization();

        studies.MapGet("/", async (StudyDbContext db, CancellationToken ct) =>
        {
            // Sorted most-recently-saved first. Only the columns the listing needs — no pgn, and
            // tree only because move_count is derived from it.
            var rows = await db.Studies
                               .AsNoTracking()
                               .OrderByDescending(s => s.UpdatedAt)
                               .ThenByDescending(s => s.Id)
                               .Select(s => new { s.Id, s.Name, s.Tree, s.CreatedAt, s.UpdatedAt })
                               .ToListAsync(ct);

            return Results.Ok(rows.Select(r => new StudyListRow(
                                       r.Id,
                                       r.Name,
                                       r.CreatedAt.ToIso(),
                                       r.UpdatedAt.ToIso(),
                                       StudyMapping.CountMoves(r.Tree)))
                                  .ToList());
        });

        studies.MapGet("/{id:int}", async (int id, StudyDbContext db, CancellationToken ct) =>
        {
            Study? study = await db.Studies.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
            return study is null ? NotFound(id) : Results.Ok(study.ToRecord());
        });

        studies.MapPost("/", async (StudyCreateRequest body, StudyDbContext db, CancellationToken ct) =>
        {
            if (Invalid(body.Name, required: true) is { } problem)
            {
                return problem;
            }

            DateTime now = StudyMapping.NowUtc();
            Study study = new()
            {
                Name = body.Name!,
                StartFen = body.StartFen ?? string.Empty,
                Tree = RawTree(body.Tree) ?? "null",
                Pgn = body.Pgn ?? string.Empty,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Studies.Add(study);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/studies/{study.Id}", study.ToRecord());
        });

        studies.MapPut("/{id:int}", async (int id, StudyUpdateRequest body, StudyDbContext db, CancellationToken ct) =>
        {
            if (Invalid(body.Name, required: false) is { } problem)
            {
                return problem;
            }

            Study? study = await db.Studies.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (study is null)
            {
                return NotFound(id);
            }

            // Partial: only fields actually present in the body are touched.
            if (body.Name is not null)
            {
                study.Name = body.Name;
            }

            if (body.StartFen is not null)
            {
                study.StartFen = body.StartFen;
            }

            if (RawTree(body.Tree) is { } tree)
            {
                study.Tree = tree;
            }

            if (body.Pgn is not null)
            {
                study.Pgn = body.Pgn;
            }

            // updated_at is always bumped, even when the body changed nothing.
            study.UpdatedAt = StudyMapping.NowUtc();
            await db.SaveChangesAsync(ct);

            return Results.Ok(study.ToRecord());
        });

        studies.MapDelete("/{id:int}", async (int id, StudyDbContext db, CancellationToken ct) =>
        {
            int deleted = await db.Studies.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? NotFound(id) : Results.NoContent();
        });

        return routes;
    }

    /// <summary>404 in the shape the frontend reads: it does JSON.parse(body).detail.</summary>
    private static IResult NotFound(int id) =>
        Results.Problem(detail: $"study {id} not found", statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// 422, not ASP.NET's usual 400 — docs/API.md promises 422 for a bad name, and the frontend
    /// surfaces `detail` verbatim in a toast.
    /// </summary>
    private static IResult? Invalid(string? name, bool required)
    {
        if (name is null)
        {
            return required
                ? Results.Problem(detail: "name is required", statusCode: StatusCodes.Status422UnprocessableEntity)
                : null;
        }

        if (name.Length is 0 or > NameMaxLength)
        {
            return Results.Problem(
                detail: $"name must be between 1 and {NameMaxLength} characters",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return null;
    }

    /// <summary>
    /// The tree as raw JSON text for the jsonb column, or null when the caller omitted it.
    /// A literal `null` in the body binds to null too, so it reads as "leave alone" — see
    /// StudyUpdateRequest.
    /// </summary>
    private static string? RawTree(JsonElement? tree) =>
        tree is { } element && element.ValueKind != JsonValueKind.Undefined
            ? element.GetRawText()
            : null;
}
