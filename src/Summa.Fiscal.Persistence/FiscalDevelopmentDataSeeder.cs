using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence;

public static class FiscalDevelopmentDataSeeder
{
    public static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid BusinessUnitId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid DeviceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid OperatorId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static async Task SeedAsync(
        SummaFiscalDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        if (await dbContext.Companies.AnyAsync(x => x.Id == CompanyId, cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var company = new CompanyRecord
        {
            Id = CompanyId,
            Tin = "02825767",
            LegalName = "DRUŠTVO SA OGRANIČENOM ODGOVORNOŠĆU ZA TRGOVINU I USLUGE \"SUMMA SUMMARUM\" - BAR",
            ShortName = "Summa Summarum",
            Country = "MNE",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            FiscalProfile = new FiscalProfileRecord
            {
                CompanyId = CompanyId,
                Environment = "Test",
                Endpoint = "https://efitest.tax.gov.me/fs-v1",
                SoftwareCode = "zm955pb829",
                MaintainerCode = "sv742uc940",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            BusinessUnits =
            [
                new BusinessUnitRecord
                {
                    Id = BusinessUnitId,
                    CompanyId = CompanyId,
                    Code = "oo940dt107",
                    Name = "Summa Summarum",
                    Address = "MAKEDONSKA",
                    Town = "Bar",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Devices =
                    [
                        new FiscalDeviceRecord
                        {
                            Id = DeviceId,
                            TcrCode = "wx860oc926",
                            InternalCode = "ENU-summa",
                            IsActive = true,
                            CreatedAt = now,
                            UpdatedAt = now
                        }
                    ]
                }
            ],
            Operators =
            [
                new FiscalOperatorRecord
                {
                    Id = OperatorId,
                    CompanyId = CompanyId,
                    OperatorCode = "xg960dc979",
                    FirstName = "Željko",
                    LastName = "Đuranović",
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                }
            ]
        };

        dbContext.Companies.Add(company);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
