using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recuro.Onboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CaseDepartment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "department",
                table: "onboarding_cases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "department",
                table: "onboarding_cases");
        }
    }
}
