using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recuro.Offer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_tracks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    req_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stage_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_tracks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bgv_tracks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    gate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    blockers = table.Column<string>(type: "jsonb", nullable: false),
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
                name: "offer_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    letter_version = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offer_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    req_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    candidate_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    designation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    grade = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    reporting_manager = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    joining_date = table.Column<DateOnly>(type: "date", nullable: false),
                    probation_months = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    letter_version = table.Column<int>(type: "integer", nullable: false),
                    route = table.Column<string>(type: "jsonb", nullable: true),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_chase_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    chase_every = table.Column<TimeSpan>(type: "interval", nullable: false),
                    chase_count = table.Column<int>(type: "integer", nullable: false),
                    conditional = table.Column<bool>(type: "boolean", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    trail = table.Column<string>(type: "jsonb", nullable: false),
                    band_max = table.Column<decimal>(type: "numeric(10,1)", precision: 10, scale: 1, nullable: false),
                    band_min = table.Column<decimal>(type: "numeric(10,1)", precision: 10, scale: 1, nullable: false),
                    ctc_benefits = table.Column<decimal>(type: "numeric(10,1)", precision: 10, scale: 1, nullable: false),
                    ctc_fixed = table.Column<decimal>(type: "numeric(10,1)", precision: 10, scale: 1, nullable: false),
                    ctc_variable = table.Column<decimal>(type: "numeric(10,1)", precision: 10, scale: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offers", x => x.id);
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

            migrationBuilder.CreateIndex(
                name: "ix_application_tracks_tenant_id",
                table: "application_tracks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_application_tracks_tenant_id_app_id",
                table: "application_tracks",
                columns: new[] { "tenant_id", "app_id" },
                unique: true);

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
                name: "ix_offer_documents_tenant_id",
                table: "offer_documents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_offer_documents_tenant_id_offer_id_kind_letter_version",
                table: "offer_documents",
                columns: new[] { "tenant_id", "offer_id", "kind", "letter_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_offers_state_next_chase_at",
                table: "offers",
                columns: new[] { "state", "next_chase_at" });

            migrationBuilder.CreateIndex(
                name: "ix_offers_tenant_id",
                table: "offers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_offers_tenant_id_app_id",
                table: "offers",
                columns: new[] { "tenant_id", "app_id" });

            migrationBuilder.CreateIndex(
                name: "ix_offers_tenant_id_candidate_id",
                table: "offers",
                columns: new[] { "tenant_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "ix_offers_tenant_id_state",
                table: "offers",
                columns: new[] { "tenant_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_offers_workflow_instance_id",
                table: "offers",
                column: "workflow_instance_id");

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
                name: "application_tracks");

            migrationBuilder.DropTable(
                name: "bgv_tracks");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "offer_documents");

            migrationBuilder.DropTable(
                name: "offers");

            migrationBuilder.DropTable(
                name: "outbox_messages");
        }
    }
}
