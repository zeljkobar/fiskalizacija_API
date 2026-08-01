namespace Summa.Fiscal.Domain.Invoices;

public enum FiscalStatus
{
    Draft = 0,
    Validated = 1,
    ReadyForFiscalization = 2,
    FiscalizationPending = 3,
    Fiscalized = 4,
    FiscalizationFailed = 5,
    OfflineIssued = 6,
    RetryPending = 7,
    Cancelled = 8,
    StornoCreated = 9
}
