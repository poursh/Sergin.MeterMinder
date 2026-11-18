using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddRunActivity : Migration
{
    private static readonly string[] ActingUserStartedAtColumns = ["acting_user_id", "started_at_utc"];
    private static readonly bool[] ActingUserStartedAtDescending = [false, true];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "target_label",
            schema: "dm",
            table: "scheduled_task_run_items",
            type: "text",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_scheduled_task_runs_acting_user_id_started_at_utc",
            schema: "dm",
            table: "scheduled_task_runs",
            columns: ActingUserStartedAtColumns,
            descending: ActingUserStartedAtDescending);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_scheduled_task_runs_acting_user_id_started_at_utc",
            schema: "dm",
            table: "scheduled_task_runs");

        migrationBuilder.DropColumn(
            name: "target_label",
            schema: "dm",
            table: "scheduled_task_run_items");
    }
}
