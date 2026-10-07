using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Recuro.Pipeline.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_counters",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_counters", x => new { x.tenant_id, x.year });
                });

            migrationBuilder.CreateTable(
                name: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    req_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    rejection_regret_due_by = table.Column<DateOnly>(type: "date", nullable: true),
                    rejection_retain_until = table.Column<DateOnly>(type: "date", nullable: true),
                    stage_entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    held_from_stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    held_from_entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    held_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tat_breached_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applications", x => x.id);
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
                name: "sourcing_gates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    req_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_open = table.Column<bool>(type: "boolean", nullable: false),
                    target_closure = table.Column<DateOnly>(type: "date", nullable: true),
                    was_cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sourcing_gates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "application_stage_moves",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    by_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_stage_moves", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_stage_moves_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_counters_tenant_id",
                table: "application_counters",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_application_stage_moves_application_id",
                table: "application_stage_moves",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_applications_stage_stage_entered_at",
                table: "applications",
                columns: new[] { "stage", "stage_entered_at" },
                filter: "tat_breached_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_applications_tenant_id",
                table: "applications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_applications_tenant_id_app_id",
                table: "applications",
                columns: new[] { "tenant_id", "app_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_applications_tenant_id_req_id_candidate_id",
                table: "applications",
                columns: new[] { "tenant_id", "req_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_applications_tenant_id_req_id_stage",
                table: "applications",
                columns: new[] { "tenant_id", "req_id", "stage" });

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
                name: "application_counters");

            migrationBuilder.DropTable(
                name: "application_stage_moves");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "sourcing_gates");

            migrationBuilder.DropTable(
                name: "applications");
        }
    }
}
