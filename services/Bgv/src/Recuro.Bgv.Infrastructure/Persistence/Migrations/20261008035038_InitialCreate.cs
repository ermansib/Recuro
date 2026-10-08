using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Recuro.Bgv.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bgv_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    req_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    vendor_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    vendor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    vendor_case_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    consent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consent_text_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    consent_source = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    grade = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    role_flags = table.Column<string[]>(type: "text[]", nullable: false),
                    scope = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    matrix_version_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    initiated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    initiated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tat_working_days = table.Column<int>(type: "integer", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    adverse_check = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    adverse_description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    adverse_action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    adverse_flagged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    adverse_flagged_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    adverse_workflow_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    resolution_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    resolution_decided_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resolution_decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cleared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    needs_reassignment = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bgv_cases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bgv_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    req_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    at_bgv = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bgv_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.event_id, x.consumer });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    envelope = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bgv_checks",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: true),
                    sensitive_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bgv_checks", x => x.id);
                    table.ForeignKey(
                        name: "fk_bgv_checks_bgv_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "bgv_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bgv_cases_tenant_id",
                table: "bgv_cases",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bgv_cases_tenant_id_app_id",
                table: "bgv_cases",
                columns: new[] { "tenant_id", "app_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bgv_cases_tenant_id_status_due_at",
                table: "bgv_cases",
                columns: new[] { "tenant_id", "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bgv_cases_tenant_id_vendor_id_status",
                table: "bgv_cases",
                columns: new[] { "tenant_id", "vendor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_bgv_checks_case_id",
                table: "bgv_checks",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_bgv_requests_tenant_id",
                table: "bgv_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bgv_requests_tenant_id_app_id",
                table: "bgv_requests",
                columns: new[] { "tenant_id", "app_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL AND failed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bgv_checks");

            migrationBuilder.DropTable(
                name: "bgv_requests");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "bgv_cases");
        }
    }
}
