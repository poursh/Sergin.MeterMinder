using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddRowVersionColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Added nullable, backfilled, then made NOT NULL: existing rows need a version before the constraint.
        // gen_random_uuid() is v4, not v7; a backfilled version only has to differ from the next one written.
        migrationBuilder.AddColumn<Guid>(
            name: "row_version",
            schema: "dm",
            table: "device",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "row_version",
            schema: "dm",
            table: "manufacturer",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql("UPDATE dm.device SET row_version = gen_random_uuid() WHERE row_version IS NULL;");
        migrationBuilder.Sql("UPDATE dm.manufacturer SET row_version = gen_random_uuid() WHERE row_version IS NULL;");

        migrationBuilder.AlterColumn<Guid>(
            name: "row_version",
            schema: "dm",
            table: "device",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "row_version",
            schema: "dm",
            table: "manufacturer",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "row_version", schema: "dm", table: "device");
        migrationBuilder.DropColumn(name: "row_version", schema: "dm", table: "manufacturer");
    }
}
