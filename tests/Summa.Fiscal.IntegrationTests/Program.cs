using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Summa.Fiscal.Api.Security;
using Summa.Fiscal.Application.Abstractions;

var companyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var otherCompanyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
var authorizer = new BootstrapAdminAuthorizer(Options.Create(new ApiAccessOptions
{
    BootstrapAdminKey = "integration-bootstrap"
}));

var scoped = Context("client-1", "Admin portal",
    [FiscalApiPermissions.CompaniesRead, FiscalApiPermissions.ActivationRead], [companyId], "admin-42", "Security Check");
Assert(authorizer.Authorize(scoped, FiscalApiPermissions.CompaniesRead, companyId).IsAllowed,
    "Klijent mora imati dozvolu za dodijeljenu firmu.");
Assert(!authorizer.Authorize(scoped, FiscalApiPermissions.CompaniesRead, otherCompanyId).IsAllowed,
    "Klijent ne smije pristupiti drugoj firmi.");
Assert(!authorizer.Authorize(scoped, FiscalApiPermissions.CertificatesManage, companyId).IsAllowed,
    "Klijent ne smije koristiti nedodijeljenu dozvolu.");
Assert(authorizer.Authorize(scoped, FiscalApiPermissions.ActivationRead, companyId).IsAllowed,
    "Klijent mora moći čitati activation status dodijeljene firme.");
Assert(!authorizer.Authorize(scoped, FiscalApiPermissions.ActivationProduction, companyId).IsAllowed,
    "Produkcijska aktivacija mora zahtijevati posebnu dozvolu.");

var access = authorizer.Authorize(scoped, FiscalApiPermissions.CompaniesRead, companyId);
Assert(access.Actor.Contains("api-client:client-1:Admin portal", StringComparison.Ordinal) &&
       access.Actor.Contains("user:admin-42:Security Check", StringComparison.Ordinal),
    "Audit identitet mora sadržati aplikaciju i administratora.");

var development = Context("development-client", "Development", ["*"], [companyId], null, null);
Assert(!authorizer.Authorize(development, FiscalApiPermissions.CompaniesRead, companyId).IsAuthenticated,
    "Development klijent ne smije otvoriti administratorske rute.");

var platform = Context("platform-1", "Platform", [FiscalApiPermissions.PlatformAdmin], [], null, null);
Assert(authorizer.Authorize(platform, FiscalApiPermissions.CertificatesManage, otherCompanyId).IsAllowed,
    "Platform admin mora imati globalni pristup.");

var fiscalAccess = new FiscalAccessAuthorizer();
var fiscalPlatform = Context("fiscal-platform", "Fiscal platform",
    [FiscalApiPermissions.PlatformAdmin, FiscalApiPermissions.InvoicesRead, FiscalApiPermissions.InvoicesFiscalize],
    [], null, null).User;
Assert(fiscalAccess.HasAccess(fiscalPlatform, FiscalApiPermissions.InvoicesFiscalize, companyId),
    "Platform admin sa fiskalnom dozvolom mora pristupiti svakoj sadašnjoj firmi.");
Assert(fiscalAccess.HasAccess(fiscalPlatform, FiscalApiPermissions.InvoicesFiscalize, otherCompanyId),
    "Platform admin sa fiskalnom dozvolom mora pristupiti i budućim firmama bez posebne dodjele.");
Assert(!fiscalAccess.HasAccess(fiscalPlatform, FiscalApiPermissions.InvoicesStorno, companyId),
    "Platform admin ne smije zaobići konkretnu dozvolu za fiskalnu operaciju.");

var tenantFiscalClient = Context("tenant-fiscal", "Tenant fiscal",
    [FiscalApiPermissions.InvoicesFiscalize], [companyId], null, null).User;
Assert(fiscalAccess.HasAccess(tenantFiscalClient, FiscalApiPermissions.InvoicesFiscalize, companyId),
    "Obični fiskalni klijent mora pristupiti dodijeljenoj firmi.");
Assert(!fiscalAccess.HasAccess(tenantFiscalClient, FiscalApiPermissions.InvoicesFiscalize, otherCompanyId),
    "Obični fiskalni klijent ne smije pristupiti nedodijeljenoj firmi.");

var bootstrap = new DefaultHttpContext();
bootstrap.Request.Headers[BootstrapAdminAuthorizer.HeaderName] = "integration-bootstrap";
Assert(authorizer.Authorize(bootstrap, FiscalApiPermissions.ClientsAdmin).IsBootstrap,
    "Bootstrap pristup mora ostati dostupan za inicijalizaciju i oporavak.");

Console.WriteLine("Granularna administratorska autorizacija i tenant izolacija su provjerene.");
await VerifyAdvanceEndpointPermissionsAsync();
await VerifyOrderSummaryEndpointPermissionsAsync();
await AdvancePostgreSqlChecks.RunAsync();

