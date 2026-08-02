using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Summa.Fiscal.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvironmentProfilesAndTcrRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fiscal_profiles_CompanyId",
                schema: "fiscal",
                table: "fiscal_profiles");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_operators_CompanyId_OperatorCode",
                schema: "fiscal",
                table: "fiscal_operators");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_devices_BusinessUnitId",
                schema: "fiscal",
                table: "fiscal_devices");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_devices_TcrCode",
                schema: "fiscal",
                table: "fiscal_devices");

            migrationBuilder.DropIndex(
                name: "IX_business_units_CompanyId_Code",
                schema: "fiscal",
                table: "business_units");

            migrationBuilder.AddColumn<bool>(
                name: "IsSoftwareCertified",
                schema: "fiscal",
                table: "fiscal_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PaymentPolicy",
                schema: "fiscal",
                table: "fiscal_profiles",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Any");

            migrationBuilder.AddColumn<string>(
                name: "ProducerCode",
                schema: "fiscal",
                table: "fiscal_profiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SoftwareName",
                schema: "fiscal",
                table: "fiscal_profiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SoftwareVersion",
                schema: "fiscal",
                table: "fiscal_profiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Environment",
                schema: "fiscal",
                table: "fiscal_operators",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Test");

            migrationBuilder.AlterColumn<string>(
                name: "TcrCode",
                schema: "fiscal",
                table: "fiscal_devices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RegisteredAt",
                schema: "fiscal",
                table: "fiscal_devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegistrationStatus",
                schema: "fiscal",
                table: "fiscal_devices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Registered");

            migrationBuilder.AddColumn<string>(
                name: "ActiveEnvironment",
                schema: "fiscal",
                table: "companies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Test");

            migrationBuilder.AddColumn<string>(
                name: "Environment",
                schema: "fiscal",
                table: "business_units",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Test");

            migrationBuilder.Sql("""
                UPDATE fiscal.companies AS c
                SET "ActiveEnvironment" = p."Environment"
                FROM fiscal.fiscal_profiles AS p
                WHERE p."CompanyId" = c."Id";

                UPDATE fiscal.business_units AS b
                SET "Environment" = p."Environment"
                FROM fiscal.fiscal_profiles AS p
                WHERE p."CompanyId" = b."CompanyId";

                UPDATE fiscal.fiscal_operators AS o
                SET "Environment" = p."Environment"
                FROM fiscal.fiscal_profiles AS p
                WHERE p."CompanyId" = o."CompanyId";

                UPDATE fiscal.fiscal_devices
                SET "RegisteredAt" = "UpdatedAt"
                WHERE "TcrCode" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_profiles_CompanyId_Environment",
                schema: "fiscal",
                table: "fiscal_profiles",
                columns: new[] { "CompanyId", "Environment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_operators_CompanyId_Environment_OperatorCode",
                schema: "fiscal",
                table: "fiscal_operators",
                columns: new[] { "CompanyId", "Environment", "OperatorCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_devices_BusinessUnitId_InternalCode",
                schema: "fiscal",
                table: "fiscal_devices",
                columns: new[] { "BusinessUnitId", "InternalCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_devices_TcrCode",
                schema: "fiscal",
                table: "fiscal_devices",
                column: "TcrCode",
                unique: true,
                filter: "\"TcrCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_business_units_CompanyId_Environment_Code",
                schema: "fiscal",
                table: "business_units",
                columns: new[] { "CompanyId", "Environment", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fiscal_profiles_CompanyId_Environment",
                schema: "fiscal",
                table: "fiscal_profiles");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_operators_CompanyId_Environment_OperatorCode",
                schema: "fiscal",
                table: "fiscal_operators");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_devices_BusinessUnitId_InternalCode",
                schema: "fiscal",
                table: "fiscal_devices");

            migrationBuilder.DropIndex(
                name: "IX_fiscal_devices_TcrCode",
                schema: "fiscal",
                table: "fiscal_devices");

            migrationBuilder.DropIndex(
                name: "IX_business_units_CompanyId_Environment_Code",
                schema: "fiscal",
                table: "business_units");

            migrationBuilder.DropColumn(
                name: "IsSoftwareCertified",
                schema: "fiscal",
                table: "fiscal_profiles");

            migrationBuilder.DropColumn(
                name: "PaymentPolicy",
                schema: "fiscal",
                table: "fiscal_profiles");

            migrationBuilder.DropColumn(
                name: "ProducerCode",
                schema: "fiscal",
                table: "fiscal_profiles");

            migrationBuilder.DropColumn(
                name: "SoftwareName",
                schema: "fiscal",
                table: "fiscal_profiles");

            migrationBuilder.DropColumn(
                name: "SoftwareVersion",
                schema: "fiscal",
                table: "fiscal_profiles");

            migrationBuilder.DropColumn(
                name: "Environment",
                schema: "fiscal",
                table: "fiscal_operators");

            migrationBuilder.DropColumn(
                name: "RegisteredAt",
                schema: "fiscal",
                table: "fiscal_devices");

            migrationBuilder.DropColumn(
                name: "RegistrationStatus",
                schema: "fiscal",
                table: "fiscal_devices");

            migrationBuilder.DropColumn(
                name: "ActiveEnvironment",
                schema: "fiscal",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "Environment",
                schema: "fiscal",
                table: "business_units");

            migrationBuilder.AlterColumn<string>(
                name: "TcrCode",
                schema: "fiscal",
                table: "fiscal_devices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_profiles_CompanyId",
                schema: "fiscal",
                table: "fiscal_profiles",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fiscal_operators_CompanyId_OperatorCode",
                schema: "fiscal",
                table: "fiscal_operators",
                columns: new[] { "CompanyId", "OperatorCode" },
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
                name: "IX_business_units_CompanyId_Code",
                schema: "fiscal",
                table: "business_units",
                columns: new[] { "CompanyId", "Code" },
                unique: true);
        }
    }
}
