using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Recuro.Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_facts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    req_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    furthest_stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    screened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    interviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    bgv_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    offered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    offer_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    offer_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    offer_declined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    joining_date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_facts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feedback_facts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    interviewer_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    app_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    within_sla = table.Column<bool>(type: "boolean", nullable: true),
                    overdue_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_facts", x => x.id);
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
                name: "kpi_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cadence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    definitions_version = table.Column<int>(type: "integer", nullable: false),
                    up_to_sequence = table.Column<long>(type: "bigint", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    computed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kpi_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "metric_definition_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    definitions = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_metric_definition_sets", x => x.id);
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
                name: "projection_checkpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    projection = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projection_checkpoints", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recruitment_costs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recruitment_costs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_packs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cadence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    recipient_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    pdf = table.Column<byte[]>(type: "bytea", nullable: false),
                    csv = table.Column<string>(type: "text", nullable: false),
                    delivery = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ready_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_packs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    monthly_pack = table.Column<bool>(type: "boolean", nullable: false),
                    monthly_recipient_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quarterly_pack = table.Column<bool>(type: "boolean", nullable: false),
                    quarterly_recipient_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "requisition_facts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    req_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    grade = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    budget_status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sourcing_unlocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisition_facts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_facts_tenant_id",
                table: "application_facts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_application_facts_tenant_id_app_id",
                table: "application_facts",
                columns: new[] { "tenant_id", "app_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_application_facts_tenant_id_candidate_id",
                table: "application_facts",
                columns: new[] { "tenant_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_application_facts_tenant_id_created_at",
                table: "application_facts",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_application_facts_tenant_id_offer_accepted_at",
                table: "application_facts",
                columns: new[] { "tenant_id", "offer_accepted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_facts_tenant_id",
                table: "feedback_facts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_facts_tenant_id_interview_id_interviewer_id",
                table: "feedback_facts",
                columns: new[] { "tenant_id", "interview_id", "interviewer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feedback_facts_tenant_id_overdue_at",
                table: "feedback_facts",
                columns: new[] { "tenant_id", "overdue_at" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_facts_tenant_id_submitted_at",
                table: "feedback_facts",
                columns: new[] { "tenant_id", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_kpi_snapshots_tenant_id",
                table: "kpi_snapshots",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_kpi_snapshots_tenant_id_period_revision",
                table: "kpi_snapshots",
                columns: new[] { "tenant_id", "period", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_metric_definition_sets_tenant_id",
                table: "metric_definition_sets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_metric_definition_sets_tenant_id_version",
                table: "metric_definition_sets",
                columns: new[] { "tenant_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL AND failed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_projection_checkpoints_tenant_id",
                table: "projection_checkpoints",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_projection_checkpoints_tenant_id_projection",
                table: "projection_checkpoints",
                columns: new[] { "tenant_id", "projection" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recruitment_costs_tenant_id",
                table: "recruitment_costs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_recruitment_costs_tenant_id_month",
                table: "recruitment_costs",
                columns: new[] { "tenant_id", "month" });

            migrationBuilder.CreateIndex(
                name: "ix_report_events_sequence",
                table: "report_events",
                column: "sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_events_tenant_id",
                table: "report_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_events_tenant_id_sequence",
                table: "report_events",
                columns: new[] { "tenant_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_report_packs_ready_event_id",
                table: "report_packs",
                column: "ready_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_packs_tenant_id",
                table: "report_packs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_packs_tenant_id_period_cadence_recipient_role",
                table: "report_packs",
                columns: new[] { "tenant_id", "period", "cadence", "recipient_role" });

            migrationBuilder.CreateIndex(
                name: "ix_report_settings_tenant_id",
                table: "report_settings",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requisition_facts_tenant_id",
                table: "requisition_facts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisition_facts_tenant_id_approved_at",
                table: "requisition_facts",
                columns: new[] { "tenant_id", "approved_at" });

            migrationBuilder.CreateIndex(
                name: "ix_requisition_facts_tenant_id_req_id",
                table: "requisition_facts",
                columns: new[] { "tenant_id", "req_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_facts");

            migrationBuilder.DropTable(
                name: "feedback_facts");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "kpi_snapshots");

            migrationBuilder.DropTable(
                name: "metric_definition_sets");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "projection_checkpoints");

            migrationBuilder.DropTable(
                name: "recruitment_costs");

            migrationBuilder.DropTable(
                name: "report_events");

            migrationBuilder.DropTable(
                name: "report_packs");

            migrationBuilder.DropTable(
                name: "report_settings");

            migrationBuilder.DropTable(
                name: "requisition_facts");
        }
    }
}
