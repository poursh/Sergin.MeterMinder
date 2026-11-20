using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class ReplaceScheduledTasksWithTasks : Migration
{
    private static readonly string[] RunItemTargetIdRunIdColumns = ["target_id", "run_id"];
    private static readonly string[] RunItemStatusLastActivityColumns = ["status", "last_activity_at_utc"];
    private static readonly string[] RunActingUserIdStartedAtColumns = ["acting_user_id", "started_at_utc"];
    private static readonly string[] RunCommandNameScopeIdColumns = ["command_name", "scope_id"];
    private static readonly string[] RunScheduleIdStartedAtColumns = ["schedule_id", "started_at_utc"];
    private static readonly string[] RunTaskIdStartedAtColumns = ["task_id", "started_at_utc"];
    private static readonly bool[] StartedAtDescending = [false, true];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "scheduled_task_run_items",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "scheduled_task_runs",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "scheduled_task_targets",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "scheduled_tasks",
            schema: "dm");

        migrationBuilder.CreateTable(
            name: "task_schedules",
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
                table.PrimaryKey("pk_task_schedules", x => x.id);
                table.CheckConstraint("ck_task_schedules_soft_delete", "(deleted_at_utc IS NULL) = (deleted_by IS NULL)");
            });

        migrationBuilder.CreateTable(
            name: "task_runs",
            schema: "dm",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                trigger = table.Column<string>(type: "text", nullable: false),
                acting_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                scope_path = table.Column<string>(type: "ltree", nullable: false),
                command_name = table.Column<string>(type: "text", nullable: false),
                arguments = table.Column<string>(type: "jsonb", nullable: false),
                target_kind = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                total = table.Column<int>(type: "integer", nullable: false),
                succeeded = table.Column<int>(type: "integer", nullable: false),
                failed = table.Column<int>(type: "integer", nullable: false),
                cancelled = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                cancel_requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_task_runs", x => x.id);
                table.ForeignKey(
                    name: "fk_task_runs_task_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "dm",
                    principalTable: "task_schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "task_schedule_targets",
            schema: "dm",
            columns: table => new
            {
                target_id = table.Column<Guid>(type: "uuid", nullable: false),
                schedule_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_task_schedule_targets", x => new { x.schedule_id, x.target_id });
                table.ForeignKey(
                    name: "fk_task_schedule_targets_task_schedules_schedule_id",
                    column: x => x.schedule_id,
                    principalSchema: "dm",
                    principalTable: "task_schedules",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "task_run_items",
            schema: "dm",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                target_id = table.Column<Guid>(type: "uuid", nullable: true),
                target_label = table.Column<string>(type: "text", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                error_code = table.Column<string>(type: "text", nullable: true),
                error_description = table.Column<string>(type: "text", nullable: true),
                started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                engine_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                last_activity_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                progress = table.Column<short>(type: "smallint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_task_run_items", x => x.id);
                table.ForeignKey(
                    name: "fk_task_run_items_task_runs_run_id",
                    column: x => x.run_id,
                    principalSchema: "dm",
                    principalTable: "task_runs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_task_run_items_run_id",
            schema: "dm",
            table: "task_run_items",
            column: "run_id");

        migrationBuilder.CreateIndex(
            name: "ix_task_run_items_target_id_run_id",
            schema: "dm",
            table: "task_run_items",
            columns: RunItemTargetIdRunIdColumns);

        migrationBuilder.CreateIndex(
            name: "ix_task_run_items_unfinished_last_activity",
            schema: "dm",
            table: "task_run_items",
            columns: RunItemStatusLastActivityColumns,
            filter: "status IN ('Pending', 'Running', 'Retrying')");

        migrationBuilder.CreateIndex(
            name: "ix_task_runs_acting_user_id_started_at_utc",
            schema: "dm",
            table: "task_runs",
            columns: RunActingUserIdStartedAtColumns,
            descending: StartedAtDescending);

        migrationBuilder.CreateIndex(
            name: "ix_task_runs_collection_running",
            schema: "dm",
            table: "task_runs",
            columns: RunCommandNameScopeIdColumns,
            unique: true,
            filter: "status = 'Running' AND target_kind = 'Collection'");

        migrationBuilder.CreateIndex(
            name: "ix_task_runs_schedule_id_running",
            schema: "dm",
            table: "task_runs",
            column: "schedule_id",
            unique: true,
            filter: "status = 'Running'");

        migrationBuilder.CreateIndex(
            name: "ix_task_runs_schedule_id_started_at_utc",
            schema: "dm",
            table: "task_runs",
            columns: RunScheduleIdStartedAtColumns,
            descending: StartedAtDescending);

        migrationBuilder.CreateIndex(
            name: "ix_task_schedule_targets_target_id",
            schema: "dm",
            table: "task_schedule_targets",
            column: "target_id");

        migrationBuilder.CreateIndex(
            name: "ix_task_schedules_scope_id",
            schema: "dm",
            table: "task_schedules",
            column: "scope_id");

        migrationBuilder.CreateIndex(
            name: "ix_task_schedules_scope_path",
            schema: "dm",
            table: "task_schedules",
            column: "scope_path")
            .Annotation("Npgsql:IndexMethod", "gist");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "task_run_items",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "task_schedule_targets",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "task_runs",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "task_schedules",
            schema: "dm");

        migrationBuilder.CreateTable(
            name: "scheduled_tasks",
            schema: "dm",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                arguments = table.Column<string>(type: "jsonb", nullable: false),
                command_name = table.Column<string>(type: "text", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                cron = table.Column<string>(type: "text", nullable: false),
                delegating_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                engine_trigger_id = table.Column<Guid>(type: "uuid", nullable: true),
                modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                row_version = table.Column<Guid>(type: "uuid", nullable: false),
                scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                scope_path = table.Column<string>(type: "ltree", nullable: false),
                state = table.Column<string>(type: "text", nullable: false),
                time_zone_id = table.Column<string>(type: "text", nullable: false)
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
                acting_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                arguments = table.Column<string>(type: "jsonb", nullable: false),
                cancel_requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                cancelled = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                command_name = table.Column<string>(type: "text", nullable: false),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                failed = table.Column<int>(type: "integer", nullable: false),
                scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                scope_path = table.Column<string>(type: "ltree", nullable: false),
                started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                succeeded = table.Column<int>(type: "integer", nullable: false),
                task_id = table.Column<Guid>(type: "uuid", nullable: true),
                total = table.Column<int>(type: "integer", nullable: false),
                trigger = table.Column<string>(type: "text", nullable: false)
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
                task_id = table.Column<Guid>(type: "uuid", nullable: false),
                target_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                root_id = table.Column<Guid>(type: "uuid", nullable: true)
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
                attempts = table.Column<int>(type: "integer", nullable: false),
                completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                engine_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                error_code = table.Column<string>(type: "text", nullable: true),
                error_description = table.Column<string>(type: "text", nullable: true),
                last_activity_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                progress = table.Column<short>(type: "smallint", nullable: true),
                root_id = table.Column<Guid>(type: "uuid", nullable: true),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                target_id = table.Column<Guid>(type: "uuid", nullable: false),
                target_label = table.Column<string>(type: "text", nullable: true)
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
            name: "ix_scheduled_task_run_items_target_id_run_id",
            schema: "dm",
            table: "scheduled_task_run_items",
            columns: RunItemTargetIdRunIdColumns);

        migrationBuilder.CreateIndex(
            name: "ix_scheduled_task_run_items_unfinished_last_activity",
            schema: "dm",
            table: "scheduled_task_run_items",
            columns: RunItemStatusLastActivityColumns,
            filter: "status IN ('Pending', 'Running', 'Retrying')");

        migrationBuilder.CreateIndex(
            name: "ix_scheduled_task_runs_acting_user_id_started_at_utc",
            schema: "dm",
            table: "scheduled_task_runs",
            columns: RunActingUserIdStartedAtColumns,
            descending: StartedAtDescending);

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
            descending: StartedAtDescending);

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
}
