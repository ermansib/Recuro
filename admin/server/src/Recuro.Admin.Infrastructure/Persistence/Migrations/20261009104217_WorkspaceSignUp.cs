using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recuro.Admin.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceSignUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CareersTagline",
                table: "tenants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "tenants",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "INR");

            migrationBuilder.AddColumn<string>(
                name: "EmailDomain",
                table: "tenants",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "tenants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "en-IN");

            migrationBuilder.AddColumn<string>(
                name: "MfaRoles",
                table: "tenants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "hrhead,mdceo");

            migrationBuilder.AddColumn<string>(
                name: "OrgType",
                table: "tenants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SmallBusiness");

            migrationBuilder.AddColumn<int>(
                name: "SessionIdleMinutes",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<string>(
                name: "SsoProviders",
                table: "tenants",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "google,microsoft");

            // Existing tenants get their org type's starting settings (OrgTypeDefaults).
            migrationBuilder.Sql(
                """
                UPDATE tenants SET "OrgType" = 'Agency', "CareersTagline" = 'Find your next role through us' WHERE "Kind" = 'Agency';
                UPDATE tenants SET "CareersTagline" = 'Build something great with us' WHERE "Kind" <> 'Agency';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CareersTagline",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "EmailDomain",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "Locale",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "MfaRoles",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "OrgType",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "SessionIdleMinutes",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "SsoProviders",
                table: "tenants");
        }
    }
}
