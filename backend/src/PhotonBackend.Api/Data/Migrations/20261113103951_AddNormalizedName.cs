using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotonBackend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Players",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            // D14: isi dari nama yang ada SEBELUM index unik. Nama kembar (case-insensitive) → yang lebih baru diberi
            // akhiran "-2", "-3", ... (DisplayName ikut, dipotong agar tetap ≤ 24 karakter).
            migrationBuilder.Sql(@"
WITH r AS (
  SELECT ""Id"", ""DisplayName"",
         row_number() OVER (PARTITION BY lower(btrim(""DisplayName"")) ORDER BY ""CreatedAt"", ""Id"") AS n
  FROM ""Players"")
UPDATE ""Players"" p SET
  ""DisplayName"" = CASE WHEN r.n = 1 THEN btrim(r.""DisplayName"")
                       ELSE left(btrim(r.""DisplayName""), 24 - length('-' || r.n)) || '-' || r.n END
FROM r WHERE p.""Id"" = r.""Id"";
UPDATE ""Players"" SET ""NormalizedName"" = lower(btrim(""DisplayName""));");

            migrationBuilder.CreateIndex(
                name: "IX_Players_NormalizedName",
                table: "Players",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Players_NormalizedName",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Players");
        }
    }
}
