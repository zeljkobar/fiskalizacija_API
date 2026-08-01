using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateExpiryAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fiscal_certificate_expiry_alerts",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CertificateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThresholdDays = table.Column<int>(type: "integer", nullable: false),
                    CertificateValidTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_certificate_expiry_alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_certificate_expiry_alerts_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "fiscal",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fiscal_certificate_expiry_alerts_fiscal_certificates_Certif~",
                        column: x => x.CertificateId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_certificate_expiry_alerts_CertificateId_ThresholdDays",
                schema: "fiscal",
                table: "fiscal_certificate_expiry_alerts",
                columns: new[] { "CertificateId", "ThresholdDays" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_certificate_expiry_alerts_CompanyId_IsAcknowledged_C~",
                schema: "fiscal",
                table: "fiscal_certificate_expiry_alerts",
                columns: new[] { "CompanyId", "IsAcknowledged", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_certificate_expiry_alerts",
                schema: "fiscal");
        }
    }
}
