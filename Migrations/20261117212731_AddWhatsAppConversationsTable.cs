using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaliYikamaAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppConversationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Step = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ServiceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CarpetCount = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppConversations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversations_CompanyId_PhoneNumber",
                table: "WhatsAppConversations",
                columns: new[] { "CompanyId", "PhoneNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppConversations_LastActivityAt",
                table: "WhatsAppConversations",
                column: "LastActivityAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppConversations");
        }
    }
}
