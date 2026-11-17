using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaliYikamaAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyWhatsAppPhoneNumberIdAndPrefixIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WhatsAppPhoneNumberId",
                table: "Companies",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Companies",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "WhatsAppPhoneNumberId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_CodePrefix",
                table: "Companies",
                column: "CodePrefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_WhatsAppPhoneNumberId",
                table: "Companies",
                column: "WhatsAppPhoneNumberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_CodePrefix",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_WhatsAppPhoneNumberId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsAppPhoneNumberId",
                table: "Companies");
        }
    }
}
