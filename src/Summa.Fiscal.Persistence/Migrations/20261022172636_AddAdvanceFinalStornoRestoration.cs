using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvanceFinalStornoRestoration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdvanceRestorationStornoIic",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdvanceRestorationStornoInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AdvanceRestorationStornoIssuedAt",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_AdvanceRestorationStornoInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "AdvanceRestorationStornoInvoiceId",
                unique: true,
                filter: "\"AdvanceRestorationStornoInvoiceId\" IS NOT NULL AND \"Status\" <> 'Cancelled'");

            migrationBuilder.AddForeignKey(
                name: "FK_fiscal_invoices_fiscal_invoices_AdvanceRestorationStornoInv~",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "AdvanceRestorationStornoInvoiceId",
                principalSchema: "fiscal",
                principalTable: "fiscal_invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fiscal_invoices_fiscal_invoices_AdvanceRestorationStornoInv~",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_AdvanceRestorationStornoInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "AdvanceRestorationStornoIic",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "AdvanceRestorationStornoInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "AdvanceRestorationStornoIssuedAt",
                schema: "fiscal",
                table: "fiscal_invoices");
        }
    }
}
