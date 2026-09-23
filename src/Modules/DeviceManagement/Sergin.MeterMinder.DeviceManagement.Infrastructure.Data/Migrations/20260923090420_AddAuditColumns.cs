using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <summary>
/// Adds the audit stamps for the three types DeviceManagement configures Audited(). created_* is added
/// nullable, backfilled, then made NOT NULL. Rows that predate auditing get a stand-in, not their real
/// history: the migration time and the platform's fixed system actor (the outbox relay identity's id).
/// modified_* stays NULL for them — nothing is known to have modified them.
/// </summary>
public partial class AddAuditColumns : Migration
{
    private const string Schema = "dm";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AddColumns(migrationBuilder, "device");
        AddColumns(migrationBuilder, "device_model");
        AddColumns(migrationBuilder, "manufacturer");

        migrationBuilder.Sql("UPDATE dm.device SET created_at_utc = now(), created_by = '01920000-0000-7000-8000-00000000000f';");
        migrationBuilder.Sql("UPDATE dm.device_model SET created_at_utc = now(), created_by = '01920000-0000-7000-8000-00000000000f';");
        migrationBuilder.Sql("UPDATE dm.manufacturer SET created_at_utc = now(), created_by = '01920000-0000-7000-8000-00000000000f';");

        RequireCreated(migrationBuilder, "device");
        RequireCreated(migrationBuilder, "device_model");
        RequireCreated(migrationBuilder, "manufacturer");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        DropColumns(migrationBuilder, "device");
        DropColumns(migrationBuilder, "device_model");
        DropColumns(migrationBuilder, "manufacturer");
    }

    private static void AddColumns(MigrationBuilder migrationBuilder, string table)
    {
        migrationBuilder.AddColumn<DateTime>(name: "created_at_utc", schema: Schema, table: table, type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "created_by", schema: Schema, table: table, type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "modified_at_utc", schema: Schema, table: table, type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "modified_by", schema: Schema, table: table, type: "uuid", nullable: true);
    }

    private static void RequireCreated(MigrationBuilder migrationBuilder, string table)
    {
        migrationBuilder.AlterColumn<DateTime>(
            name: "created_at_utc", schema: Schema, table: table, type: "timestamp with time zone", nullable: false,
            oldClrType: typeof(DateTime), oldType: "timestamp with time zone", oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "created_by", schema: Schema, table: table, type: "uuid", nullable: false,
            oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
    }

    private static void DropColumns(MigrationBuilder migrationBuilder, string table)
    {
        migrationBuilder.DropColumn(name: "created_at_utc", schema: Schema, table: table);
        migrationBuilder.DropColumn(name: "created_by", schema: Schema, table: table);
        migrationBuilder.DropColumn(name: "modified_at_utc", schema: Schema, table: table);
        migrationBuilder.DropColumn(name: "modified_by", schema: Schema, table: table);
    }
}
