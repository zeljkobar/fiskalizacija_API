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
    public DbSet<FiscalActivationRecord> FiscalActivations => Set<FiscalActivationRecord>();
    public DbSet<FiscalAuditRecord> FiscalAudits => Set<FiscalAuditRecord>();
    public DbSet<FiscalInvoiceRecord> FiscalInvoices => Set<FiscalInvoiceRecord>();
    public DbSet<FiscalInvoiceItemRecord> FiscalInvoiceItems => Set<FiscalInvoiceItemRecord>();
    public DbSet<FiscalPaymentRecord> FiscalPayments => Set<FiscalPaymentRecord>();
    public DbSet<FiscalAdvanceUsageRecord> FiscalAdvanceUsages => Set<FiscalAdvanceUsageRecord>();
    public DbSet<FiscalSummaryOrderRecord> FiscalSummaryOrders => Set<FiscalSummaryOrderRecord>();
    public DbSet<CashDepositRecord> CashDeposits => Set<CashDepositRecord>();
    public DbSet<FiscalExchangeRecord> FiscalExchanges => Set<FiscalExchangeRecord>();
    public DbSet<FiscalApiRequestRecord> FiscalApiRequests => Set<FiscalApiRequestRecord>();
    public DbSet<AdminWorkflowDebt> AdminWorkflowDebts => Set<AdminWorkflowDebt>();
    public DbSet<InvoiceSequenceRecord> InvoiceSequences => Set<InvoiceSequenceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("fiscal");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SummaFiscalDbContext).Assembly);
        modelBuilder.Entity<AdminWorkflowDebt>().HasNoKey().ToView("admin_workflow_debts");
        modelBuilder.HasDbFunction(typeof(SummaFiscalDbContext).GetMethod(nameof(LocalTime))!)
            .HasName("timezone").IsBuiltIn().HasStoreType("timestamp without time zone");
    }

    public static DateTime LocalTime(string zone, DateTimeOffset time) => throw new NotSupportedException();
}
