using System.Text.Json.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Middleware;
using Summa.Fiscal.Api.Security;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Application.Admin;
using Summa.Fiscal.Application.Invoices;
using Summa.Fiscal.Application.Onboarding;
using Summa.Fiscal.Application.Certificates;
using Summa.Fiscal.Application.Activation;
using Summa.Fiscal.Infrastructure.Audit;
using Summa.Fiscal.Infrastructure.Certificates;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;
using Summa.Fiscal.Infrastructure.Persistence;
using Summa.Fiscal.Persistence;
using Summa.Fiscal.Persistence.Repositories;
using Summa.Fiscal.Api;

var isBackfill = args.FirstOrDefault() == "admin-exchange-backfill";
var builder = WebApplication.CreateBuilder(isBackfill ? [] : args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services
    .AddControllers(options => options.Filters.Add<FiscalRequestResultFilter>())
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
                    context.HttpContext.Request.Path.StartsWithSegments("/api/v1/admin") && HttpMethods.IsGet(context.HttpContext.Request.Method) ||
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
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
var fiscalDatabaseConnection = builder.Configuration.GetConnectionString("FiscalDatabase");
var usePostgreSql = !string.IsNullOrWhiteSpace(fiscalDatabaseConnection);
if (usePostgreSql)
{
    builder.Services.AddFiscalPersistence(fiscalDatabaseConnection!);
    builder.Services.AddScoped<IFiscalInvoiceRepository, PostgreSqlFiscalInvoiceRepository>();
    builder.Services.AddScoped<IApiClientRegistry, PostgreSqlApiClientRegistry>();
    builder.Services.AddScoped<IFiscalOnboardingRepository, PostgreSqlFiscalOnboardingRepository>();
    builder.Services.AddScoped<ICertificateExpiryRepository, PostgreSqlCertificateExpiryRepository>();
    builder.Services.AddScoped<IFiscalActivationRepository, PostgreSqlFiscalActivationRepository>();
    builder.Services.AddScoped<IAuditService, PostgreSqlAuditService>();
    builder.Services.AddScoped<IInvoiceNumberSequence, PostgreSqlInvoiceNumberSequence>();
    if (builder.Configuration.GetValue("AdminDiagnostics:ReadEnabled", true))
        builder.Services.AddScoped<IAdminReadStore, PostgreSqlAdminReadStore>();
    builder.Services.AddSingleton<IAdminDiagnosticWriter, PostgreSqlAdminDiagnosticWriter>();
    builder.Services.AddHostedService<FiscalRequestJournalMaintenance>();
}
else
{
    builder.Services.AddSingleton<IFiscalInvoiceRepository, InMemoryFiscalInvoiceRepository>();
    builder.Services.AddSingleton<IApiClientRegistry, UnavailableApiClientRegistry>();
    builder.Services.AddSingleton<IAuditService, InMemoryAuditService>();
    builder.Services.AddSingleton<IInvoiceNumberSequence, InMemoryInvoiceNumberSequence>();
}
builder.Services.Configure<ApiAccessOptions>(
    builder.Configuration.GetSection(ApiAccessOptions.SectionName));
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<ApiAccessOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        options => builder.Configuration.GetSection(ApiAccessOptions.SectionName).Bind(options));
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<AdminDiagnosticsOptions>(builder.Configuration.GetSection("AdminDiagnostics"));
builder.Services.AddSingleton<IFiscalAuditIdentity, HttpFiscalAuditIdentity>();
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
builder.Services.AddSingleton<IFiscalInvoiceValidator, FiscalInvoiceValidator>();
builder.Services.AddScoped<IFiscalInvoiceApplicationService, FiscalInvoiceApplicationService>();
builder.Services.AddScoped<IFiscalOnboardingService, FiscalOnboardingService>();
builder.Services.AddScoped<ICertificateExpiryService, CertificateExpiryService>();
builder.Services.AddScoped<IFiscalActivationService, FiscalActivationService>();
builder.Services.AddSingleton(
    builder.Configuration.GetSection(FiscalActivationPolicy.SectionName).Get<FiscalActivationPolicy>()
    ?? new FiscalActivationPolicy());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPfxCertificateLoader, PfxCertificateLoader>();
builder.Services.AddSingleton<IFiscalCertificateInspector, FiscalCertificateInspector>();
builder.Services.AddSingleton<IFiscalCertificateVault, EncryptedFileCertificateVault>();
builder.Services.AddSingleton<IIicGeneratorV5, IicGeneratorV5>();
builder.Services.AddSingleton<IRegisterInvoiceXmlBuilderV5, RegisterInvoiceXmlBuilderV5>();
builder.Services.AddSingleton<IFiscalXmlSignerV5, FiscalXmlSignerV5>();
builder.Services.AddSingleton<ISoapEnvelopeV5, SoapEnvelopeV5>();
builder.Services.AddSingleton<IRegisterInvoiceResponseParserV5, RegisterInvoiceResponseParserV5>();
builder.Services.AddSingleton<IRegisterTcrXmlBuilderV5, RegisterTcrXmlBuilderV5>();
builder.Services.AddSingleton<IRegisterTcrResponseParserV5, RegisterTcrResponseParserV5>();
builder.Services.AddScoped<IFiscalTcrRegistrationService, FiscalTcrRegistrationServiceV5>();
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
    var files = new FileFiscalExchangeStoreV5(options);
    var writer = serviceProvider.GetService<IAdminDiagnosticWriter>();
    return writer == null || !builder.Configuration.GetValue("AdminDiagnostics:IndexExchanges", true) ? files :
        new IndexedFiscalExchangeStoreV5(files, writer, serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<ILogger<IndexedFiscalExchangeStoreV5>>());
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

if (isBackfill)
{
    if (args.Length < 3 || args[1] is not ("--dry-run" or "--apply"))
        throw new ArgumentException("Upotreba: admin-exchange-backfill --dry-run|--apply <arhiva> [--expected-database <ime>] [--after <id>].");
    string? Value(string option)
    {
        var i = Array.IndexOf(args, option, 3);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    var importer = new AdminExchangeBackfill(app.Services.GetRequiredService<IDbContextFactory<SummaFiscalDbContext>>());
    var result = await importer.RunAsync(args[2], args[1] == "--apply", Value("--expected-database"), Value("--after"),
        checkpoint => Console.WriteLine($"checkpoint:{checkpoint}"), CancellationToken.None);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    await app.DisposeAsync();
    return;
}

if (usePostgreSql && builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<SummaFiscalDbContext>();
    await dbContext.Database.MigrateAsync();
}

if (usePostgreSql && app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<SummaFiscalDbContext>();
    await FiscalDevelopmentDataSeeder.SeedAsync(dbContext);
}

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<FiscalRequestJournal>();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
