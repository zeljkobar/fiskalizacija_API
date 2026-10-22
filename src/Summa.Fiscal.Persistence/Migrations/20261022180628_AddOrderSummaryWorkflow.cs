using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderSummaryWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.AddColumn<bool>(
                name: "IsOrderCorrection",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSummaryCorrection",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OrderSummaryJson",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fiscal_summary_orders",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummaryInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_summary_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_summary_orders_fiscal_invoices_OrderInvoiceId",
                        column: x => x.OrderInvoiceId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fiscal_summary_orders_fiscal_invoices_SummaryInvoiceId",
                        column: x => x.SummaryInvoiceId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "OriginalInvoiceId",
                unique: true,
                filter: "\"OriginalInvoiceId\" IS NOT NULL AND \"AdvanceSettlementFinalInvoiceId\" IS NULL AND NOT \"IsOrderCorrection\" AND NOT \"IsSummaryCorrection\" AND \"Status\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_summary_orders_OrderInvoiceId",
                schema: "fiscal",
                table: "fiscal_summary_orders",
                column: "OrderInvoiceId",
                unique: true,
                filter: "\"ReleasedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_summary_orders_SummaryInvoiceId_OrderInvoiceId",
                schema: "fiscal",
                table: "fiscal_summary_orders",
                columns: new[] { "SummaryInvoiceId", "OrderInvoiceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_summary_orders",
                schema: "fiscal");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "IsOrderCorrection",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "IsSummaryCorrection",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "OrderSummaryJson",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "OriginalInvoiceId",
                unique: true,
                filter: "\"OriginalInvoiceId\" IS NOT NULL AND \"AdvanceSettlementFinalInvoiceId\" IS NULL AND \"Status\" <> 'Cancelled'");
        }
    }
}
