using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence.Configuration;

internal abstract class FiscalRecordConfiguration<T> : IEntityTypeConfiguration<T>
    where T : FiscalRecord
{
    public void Configure(EntityTypeBuilder<T> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        ConfigureRecord(builder);
    }

    protected abstract void ConfigureRecord(EntityTypeBuilder<T> builder);
}

internal sealed class CompanyConfiguration : FiscalRecordConfiguration<CompanyRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<CompanyRecord> b)
    {
        b.ToTable("companies");
        b.Property(x => x.Tin).HasMaxLength(13).IsRequired();
        b.Property(x => x.LegalName).HasMaxLength(300).IsRequired();
        b.Property(x => x.ShortName).HasMaxLength(150);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Town).HasMaxLength(100);
        b.Property(x => x.Country).HasMaxLength(3).HasDefaultValue("MNE").IsRequired();
        b.Property(x => x.ActiveEnvironment).HasMaxLength(20).HasDefaultValue("Test").IsRequired();
        b.HasIndex(x => x.Tin).IsUnique();
    }
}

internal sealed class ApiClientConfiguration : FiscalRecordConfiguration<ApiClientRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<ApiClientRecord> b)
    {
        b.ToTable("api_clients");
        b.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ApiKeyHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ApiKeyPrefix).HasMaxLength(20).IsRequired();
        b.Property(x => x.Permissions).HasMaxLength(1000).IsRequired();
        b.HasIndex(x => x.ClientId).IsUnique();
        b.HasIndex(x => x.ApiKeyHash).IsUnique();
    }
}

