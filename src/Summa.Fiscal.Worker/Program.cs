using Summa.Fiscal.Worker;
using Summa.Fiscal.Application.Certificates;
using Summa.Fiscal.Persistence;
using Summa.Fiscal.Persistence.Repositories;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("FiscalDatabase")
    ?? throw new InvalidOperationException("ConnectionStrings:FiscalDatabase nije konfigurisan za Worker.");
builder.Services.AddFiscalPersistence(connectionString);
builder.Services.AddScoped<ICertificateExpiryRepository, PostgreSqlCertificateExpiryRepository>();
builder.Services.AddScoped<ICertificateExpiryService, CertificateExpiryService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<CertificateExpiryWorkerOptions>(
    builder.Configuration.GetSection(CertificateExpiryWorkerOptions.SectionName));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
