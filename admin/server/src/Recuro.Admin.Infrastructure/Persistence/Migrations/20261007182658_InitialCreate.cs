using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Recuro.Admin.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "screen_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Module = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DefaultTitle = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DefaultSubtitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CanDisable = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_screen_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Plan = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CustomDomain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    ThemePresetKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ThemeMode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "theme_presets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    light_primary = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    light_secondary = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    light_accent = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    light_background = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    light_surface = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    light_text = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    dark_primary = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    dark_secondary = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    dark_accent = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    dark_background = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    dark_surface = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    dark_text = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_theme_presets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "field_definitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DefaultLabel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DataType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ScreenDefinitionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_field_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_field_definitions_screen_definitions_ScreenDefinitionId",
                        column: x => x.ScreenDefinitionId,
                        principalTable: "screen_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_screen_configurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScreenKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Subtitle = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_screen_configurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_screen_configurations_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_field_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FieldKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    TenantScreenConfigurationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_field_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tenant_field_settings_tenant_screen_configurations_TenantSc~",
                        column: x => x.TenantScreenConfigurationId,
                        principalTable: "tenant_screen_configurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_field_definitions_ScreenDefinitionId",
                table: "field_definitions",
                column: "ScreenDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_screen_definitions_Key",
                table: "screen_definitions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_field_settings_TenantScreenConfigurationId",
                table: "tenant_field_settings",
                column: "TenantScreenConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_screen_configurations_TenantId_ScreenKey",
                table: "tenant_screen_configurations",
                columns: new[] { "TenantId", "ScreenKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_CustomDomain",
                table: "tenants",
                column: "CustomDomain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_Slug",
                table: "tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_theme_presets_Key",
                table: "theme_presets",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "field_definitions");

            migrationBuilder.DropTable(
                name: "tenant_field_settings");

            migrationBuilder.DropTable(
                name: "theme_presets");

            migrationBuilder.DropTable(
                name: "screen_definitions");

            migrationBuilder.DropTable(
                name: "tenant_screen_configurations");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
