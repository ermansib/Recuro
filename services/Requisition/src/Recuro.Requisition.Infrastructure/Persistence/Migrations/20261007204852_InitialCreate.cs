using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recuro.Requisition.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "job_descriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    req_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    history = table.Column<string>(type: "jsonb", nullable: false),
                    assessments = table.Column<string>(type: "jsonb", nullable: false),
                    benchmark = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    competencies = table.Column<string>(type: "jsonb", nullable: false),
                    experience = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    grade = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    location = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    min_qualification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    purpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reports_to = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    responsibilities = table.Column<string>(type: "jsonb", nullable: false),
                    team_size = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_descriptions", x => x.id);
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
                name: "req_id_sequences",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_req_id_sequences", x => new { x.tenant_id, x.year });
                });

            migrationBuilder.CreateTable(
                name: "requisitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    req_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    owner_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    target_closure = table.Column<DateOnly>(type: "date", nullable: true),
                    config_version_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    state_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    band = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    designation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    employment_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    grade = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    joining_date = table.Column<DateOnly>(type: "date", nullable: true),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nature = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    oob_justification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    out_of_budget = table.Column<bool>(type: "boolean", nullable: false),
                    positions = table.Column<int>(type: "integer", nullable: false),
                    qualifications = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    replacement_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    reporting_manager = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sourcing_channels = table.Column<string>(type: "jsonb", nullable: false),
                    route_approver_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    route_approving = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    route_initiating = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    route_recommending = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requisitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_descriptions_tenant_id",
                table: "job_descriptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_descriptions_tenant_id_req_id",
                table: "job_descriptions",
                columns: new[] { "tenant_id", "req_id" });

            migrationBuilder.CreateIndex(
                name: "ix_job_descriptions_tenant_id_requisition_id",
                table: "job_descriptions",
                columns: new[] { "tenant_id", "requisition_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL AND failed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_req_id_sequences_tenant_id",
                table: "req_id_sequences",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisitions_tenant_id",
                table: "requisitions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_requisitions_tenant_id_created_at",
                table: "requisitions",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_requisitions_tenant_id_req_id",
                table: "requisitions",
                columns: new[] { "tenant_id", "req_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requisitions_tenant_id_state",
                table: "requisitions",
                columns: new[] { "tenant_id", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "job_descriptions");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "req_id_sequences");

            migrationBuilder.DropTable(
                name: "requisitions");
        }
    }
}
