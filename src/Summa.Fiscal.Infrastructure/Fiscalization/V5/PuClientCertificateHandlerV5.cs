using System.Security.Cryptography.X509Certificates;
using Summa.Fiscal.Infrastructure.Certificates;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed class PuClientCertificateHandlerV5 : HttpClientHandler
{
    private readonly LoadedFiscalCertificate _loadedCertificate;
    private readonly X509Certificate2? _storeCertificate;

    public PuClientCertificateHandlerV5(LoadedFiscalCertificate loadedCertificate)
    {
        _loadedCertificate =
            loadedCertificate ?? throw new ArgumentNullException(nameof(loadedCertificate));
        ClientCertificateOptions = ClientCertificateOption.Manual;

        if (OperatingSystem.IsWindows())
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(
                X509FindType.FindByThumbprint,
                _loadedCertificate.Thumbprint,
                validOnly: false);
            _storeCertificate = matches
                .OfType<X509Certificate2>()
                .FirstOrDefault(certificate => certificate.HasPrivateKey);
        }

        ClientCertificates.Add(_storeCertificate ?? _loadedCertificate.Certificate);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _loadedCertificate.Dispose();
            _storeCertificate?.Dispose();
        }
    }
}
