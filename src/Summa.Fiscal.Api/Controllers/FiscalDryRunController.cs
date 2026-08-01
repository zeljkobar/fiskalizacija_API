using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Summa.Fiscal.Api.Contracts;
using Summa.Fiscal.Api.Middleware;
using Summa.Fiscal.Infrastructure.Certificates;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;

namespace Summa.Fiscal.Api.Controllers;

[ApiController]
[Route("api/v1/fiscal/test")]
public sealed class FiscalDryRunController(
    IHostEnvironment hostEnvironment,
    IOptions<PuFiscalizationOptionsV5> fiscalOptions,
    IOptions<FiscalDevelopmentCertificateOptions> certificateOptions,
    IPfxCertificateLoader certificateLoader,
    IFiscalDryRunServiceV5 dryRunService,
    IPuFiscalSoapClientV5 puClient,
    ICashDepositDryRunServiceV5 cashDepositDryRunService,
    IPuCashDepositSoapClientV5 cashDepositClient) : ControllerBase
{
    private const string TestConfirmation = "SEND_TO_PU_TEST";

    [HttpPost("dry-run")]
    [ProducesResponseType(typeof(ApiResponse<FiscalDryRunResultV5>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ApiResponse<FiscalDryRunResultV5>> Create(
        [FromBody] FiscalDryRunRequest request)
    {
        if (!hostEnvironment.IsDevelopment())
        {
            return NotFound();
        }

        var certificateConfiguration = certificateOptions.Value;
        if (string.IsNullOrWhiteSpace(certificateConfiguration.Path) ||
            string.IsNullOrWhiteSpace(certificateConfiguration.Password))
        {
            throw new InvalidOperationException(
                "Razvojni sertifikat nije konfigurisan kroz environment varijable.");
        }

        using var certificate = certificateLoader.Load(
            certificateConfiguration.Path,
            certificateConfiguration.Password,
            new(
                RequireCurrentlyValid: true,
                ExpectedIssuerTin: fiscalOptions.Value.IssuerTin));

        var schemaPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fiscalization",
            "V5",
            "Schemas",
            "FiscalService_v5_official.xsd");
        var result = dryRunService.Create(
            new(
                request.InvoiceOrdinalNumber,
                request.IssueDateTime,
                request.ItemName,
                request.NetAmount,
                request.VatRate),
            fiscalOptions.Value,
            certificate.Certificate,
            schemaPath);
        var correlationId =
            HttpContext.Items[CorrelationIdMiddleware.ItemName]?.ToString()
            ?? HttpContext.TraceIdentifier;

        return Ok(ApiResponse<FiscalDryRunResultV5>.Ok(result, correlationId));
    }

    [HttpPost("send")]
    [ProducesResponseType(typeof(ApiResponse<FiscalTestSendResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<FiscalTestSendResponse>>> Send(
        [FromBody] FiscalTestSendRequest request,
        CancellationToken cancellationToken)
    {
        if (!hostEnvironment.IsDevelopment())
        {
            return NotFound();
        }

        var configuration = fiscalOptions.Value;
        if (!string.Equals(configuration.Environment, "Test", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint) ||
            !string.Equals(endpoint.Host, "efitest.tax.gov.me", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(Failure(
                "PU_TEST_ENDPOINT_REQUIRED",
                "Kontrolisano slanje je dozvoljeno isključivo prema testnom PU endpointu."));
        }

        if (!string.Equals(request.Confirmation, TestConfirmation, StringComparison.Ordinal))
        {
            return BadRequest(Failure(
                "TEST_SEND_CONFIRMATION_REQUIRED",
                $"Za testno slanje confirmation mora biti {TestConfirmation}."));
        }

        var certificateConfiguration = certificateOptions.Value;
        if (string.IsNullOrWhiteSpace(certificateConfiguration.Path) ||
            string.IsNullOrWhiteSpace(certificateConfiguration.Password))
        {
            throw new InvalidOperationException(
                "Razvojni sertifikat nije konfigurisan kroz environment varijable.");
        }

        using var certificate = certificateLoader.Load(
            certificateConfiguration.Path,
            certificateConfiguration.Password,
            new(
                RequireCurrentlyValid: true,
                ExpectedIssuerTin: configuration.IssuerTin));
        var dryRun = dryRunService.Create(
            new(
                request.InvoiceOrdinalNumber,
                request.IssueDateTime,
                "Kontrolni test fiskalizacije",
                1.00m,
                21.00m),
            configuration,
            certificate.Certificate,
            SchemaPath());
        var correlationId = CorrelationId();
        var transport = await puClient.RegisterInvoiceAsync(
            endpoint,
            System.Xml.Linq.XDocument.Parse(dryRun.SignedRequestXml),
            correlationId,
            cancellationToken);
        var result = new FiscalTestSendResponse(
            transport.ExchangeId,
            (int)transport.StatusCode,
            transport.Response.IsSuccess,
            dryRun.InvoiceNumber,
            dryRun.Iic,
            transport.Response.Fic,
            transport.Response.Fault?.Code,
            transport.Response.Fault?.Message);

        return Ok(ApiResponse<FiscalTestSendResponse>.Ok(result, correlationId));
    }

    [HttpPost("cash-deposit/send")]
    [ProducesResponseType(typeof(ApiResponse<CashDepositTestSendResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CashDepositTestSendResponse>>> SendCashDeposit(
        [FromBody] CashDepositTestSendRequest request,
        CancellationToken cancellationToken)
    {
        if (!hostEnvironment.IsDevelopment())
            return NotFound();

        var configuration = fiscalOptions.Value;
        if (!string.Equals(configuration.Environment, "Test", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint) ||
            !string.Equals(endpoint.Host, "efitest.tax.gov.me", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(Failure(
                "PU_TEST_ENDPOINT_REQUIRED",
                "Depozit se može poslati samo testnom PU endpointu."));
        }

        if (!string.Equals(
                request.Confirmation,
                "REGISTER_INITIAL_CASH_DEPOSIT",
                StringComparison.Ordinal))
        {
            return BadRequest(Failure(
                "CASH_DEPOSIT_CONFIRMATION_REQUIRED",
                "Nije potvrđena prijava početnog testnog depozita."));
        }

        var certificateConfiguration = certificateOptions.Value;
        using var certificate = certificateLoader.Load(
            certificateConfiguration.Path,
            certificateConfiguration.Password,
            new(
                RequireCurrentlyValid: true,
                ExpectedIssuerTin: configuration.IssuerTin));
        var dryRun = cashDepositDryRunService.CreateInitial(
            request.CashAmount,
            request.ChangeDateTime,
            configuration,
            certificate.Certificate,
            SchemaPath());
        var correlationId = CorrelationId();
        var transport = await cashDepositClient.RegisterAsync(
            endpoint,
            System.Xml.Linq.XDocument.Parse(dryRun.SignedRequestXml),
            correlationId,
            cancellationToken);
        var result = new CashDepositTestSendResponse(
            transport.ExchangeId,
            (int)transport.StatusCode,
            transport.Response.IsSuccess,
            request.CashAmount,
            transport.Response.Fcdc,
            transport.Response.Fault?.Code,
            transport.Response.Fault?.Message);

        return Ok(ApiResponse<CashDepositTestSendResponse>.Ok(result, correlationId));
    }

    private string SchemaPath() => Path.Combine(
        AppContext.BaseDirectory,
        "Fiscalization",
        "V5",
        "Schemas",
        "FiscalService_v5_official.xsd");

    private string CorrelationId() =>
        HttpContext.Items[CorrelationIdMiddleware.ItemName]?.ToString()
        ?? HttpContext.TraceIdentifier;

    private ApiResponse<object> Failure(string code, string message) =>
        ApiResponse<object>.Fail(new(code, message, []), CorrelationId());
}

public sealed record FiscalDryRunRequest(
    int InvoiceOrdinalNumber,
    DateTimeOffset IssueDateTime,
    string ItemName,
    decimal NetAmount,
    decimal VatRate);

public sealed record FiscalTestSendRequest(
    string Confirmation,
    int InvoiceOrdinalNumber,
    DateTimeOffset IssueDateTime);

public sealed record FiscalTestSendResponse(
    Guid ExchangeId,
    int HttpStatusCode,
    bool IsSuccess,
    string InvoiceNumber,
    string Iic,
    string? Jikr,
    string? FaultCode,
    string? FaultMessage);

public sealed record CashDepositTestSendRequest(
    string Confirmation,
    decimal CashAmount,
    DateTimeOffset ChangeDateTime);

public sealed record CashDepositTestSendResponse(
    Guid ExchangeId,
    int HttpStatusCode,
    bool IsSuccess,
    decimal CashAmount,
    string? Fcdc,
    string? FaultCode,
    string? FaultMessage);
