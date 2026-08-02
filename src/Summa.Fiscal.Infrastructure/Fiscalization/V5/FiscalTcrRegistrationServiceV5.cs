using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Summa.Fiscal.Application.Activation;
using Summa.Fiscal.Application.Onboarding;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed class FiscalTcrRegistrationServiceV5(
    IFiscalOnboardingRepository repository,
    IFiscalCertificateVault vault,
    IRegisterTcrXmlBuilderV5 xmlBuilder,
    IFiscalXmlSignerV5 signer,
    ISoapEnvelopeV5 envelope,
    IRegisterTcrResponseParserV5 parser,
    IFiscalExchangeStoreV5 exchangeStore,
    IFiscalActivationRepository activationRepository) : IFiscalTcrRegistrationService
{
    public async Task<RegisterProductionTcrResult> RegisterProductionAsync(Guid companyId,
        RegisterProductionTcrCommand command, string actor, string correlationId, CancellationToken cancellationToken)
    {
        var company = await repository.GetCompanyAsync(companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("COMPANY_NOT_FOUND", "Firma ne postoji.");
        if (await activationRepository.IsProductionActiveAsync(companyId, cancellationToken))
            throw new FiscalOnboardingException("PRODUCTION_CONFIGURATION_LOCKED", "Produkcijska konfiguracija je zaključana. Registracija novog ENU-a zahtijeva kontrolisani povratak u Test režim.");
        if (string.IsNullOrWhiteSpace(command.InternalCode) || command.InternalCode.Length > 50)
            throw new FiscalOnboardingException("TCR_INTERNAL_CODE_INVALID", "Interna oznaka ENU-a mora imati od 1 do 50 znakova.");
        var expectedConfirmation = $"REGISTER_PRODUCTION_ENU:{company.Tin}:{command.InternalCode}";
        if (!string.Equals(command.Confirmation, expectedConfirmation, StringComparison.Ordinal))
            throw new FiscalOnboardingException("TCR_REGISTRATION_CONFIRMATION_INVALID", $"Potvrda nije ispravna. Očekivana vrijednost je {expectedConfirmation}.");
        var profile = await repository.GetProductionProfileAsync(companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("PRODUCTION_PROFILE_NOT_FOUND", "Produkcioni profil nije podešen.");
        var certificates = await repository.ListCertificatesAsync(companyId, cancellationToken);
        var certificate = certificates.SingleOrDefault(x => x.IsActive && x.ValidTo > DateTimeOffset.UtcNow)
            ?? throw new FiscalOnboardingException("ACTIVE_CERTIFICATE_MISSING", "Nema aktivnog važećeg fiskalnog sertifikata.");
        var storageKey = await repository.GetCertificateStorageKeyAsync(companyId, certificate.Id, cancellationToken)
            ?? throw new FiscalOnboardingException("CERTIFICATE_STORAGE_NOT_FOUND", "Skladišni zapis sertifikata ne postoji.");
        var material = await vault.LoadAsync(storageKey, cancellationToken);
        var device = await repository.CreatePendingProductionDeviceAsync(companyId, profile.BusinessUnit.Id, command.InternalCode.Trim(), cancellationToken);
        if (device.RegistrationStatus == "Registered" && !string.IsNullOrWhiteSpace(device.TcrCode))
            return new(device.Id, companyId, device.BusinessUnitId, device.InternalCode, device.TcrCode, device.RegisteredAt!.Value, Guid.Empty);

        try
        {
            using var signingCertificate = X509CertificateLoader.LoadPkcs12(material.PfxBytes, material.Password,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
            var unsigned = xmlBuilder.BuildUnsigned(new(Guid.NewGuid(), MontenegroNow(), company.Tin,
                profile.BusinessUnit.Code, device.InternalCode, profile.SoftwareCode, profile.MaintainerCode, command.ValidFrom));
            var signed = signer.SignRequest(unsigned, signingCertificate);
            if (!signed.SignatureVerified) throw new InvalidOperationException("Digitalni potpis RegisterTCR zahtjeva nije validan.");
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "Fiscalization", "V5", "Schemas", "FiscalService_v5_official.xsd");
            var validation = new FiscalXmlSchemaValidatorV5(schemaPath).Validate(signed.SignedDocument);
            if (!validation.IsValid) throw new InvalidOperationException($"RegisterTCR nije prošao PU XSD: {string.Join("; ", validation.Errors.Select(x => x.Message))}");

            using var handler = new PuClientCertificateHandlerV5(signingCertificate);
            using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var transport = await new PuTcrSoapClientV5(httpClient, envelope, parser, exchangeStore)
                .RegisterAsync(new Uri(profile.Endpoint), signed.SignedDocument, correlationId, cancellationToken);
            if (!transport.Response.IsSuccess || string.IsNullOrWhiteSpace(transport.Response.TcrCode))
                throw new FiscalOnboardingException(transport.Response.Fault?.Code ?? "TCR_REGISTRATION_REJECTED",
                    transport.Response.Fault?.Message ?? "PU je odbila registraciju ENU-a.");
            var registeredAt = DateTimeOffset.UtcNow;
            var completed = await repository.CompleteDeviceRegistrationAsync(companyId, device.Id, transport.Response.TcrCode, registeredAt, cancellationToken);
            await repository.AddAuditAsync(companyId, "PRODUCTION_TCR_REGISTERED", correlationId, actor,
                JsonSerializer.Serialize(new { completed.Id, completed.InternalCode, completed.TcrCode, command.ValidFrom, transport.ExchangeId }), cancellationToken);
            return new(completed.Id, companyId, completed.BusinessUnitId, completed.InternalCode, completed.TcrCode!, registeredAt, transport.ExchangeId);
        }
        catch
        {
            await repository.MarkDeviceRegistrationFailedAsync(companyId, device.Id, CancellationToken.None);
            await repository.AddAuditAsync(companyId, "PRODUCTION_TCR_REGISTRATION_FAILED", correlationId, actor,
                JsonSerializer.Serialize(new { device.Id, device.InternalCode, command.ValidFrom }), CancellationToken.None);
            throw;
        }
    }

    private static DateTimeOffset MontenegroNow()
    {
        var id = OperatingSystem.IsWindows() ? "Central European Standard Time" : "Europe/Podgorica";
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(id));
    }
}
