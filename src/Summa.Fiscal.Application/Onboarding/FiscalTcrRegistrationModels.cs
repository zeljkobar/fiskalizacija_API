namespace Summa.Fiscal.Application.Onboarding;

public sealed record RegisterProductionTcrCommand(
    string InternalCode,
    DateOnly ValidFrom,
    string Confirmation);

public sealed record RegisterProductionTcrResult(
    Guid DeviceId,
    Guid CompanyId,
    Guid BusinessUnitId,
    string InternalCode,
    string TcrCode,
    DateTimeOffset RegisteredAt,
    Guid ExchangeId);

public interface IFiscalTcrRegistrationService
{
    Task<RegisterProductionTcrResult> RegisterProductionAsync(
        Guid companyId,
        RegisterProductionTcrCommand command,
        string actor,
        string correlationId,
        CancellationToken cancellationToken);
}
