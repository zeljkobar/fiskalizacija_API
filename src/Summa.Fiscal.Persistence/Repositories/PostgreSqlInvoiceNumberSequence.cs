using System.Data;
using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Application.Abstractions;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed class PostgreSqlInvoiceNumberSequence(SummaFiscalDbContext dbContext)
    : IInvoiceNumberSequence
{
    public async Task<int> ReserveNextAsync(
        Guid deviceId,
        int year,
        CancellationToken cancellationToken)
    {
        if (deviceId == Guid.Empty) throw new ArgumentException("ENU uređaj je obavezan.", nameof(deviceId));
        if (year is < 2000 or > 9999) throw new ArgumentOutOfRangeException(nameof(year));

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO fiscal.invoice_sequences
                ("Id", "DeviceId", "Year", "LastNumber", "CreatedAt", "UpdatedAt")
            VALUES
                (@id, @deviceId, @year, 1, @now, @now)
            ON CONFLICT ("DeviceId", "Year")
            DO UPDATE SET
                "LastNumber" = fiscal.invoice_sequences."LastNumber" + 1,
                "UpdatedAt" = @now
            RETURNING "LastNumber";
            """;
        AddParameter(command, "@id", Guid.NewGuid());
        AddParameter(command, "@deviceId", deviceId);
        AddParameter(command, "@year", year);
        AddParameter(command, "@now", DateTimeOffset.UtcNow);

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
