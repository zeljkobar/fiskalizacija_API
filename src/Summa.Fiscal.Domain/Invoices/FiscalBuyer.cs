namespace Summa.Fiscal.Domain.Invoices;

public enum BuyerIdentificationType
{
    Tin = 0,
    Id = 1,
    Passport = 2,
    Vat = 3,
    Tax = 4,
    Social = 5
}

public sealed record FiscalBuyer(
    BuyerIdentificationType IdentificationType,
    string IdentificationNumber,
    string Name,
    string? Address = null,
    string? Town = null,
    string? Country = null,
    string? TaxIdentificationCode = null)
{
    public string IdentificationNumber { get; init; } = IdentificationNumber?.Trim() ?? string.Empty;
    public string Name { get; init; } = Name?.Trim() ?? string.Empty;
    public string? Address { get; init; } = Normalize(Address);
    public string? Town { get; init; } = Normalize(Town);
    public string? Country { get; init; } = Normalize(Country)?.ToUpperInvariant();
    public string? TaxIdentificationCode { get; init; } = Normalize(TaxIdentificationCode);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
