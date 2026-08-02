using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBuyerAndCorrectiveInvoiceWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerAddress",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerCountry",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerIdentificationNumber",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerIdentificationType",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerTaxIdentificationCode",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerTown",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectiveType",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalIic",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OriginalIssueDateTime",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PaymentDeadline",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SupplyPeriodEnd",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SupplyPeriodStart",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "OriginalInvoiceId",
                unique: true,
                filter: "\"OriginalInvoiceId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_fiscal_invoices_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "OriginalInvoiceId",
                principalSchema: "fiscal",
                principalTable: "fiscal_invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fiscal_invoices_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "BuyerAddress",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "BuyerCountry",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "BuyerIdentificationNumber",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "BuyerIdentificationType",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "BuyerName",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "BuyerTaxIdentificationCode",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "BuyerTown",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "CorrectiveType",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "OriginalIic",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "OriginalInvoiceId",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "OriginalIssueDateTime",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "PaymentDeadline",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "SupplyPeriodEnd",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "SupplyPeriodStart",
                schema: "fiscal",
                table: "fiscal_invoices");
        }
    }
}
