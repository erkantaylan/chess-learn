using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repertoire.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStudyOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Studies become per-user, and the ones already here were saved before anyone was
            // recorded as their owner. There is no honest owner to backfill (one shared library,
            // possibly several people writing to it), so they go — agreed, not an oversight.
            // Emptying the table first is also what lets owner be NOT NULL with no default: a
            // default of '' would quietly accept any future insert that forgot to set an owner,
            // and file it under one shared nobody.
            migrationBuilder.Sql("DELETE FROM studies;");

            migrationBuilder.DropIndex(
                name: "ix_studies_updated_at",
                table: "studies");

            migrationBuilder.AddColumn<string>(
                name: "owner",
                table: "studies",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_studies_owner_updated_at",
                table: "studies",
                columns: new[] { "owner", "updated_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the shared, ownerless table. The studies Up deleted do not come back.
            migrationBuilder.DropIndex(
                name: "ix_studies_owner_updated_at",
                table: "studies");

            migrationBuilder.DropColumn(
                name: "owner",
                table: "studies");

            migrationBuilder.CreateIndex(
                name: "ix_studies_updated_at",
                table: "studies",
                column: "updated_at");
        }
    }
}
