using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminFiscalReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW fiscal.admin_workflow_debts AS
                SELECT u."FinalInvoiceId" AS "InvoiceId", 'AdvanceSettlementDue'::text AS "Kind", SUM(u."UsedAmount") AS "Amount"
                FROM fiscal.fiscal_advance_usages u
                JOIN fiscal.fiscal_invoices f ON f."Id" = u."FinalInvoiceId"
                WHERE u."ReleasedAt" IS NULL AND f."Status" = 'Fiscalized' AND NULLIF(f."Jikr",'') IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM fiscal.fiscal_invoices s WHERE s."OriginalInvoiceId" = u."AdvanceInvoiceId"
                      AND s."AdvanceSettlementFinalInvoiceId" = f."Id" AND s."CompanyId" = f."CompanyId"
                      AND s."Status" = 'Fiscalized' AND NULLIF(s."Jikr",'') IS NOT NULL)
                GROUP BY u."FinalInvoiceId"
                UNION ALL
                SELECT u."FinalInvoiceId", 'AdvanceRestorationDue', SUM(u."UsedAmount")
                FROM fiscal.fiscal_advance_usages u JOIN fiscal.fiscal_invoices f ON f."Id" = u."FinalInvoiceId"
                WHERE u."ReleasedAt" IS NULL AND f."Status" = 'StornoCreated' AND NULLIF(f."Jikr",'') IS NOT NULL
                GROUP BY u."FinalInvoiceId"
                UNION ALL
                SELECT s."Id", 'SummaryCorrectionDue', -SUM(c."TotalAmount")
                FROM fiscal.fiscal_summary_orders so
                JOIN fiscal.fiscal_invoices s ON s."Id" = so."SummaryInvoiceId"
                JOIN fiscal.fiscal_invoices c ON c."OriginalInvoiceId" = so."OrderInvoiceId" AND c."CompanyId" = s."CompanyId"
                WHERE so."ReleasedAt" IS NULL AND s."InvoiceType" = 'Summary' AND s."Status" IN ('Fiscalized','StornoCreated')
                  AND NULLIF(s."Jikr",'') IS NOT NULL AND c."IsOrderCorrection" AND c."Status" IN ('Fiscalized','StornoCreated')
                  AND NULLIF(c."Jikr",'') IS NOT NULL
                  AND NOT COALESCE((s."OrderSummaryJson"->'AccountedCorrectionIds') ? c."Id"::text, false)
                  AND NOT EXISTS (
                    SELECT 1 FROM fiscal.fiscal_invoices a WHERE a."OriginalInvoiceId" = s."Id" AND a."CompanyId" = s."CompanyId"
                      AND a."IsSummaryCorrection" AND a."Status" IN ('Fiscalized','StornoCreated') AND NULLIF(a."Jikr",'') IS NOT NULL
                      AND (a."OrderSummaryJson"->'AccountedCorrectionIds') ? c."Id"::text)
                GROUP BY s."Id" HAVING -SUM(c."TotalAmount") > 0
                """);
            migrationBuilder.AddColumn<string>(
                name: "AdminEnvironment",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminEnvironmentSource",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstSendContextJson",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreparationContextJson",
                schema: "fiscal",
                table: "fiscal_invoices",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContextJson",
                schema: "fiscal",
                table: "fiscal_exchanges",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Environment",
                schema: "fiscal",
                table: "fiscal_exchanges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentSource",
                schema: "fiscal",
                table: "fiscal_exchanges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceState",
                schema: "fiscal",
                table: "fiscal_exchanges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalIdentifier",
                schema: "fiscal",
                table: "fiscal_exchanges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalCompletion",
                schema: "fiscal",
                table: "fiscal_exchanges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                schema: "fiscal",
                table: "fiscal_exchanges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileFingerprint",
                schema: "fiscal",
                table: "fiscal_exchanges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provenance",
                schema: "fiscal",
                table: "fiscal_exchanges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestUuid",
                schema: "fiscal",
                table: "fiscal_exchanges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientId",
                schema: "fiscal",
                table: "fiscal_audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Executor",
                schema: "fiscal",
                table: "fiscal_audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InitiatorId",
                schema: "fiscal",
                table: "fiscal_audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InitiatorName",
                schema: "fiscal",
                table: "fiscal_audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvoiceId",
                schema: "fiscal",
                table: "fiscal_audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fiscal_api_requests",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    Method = table.Column<string>(type: "text", nullable: false),
                    RouteTemplate = table.Column<string>(type: "text", nullable: false),
                    ApiHttpStatus = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    ClientId = table.Column<string>(type: "text", nullable: true),
                    InitiatorId = table.Column<string>(type: "text", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "text", nullable: false),
                    Security = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_api_requests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_payments_PaymentType_InvoiceId",
                schema: "fiscal",
                table: "fiscal_payments",
                columns: new[] { "PaymentType", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_CompanyId_AdminEnvironment_IssueDateTime_Id",
                schema: "fiscal",
                table: "fiscal_invoices",
                columns: new[] { "CompanyId", "AdminEnvironment", "IssueDateTime", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_CompanyId_BuyerIdentificationNumber",
                schema: "fiscal",
                table: "fiscal_invoices",
                columns: new[] { "CompanyId", "BuyerIdentificationNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_CompanyId_InvoiceNumber",
                schema: "fiscal",
                table: "fiscal_invoices",
                columns: new[] { "CompanyId", "InvoiceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_CompanyId_IssueDateTime_Id",
                schema: "fiscal",
                table: "fiscal_invoices",
                columns: new[] { "CompanyId", "IssueDateTime", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_exchanges_CompanyId_Environment_CreatedAt_Id",
                schema: "fiscal",
                table: "fiscal_exchanges",
                columns: new[] { "CompanyId", "Environment", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_exchanges_CorrelationId_Id",
                schema: "fiscal",
                table: "fiscal_exchanges",
                columns: new[] { "CorrelationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_exchanges_InvoiceId_CreatedAt_Id",
                schema: "fiscal",
                table: "fiscal_exchanges",
                columns: new[] { "InvoiceId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_audit_logs_CompanyId_CorrelationId_CreatedAt",
                schema: "fiscal",
                table: "fiscal_audit_logs",
                columns: new[] { "CompanyId", "CorrelationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_audit_logs_CompanyId_InvoiceId_CreatedAt",
                schema: "fiscal",
                table: "fiscal_audit_logs",
                columns: new[] { "CompanyId", "InvoiceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_api_requests_CompanyId_CreatedAt_Id",
                schema: "fiscal",
                table: "fiscal_api_requests",
                columns: new[] { "CompanyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_api_requests_CorrelationId_Id",
                schema: "fiscal",
                table: "fiscal_api_requests",
                columns: new[] { "CorrelationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_api_requests_InvoiceId_CreatedAt",
                schema: "fiscal",
                table: "fiscal_api_requests",
                columns: new[] { "InvoiceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_api_requests_Security_CreatedAt_Id",
                schema: "fiscal",
                table: "fiscal_api_requests",
                columns: new[] { "Security", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW fiscal.admin_workflow_debts");
            migrationBuilder.DropTable(
                name: "fiscal_api_requests",
                schema: "fiscal");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_payments_PaymentType_InvoiceId",
                schema: "fiscal",
                table: "fiscal_payments");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_CompanyId_AdminEnvironment_IssueDateTime_Id",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_CompanyId_BuyerIdentificationNumber",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_CompanyId_InvoiceNumber",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_invoices_CompanyId_IssueDateTime_Id",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_exchanges_CompanyId_Environment_CreatedAt_Id",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_exchanges_CorrelationId_Id",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_exchanges_InvoiceId_CreatedAt_Id",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_audit_logs_CompanyId_CorrelationId_CreatedAt",
                schema: "fiscal",
                table: "fiscal_audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_audit_logs_CompanyId_InvoiceId_CreatedAt",
                schema: "fiscal",
                table: "fiscal_audit_logs");

            migrationBuilder.DropColumn(
                name: "AdminEnvironment",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "AdminEnvironmentSource",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "FirstSendContextJson",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "PreparationContextJson",
                schema: "fiscal",
                table: "fiscal_invoices");

            migrationBuilder.DropColumn(
                name: "ContextJson",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropColumn(
                name: "Environment",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropColumn(
                name: "EnvironmentSource",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropColumn(
                name: "EvidenceState",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropColumn(
                name: "FiscalIdentifier",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropColumn(
                name: "LocalCompletion",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropColumn(
                name: "Outcome",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropColumn(
                name: "ProfileFingerprint",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropColumn(
                name: "Provenance",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropColumn(
                name: "RequestUuid",
                schema: "fiscal",
                table: "fiscal_exchanges");

            migrationBuilder.DropColumn(
                name: "ClientId",
                schema: "fiscal",
                table: "fiscal_audit_logs");

            migrationBuilder.DropColumn(
                name: "Executor",
                schema: "fiscal",
                table: "fiscal_audit_logs");

            migrationBuilder.DropColumn(
                name: "InitiatorId",
                schema: "fiscal",
                table: "fiscal_audit_logs");

            migrationBuilder.DropColumn(
                name: "InitiatorName",
                schema: "fiscal",
                table: "fiscal_audit_logs");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                schema: "fiscal",
                table: "fiscal_audit_logs");
        }
    }
}
