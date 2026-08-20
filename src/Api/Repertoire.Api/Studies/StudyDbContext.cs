using Microsoft.EntityFrameworkCore;

namespace Repertoire.Api.Studies;

public class StudyDbContext(DbContextOptions<StudyDbContext> options) : DbContext(options)
{
    public DbSet<Study> Studies => Set<Study>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Study>(study =>
        {
            study.ToTable("studies");

            study.HasKey(s => s.Id);
            study.Property(s => s.Id).HasColumnName("id").UseIdentityByDefaultColumn();

            study.Property(s => s.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            study.Property(s => s.StartFen).HasColumnName("start_fen").IsRequired().HasDefaultValue(string.Empty);

            // jsonb, not json/text: it is the whole reason for moving to Postgres — the move tree
            // becomes queryable and indexable later ("every study containing this position")
            // instead of being an opaque blob the app has to pull down and walk.
            study.Property(s => s.Tree).HasColumnName("tree").HasColumnType("jsonb").IsRequired().HasDefaultValue("null");

            study.Property(s => s.Pgn).HasColumnName("pgn").IsRequired().HasDefaultValue(string.Empty);

            study.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
            study.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // The listing sorts by updated_at desc, id desc on every page load.
            study.HasIndex(s => s.UpdatedAt).HasDatabaseName("ix_studies_updated_at");
        });
    }
}
