namespace Summa.Fiscal.Infrastructure.Certificates;

public sealed class FiscalDevelopmentCertificateOptions
{
    public const string SectionName = "Fiscalization:DevelopmentCertificate";

    public string Path { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
