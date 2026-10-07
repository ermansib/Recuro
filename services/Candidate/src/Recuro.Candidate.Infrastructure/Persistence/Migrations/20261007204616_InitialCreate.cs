using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Recuro.Candidate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    email_fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    phone_fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    experience_years = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    current_ctc = table.Column<string>(type: "text", nullable: true),
                    expected_ctc = table.Column<string>(type: "text", nullable: true),
                    notice_days = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    referrer_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    consultant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    channel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    resume_file_name = table.Column<string>(type: "text", nullable: true),
                    resume_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    resume_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    resume_storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resume_uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    legal_hold = table.Column<bool>(type: "boolean", nullable: false),
                    legal_hold_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    retention_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    retain_until = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidates", x => x.id);
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
                name: "candidate_applications",
                columns: table => new
                {
                    app_id = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    retain_until = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_applications", x => new { x.candidate_id, x.app_id });
                    table.ForeignKey(
                        name: "fk_candidate_applications_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "candidate_consents",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    text_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_consents", x => x.id);
                    table.ForeignKey(
                        name: "fk_candidate_consents_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_candidate_consents_candidate_id",
                table: "candidate_consents",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidates_tenant_id",
                table: "candidates",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_candidates_tenant_id_email_fingerprint",
                table: "candidates",
                columns: new[] { "tenant_id", "email_fingerprint" });

            migrationBuilder.CreateIndex(
                name: "ix_candidates_tenant_id_phone_fingerprint",
                table: "candidates",
                columns: new[] { "tenant_id", "phone_fingerprint" });

            migrationBuilder.CreateIndex(
                name: "ix_candidates_tenant_id_retention_status_retain_until",
                table: "candidates",
                columns: new[] { "tenant_id", "retention_status", "retain_until" });

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
                name: "candidate_applications");

            migrationBuilder.DropTable(
                name: "candidate_consents");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "candidates");
        }
    }
}
