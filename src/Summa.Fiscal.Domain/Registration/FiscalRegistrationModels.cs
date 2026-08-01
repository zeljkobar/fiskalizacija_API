namespace Summa.Fiscal.Domain.Registration;

public sealed record FiscalCompany(Guid Id, string Name, string TaxNumber, bool IsActive);

public sealed record BusinessUnit(Guid Id, Guid CompanyId, string Code, string Name, bool IsActive);

public sealed record FiscalDevice(Guid Id, Guid BusinessUnitId, string Code, bool IsActive);

public sealed record FiscalOperator(Guid Id, Guid CompanyId, string Code, string Name, bool IsActive);
