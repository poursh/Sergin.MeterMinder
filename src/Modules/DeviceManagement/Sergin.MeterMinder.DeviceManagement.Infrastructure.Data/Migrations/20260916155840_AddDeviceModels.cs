using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddDeviceModels : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing devices carry a manufacturer but no model, and device_model_id is NOT NULL. Migrations
        // auto-apply only in Development and there is no production data, so the rows are dropped rather than
        // backfilled with placeholder models (spec, Decision 7).
        migrationBuilder.Sql("DELETE FROM dm.device;");

        migrationBuilder.DropForeignKey(
            name: "fk_device_manufacturer_manufacturer_id",
            schema: "dm",
            table: "device");

        migrationBuilder.RenameColumn(
            name: "manufacturer_id",
            schema: "dm",
            table: "device",
            newName: "device_model_id");

        migrationBuilder.RenameIndex(
            name: "ix_device_manufacturer_id",
            schema: "dm",
            table: "device",
            newName: "ix_device_device_model_id");

        migrationBuilder.CreateTable(
            name: "device_model",
            schema: "dm",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                manufacturer_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_device_model", x => x.id);
                table.ForeignKey(
                    name: "fk_device_model_manufacturer_manufacturer_id",
                    column: x => x.manufacturer_id,
                    principalSchema: "dm",
                    principalTable: "manufacturer",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_device_model_manufacturer_id_name",
            schema: "dm",
            table: "device_model",
            columns: ["manufacturer_id", "name"],
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "fk_device_device_model_device_model_id",
            schema: "dm",
            table: "device",
            column: "device_model_id",
            principalSchema: "dm",
            principalTable: "device_model",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_device_device_model_device_model_id",
            schema: "dm",
            table: "device");

        migrationBuilder.DropTable(
            name: "device_model",
            schema: "dm");

        migrationBuilder.RenameColumn(
            name: "device_model_id",
            schema: "dm",
            table: "device",
            newName: "manufacturer_id");

        migrationBuilder.RenameIndex(
            name: "ix_device_device_model_id",
            schema: "dm",
            table: "device",
            newName: "ix_device_manufacturer_id");

        migrationBuilder.AddForeignKey(
            name: "fk_device_manufacturer_manufacturer_id",
            schema: "dm",
            table: "device",
            column: "manufacturer_id",
            principalSchema: "dm",
            principalTable: "manufacturer",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    }
}
