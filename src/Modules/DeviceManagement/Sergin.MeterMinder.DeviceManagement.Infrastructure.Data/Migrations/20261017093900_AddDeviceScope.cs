using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddDeviceScope : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:PostgresExtension:ltree", ",,");

        // Added nullable, backfilled, then made NOT NULL: every existing device was created before scopes, so it
        // lives at the root. The id and path are AuthorizationScope.RootId and RootPath.
        migrationBuilder.AddColumn<Guid>(
            name: "scope_id",
            schema: "dm",
            table: "device",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "scope_path",
            schema: "dm",
            table: "device",
            type: "ltree",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE dm.device SET scope_id = '01920000-0000-7000-8000-0000000000b1', "
            + "scope_path = '019200000000700080000000000000b1' WHERE scope_id IS NULL;");

        migrationBuilder.AlterColumn<Guid>(
            name: "scope_id",
            schema: "dm",
            table: "device",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "scope_path",
            schema: "dm",
            table: "device",
            type: "ltree",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "ltree",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_device_scope_id",
            schema: "dm",
            table: "device",
            column: "scope_id");

        migrationBuilder.CreateIndex(
            name: "ix_device_scope_path",
            schema: "dm",
            table: "device",
            column: "scope_path")
            .Annotation("Npgsql:IndexMethod", "gist");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_device_scope_id",
            schema: "dm",
            table: "device");

        migrationBuilder.DropIndex(
            name: "ix_device_scope_path",
            schema: "dm",
            table: "device");

        migrationBuilder.DropColumn(
            name: "scope_id",
            schema: "dm",
            table: "device");

        migrationBuilder.DropColumn(
            name: "scope_path",
            schema: "dm",
            table: "device");

        migrationBuilder.AlterDatabase()
            .OldAnnotation("Npgsql:PostgresExtension:ltree", ",,");
    }
}