internal sealed class ApiClientCompanyAccessConfiguration
    : FiscalRecordConfiguration<ApiClientCompanyAccessRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<ApiClientCompanyAccessRecord> b)
    {
        b.ToTable("api_client_company_access");
        b.HasIndex(x => new { x.ApiClientId, x.CompanyId }).IsUnique();
        b.HasOne(x => x.ApiClient).WithMany(x => x.CompanyAccesses)
            .HasForeignKey(x => x.ApiClientId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Company).WithMany(x => x.ApiClientAccesses)
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FiscalProfileConfiguration : FiscalRecordConfiguration<FiscalProfileRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<FiscalProfileRecord> b)
    {
        b.ToTable("fiscal_profiles");
        b.Property(x => x.Environment).HasMaxLength(20).IsRequired();
        b.Property(x => x.Endpoint).HasMaxLength(500).IsRequired();
        b.Property(x => x.ProducerCode).HasMaxLength(50);
        b.Property(x => x.SoftwareName).HasMaxLength(200);
        b.Property(x => x.SoftwareVersion).HasMaxLength(50);
        b.Property(x => x.PaymentPolicy).HasMaxLength(30).HasDefaultValue("Any").IsRequired();
        b.Property(x => x.SoftwareCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.MaintainerCode).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.Environment }).IsUnique();
        b.HasOne(x => x.Company).WithMany(x => x.FiscalProfiles)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FiscalActivationConfiguration : FiscalRecordConfiguration<FiscalActivationRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<FiscalActivationRecord> b)
    {
        b.ToTable("fiscal_activations");
        b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.Property(x => x.TestJikr).HasMaxLength(100);
        b.Property(x => x.TestConfigurationHash).HasMaxLength(64);
        b.Property(x => x.TestPassedBy).HasMaxLength(200);
        b.Property(x => x.ProductionActivatedBy).HasMaxLength(200);
        b.HasIndex(x => x.CompanyId).IsUnique();
        b.HasOne(x => x.Company).WithOne(x => x.FiscalActivation)
            .HasForeignKey<FiscalActivationRecord>(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FiscalInvoiceRecord>().WithMany()
            .HasForeignKey(x => x.TestInvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BusinessUnitConfiguration : FiscalRecordConfiguration<BusinessUnitRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<BusinessUnitRecord> b)
    {
        b.ToTable("business_units");
        b.Property(x => x.Environment).HasMaxLength(20).HasDefaultValue("Test").IsRequired();
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Town).HasMaxLength(100);
        b.HasIndex(x => new { x.CompanyId, x.Environment, x.Code }).IsUnique();
        b.HasOne(x => x.Company).WithMany(x => x.BusinessUnits)
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FiscalDeviceConfiguration : FiscalRecordConfiguration<FiscalDeviceRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<FiscalDeviceRecord> b)
    {
        b.ToTable("fiscal_devices");
        b.Property(x => x.TcrCode).HasMaxLength(50);
        b.Property(x => x.InternalCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.RegistrationStatus).HasMaxLength(30).HasDefaultValue("Registered").IsRequired();
        b.HasIndex(x => x.TcrCode).IsUnique().HasFilter("\"TcrCode\" IS NOT NULL");
        b.HasIndex(x => new { x.BusinessUnitId, x.InternalCode }).IsUnique();
        b.HasOne(x => x.BusinessUnit).WithMany(x => x.Devices)
            .HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FiscalOperatorConfiguration : FiscalRecordConfiguration<FiscalOperatorRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<FiscalOperatorRecord> b)
    {
        b.ToTable("fiscal_operators");
        b.Property(x => x.Environment).HasMaxLength(20).HasDefaultValue("Test").IsRequired();
        b.Property(x => x.OperatorCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.FirstName).HasMaxLength(100);
        b.Property(x => x.LastName).HasMaxLength(100);
        b.HasIndex(x => new { x.CompanyId, x.Environment, x.OperatorCode }).IsUnique();
        b.HasOne(x => x.Company).WithMany(x => x.Operators)
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FiscalCertificateConfiguration : FiscalRecordConfiguration<FiscalCertificateRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<FiscalCertificateRecord> b)
    {
        b.ToTable("fiscal_certificates");
        b.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.Thumbprint).HasMaxLength(100).IsRequired();
        b.Property(x => x.SerialNumber).HasMaxLength(200).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Issuer).HasMaxLength(1000).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.Thumbprint }).IsUnique();
        b.HasIndex(x => x.CompanyId).IsUnique().HasFilter("\"IsActive\" = TRUE");
        b.HasOne(x => x.Company).WithMany(x => x.Certificates)
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FiscalAuditConfiguration : FiscalRecordConfiguration<FiscalAuditRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<FiscalAuditRecord> b)
    {
        b.ToTable("fiscal_audit_logs");
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(200).IsRequired();
        b.Property(x => x.Actor).HasMaxLength(200).IsRequired();
        b.Property(x => x.DataJson).HasColumnType("jsonb").IsRequired();
        b.HasIndex(x => x.CompanyId);
        b.HasIndex(x => x.CorrelationId);
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FiscalCertificateAlertConfiguration : FiscalRecordConfiguration<FiscalCertificateAlertRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<FiscalCertificateAlertRecord> b)
    {
        b.ToTable("fiscal_certificate_expiry_alerts");
        b.Property(x => x.AcknowledgedBy).HasMaxLength(200);
        b.HasIndex(x => new { x.CertificateId, x.ThresholdDays }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.IsAcknowledged, x.CreatedAt });
        b.HasOne(x => x.Certificate).WithMany(x => x.ExpiryAlerts)
            .HasForeignKey(x => x.CertificateId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Company).WithMany()
            .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FiscalInvoiceConfiguration : FiscalRecordConfiguration<FiscalInvoiceRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<FiscalInvoiceRecord> b)
    {
        b.ToTable("fiscal_invoices");
        b.Property(x => x.InvoiceType).HasMaxLength(30).IsRequired();
        b.Property(x => x.InvoiceNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.NetAmount).HasPrecision(18, 2);
        b.Property(x => x.VatAmount).HasPrecision(18, 2);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.Iic).HasMaxLength(100);
        b.Property(x => x.IicSignature).HasMaxLength(1024);
        b.Property(x => x.Jikr).HasMaxLength(100);
        b.Property(x => x.QrCodeData).HasMaxLength(2000);
        b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => x.Iic).IsUnique().HasFilter("\"Iic\" IS NOT NULL");
        b.HasIndex(x => x.Jikr).IsUnique().HasFilter("\"Jikr\" IS NOT NULL");
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Operator).WithMany().HasForeignKey(x => x.OperatorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FiscalInvoiceItemConfiguration : FiscalRecordConfiguration<FiscalInvoiceItemRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<FiscalInvoiceItemRecord> b)
    {
        b.ToTable("fiscal_invoice_items");
        b.Property(x => x.Code).HasMaxLength(100);
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(30);
        b.Property(x => x.Quantity).HasPrecision(18, 4);
        b.Property(x => x.UnitPriceBeforeVat).HasPrecision(18, 4);
        b.Property(x => x.UnitPriceAfterVat).HasPrecision(18, 4);
        b.Property(x => x.RebateRate).HasPrecision(9, 4);
        b.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        b.Property(x => x.VatRate).HasPrecision(9, 4);
        b.Property(x => x.VatAmount).HasPrecision(18, 2);
        b.Property(x => x.NetAmount).HasPrecision(18, 2);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.HasIndex(x => new { x.InvoiceId, x.LineNumber }).IsUnique();
        b.HasOne(x => x.Invoice).WithMany(x => x.Items)
            .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FiscalPaymentConfiguration : FiscalRecordConfiguration<FiscalPaymentRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<FiscalPaymentRecord> b)
    {
        b.ToTable("fiscal_payments");
        b.Property(x => x.PaymentType).HasMaxLength(30).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Reference).HasMaxLength(200);
        b.HasOne(x => x.Invoice).WithMany(x => x.Payments)
            .HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CashDepositConfiguration : FiscalRecordConfiguration<CashDepositRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<CashDepositRecord> b)
    {
        b.ToTable("cash_deposits");
        b.Property(x => x.Operation).HasMaxLength(30).IsRequired();
        b.Property(x => x.CashAmount).HasPrecision(18, 2);
        b.Property(x => x.Fcdc).HasMaxLength(100);
        b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.RequestUuid).IsUnique();
        b.HasIndex(x => x.Fcdc).IsUnique().HasFilter("\"Fcdc\" IS NOT NULL");
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FiscalExchangeConfiguration : FiscalRecordConfiguration<FiscalExchangeRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<FiscalExchangeRecord> b)
    {
        b.ToTable("fiscal_exchanges");
        b.Property(x => x.Operation).HasMaxLength(100).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(200).IsRequired();
        b.Property(x => x.Endpoint).HasMaxLength(500).IsRequired();
        b.Property(x => x.SoapAction).HasMaxLength(300).IsRequired();
        b.Property(x => x.RequestSha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.ResponseSha256).HasMaxLength(64);
        b.Property(x => x.RequestStoragePath).HasMaxLength(1000).IsRequired();
        b.Property(x => x.ResponseStoragePath).HasMaxLength(1000);
        b.Property(x => x.FaultCode).HasMaxLength(100);
        b.Property(x => x.FaultMessage).HasMaxLength(2000);
        b.HasIndex(x => x.CorrelationId);
        b.HasIndex(x => x.InvoiceId);
    }
}

internal sealed class InvoiceSequenceConfiguration : FiscalRecordConfiguration<InvoiceSequenceRecord>
{
    protected override void ConfigureRecord(EntityTypeBuilder<InvoiceSequenceRecord> b)
    {
        b.ToTable("invoice_sequences");
        b.HasIndex(x => new { x.DeviceId, x.Year }).IsUnique();
        b.HasOne(x => x.Device).WithMany()
            .HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
    }
}
