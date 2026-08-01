using Microsoft.Extensions.Options;
using Summa.Fiscal.Application.Certificates;

namespace Summa.Fiscal.Worker;

public sealed class CertificateExpiryWorkerOptions
{
    public const string SectionName = "CertificateExpiryWorker";
    public int IntervalMinutes { get; init; } = 360;
}

public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    IOptions<CertificateExpiryWorkerOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Clamp(options.Value.IntervalMinutes, 5, 1440);
        await ScanAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ScanAsync(stoppingToken);
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ICertificateExpiryService>();
            var result = await service.ScanAsync(cancellationToken);
            logger.LogInformation(
                "Certificate expiry scan completed. Checked: {Checked}, alerts created: {Created}, time: {ScannedAt}",
                result.CertificatesChecked, result.AlertsCreated, result.ScannedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Certificate expiry scan failed.");
        }
    }
}
