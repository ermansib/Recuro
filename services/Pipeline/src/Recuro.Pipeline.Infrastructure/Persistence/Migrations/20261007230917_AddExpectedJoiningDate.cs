using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recuro.Pipeline.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpectedJoiningDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expected_joining_date",
                table: "applications",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expected_joining_date",
                table: "applications");
        }
    }
}
