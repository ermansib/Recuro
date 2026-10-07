using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recuro.Config.Infrastructure.Persistence.Migrations
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
                name: "rule_set_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    matrix_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    proposed_by_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    proposed_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decided_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rule_set_versions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL AND failed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rule_set_versions_tenant_id",
                table: "rule_set_versions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rule_set_versions_tenant_id_matrix_type_number",
                table: "rule_set_versions",
                columns: new[] { "tenant_id", "matrix_type", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rule_set_versions_tenant_id_matrix_type_status_effective_fr",
                table: "rule_set_versions",
                columns: new[] { "tenant_id", "matrix_type", "status", "effective_from" });

            // RCU-CFG-001/003: an approved or rejected version is immutable at the data layer too, so a
            // pinned workflow always re-reads exactly the rules it started under.
            migrationBuilder.Sql("""
                CREATE FUNCTION rule_set_versions_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD.status <> 'Draft' THEN
                        RAISE EXCEPTION 'rule set version % is % and immutable (% refused)', OLD.id, OLD.status, TG_OP
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
                END;
                $$;

                CREATE TRIGGER rule_set_versions_immutable BEFORE UPDATE OR DELETE ON rule_set_versions
                    FOR EACH ROW EXECUTE FUNCTION rule_set_versions_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS rule_set_versions_immutable ON rule_set_versions;
                DROP FUNCTION IF EXISTS rule_set_versions_guard();
                """);

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "rule_set_versions");
        }
    }
}
