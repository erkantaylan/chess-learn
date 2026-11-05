using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Repertoire.Api.Studies;

namespace Repertoire.Api.Tests;

/// <summary>
/// AddStudyOwner on a database that already holds ownerless studies, the way production does —
/// on a database of its own in the shared container, since the app's is already fully migrated.
/// </summary>
[Collection(StudiesApiCollection.Name)]
public class StudyOwnerMigrationTests(StudiesApiFactory app)
{
    private const string BeforeOwners = "20261103233542_AddDataProtectionKeys";

    [Fact]
    public async Task Existing_ownerless_studies_are_deleted_and_owner_has_no_default()
    {
        string connection = new NpgsqlConnectionStringBuilder(app.ConnectionString)
        {
            Database = $"migration_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using StudyDbContext db = new(new DbContextOptionsBuilder<StudyDbContext>().UseNpgsql(connection).Options);
        IMigrator migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeOwners);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO studies (name, created_at, updated_at) VALUES ('Shared, before owners', now(), now())");

        await migrator.MigrateAsync();

        Assert.Equal(0, await db.Studies.CountAsync());

        // No '' default to fall back on: an insert that forgets the owner fails, loudly.
        PostgresException missingOwner = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO studies (name, created_at, updated_at) VALUES ('No owner', now(), now())"));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, missingOwner.SqlState);
    }
}
