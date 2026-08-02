using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Abstractions;

public interface IIicGenerator
{
    Task<string> GenerateAsync(FiscalInvoice invoice, CancellationToken cancellationToken);
}

public interface IFiscalXmlBuilder
{
    Task<string> BuildRegisterInvoiceRequestAsync(
        FiscalInvoice invoice,
        CancellationToken cancellationToken);
}

public interface IDigitalSigningService
{
    Task<string> SignAsync(string unsignedXml, Guid companyId, CancellationToken cancellationToken);
}

public interface IPuFiscalClient
{
    Task<PuFiscalResponse> RegisterInvoiceAsync(
        string signedXml,
        string correlationId,
        CancellationToken cancellationToken);
}

public interface IAuditService
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);
}

public sealed record PuFiscalResponse(bool IsSuccess, string? Jikr, string? ErrorCode, string? ErrorMessage);

public sealed record AuditEntry(
    string Action,
    Guid? InvoiceId,
    Guid? CompanyId,
    string CorrelationId,
    string Actor,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string?> Data);
