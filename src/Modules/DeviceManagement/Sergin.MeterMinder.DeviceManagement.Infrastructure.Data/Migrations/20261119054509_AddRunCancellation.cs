using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddRunCancellation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "cancel_requested_at_utc",
            schema: "dm",
            table: "scheduled_task_runs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "cancelled",
            schema: "dm",
            table: "scheduled_task_runs",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "cancel_requested_at_utc",
            schema: "dm",
            table: "scheduled_task_runs");

        migrationBuilder.DropColumn(
            name: "cancelled",
            schema: "dm",
            table: "scheduled_task_runs");
    }
}
