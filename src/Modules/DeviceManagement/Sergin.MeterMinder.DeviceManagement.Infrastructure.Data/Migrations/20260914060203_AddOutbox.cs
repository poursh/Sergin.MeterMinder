using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddOutbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
            name: "inbox_messages",
            schema: "dm");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "dm");
    }
}
