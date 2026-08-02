using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddControlledFiscalActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fiscal_activations",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TestInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TestJikr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TestConfigurationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TestPassedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TestPassedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProductionActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProductionActivatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_activations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_activations_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "fiscal",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fiscal_activations_fiscal_invoices_TestInvoiceId",
                        column: x => x.TestInvoiceId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_activations_CompanyId",
                schema: "fiscal",
                table: "fiscal_activations",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_activations_TestInvoiceId",
                schema: "fiscal",
                table: "fiscal_activations",
                column: "TestInvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_activations",
                schema: "fiscal");
        }
    }
}
