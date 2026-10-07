using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recuro.Workflow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_reminder_at",
                table: "approval_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reminders",
                table: "approval_tasks",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateIndex(
                name: "ix_approval_tasks_next_reminder_at",
                table: "approval_tasks",
                column: "next_reminder_at",
                filter: "next_reminder_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_approval_tasks_next_reminder_at",
                table: "approval_tasks");

            migrationBuilder.DropColumn(
                name: "next_reminder_at",
                table: "approval_tasks");

            migrationBuilder.DropColumn(
                name: "reminders",
                table: "approval_tasks");
        }
    }
}
