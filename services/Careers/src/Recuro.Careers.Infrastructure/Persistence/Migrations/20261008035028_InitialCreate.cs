using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Recuro.Careers.Infrastructure.Persistence.Migrations
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
                name: "job_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    req_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    location_filter = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    experience = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    qualification = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    industry = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    visible_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_postings", x => x.id);
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
                name: "public_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    req_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    position = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    client_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    candidate_created_here = table.Column<bool>(type: "boolean", nullable: false),
                    duplicate_of = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    app_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    public_stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pipeline_stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    final_rejected_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    regret_send_at = table.Column<DateOnly>(type: "date", nullable: true),
                    regret_delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_public_applications", x => x.id);
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

            migrationBuilder.CreateTable(
                name: "job_posting_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    by_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    job_posting_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_posting_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_posting_history_job_postings_job_posting_id",
                        column: x => x.job_posting_id,
                        principalTable: "job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_posting_tags",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    text = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    tone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    job_posting_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_posting_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_posting_tags_job_postings_job_posting_id",
                        column: x => x.job_posting_id,
                        principalTable: "job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "public_application_consents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    text_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_public_application_consents", x => x.id);
                    table.ForeignKey(
                        name: "fk_public_application_consents_public_applications_application",
                        column: x => x.application_id,
                        principalTable: "public_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_posting_history_job_posting_id",
                table: "job_posting_history",
                column: "job_posting_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_posting_tags_job_posting_id",
                table: "job_posting_tags",
                column: "job_posting_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_tenant_id",
                table: "job_postings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_tenant_id_posting_id",
                table: "job_postings",
                columns: new[] { "tenant_id", "posting_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_tenant_id_req_id",
                table: "job_postings",
                columns: new[] { "tenant_id", "req_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_postings_tenant_id_visible_from_posting_id",
                table: "job_postings",
                columns: new[] { "tenant_id", "visible_from", "posting_id" },
                filter: "status = 'Published'");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL AND failed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_public_application_consents_application_id",
                table: "public_application_consents",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_public_applications_final_rejected_event_id",
                table: "public_applications",
                column: "final_rejected_event_id",
                filter: "final_rejected_event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_public_applications_tenant_id",
                table: "public_applications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_public_applications_tenant_id_app_id",
                table: "public_applications",
                columns: new[] { "tenant_id", "app_id" },
                unique: true,
                filter: "app_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_public_applications_tenant_id_client_key",
                table: "public_applications",
                columns: new[] { "tenant_id", "client_key" },
                unique: true,
                filter: "client_key IS NOT NULL");

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
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "job_posting_history");

            migrationBuilder.DropTable(
                name: "job_posting_tags");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "public_application_consents");

            migrationBuilder.DropTable(
                name: "sourcing_gates");

            migrationBuilder.DropTable(
                name: "job_postings");

            migrationBuilder.DropTable(
                name: "public_applications");
        }
    }
}
