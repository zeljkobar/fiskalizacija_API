using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApiClientsAndTenantAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_clients",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApiKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApiKeyPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Permissions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "api_client_company_access",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_client_company_access", x => x.Id);
                    table.ForeignKey(
                        name: "FK_api_client_company_access_api_clients_ApiClientId",
                        column: x => x.ApiClientId,
                        principalSchema: "fiscal",
                        principalTable: "api_clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_api_client_company_access_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "fiscal",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_client_company_access_ApiClientId_CompanyId",
                schema: "fiscal",
                table: "api_client_company_access",
                columns: new[] { "ApiClientId", "CompanyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_client_company_access_CompanyId",
                schema: "fiscal",
                table: "api_client_company_access",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_api_clients_ApiKeyHash",
                schema: "fiscal",
                table: "api_clients",
                column: "ApiKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_clients_ClientId",
                schema: "fiscal",
                table: "api_clients",
                column: "ClientId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_client_company_access",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "api_clients",
                schema: "fiscal");
        }
    }
}
