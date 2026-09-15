using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddDeviceIdUniqueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_device_device_id",
            schema: "dm",
            table: "device",
            column: "device_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_device_device_id",
            schema: "dm",
            table: "device");
    }
}
