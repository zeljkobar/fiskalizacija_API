using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence;

public sealed class SummaFiscalDbContext(DbContextOptions<SummaFiscalDbContext> options)
    : DbContext(options)
{
    public DbSet<CompanyRecord> Companies => Set<CompanyRecord>();
    public DbSet<ApiClientRecord> ApiClients => Set<ApiClientRecord>();
    public DbSet<ApiClientCompanyAccessRecord> ApiClientCompanyAccesses => Set<ApiClientCompanyAccessRecord>();
    public DbSet<FiscalProfileRecord> FiscalProfiles => Set<FiscalProfileRecord>();
    public DbSet<BusinessUnitRecord> BusinessUnits => Set<BusinessUnitRecord>();
    public DbSet<FiscalDeviceRecord> FiscalDevices => Set<FiscalDeviceRecord>();
    public DbSet<FiscalOperatorRecord> FiscalOperators => Set<FiscalOperatorRecord>();
    public DbSet<FiscalCertificateRecord> FiscalCertificates => Set<FiscalCertificateRecord>();
    public DbSet<FiscalCertificateAlertRecord> FiscalCertificateAlerts => Set<FiscalCertificateAlertRecord>();
    public DbSet<FiscalAuditRecord> FiscalAudits => Set<FiscalAuditRecord>();
    public DbSet<FiscalInvoiceRecord> FiscalInvoices => Set<FiscalInvoiceRecord>();
    public DbSet<FiscalInvoiceItemRecord> FiscalInvoiceItems => Set<FiscalInvoiceItemRecord>();
    public DbSet<FiscalPaymentRecord> FiscalPayments => Set<FiscalPaymentRecord>();
    public DbSet<CashDepositRecord> CashDeposits => Set<CashDepositRecord>();
    public DbSet<FiscalExchangeRecord> FiscalExchanges => Set<FiscalExchangeRecord>();
    public DbSet<InvoiceSequenceRecord> InvoiceSequences => Set<InvoiceSequenceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("fiscal");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SummaFiscalDbContext).Assembly);
    }
}
