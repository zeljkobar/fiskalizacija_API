namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed class PuFiscalizationOptionsV5
{
    public const string SectionName = "Fiscalization:PuV5";

    public string Environment { get; init; } = "Test";
    public string Endpoint { get; init; } = string.Empty;
    public string IssuerTin { get; init; } = string.Empty;
    public string BusinessUnitCode { get; init; } = string.Empty;
    public string TcrCode { get; init; } = string.Empty;
    public string SoftwareCode { get; init; } = string.Empty;
    public string OperatorCode { get; init; } = string.Empty;
    public string SellerName { get; init; } = string.Empty;
    public string SellerAddress { get; init; } = string.Empty;
    public string SellerTown { get; init; } = string.Empty;
    public string SellerCountry { get; init; } = "MNE";

    public PuFiscalizationReadinessV5 GetReadiness()
    {
        var missing = new List<string>();

        Require(missing, nameof(Endpoint), Endpoint);
        Require(missing, nameof(IssuerTin), IssuerTin);
        Require(missing, nameof(BusinessUnitCode), BusinessUnitCode);
        Require(missing, nameof(TcrCode), TcrCode);
        Require(missing, nameof(SoftwareCode), SoftwareCode);
        Require(missing, nameof(OperatorCode), OperatorCode);
        Require(missing, nameof(SellerName), SellerName);

        if (!string.IsNullOrWhiteSpace(Endpoint) &&
            (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpointUri) ||
             endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            missing.Add($"{nameof(Endpoint)}:HTTPS");
        }

        return new(missing.Count == 0, missing);
    }

    public void EnsureReadyForInvoice()
    {
        var readiness = GetReadiness();
        if (!readiness.IsReady)
        {
            throw new InvalidOperationException(
                $"PU v5 konfiguracija nije kompletna. Nedostaje: {string.Join(", ", readiness.MissingFields)}.");
        }
    }

    private static void Require(ICollection<string> missing, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(name);
        }
    }
}

public sealed record PuFiscalizationReadinessV5(
    bool IsReady,
    IReadOnlyCollection<string> MissingFields);
