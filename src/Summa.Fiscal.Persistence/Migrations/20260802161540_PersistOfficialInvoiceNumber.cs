using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistOfficialInvoiceNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OfficialInvoiceNumber",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_OfficialInvoiceNumber",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "OfficialInvoiceNumber",
                unique: true,
                filter: "\"OfficialInvoiceNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_OfficialInvoiceNumber",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "OfficialInvoiceNumber",
                schema: "fiscal",
                table: "fiscal_invoices");
        }
    }
}
