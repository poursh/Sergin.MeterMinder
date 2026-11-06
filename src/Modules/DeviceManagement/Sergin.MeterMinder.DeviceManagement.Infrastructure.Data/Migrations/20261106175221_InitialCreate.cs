using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    private static readonly string[] ModelManufacturerNameColumns = ["manufacturer_id", "name"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "dm");

        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:PostgresExtension:ltree", ",,");

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "dm",
            columns: table => new
            {
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                handler = table.Column<string>(type: "text", nullable: false),
                processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inbox_messages", x => new { x.message_id, x.handler });
            });

        migrationBuilder.CreateTable(
            name: "manufacturer",
            schema: "dm",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                address = table.Column<string>(type: "text", nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                row_version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_manufacturer", x => x.id);
                table.CheckConstraint("ck_manufacturer_soft_delete", "(deleted_at_utc IS NULL) = (deleted_by IS NULL)");
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "dm",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                content = table.Column<string>(type: "jsonb", nullable: false),
                occurred_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                correlation_id = table.Column<string>(type: "text", nullable: false),
                causation_id = table.Column<Guid>(type: "uuid", nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false),
                next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                processed_on_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "device_model",
            schema: "dm",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                manufacturer_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                modified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                modified_by = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_device_model", x => x.id);
                table.CheckConstraint("ck_device_model_soft_delete", "(deleted_at_utc IS NULL) = (deleted_by IS NULL)");
                table.ForeignKey(
                    name: "fk_device_model_manufacturer_manufacturer_id",
                    column: x => x.manufacturer_id,
                    principalSchema: "dm",
                    principalTable: "manufacturer",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "device",
            schema: "dm",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                device_id = table.Column<string>(type: "text", nullable: false),
                device_model_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("pk_device", x => x.id);
                table.CheckConstraint("ck_device_soft_delete", "(deleted_at_utc IS NULL) = (deleted_by IS NULL)");
                table.ForeignKey(
                    name: "fk_device_device_model_device_model_id",
                    column: x => x.device_model_id,
                    principalSchema: "dm",
                    principalTable: "device_model",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_device_device_id",
            schema: "dm",
            table: "device",
            column: "device_id",
            unique: true,
            filter: "deleted_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_device_device_model_id",
            schema: "dm",
            table: "device",
            column: "device_model_id");

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

        migrationBuilder.CreateIndex(
            name: "ix_device_model_manufacturer_id_name",
            schema: "dm",
            table: "device_model",
            columns: ModelManufacturerNameColumns,
            unique: true,
            filter: "deleted_at_utc IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_unprocessed",
            schema: "dm",
            table: "outbox_messages",
            column: "id",
            filter: "processed_on_utc IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "device",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "device_model",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "manufacturer",
            schema: "dm");
    }
}
