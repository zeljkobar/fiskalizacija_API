using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFiscalSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fiscal");

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tin = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    LegalName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ShortName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    IsVatPayer = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_exchanges",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CashDepositId = table.Column<Guid>(type: "uuid", nullable: true),
                    Operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SoapAction = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    RequestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestStoragePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ResponseStoragePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FaultCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FaultMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_exchanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "business_units",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Town = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_business_units_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "fiscal",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_certificates",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Thumbprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_certificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_certificates_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "fiscal",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_operators",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_operators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_operators_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "fiscal",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_profiles",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SoftwareCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MaintainerCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_profiles_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "fiscal",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_devices",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    TcrCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InternalCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_devices_business_units_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalSchema: "fiscal",
                        principalTable: "business_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_deposits",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CashAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ChangeDateTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestUuid = table.Column<Guid>(type: "uuid", nullable: false),
                    Fcdc = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_deposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cash_deposits_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "fiscal",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cash_deposits_fiscal_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_invoices",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InvoiceOrdinalNumber = table.Column<int>(type: "integer", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IssueDateTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Iic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IicSignature = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Jikr = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestUuid = table.Column<Guid>(type: "uuid", nullable: false),
                    FiscalizedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_invoices_business_units_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalSchema: "fiscal",
                        principalTable: "business_units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fiscal_invoices_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "fiscal",
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fiscal_invoices_fiscal_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fiscal_invoices_fiscal_operators_OperatorId",
                        column: x => x.OperatorId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_sequences",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_sequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_sequences_fiscal_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_invoice_items",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPriceBeforeVat = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPriceAfterVat = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RebateRate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_invoice_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_invoice_items_fiscal_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fiscal_payments",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiscal_payments_fiscal_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "fiscal",
                        principalTable: "fiscal_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_units_CompanyId_Code",
                schema: "fiscal",
                table: "business_units",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_deposits_CompanyId",
                schema: "fiscal",
                table: "cash_deposits",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_deposits_DeviceId",
                schema: "fiscal",
                table: "cash_deposits",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_deposits_Fcdc",
                schema: "fiscal",
                table: "cash_deposits",
                column: "Fcdc",
                unique: true,
                filter: "\"Fcdc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_cash_deposits_RequestUuid",
                schema: "fiscal",
                table: "cash_deposits",
                column: "RequestUuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_companies_Tin",
                schema: "fiscal",
                table: "companies",
                column: "Tin",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_certificates_CompanyId_Thumbprint",
                schema: "fiscal",
                table: "fiscal_certificates",
                columns: new[] { "CompanyId", "Thumbprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_devices_BusinessUnitId",
                schema: "fiscal",
                table: "fiscal_devices",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_devices_TcrCode",
                schema: "fiscal",
                table: "fiscal_devices",
                column: "TcrCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_exchanges_CorrelationId",
                schema: "fiscal",
                table: "fiscal_exchanges",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_exchanges_InvoiceId",
                schema: "fiscal",
                table: "fiscal_exchanges",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoice_items_InvoiceId_LineNumber",
                schema: "fiscal",
                table: "fiscal_invoice_items",
                columns: new[] { "InvoiceId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_BusinessUnitId",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_CompanyId_IdempotencyKey",
                schema: "fiscal",
                table: "fiscal_invoices",
                columns: new[] { "CompanyId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_DeviceId",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_Iic",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "Iic",
                unique: true,
                filter: "\"Iic\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_Jikr",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "Jikr",
                unique: true,
                filter: "\"Jikr\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_invoices_OperatorId",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_operators_CompanyId_OperatorCode",
                schema: "fiscal",
                table: "fiscal_operators",
                columns: new[] { "CompanyId", "OperatorCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_payments_InvoiceId",
                schema: "fiscal",
                table: "fiscal_payments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_profiles_CompanyId",
                schema: "fiscal",
                table: "fiscal_profiles",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_sequences_DeviceId_Year",
                schema: "fiscal",
                table: "invoice_sequences",
                columns: new[] { "DeviceId", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_deposits",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_certificates",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_exchanges",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_invoice_items",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_payments",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_profiles",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "invoice_sequences",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_invoices",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_devices",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_operators",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "business_units",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "fiscal");
        }
    }
}
