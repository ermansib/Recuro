using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recuro.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    entity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    before = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    after = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    config_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    previous_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_seals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    up_to_sequence = table.Column<long>(type: "bigint", nullable: false),
                    hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    sealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_seals", x => x.id);
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

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_tenant_id",
                table: "audit_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_tenant_id_correlation_id",
                table: "audit_entries",
                columns: new[] { "tenant_id", "correlation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_tenant_id_entity",
                table: "audit_entries",
                columns: new[] { "tenant_id", "entity" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_tenant_id_occurred_at",
                table: "audit_entries",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_tenant_id_sequence",
                table: "audit_entries",
                columns: new[] { "tenant_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_seals_tenant_id",
                table: "audit_seals",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_seals_tenant_id_up_to_sequence",
                table: "audit_seals",
                columns: new[] { "tenant_id", "up_to_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL AND failed_at IS NULL");

            // RCU-AUD-002: append-only at the data layer too. Any UPDATE, DELETE or TRUNCATE fails,
            // whatever code or person runs it.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_reject_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit records are append-only (% on %)', TG_OP, TG_TABLE_NAME
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER audit_entries_append_only BEFORE UPDATE OR DELETE ON audit_entries
                    FOR EACH ROW EXECUTE FUNCTION audit_reject_change();
                CREATE TRIGGER audit_entries_no_truncate BEFORE TRUNCATE ON audit_entries
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_reject_change();
                CREATE TRIGGER audit_seals_append_only BEFORE UPDATE OR DELETE ON audit_seals
                    FOR EACH ROW EXECUTE FUNCTION audit_reject_change();
                CREATE TRIGGER audit_seals_no_truncate BEFORE TRUNCATE ON audit_seals
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_reject_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_entries_append_only ON audit_entries;
                DROP TRIGGER IF EXISTS audit_entries_no_truncate ON audit_entries;
                DROP TRIGGER IF EXISTS audit_seals_append_only ON audit_seals;
                DROP TRIGGER IF EXISTS audit_seals_no_truncate ON audit_seals;
                DROP FUNCTION IF EXISTS audit_reject_change();
                """);

            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "audit_seals");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "outbox_messages");
        }
    }
}
