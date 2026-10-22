using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvanceInvoiceWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.AddColumn<string>(
                name: "AdvanceIic",
                schema: "fiscal",
                table: "fiscal_payments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdvanceInvoiceId",
                schema: "fiscal",
                table: "fiscal_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdvanceSettlementAllocationsJson",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "AdvanceSettlementFinalIic",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdvanceSettlementFinalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AdvanceSettlementFinalIssuedAt",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fiscal_advance_usages",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvanceInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvanceIic = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UsedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_advance_usages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_advance_usages_fiscal_invoices_AdvanceInvoiceId",
                        column: x => x.AdvanceInvoiceId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fiscal_advance_usages_fiscal_invoices_FinalInvoiceId",
                        column: x => x.FinalInvoiceId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_payments_AdvanceInvoiceId",
                schema: "fiscal",
                table: "fiscal_payments",
                column: "AdvanceInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_AdvanceSettlementFinalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "AdvanceSettlementFinalInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "OriginalInvoiceId",
                unique: true,
                filter: "\"OriginalInvoiceId\" IS NOT NULL AND \"AdvanceSettlementFinalInvoiceId\" IS NULL AND \"Status\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId_AdvanceSettlementFinalInv~",
                schema: "fiscal",
                table: "fiscal_invoices",
                columns: new[] { "OriginalInvoiceId", "AdvanceSettlementFinalInvoiceId" },
                unique: true,
                filter: "\"AdvanceSettlementFinalInvoiceId\" IS NOT NULL AND \"Status\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_advance_usages_AdvanceInvoiceId_FinalInvoiceId",
                schema: "fiscal",
                table: "fiscal_advance_usages",
                columns: new[] { "AdvanceInvoiceId", "FinalInvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_advance_usages_FinalInvoiceId",
                schema: "fiscal",
                table: "fiscal_advance_usages",
                column: "FinalInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_fiscal_invoices_fiscal_invoices_AdvanceSettlementFinalInvoi~",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "AdvanceSettlementFinalInvoiceId",
                principalSchema: "fiscal",
                principalTable: "fiscal_invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_fiscal_payments_fiscal_invoices_AdvanceInvoiceId",
                schema: "fiscal",
                table: "fiscal_payments",
                column: "AdvanceInvoiceId",
                principalSchema: "fiscal",
                principalTable: "fiscal_invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fiscal_invoices_fiscal_invoices_AdvanceSettlementFinalInvoi~",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_fiscal_payments_fiscal_invoices_AdvanceInvoiceId",
                schema: "fiscal",
                table: "fiscal_payments");

            migrationBuilder.DropTable(
                name: "fiscal_advance_usages",
                schema: "fiscal");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_payments_AdvanceInvoiceId",
                schema: "fiscal",
                table: "fiscal_payments");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_AdvanceSettlementFinalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId_AdvanceSettlementFinalInv~",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "AdvanceIic",
                schema: "fiscal",
                table: "fiscal_payments");

            migrationBuilder.DropColumn(
                name: "AdvanceInvoiceId",
                schema: "fiscal",
                table: "fiscal_payments");

            migrationBuilder.DropColumn(
                name: "AdvanceSettlementAllocationsJson",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "AdvanceSettlementFinalIic",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "AdvanceSettlementFinalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "AdvanceSettlementFinalIssuedAt",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "OriginalInvoiceId",
                unique: true,
                filter: "\"OriginalInvoiceId\" IS NOT NULL");
        }
    }
}
