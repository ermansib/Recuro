using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Recuro.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    bounced = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "directory_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    roles = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_directory_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recipient_user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    recipient_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    to_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    to_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    from_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cta = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    signature = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    template_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    matrix_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    critical = table.Column<bool>(type: "boolean", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paragraphs = table.Column<string[]>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feed_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    recipient_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recipient_user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    recipient_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    icon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    matrix_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    critical = table.Column<bool>(type: "boolean", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_items", x => x.id);
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
                name: "read_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_read_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subject_owners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subject_owners", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contact_statuses_tenant_id",
                table: "contact_statuses",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_statuses_tenant_id_address",
                table: "contact_statuses",
                columns: new[] { "tenant_id", "address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_directory_users_tenant_id",
                table: "directory_users",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_directory_users_tenant_id_user_id",
                table: "directory_users",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_status_next_attempt_at",
                table: "email_messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_tenant_id",
                table: "email_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_tenant_id_created_at",
                table: "email_messages",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_tenant_id_recipient_user_id_created_at",
                table: "email_messages",
                columns: new[] { "tenant_id", "recipient_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_tenant_id_source_event_id_template_key_recip",
                table: "email_messages",
                columns: new[] { "tenant_id", "source_event_id", "template_key", "recipient_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feed_items_sequence",
                table: "feed_items",
                column: "sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feed_items_tenant_id",
                table: "feed_items",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_feed_items_tenant_id_recipient_role_sequence",
                table: "feed_items",
                columns: new[] { "tenant_id", "recipient_role", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_feed_items_tenant_id_recipient_user_id_sequence",
                table: "feed_items",
                columns: new[] { "tenant_id", "recipient_user_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_feed_items_tenant_id_source_event_id_template_key_recipient",
                table: "feed_items",
                columns: new[] { "tenant_id", "source_event_id", "template_key", "recipient_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL AND failed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_read_receipts_tenant_id",
                table: "read_receipts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_read_receipts_tenant_id_user_id_item_id",
                table: "read_receipts",
                columns: new[] { "tenant_id", "user_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subject_owners_tenant_id",
                table: "subject_owners",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_subject_owners_tenant_id_subject",
                table: "subject_owners",
                columns: new[] { "tenant_id", "subject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_statuses");

            migrationBuilder.DropTable(
                name: "directory_users");

            migrationBuilder.DropTable(
                name: "email_messages");

            migrationBuilder.DropTable(
                name: "feed_items");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "read_receipts");

            migrationBuilder.DropTable(
                name: "subject_owners");
        }
    }
}
