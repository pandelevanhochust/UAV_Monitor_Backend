using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UavSystem.DeviceService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    device_id = table.Column<long>(type: "bigint", nullable: false),
                    location_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Offline"),
                    assigned_monitor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    api_key_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_devices", x => x.device_id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_devices_monitor",
                table: "devices",
                column: "assigned_monitor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "devices");
        }
    }
}
