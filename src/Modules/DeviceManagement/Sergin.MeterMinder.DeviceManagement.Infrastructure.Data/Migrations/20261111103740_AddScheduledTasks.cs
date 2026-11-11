using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddScheduledTasks : Migration
{
    private static readonly string[] RunItemStatusLastActivityColumns = ["status", "last_activity_at_utc"];
    private static readonly string[] RunTaskIdStartedAtColumns = ["task_id", "started_at_utc"];
    private static readonly bool[] RunTaskIdStartedAtDescending = [false, true];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "scheduled_tasks",
            schema: "dm",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                command_name = table.Column<string>(type: "text", nullable: false),
                arguments = table.Column<string>(type: "jsonb", nullable: false),
                cron = table.Column<string>(type: "text", nullable: false),
                time_zone_id = table.Column<string>(type: "text", nullable: false),
                state = table.Column<string>(type: "text", nullable: false),
                delegating_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                engine_trigger_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                row_version = table.Column<Guid>(type: "uuid", nullable: false),
                scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                scope_path = table.Column<string>(type: "ltree", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_scheduled_tasks", x => x.id);
                table.CheckConstraint("ck_scheduled_tasks_soft_delete", "(deleted_at_utc IS NULL) = (deleted_by IS NULL)");
            });

        migrationBuilder.CreateTable(
            name: "scheduled_task_runs",
            schema: "dm",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                task_id = table.Column<Guid>(type: "uuid", nullable: true),
                trigger = table.Column<string>(type: "text", nullable: false),
                acting_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                scope_path = table.Column<string>(type: "ltree", nullable: false),
                command_name = table.Column<string>(type: "text", nullable: false),
                arguments = table.Column<string>(type: "jsonb", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                total = table.Column<int>(type: "integer", nullable: false),
                succeeded = table.Column<int>(type: "integer", nullable: false),
                failed = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_scheduled_task_runs", x => x.id);
                table.ForeignKey(
                    name: "fk_scheduled_task_runs_scheduled_tasks_task_id",
                    column: x => x.task_id,
                    principalSchema: "dm",
                    principalTable: "scheduled_tasks",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "scheduled_task_targets",
            schema: "dm",
            columns: table => new
            {
                target_id = table.Column<Guid>(type: "uuid", nullable: false),
                task_id = table.Column<Guid>(type: "uuid", nullable: false),
                root_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_scheduled_task_targets", x => new { x.task_id, x.target_id });
                table.ForeignKey(
                    name: "fk_scheduled_task_targets_scheduled_tasks_task_id",
                    column: x => x.task_id,
                    principalSchema: "dm",
                    principalTable: "scheduled_tasks",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "scheduled_task_run_items",
            schema: "dm",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                target_id = table.Column<Guid>(type: "uuid", nullable: false),
                root_id = table.Column<Guid>(type: "uuid", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                error_code = table.Column<string>(type: "text", nullable: true),
                error_description = table.Column<string>(type: "text", nullable: true),
                started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                engine_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                last_activity_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_scheduled_task_run_items", x => x.id);
                table.ForeignKey(
                    name: "fk_scheduled_task_run_items_scheduled_task_runs_run_id",
                    column: x => x.run_id,
                    principalSchema: "dm",
                    principalTable: "scheduled_task_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_scheduled_task_run_items_run_id",
            schema: "dm",
            table: "scheduled_task_run_items",
            column: "run_id");

        migrationBuilder.CreateIndex(
            name: "ix_scheduled_task_run_items_unfinished_last_activity",
            schema: "dm",
            table: "scheduled_task_run_items",
            columns: RunItemStatusLastActivityColumns,
            filter: "status IN ('Pending', 'Running', 'Retrying')");

        migrationBuilder.CreateIndex(
            name: "ix_scheduled_task_runs_task_id_running",
            schema: "dm",
            table: "scheduled_task_runs",
            column: "task_id",
            unique: true,
            filter: "status = 'Running'");

        migrationBuilder.CreateIndex(
            name: "ix_scheduled_task_runs_task_id_started_at_utc",
            schema: "dm",
            table: "scheduled_task_runs",
            columns: RunTaskIdStartedAtColumns,
            descending: RunTaskIdStartedAtDescending);

        migrationBuilder.CreateIndex(
            name: "ix_scheduled_task_targets_target_id",
            schema: "dm",
            table: "scheduled_task_targets",
            column: "target_id");

        migrationBuilder.CreateIndex(
            name: "ix_scheduled_tasks_scope_id",
            schema: "dm",
            table: "scheduled_tasks",
            column: "scope_id");

        migrationBuilder.CreateIndex(
            name: "ix_scheduled_tasks_scope_path",
            schema: "dm",
            table: "scheduled_tasks",
            column: "scope_path")
            .Annotation("Npgsql:IndexMethod", "gist");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "scheduled_task_run_items",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "scheduled_task_targets",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "scheduled_task_runs",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "scheduled_tasks",
            schema: "dm");
    }
}
