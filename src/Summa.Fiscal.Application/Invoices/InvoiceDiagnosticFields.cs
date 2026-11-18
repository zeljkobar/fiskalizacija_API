using Summa.Fiscal.Application.Admin;

namespace Summa.Fiscal.Application.Invoices;

public abstract record InvoiceDiagnosticFields
{
    public Guid? BusinessUnitId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public Guid? OperatorId { get; private set; }
    public string Environment { get; private set; } = "Unknown";
    public string EnvironmentSource { get; private set; } = "Unknown";
    public AdminLabel? BusinessUnit { get; private set; }
    public AdminLabel? Device { get; private set; }
    public AdminLabel? Operator { get; private set; }
    public DocumentCategory? Category { get; private set; }
    public InvoiceAttemptSummary? LastAttempt { get; private set; }
    public AdminError? LastError { get; private set; }
    public AdminError? LastPuError { get; private set; }
    public string DiagnosticsAvailability { get; private set; } = "NotRecorded";
    public InvoiceFiscalContext? FiscalContext { get; private set; }
    public IReadOnlyDictionary<string, string>? Links { get; private set; }

    public void SetResourceIds(Guid businessUnitId, Guid deviceId, Guid operatorId)
    {
        BusinessUnitId = businessUnitId; DeviceId = deviceId; OperatorId = operatorId;
    }
    public void AttachDiagnostics(InvoiceAdminDetail value, bool allowExchanges, bool full)
    {
        var x = value.Invoice;
        SetResourceIds(x.BusinessUnitId, x.DeviceId, x.OperatorId);
        Environment = x.Environment; EnvironmentSource = x.EnvironmentSource;
        LastError = x.LastError; LastPuError = value.LastPuError; DiagnosticsAvailability = value.DiagnosticsAvailability;
        if (value.LastAttempt is { } a)
            LastAttempt = new(a.StartedAt, a.CompletedAt, a.Outcome, allowExchanges ? a.ExchangeId : null, allowExchanges ? a.CorrelationId : null);
        if (!full) return;
        BusinessUnit = x.BusinessUnit; Device = x.Device; Operator = x.Operator; Category = x.Category;
        FiscalContext = new(value.Preparation, value.FirstSend);
        var links = new Dictionary<string, string>();
        if (allowExchanges) links["exchanges"] = $"/api/v1/admin/invoices/{x.Id:D}/exchanges";
        if (x.OriginalInvoiceId is { } original) links["originalInvoice"] = $"/api/v1/fiscal/invoices/{original:D}";
        if (x.AdvanceSettlementFinalInvoiceId is { } final) links["advanceSettlementFinalInvoice"] = $"/api/v1/fiscal/invoices/{final:D}";
        if (x.AdvanceRestorationStornoInvoiceId is { } storno) links["advanceRestorationStornoInvoice"] = $"/api/v1/fiscal/invoices/{storno:D}";
        Links = links;
    }
}
public sealed record InvoiceAttemptSummary(DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string Outcome, Guid? ExchangeId, string? CorrelationId);
public sealed record InvoiceFiscalContext(FiscalContextSnapshot? Preparation, FiscalContextSnapshot? FirstSend);
