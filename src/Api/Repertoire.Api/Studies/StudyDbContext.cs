using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Repertoire.Api.Studies;

public class StudyDbContext(DbContextOptions<StudyDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Study> Studies => Set<Study>();

    /// <summary>
    /// The ASP.NET Data Protection key ring — what the <c>repertoire.auth</c> cookie and the OIDC
    /// correlation/nonce cookies are encrypted with. Held here rather than in the container, so a
    /// redeploy does not sign everyone out. See <c>DataProtectionExtensions</c>.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Study>(study =>
        {
            study.ToTable("studies");

            study.HasKey(s => s.Id);
            study.Property(s => s.Id).HasColumnName("id").UseIdentityByDefaultColumn();

            // 255: the longest `sub` OIDC Core allows (§2, "MUST NOT exceed 255 ASCII characters").
            study.Property(s => s.Owner).HasColumnName("owner").HasMaxLength(255).IsRequired();

            study.Property(s => s.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            study.Property(s => s.StartFen).HasColumnName("start_fen").IsRequired().HasDefaultValue(string.Empty);

            // jsonb, not json/text: it is the whole reason for moving to Postgres — the move tree
            // becomes queryable and indexable later ("every study containing this position")
            // instead of being an opaque blob the app has to pull down and walk.
            study.Property(s => s.Tree).HasColumnName("tree").HasColumnType("jsonb").IsRequired().HasDefaultValue("null");

            study.Property(s => s.Pgn).HasColumnName("pgn").IsRequired().HasDefaultValue(string.Empty);

            study.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
            study.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // The listing is one owner's studies, updated_at desc, id desc, on every page load: an
            // equality on owner then the whole sort, so Postgres reads it straight off the index
            // (scanned backwards) with no sort step. Replaces the old global updated_at index,
            // which nothing filters or sorts by on its own any more.
            study.HasIndex(s => new { s.Owner, s.UpdatedAt, s.Id }).HasDatabaseName("ix_studies_owner_updated_at");
        });

        // The framework's own entity, renamed to match the snake_case of everything else here.
        modelBuilder.Entity<DataProtectionKey>(key =>
        {
            key.ToTable("data_protection_keys");
            key.HasKey(k => k.Id);
            key.Property(k => k.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            key.Property(k => k.FriendlyName).HasColumnName("friendly_name");
            key.Property(k => k.Xml).HasColumnName("xml");
        });
    }
}
