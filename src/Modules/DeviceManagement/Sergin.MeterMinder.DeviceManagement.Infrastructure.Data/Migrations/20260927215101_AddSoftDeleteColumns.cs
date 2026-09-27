using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddSoftDeleteColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_device_model_manufacturer_id_name",
            schema: "dm",
            table: "device_model");

        migrationBuilder.DropIndex(
            name: "ix_device_device_id",
            schema: "dm",
            table: "device");

        migrationBuilder.AddColumn<DateTime>(
            name: "deleted_at_utc",
            schema: "dm",
            table: "manufacturer",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "deleted_by",
            schema: "dm",
            table: "manufacturer",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "deleted_at_utc",
            schema: "dm",
            table: "device_model",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "deleted_by",
            schema: "dm",
            table: "device_model",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "deleted_at_utc",
            schema: "dm",
            table: "device",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "deleted_by",
            schema: "dm",
            table: "device",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_manufacturer_soft_delete",
            schema: "dm",
            table: "manufacturer",
            sql: "(deleted_at_utc IS NULL) = (deleted_by IS NULL)");

        migrationBuilder.CreateIndex(
            name: "ix_device_model_manufacturer_id_name",
            schema: "dm",
            table: "device_model",
            columns: ["manufacturer_id", "name"],
            unique: true,
            filter: "deleted_at_utc IS NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_device_model_soft_delete",
            schema: "dm",
            table: "device_model",
            sql: "(deleted_at_utc IS NULL) = (deleted_by IS NULL)");

        migrationBuilder.CreateIndex(
            name: "ix_device_device_id",
            schema: "dm",
            table: "device",
            column: "device_id",
            unique: true,
            filter: "deleted_at_utc IS NULL");

        migrationBuilder.AddCheckConstraint(
            name: "ck_device_soft_delete",
            schema: "dm",
            table: "device",
            sql: "(deleted_at_utc IS NULL) = (deleted_by IS NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_manufacturer_soft_delete",
            schema: "dm",
            table: "manufacturer");

        migrationBuilder.DropIndex(
            name: "ix_device_model_manufacturer_id_name",
            schema: "dm",
            table: "device_model");

        migrationBuilder.DropCheckConstraint(
            name: "ck_device_model_soft_delete",
            schema: "dm",
            table: "device_model");

        migrationBuilder.DropIndex(
            name: "ix_device_device_id",
            schema: "dm",
            table: "device");

        migrationBuilder.DropCheckConstraint(
            name: "ck_device_soft_delete",
            schema: "dm",
            table: "device");

        migrationBuilder.DropColumn(
            name: "deleted_at_utc",
            schema: "dm",
            table: "manufacturer");

        migrationBuilder.DropColumn(
            name: "deleted_by",
            schema: "dm",
            table: "manufacturer");

        migrationBuilder.DropColumn(
            name: "deleted_at_utc",
            schema: "dm",
            table: "device_model");

        migrationBuilder.DropColumn(
            name: "deleted_by",
            schema: "dm",
            table: "device_model");

        migrationBuilder.DropColumn(
            name: "deleted_at_utc",
            schema: "dm",
            table: "device");

        migrationBuilder.DropColumn(
            name: "deleted_by",
            schema: "dm",
            table: "device");

        migrationBuilder.CreateIndex(
            name: "ix_device_model_manufacturer_id_name",
            schema: "dm",
            table: "device_model",
            columns: ["manufacturer_id", "name"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_device_device_id",
            schema: "dm",
            table: "device",
            column: "device_id",
            unique: true);
    }
}
