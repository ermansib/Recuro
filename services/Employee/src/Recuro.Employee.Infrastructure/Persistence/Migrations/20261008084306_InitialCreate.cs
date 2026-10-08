using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recuro.Employee.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ijp_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    req_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    department = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    grade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    eligible_grades = table.Column<string[]>(type: "text[]", nullable: false),
                    min_tenure_months = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    opens_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ijp_postings", x => x.id);
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
                name: "intake_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    employee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    req_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    candidate_created_here = table.Column<bool>(type: "boolean", nullable: false),
                    app_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    pipeline_stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    progress = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    posting_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    declared_grade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: true),
                    candidate_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    relationship = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    coi_accepted = table.Column<bool>(type: "boolean", nullable: true),
                    coi_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bonus_eligible = table.Column<bool>(type: "boolean", nullable: true),
                    bonus_payable = table.Column<bool>(type: "boolean", nullable: true),
                    duplicate_of = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_intake_records", x => x.id);
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
                name: "sourcing_gates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    req_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    was_cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    unlocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sourcing_gates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ijp_postings_tenant_id",
                table: "ijp_postings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ijp_postings_tenant_id_closes_at",
                table: "ijp_postings",
                columns: new[] { "tenant_id", "closes_at" },
                filter: "NOT withdrawn");

            migrationBuilder.CreateIndex(
                name: "ix_ijp_postings_tenant_id_req_id",
                table: "ijp_postings",
                columns: new[] { "tenant_id", "req_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_intake_records_tenant_id",
                table: "intake_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_intake_records_tenant_id_app_id",
                table: "intake_records",
                columns: new[] { "tenant_id", "app_id" },
                unique: true,
                filter: "app_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_intake_records_tenant_id_employee_id_submitted_at",
                table: "intake_records",
                columns: new[] { "tenant_id", "employee_id", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL AND failed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sourcing_gates_tenant_id",
                table: "sourcing_gates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sourcing_gates_tenant_id_req_id",
                table: "sourcing_gates",
                columns: new[] { "tenant_id", "req_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ijp_postings");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "intake_records");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "sourcing_gates");
        }
    }
}
