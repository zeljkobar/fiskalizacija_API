using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Summa.Fiscal.Persistence;

public static class FiscalPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddFiscalPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<SummaFiscalDbContext>(
            options => options.UseNpgsql(connectionString));

        return services;
    }
}
