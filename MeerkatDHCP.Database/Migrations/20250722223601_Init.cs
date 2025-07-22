using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeerkatDHCP.Database.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Operation = table.Column<byte>(type: "INTEGER", nullable: false),
                    HardwareType = table.Column<byte>(type: "INTEGER", nullable: false),
                    HardwareLength = table.Column<byte>(type: "INTEGER", nullable: false),
                    Hops = table.Column<byte>(type: "INTEGER", nullable: false),
                    TransactionId = table.Column<uint>(type: "INTEGER", nullable: false),
                    Seconds = table.Column<ushort>(type: "INTEGER", nullable: false),
                    Flags = table.Column<ushort>(type: "INTEGER", nullable: false),
                    ClientIpAddress = table.Column<byte[]>(type: "BLOB", nullable: false),
                    YourIpAddress = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ServerIpAddress = table.Column<byte[]>(type: "BLOB", nullable: false),
                    GatewayIpAddress = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ServerName = table.Column<byte[]>(type: "BLOB", nullable: false),
                    File = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Options = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scopes",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Network = table.Column<string>(type: "TEXT", nullable: false),
                    From = table.Column<string>(type: "TEXT", nullable: false),
                    To = table.Column<string>(type: "TEXT", nullable: false),
                    Gateway = table.Column<string>(type: "TEXT", nullable: false),
                    Dns1 = table.Column<string>(type: "TEXT", nullable: false),
                    Dns2 = table.Column<string>(type: "TEXT", nullable: true),
                    LeaseTimeInSeconds = table.Column<uint>(type: "INTEGER", nullable: false),
                    LastUpdate = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scopes", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Leases",
                columns: table => new
                {
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    HardwareAddress = table.Column<string>(type: "TEXT", nullable: false),
                    ClientIdentifier = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DeclinedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastUpdate = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ScopeName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leases", x => x.IpAddress);
                    table.ForeignKey(
                        name: "FK_Leases_Scopes_ScopeName",
                        column: x => x.ScopeName,
                        principalTable: "Scopes",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    HardwareAddress = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => new { x.HardwareAddress, x.IpAddress });
                    table.ForeignKey(
                        name: "FK_Reservations_Scopes_ScopeName",
                        column: x => x.ScopeName,
                        principalTable: "Scopes",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_ScopeName",
                table: "Leases",
                column: "ScopeName");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ScopeName",
                table: "Reservations",
                column: "ScopeName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Leases");

            migrationBuilder.DropTable(
                name: "RequestLogs");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "Scopes");
        }
    }
}