static async Task VerifyAdvanceEndpointPermissionsAsync()
{
    var company = Guid.NewGuid();
    var repository = new Summa.Fiscal.Infrastructure.Persistence.InMemoryFiscalInvoiceRepository();
    var service = new Summa.Fiscal.Application.Invoices.FiscalInvoiceApplicationService(
        repository, new Summa.Fiscal.Application.Invoices.FiscalInvoiceValidator(),
        new Summa.Fiscal.Infrastructure.Audit.InMemoryAuditService(),
        new Summa.Fiscal.Infrastructure.Persistence.InMemoryInvoiceNumberSequence());
    var result = await service.CreateAsync(new(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Summa.Fiscal.Domain.Invoices.InvoiceType.Advance, "1", DateTimeOffset.UtcNow, "EUR",
        new(Summa.Fiscal.Domain.Invoices.BuyerIdentificationType.Tin, "12345678", "Kupac", null, null, "MNE", null),
        null, null, null, [new("Test", 1, 121, 21, null, null, 0)],
        [new(Summa.Fiscal.Domain.Invoices.PaymentType.BankAccount, 121, null)], "api-test", "api-test", "test"), default);
    var controller = new Summa.Fiscal.Api.Controllers.FiscalInvoicesController(service, null!, new FiscalAccessAuthorizer(), null!);
    controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
    {
        HttpContext = Context("other", "Other", [FiscalApiPermissions.InvoicesRead, FiscalApiPermissions.InvoicesStorno], [Guid.NewGuid()], null, null)
    };
    Assert((await controller.GetAdvanceBalance(result.Id, default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403,
        "Druga firma ne smije čitati saldo avansa.");
    Assert((await controller.GetAdvanceStornoWorkflow(result.Id, default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403,
        "Druga firma ne smije čitati povezani storno workflow.");
    Assert((await controller.CreateAdvanceRestoration(result.Id, new(DateTimeOffset.UtcNow, ""), default)
        as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Druga firma ne smije pripremiti obnovu avansa.");
    Assert((await controller.CreateAdvanceSettlement(result.Id, new(Guid.NewGuid(), DateTimeOffset.UtcNow, [], ""), default)
        as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Druga firma ne smije pripremiti umanjenje.");
    controller.ControllerContext.HttpContext = Context("reader", "Reader", [FiscalApiPermissions.InvoicesRead], [company], null, null);
    Assert((await controller.CreateAdvanceRestoration(result.Id, new(DateTimeOffset.UtcNow, ""), default)
        as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Obnova zahtijeva posebnu storno dozvolu.");
    Assert((await controller.CancelUnsubmitted(result.Id, new($"CANCEL_UNSUBMITTED:{result.Id:D}"), default)
        as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Čitalac ne smije otkazati rezervaciju.");
    controller.ControllerContext.HttpContext = Context("owner", "Owner", [FiscalApiPermissions.InvoicesStorno], [company], null, null);
    controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "test-restore";
    Assert((await controller.CreateAdvanceRestoration(result.Id, new(DateTimeOffset.UtcNow, "wrong"), default)
        as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 400, "Obnova mora zahtijevati tačnu potvrdu.");
    Assert((await controller.CancelUnsubmitted(result.Id, new("wrong"), default)
        as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 400, "Otkazivanje mora zahtijevati tačnu potvrdu.");
    Assert((await service.GetAsync(result.Id, default))!.Status == Summa.Fiscal.Domain.Invoices.FiscalStatus.ReadyForFiscalization,
        "Neovlašćeni pozivi su promijenili dokument.");
    Console.WriteLine("Avansne rute provjeravaju firmu, dozvolu i eksplicitnu potvrdu.");
}

static async Task VerifyOrderSummaryEndpointPermissionsAsync()
{
    var company = Guid.NewGuid();
    var repo = new Summa.Fiscal.Infrastructure.Persistence.InMemoryFiscalInvoiceRepository();
    var service = new Summa.Fiscal.Application.Invoices.FiscalInvoiceApplicationService(repo,
        new Summa.Fiscal.Application.Invoices.FiscalInvoiceValidator(), new Summa.Fiscal.Infrastructure.Audit.InMemoryAuditService(),
        new Summa.Fiscal.Infrastructure.Persistence.InMemoryInvoiceNumberSequence());
    var order = await service.CreateAsync(new(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Summa.Fiscal.Domain.Invoices.InvoiceType.Normal, "1", DateTimeOffset.UtcNow, "EUR", null, null, null, null,
        [new("Test", 1, 121, 21, null, null, 0)], [new(Summa.Fiscal.Domain.Invoices.PaymentType.Order, 121, null)],
        "order-api", "test", "test"), default);
    var controller = new Summa.Fiscal.Api.Controllers.OrderSummaryInvoicesController(service, new FiscalAccessAuthorizer());
    controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
    {
        HttpContext = Context("foreign", "Foreign", [FiscalApiPermissions.InvoicesRead, FiscalApiPermissions.InvoicesCreate, FiscalApiPermissions.InvoicesStorno], [Guid.NewGuid()], null, null)
    };
    var summaryRequest = new Summa.Fiscal.Api.Controllers.CreateSummaryRequest(company, Guid.NewGuid(), Guid.NewGuid(), [order.Id],
        DateTimeOffset.UtcNow, [new(Summa.Fiscal.Domain.Invoices.PaymentType.Card, 121, null)], $"CREATE_SUMMARY:{company:D}");
    Assert((await controller.CreateSummary(summaryRequest, default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Tuđa firma ne smije kreirati sumarni.");
    Assert((await controller.OrderWorkflow(order.Id, default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Tuđa firma ne smije čitati ORDER workflow.");
    Assert((await controller.SummaryWorkflow(order.Id, default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Tuđa firma ne smije čitati sumarni workflow.");
    Assert((await controller.CorrectOrder(order.Id, new(DateTimeOffset.UtcNow, "Test", [], ""), default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Tuđa firma ne smije korigovati ORDER.");
    Assert((await controller.CorrectSummary(order.Id, new(DateTimeOffset.UtcNow, "Test", [], ""), default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Tuđa firma ne smije korigovati sumarni.");
    Assert((await controller.CancelSummary(order.Id, new(DateTimeOffset.UtcNow, "Test", [], ""), default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Tuđa firma ne smije poništiti sumarni.");
    controller.ControllerContext.HttpContext = Context("reader", "Reader", [FiscalApiPermissions.InvoicesRead], [company], null, null);
    Assert((await controller.CreateSummary(summaryRequest, default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Čitalac ne smije pripremiti sumarni.");
    Assert((await controller.CorrectOrder(order.Id, new(DateTimeOffset.UtcNow, "Test", [], ""), default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Čitalac ne smije korigovati ORDER.");
    Assert((await controller.CorrectSummary(order.Id, new(DateTimeOffset.UtcNow, "Test", [], ""), default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Čitalac ne smije korigovati sumarni.");
    Assert((await controller.CancelSummary(order.Id, new(DateTimeOffset.UtcNow, "Test", [], ""), default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 403, "Čitalac ne smije poništiti sumarni.");
    controller.ControllerContext.HttpContext = Context("owner", "Owner", [FiscalApiPermissions.InvoicesCreate, FiscalApiPermissions.InvoicesStorno], [company], null, null);
    Assert((await controller.CreateSummary(summaryRequest, default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 400, "Sumarni mora imati idempotency ključ.");
    controller.HttpContext.Request.Headers["Idempotency-Key"] = "summary-test";
    Assert((await controller.CreateSummary(summaryRequest with { Confirmation = "wrong" }, default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 400, "Sumarni mora imati tačnu potvrdu.");
    Assert((await controller.CorrectOrder(order.Id, new(DateTimeOffset.UtcNow, "Test", [], "wrong"), default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 400, "ORDER korekcija mora imati tačnu potvrdu.");
    Assert((await controller.CorrectSummary(order.Id, new(DateTimeOffset.UtcNow, "Test", [], "wrong"), default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 400, "Sumarna korekcija mora imati tačnu potvrdu.");
    Assert((await controller.CancelSummary(order.Id, new(DateTimeOffset.UtcNow, "Test", [], "wrong"), default) as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode == 400, "Poništenje mora imati tačnu potvrdu.");
    Assert((await service.GetAsync(order.Id, default))!.Status == Summa.Fiscal.Domain.Invoices.FiscalStatus.ReadyForFiscalization, "Odbijene operacije su promijenile izvor.");
    Console.WriteLine("ORDER/sumarne rute provjeravaju firmu, posebne dozvole, idempotency i eksplicitne potvrde.");
}

static DefaultHttpContext Context(
    string clientId,
    string clientName,
    IReadOnlyCollection<string> permissions,
    IReadOnlyCollection<Guid> companyIds,
    string? actorId,
    string? actorName)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, clientId),
        new(ClaimTypes.Name, clientName)
    };
    claims.AddRange(permissions.Select(x => new Claim(ApiKeyAuthenticationHandler.PermissionClaim, x)));
    claims.AddRange(companyIds.Select(x => new Claim(ApiKeyAuthenticationHandler.CompanyClaim, x.ToString())));

    var context = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"))
    };
    if (actorId is not null) context.Request.Headers[BootstrapAdminAuthorizer.ActorIdHeaderName] = actorId;
    if (actorName is not null) context.Request.Headers[BootstrapAdminAuthorizer.ActorNameHeaderName] = actorName;
    return context;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
