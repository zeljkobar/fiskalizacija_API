using System.Security.Cryptography.X509Certificates;
using Summa.Fiscal.Infrastructure.Certificates;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed class PuClientCertificateHandlerV5 : HttpClientHandler
{
    private readonly LoadedFiscalCertificate? _loadedCertificate;
    private readonly X509Certificate2 _certificate;
    private X509Certificate2? _storeCertificate;

    public PuClientCertificateHandlerV5(LoadedFiscalCertificate loadedCertificate)
    {
        _loadedCertificate =
            loadedCertificate ?? throw new ArgumentNullException(nameof(loadedCertificate));
        _certificate = loadedCertificate.Certificate;
        Configure();
    }

    public PuClientCertificateHandlerV5(X509Certificate2 certificate)
    {
        _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        Configure();
    }

    private void Configure()
    {
        ClientCertificateOptions = ClientCertificateOption.Manual;

        if (OperatingSystem.IsWindows())
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(
                X509FindType.FindByThumbprint,
                _certificate.Thumbprint,
                validOnly: false);
            _storeCertificate = matches
                .OfType<X509Certificate2>()
                .FirstOrDefault(certificate => certificate.HasPrivateKey);
        }

        ClientCertificates.Add(_storeCertificate ?? _certificate);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _loadedCertificate?.Dispose();
            if (_loadedCertificate is null) _certificate.Dispose();
            _storeCertificate?.Dispose();
        }
    }
}
