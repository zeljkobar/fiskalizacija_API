using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyOnboardingAndCertificateVault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivatedAt",
                schema: "fiscal",
                table: "fiscal_certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAt",
                schema: "fiscal",
                table: "fiscal_certificates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                schema: "fiscal",
                table: "fiscal_certificates",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Issuer",
                schema: "fiscal",
                table: "fiscal_certificates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                schema: "fiscal",
                table: "fiscal_certificates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                schema: "fiscal",
                table: "fiscal_certificates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "fiscal_audit_logs",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_audit_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_audit_logs_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "fiscal",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_certificates_CompanyId",
                schema: "fiscal",
                table: "fiscal_certificates",
                column: "CompanyId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_audit_logs_CompanyId",
                schema: "fiscal",
                table: "fiscal_audit_logs",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_audit_logs_CorrelationId",
                schema: "fiscal",
                table: "fiscal_audit_logs",
                column: "CorrelationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_audit_logs",
                schema: "fiscal");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_certificates_CompanyId",
                schema: "fiscal",
                table: "fiscal_certificates");

            migrationBuilder.DropColumn(
                name: "ActivatedAt",
                schema: "fiscal",
                table: "fiscal_certificates");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                schema: "fiscal",
                table: "fiscal_certificates");

            migrationBuilder.DropColumn(
                name: "FileName",
                schema: "fiscal",
                table: "fiscal_certificates");

            migrationBuilder.DropColumn(
                name: "Issuer",
                schema: "fiscal",
                table: "fiscal_certificates");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                schema: "fiscal",
                table: "fiscal_certificates");

            migrationBuilder.DropColumn(
                name: "Subject",
                schema: "fiscal",
                table: "fiscal_certificates");
        }
    }
}
