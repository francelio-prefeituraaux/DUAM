using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automacao.DuamApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTipoInscricaoToJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tipo_inscricao",
                table: "jobs",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tipo_inscricao",
                table: "jobs");
        }
    }
}
