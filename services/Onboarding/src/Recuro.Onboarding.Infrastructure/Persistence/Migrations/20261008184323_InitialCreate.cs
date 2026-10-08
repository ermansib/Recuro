using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Recuro.Onboarding.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bgv_tracks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    case_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bgv_tracks", x => x.id);
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
                name: "onboarding_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    offer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    req_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    joining_date = table.Column<DateOnly>(type: "date", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rules_version_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    probation_months = table.Column<int>(type: "integer", nullable: false),
                    probation_ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    probation_cycle = table.Column<int>(type: "integer", nullable: false),
                    reporting_manager_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reporting_manager = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    buddy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    day1ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    file_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    file_completed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_cases", x => x.id);
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
                name: "onboarding_checklist_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    done = table.Column<bool>(type: "boolean", nullable: false),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_onboarding_checklist_items_onboarding_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "onboarding_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "onboarding_documents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    size = table.Column<long>(type: "bigint", nullable: true),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uploaded_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_onboarding_documents_onboarding_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "onboarding_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "onboarding_milestones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cycle = table.Column<int>(type: "integer", nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2200)", maxLength: 2200, nullable: false),
                    ticket_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_onboarding_milestones_onboarding_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "onboarding_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "onboarding_probation_decisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cycle = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    extended_by_months = table.Column<int>(type: "integer", nullable: true),
                    new_probation_end = table.Column<DateOnly>(type: "date", nullable: true),
                    decided_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    decided_by_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_probation_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_onboarding_probation_decisions_onboarding_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "onboarding_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bgv_tracks_tenant_id",
                table: "bgv_tracks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bgv_tracks_tenant_id_app_id",
                table: "bgv_tracks",
                columns: new[] { "tenant_id", "app_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_cases_tenant_id",
                table: "onboarding_cases",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_cases_tenant_id_app_id",
                table: "onboarding_cases",
                columns: new[] { "tenant_id", "app_id" });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_cases_tenant_id_status_joining_date",
                table: "onboarding_cases",
                columns: new[] { "tenant_id", "status", "joining_date" });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_checklist_items_case_id",
                table: "onboarding_checklist_items",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_documents_case_id",
                table: "onboarding_documents",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_milestones_case_id",
                table: "onboarding_milestones",
                column: "case_id");

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_milestones_status_due_on",
                table: "onboarding_milestones",
                columns: new[] { "status", "due_on" });

            migrationBuilder.CreateIndex(
                name: "ix_onboarding_probation_decisions_case_id",
                table: "onboarding_probation_decisions",
                column: "case_id");

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
                name: "bgv_tracks");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "onboarding_checklist_items");

            migrationBuilder.DropTable(
                name: "onboarding_documents");

            migrationBuilder.DropTable(
                name: "onboarding_milestones");

            migrationBuilder.DropTable(
                name: "onboarding_probation_decisions");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "onboarding_cases");
        }
    }
}
