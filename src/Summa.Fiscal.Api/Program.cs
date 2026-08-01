using System.Text.Json.Serialization;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Mvc;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Middleware;
using Summa.Fiscal.Api.Security;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Application.Onboarding;
using Summa.Fiscal.Application.Certificates;
using Summa.Fiscal.Infrastructure.Audit;
using Summa.Fiscal.Infrastructure.Certificates;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;
using Summa.Fiscal.Infrastructure.Persistence;
using Summa.Fiscal.Persistence;
using Summa.Fiscal.Persistence.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var correlationId =
            context.HttpContext.Items[CorrelationIdMiddleware.ItemName]?.ToString()
            ?? context.HttpContext.TraceIdentifier;
        var details = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors.Select(error =>
                new ApiErrorDetail(
                    "INVALID_REQUEST_VALUE",
                    entry.Key,
                    string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "Vrijednost nije ispravna."
                        : error.ErrorMessage)))
            .ToArray();
        var apiError = new ApiError(
            "INVALID_REQUEST",
            "HTTP zahtjev nije ispravan.",
            details);

        return new BadRequestObjectResult(
            ApiResponse<object>.Fail(apiError, correlationId));
    };
});

builder.Services.AddHealthChecks();
var fiscalDatabaseConnection = builder.Configuration.GetConnectionString("FiscalDatabase");
var usePostgreSql = !string.IsNullOrWhiteSpace(fiscalDatabaseConnection);
if (usePostgreSql)
{
    builder.Services.AddFiscalPersistence(fiscalDatabaseConnection!);
    builder.Services.AddScoped<IFiscalInvoiceRepository, PostgreSqlFiscalInvoiceRepository>();
    builder.Services.AddScoped<IApiClientRegistry, PostgreSqlApiClientRegistry>();
    builder.Services.AddScoped<IFiscalOnboardingRepository, PostgreSqlFiscalOnboardingRepository>();
    builder.Services.AddScoped<ICertificateExpiryRepository, PostgreSqlCertificateExpiryRepository>();
}
else
{
    builder.Services.AddSingleton<IFiscalInvoiceRepository, InMemoryFiscalInvoiceRepository>();
    builder.Services.AddSingleton<IApiClientRegistry, UnavailableApiClientRegistry>();
}
builder.Services.Configure<ApiAccessOptions>(
    builder.Configuration.GetSection(ApiAccessOptions.SectionName));
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<ApiAccessOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        options => builder.Configuration.GetSection(ApiAccessOptions.SectionName).Bind(options));
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IBootstrapAdminAuthorizer, BootstrapAdminAuthorizer>();
builder.Services.AddSingleton<IFiscalAccessAuthorizer, FiscalAccessAuthorizer>();
builder.Services.Configure<PuFiscalizationOptionsV5>(
    builder.Configuration.GetSection(PuFiscalizationOptionsV5.SectionName));
builder.Services.Configure<FiscalDevelopmentCertificateOptions>(
    builder.Configuration.GetSection(FiscalDevelopmentCertificateOptions.SectionName));
builder.Services.Configure<FiscalCertificateVaultOptions>(
    builder.Configuration.GetSection(FiscalCertificateVaultOptions.SectionName));
builder.Services.Configure<FiscalExchangeStorageOptionsV5>(
    builder.Configuration.GetSection(FiscalExchangeStorageOptionsV5.SectionName));
builder.Services.AddSingleton<IAuditService, InMemoryAuditService>();
builder.Services.AddSingleton<IFiscalInvoiceValidator, FiscalInvoiceValidator>();
builder.Services.AddScoped<IFiscalInvoiceApplicationService, FiscalInvoiceApplicationService>();
builder.Services.AddScoped<IFiscalOnboardingService, FiscalOnboardingService>();
builder.Services.AddScoped<ICertificateExpiryService, CertificateExpiryService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPfxCertificateLoader, PfxCertificateLoader>();
builder.Services.AddSingleton<IFiscalCertificateInspector, FiscalCertificateInspector>();
builder.Services.AddSingleton<IFiscalCertificateVault, EncryptedFileCertificateVault>();
builder.Services.AddSingleton<IIicGeneratorV5, IicGeneratorV5>();
builder.Services.AddSingleton<IRegisterInvoiceXmlBuilderV5, RegisterInvoiceXmlBuilderV5>();
builder.Services.AddSingleton<IFiscalXmlSignerV5, FiscalXmlSignerV5>();
builder.Services.AddSingleton<ISoapEnvelopeV5, SoapEnvelopeV5>();
builder.Services.AddSingleton<IRegisterInvoiceResponseParserV5, RegisterInvoiceResponseParserV5>();
builder.Services.AddSingleton<IFiscalQrCodeGeneratorV5, FiscalQrCodeGeneratorV5>();
builder.Services.AddSingleton<IFiscalDryRunServiceV5, FiscalDryRunServiceV5>();
builder.Services.AddScoped<IFiscalInvoiceSubmissionServiceV5, FiscalInvoiceSubmissionServiceV5>();
builder.Services.AddSingleton<IRegisterCashDepositXmlBuilderV5, RegisterCashDepositXmlBuilderV5>();
builder.Services.AddSingleton<IRegisterCashDepositResponseParserV5, RegisterCashDepositResponseParserV5>();
builder.Services.AddSingleton<ICashDepositDryRunServiceV5, CashDepositDryRunServiceV5>();
builder.Services.AddSingleton<IFiscalExchangeStoreV5>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<FiscalExchangeStorageOptionsV5>>()
        .Value;
    return new FileFiscalExchangeStoreV5(options);
});
builder.Services.AddHttpClient<IPuFiscalSoapClientV5, PuFiscalSoapClientV5>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var configuration = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<PuFiscalizationOptionsV5>>()
        .Value;
    var certificateConfiguration = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<FiscalDevelopmentCertificateOptions>>()
        .Value;
    var certificateLoader = serviceProvider.GetRequiredService<IPfxCertificateLoader>();
    var loadedCertificate = certificateLoader.Load(
        certificateConfiguration.Path,
        certificateConfiguration.Password,
        new(
            RequireCurrentlyValid: true,
            ExpectedIssuerTin: configuration.IssuerTin,
            KeyStorageFlags:
                X509KeyStorageFlags.MachineKeySet |
                X509KeyStorageFlags.PersistKeySet |
                X509KeyStorageFlags.Exportable));

    return new PuClientCertificateHandlerV5(loadedCertificate);
});
builder.Services.AddHttpClient<IPuCashDepositSoapClientV5, PuCashDepositSoapClientV5>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var configuration = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<PuFiscalizationOptionsV5>>()
        .Value;
    var certificateConfiguration = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<FiscalDevelopmentCertificateOptions>>()
        .Value;
    var certificateLoader = serviceProvider.GetRequiredService<IPfxCertificateLoader>();
    var loadedCertificate = certificateLoader.Load(
        certificateConfiguration.Path,
        certificateConfiguration.Password,
        new(
            RequireCurrentlyValid: true,
            ExpectedIssuerTin: configuration.IssuerTin,
            KeyStorageFlags:
                X509KeyStorageFlags.MachineKeySet |
                X509KeyStorageFlags.PersistKeySet |
                X509KeyStorageFlags.Exportable));

    return new PuClientCertificateHandlerV5(loadedCertificate);
});

var app = builder.Build();

if (usePostgreSql && app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<SummaFiscalDbContext>();
    await FiscalDevelopmentDataSeeder.SeedAsync(dbContext);
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
