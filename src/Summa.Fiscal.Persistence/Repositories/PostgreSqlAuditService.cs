using System.Text.Json;
using Summa.Fiscal.Application.Abstractions;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed class PostgreSqlAuditService(SummaFiscalDbContext dbContext) : IAuditService
{
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        dbContext.FiscalAudits.Add(new FiscalAuditRecord
        {
            CompanyId = entry.CompanyId,
            Action = entry.Action,
            CorrelationId = entry.CorrelationId,
            Actor = entry.Actor,
            DataJson = JsonSerializer.Serialize(new
            {
                entry.InvoiceId,
                Data = entry.Data
            }),
            CreatedAt = entry.OccurredAt,
            UpdatedAt = entry.OccurredAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
