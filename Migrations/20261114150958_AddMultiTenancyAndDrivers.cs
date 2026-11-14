using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaliYikamaAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancyAndDrivers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "DriverId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CodePrefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Drivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccessCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VehiclePlate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Drivers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Companies",
                columns: new[] { "Id", "Address", "CodePrefix", "CreatedAt", "Email", "IsActive", "Name", "PasswordHash", "Phone" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111111"), "Merkez Mah. Sanayi Cad. No:10", "DNZ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "deniz@haliyikama.com", true, "Deniz Halı Yıkama", "eJicADF1ds7n6+bbv++gGQ==:7i8IXu4ChIrJCigG6YVrS7y4odRcMN1MYIc3qTooPHs=", "05321234567" });

            migrationBuilder.InsertData(
                table: "Drivers",
                columns: new[] { "Id", "AccessCode", "CompanyId", "CreatedAt", "FullName", "IsActive", "Phone", "VehiclePlate" },
                values: new object[] { new Guid("22222222-2222-2222-2222-222222222222"), "DNZ-101", new Guid("11111111-1111-1111-1111-111111111111"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Ahmet Usta (Örnek Sürücü)", true, "05551112233", "34 ABC 123" });

            // Mevcut siparişlerin DefaultCompanyId ("11111111-1111-1111-1111-111111111111") ile eşleşmesi
            migrationBuilder.Sql("UPDATE [Orders] SET [CompanyId] = '11111111-1111-1111-1111-111111111111' WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompanyId",
                table: "Orders",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DriverId",
                table: "Orders",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Email",
                table: "Companies",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_AccessCode",
                table: "Drivers",
                column: "AccessCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_CompanyId",
                table: "Drivers",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Companies_CompanyId",
                table: "Orders",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Drivers_DriverId",
                table: "Orders",
                column: "DriverId",
                principalTable: "Drivers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Companies_CompanyId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Drivers_DriverId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "Drivers");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CompanyId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_DriverId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DriverId",
                table: "Orders");
        }
    }
}
