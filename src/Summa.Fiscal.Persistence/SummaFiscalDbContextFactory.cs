using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Summa.Fiscal.Persistence;

public sealed class SummaFiscalDbContextFactory : IDesignTimeDbContextFactory<SummaFiscalDbContext>
{
    public SummaFiscalDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SUMMA_FISCAL_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=summa_fiscal_dev;Username=postgres";

        var options = new DbContextOptionsBuilder<SummaFiscalDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new SummaFiscalDbContext(options);
    }
}
