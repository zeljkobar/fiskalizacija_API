using Microsoft.EntityFrameworkCore;
using Summa.Fiscal.Application.Onboarding;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Persistence.Repositories;

public sealed class PostgreSqlFiscalOnboardingRepository(SummaFiscalDbContext dbContext)
    : IFiscalOnboardingRepository
{
    public async Task<CompanySummary> UpsertCompanyAsync(CompanyOnboardingCommand command, CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies.Include(x => x.FiscalProfiles)
            .SingleOrDefaultAsync(x => x.Tin == command.Tin, cancellationToken);
        if (company is null)
        {
            company = new CompanyRecord { Tin = command.Tin };
            dbContext.Companies.Add(company);
        }
        company.LegalName = command.LegalName.Trim();
        company.ShortName = NullIfWhiteSpace(command.ShortName);
        company.Address = NullIfWhiteSpace(command.Address);
        company.Town = NullIfWhiteSpace(command.Town);
        company.Country = command.Country.Trim().ToUpperInvariant();
        company.IsVatPayer = command.IsVatPayer;
        company.UpdatedAt = DateTimeOffset.UtcNow;
        company.ActiveEnvironment = "Test";
        var profile = company.FiscalProfiles.SingleOrDefault(x => x.Environment == "Test");
        if (profile is null) { profile = new FiscalProfileRecord { CompanyId = company.Id, Company = company, Environment = "Test" }; dbContext.FiscalProfiles.Add(profile); }
        profile.Endpoint = command.Endpoint.Trim(); profile.SoftwareCode = command.SoftwareCode.Trim(); profile.MaintainerCode = command.MaintainerCode.Trim(); profile.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync("COMPANY_TIN_CONFLICT", "Firma sa ovim PIB-om već postoji.", cancellationToken);
        return Map(company);
    }

    public async Task<CompanySummary> UpdateCompanyAsync(Guid companyId, CompanyOnboardingCommand command, CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies.Include(x => x.FiscalProfiles).SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("COMPANY_NOT_FOUND", "Firma ne postoji.");
        company.Tin = command.Tin.Trim();
        company.LegalName = command.LegalName.Trim();
        company.ShortName = NullIfWhiteSpace(command.ShortName);
        company.Address = NullIfWhiteSpace(command.Address);
        company.Town = NullIfWhiteSpace(command.Town);
        company.Country = command.Country.Trim().ToUpperInvariant();
        company.IsVatPayer = command.IsVatPayer;
        company.UpdatedAt = DateTimeOffset.UtcNow;
        var profile = company.FiscalProfiles.SingleOrDefault(x => x.Environment == "Test");
        if (profile is null) { profile = new FiscalProfileRecord { CompanyId = company.Id, Company = company, Environment = "Test" }; dbContext.FiscalProfiles.Add(profile); }
        profile.Endpoint = command.Endpoint.Trim(); profile.SoftwareCode = command.SoftwareCode.Trim(); profile.MaintainerCode = command.MaintainerCode.Trim(); profile.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync("COMPANY_TIN_CONFLICT", "Firma sa ovim PIB-om već postoji.", cancellationToken);
        return Map(company);
    }

    public async Task<IReadOnlyCollection<CompanySummary>> ListCompaniesAsync(CancellationToken cancellationToken)
    {
        var records = await dbContext.Companies.AsNoTracking().Include(x => x.FiscalProfiles).OrderBy(x => x.LegalName).ToArrayAsync(cancellationToken);
        return records.Select(Map).ToArray();
    }

    public async Task<CompanySummary?> GetCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies.AsNoTracking().Include(x => x.FiscalProfiles)
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);
        return company is null ? null : Map(company);
    }

    public async Task<CompanySummary?> GetCompanyByTinAsync(string tin, CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies.AsNoTracking().Include(x => x.FiscalProfiles)
            .SingleOrDefaultAsync(x => x.Tin == tin, cancellationToken);
        return company is null ? null : Map(company);
    }

    public async Task<CompanySummary> SetCompanyActiveAsync(Guid companyId, bool active, CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies.Include(x => x.FiscalProfiles).SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("COMPANY_NOT_FOUND", "Firma ne postoji.");
        company.IsActive = active;
        company.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(company);
    }

    public async Task<ProductionProfileSummary> UpsertProductionProfileAsync(Guid companyId, Uri endpoint, ProductionProfileCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var company = await dbContext.Companies.Include(x => x.FiscalProfiles)
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("COMPANY_NOT_FOUND", "Firma ne postoji.");
        var now = DateTimeOffset.UtcNow;
        var profile = company.FiscalProfiles.SingleOrDefault(x => x.Environment == "Production");
        if (profile is null)
        {
            profile = new FiscalProfileRecord { CompanyId = companyId, Company = company, Environment = "Production" };
            dbContext.FiscalProfiles.Add(profile);
        }
        profile.Endpoint = endpoint.AbsoluteUri.TrimEnd('/');
        profile.ProducerCode = command.ProducerCode.Trim();
        profile.SoftwareName = command.SoftwareName.Trim();
        profile.SoftwareVersion = command.SoftwareVersion.Trim();
        profile.SoftwareCode = command.SoftwareCode.Trim();
        profile.MaintainerCode = command.MaintainerCode.Trim();
        profile.IsSoftwareCertified = command.IsSoftwareCertified;
        profile.PaymentPolicy = "BankOnly";
        profile.UpdatedAt = now;

        var unit = await dbContext.BusinessUnits.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Environment == "Production" && x.Code == command.BusinessUnitCode, cancellationToken);
        if (unit is null)
        {
            unit = new BusinessUnitRecord { CompanyId = companyId, Environment = "Production", Code = command.BusinessUnitCode.Trim() };
            dbContext.BusinessUnits.Add(unit);
        }
        unit.Name = command.BusinessUnitName.Trim(); unit.Address = NullIfWhiteSpace(command.BusinessUnitAddress);
        unit.Town = NullIfWhiteSpace(command.BusinessUnitTown); unit.IsActive = true; unit.UpdatedAt = now;

        var fiscalOperator = await dbContext.FiscalOperators.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Environment == "Production" && x.OperatorCode == command.OperatorCode, cancellationToken);
        if (fiscalOperator is null)
        {
            fiscalOperator = new FiscalOperatorRecord { CompanyId = companyId, Environment = "Production", OperatorCode = command.OperatorCode.Trim() };
            dbContext.FiscalOperators.Add(fiscalOperator);
        }
        fiscalOperator.FirstName = NullIfWhiteSpace(command.OperatorFirstName); fiscalOperator.LastName = NullIfWhiteSpace(command.OperatorLastName);
        fiscalOperator.IsActive = true; fiscalOperator.UpdatedAt = now;
        await SaveAsync("PRODUCTION_PROFILE_CONFLICT", "Produkcioni fiskalni kod je već unesen.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetProductionProfileAsync(companyId, cancellationToken))!;
    }

    public async Task<ProductionProfileSummary?> GetProductionProfileAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.FiscalProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Environment == "Production", cancellationToken);
        if (profile is null) return null;
        var unit = await dbContext.BusinessUnits.AsNoTracking().Where(x => x.CompanyId == companyId && x.Environment == "Production" && x.IsActive).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var fiscalOperator = await dbContext.FiscalOperators.AsNoTracking().Where(x => x.CompanyId == companyId && x.Environment == "Production" && x.IsActive).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (unit is null || fiscalOperator is null) return null;
        var device = await dbContext.FiscalDevices.AsNoTracking().Where(x => x.BusinessUnitId == unit.Id && x.IsActive).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return new(companyId, profile.Endpoint, profile.ProducerCode ?? "", profile.SoftwareName ?? "", profile.SoftwareVersion ?? "",
            profile.SoftwareCode, profile.MaintainerCode, profile.IsSoftwareCertified, profile.PaymentPolicy, Map(unit), Map(fiscalOperator), device is null ? null : Map(device, companyId));
    }

    public async Task<FiscalDeviceSummary> CreatePendingProductionDeviceAsync(Guid companyId, Guid businessUnitId, string internalCode, CancellationToken cancellationToken)
    {
        var unit = await dbContext.BusinessUnits.SingleOrDefaultAsync(x => x.Id == businessUnitId && x.CompanyId == companyId && x.Environment == "Production", cancellationToken)
            ?? throw new FiscalOnboardingException("PRODUCTION_BUSINESS_UNIT_NOT_FOUND", "Produkcijska poslovna jedinica ne postoji.");
        var existing = await dbContext.FiscalDevices.SingleOrDefaultAsync(x => x.BusinessUnitId == businessUnitId && x.InternalCode == internalCode, cancellationToken);
        if (existing is not null)
        {
            if (existing.RegistrationStatus == "Registered" && !string.IsNullOrWhiteSpace(existing.TcrCode)) return Map(existing, companyId);
            existing.RegistrationStatus = "Pending"; existing.IsActive = false; existing.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken); return Map(existing, companyId);
        }
        var record = new FiscalDeviceRecord { BusinessUnitId = unit.Id, InternalCode = internalCode, RegistrationStatus = "Pending", IsActive = false };
        dbContext.FiscalDevices.Add(record);
        await SaveAsync("DEVICE_INTERNAL_CODE_CONFLICT", "Interna oznaka ENU-a već postoji.", cancellationToken);
        return Map(record, companyId);
    }

    public async Task<FiscalDeviceSummary> CompleteDeviceRegistrationAsync(Guid companyId, Guid deviceId, string tcrCode, DateTimeOffset registeredAt, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalDevices.Include(x => x.BusinessUnit)
            .SingleOrDefaultAsync(x => x.Id == deviceId && x.BusinessUnit.CompanyId == companyId && x.BusinessUnit.Environment == "Production", cancellationToken)
            ?? throw new FiscalOnboardingException("FISCAL_DEVICE_NOT_FOUND", "Produkcioni ENU ne postoji.");
        record.TcrCode = tcrCode.Trim(); record.RegistrationStatus = "Registered"; record.RegisteredAt = registeredAt;
        record.IsActive = true; record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync("FISCAL_DEVICE_CODE_CONFLICT", "PU kod ENU-a već postoji.", cancellationToken);
        return Map(record, companyId);
    }

    public async Task MarkDeviceRegistrationFailedAsync(Guid companyId, Guid deviceId, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalDevices.Include(x => x.BusinessUnit)
            .SingleOrDefaultAsync(x => x.Id == deviceId && x.BusinessUnit.CompanyId == companyId, cancellationToken);
        if (record is null) return;
        record.RegistrationStatus = "Failed"; record.IsActive = false; record.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<BusinessUnitSummary> AddBusinessUnitAsync(Guid companyId, BusinessUnitCommand command, CancellationToken cancellationToken)
    {
        await RequireCompanyAsync(companyId, cancellationToken);
        var environment = await ActiveEnvironmentAsync(companyId, cancellationToken);
        var record = new BusinessUnitRecord { CompanyId = companyId, Environment = environment, Code = command.Code.Trim(), Name = command.Name.Trim(), Address = NullIfWhiteSpace(command.Address), Town = NullIfWhiteSpace(command.Town) };
        dbContext.BusinessUnits.Add(record);
        await SaveAsync("BUSINESS_UNIT_CODE_CONFLICT", "Poslovna jedinica sa ovim kodom već postoji u firmi.", cancellationToken);
        return Map(record);
    }

    public async Task<IReadOnlyCollection<BusinessUnitSummary>> ListBusinessUnitsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireCompanyAsync(companyId, cancellationToken);
        var environment = await ActiveEnvironmentAsync(companyId, cancellationToken);
        var records = await dbContext.BusinessUnits.AsNoTracking().Where(x => x.CompanyId == companyId && x.Environment == environment).OrderBy(x => x.Name).ToArrayAsync(cancellationToken);
        return records.Select(Map).ToArray();
    }

    public async Task<BusinessUnitSummary?> GetBusinessUnitAsync(Guid companyId, Guid businessUnitId, CancellationToken cancellationToken)
    {
        var record = await dbContext.BusinessUnits.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == businessUnitId, cancellationToken);
        return record is null ? null : Map(record);
    }

    public async Task<BusinessUnitSummary> UpdateBusinessUnitAsync(Guid companyId, Guid businessUnitId, BusinessUnitCommand command, CancellationToken cancellationToken)
    {
        var record = await dbContext.BusinessUnits.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == businessUnitId, cancellationToken)
            ?? throw new FiscalOnboardingException("BUSINESS_UNIT_NOT_FOUND", "Poslovna jedinica ne postoji ili ne pripada firmi.");
        record.Code = command.Code.Trim(); record.Name = command.Name.Trim(); record.Address = NullIfWhiteSpace(command.Address); record.Town = NullIfWhiteSpace(command.Town); record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync("BUSINESS_UNIT_CODE_CONFLICT", "Poslovna jedinica sa ovim kodom već postoji u firmi.", cancellationToken);
        return Map(record);
    }

    public async Task<BusinessUnitSummary> SetBusinessUnitActiveAsync(Guid companyId, Guid businessUnitId, bool active, CancellationToken cancellationToken)
    {
        var record = await dbContext.BusinessUnits.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == businessUnitId, cancellationToken)
            ?? throw new FiscalOnboardingException("BUSINESS_UNIT_NOT_FOUND", "Poslovna jedinica ne postoji ili ne pripada firmi.");
        if (!active && await dbContext.FiscalDevices.AnyAsync(x => x.BusinessUnitId == businessUnitId && x.IsActive, cancellationToken))
            throw new FiscalOnboardingException("BUSINESS_UNIT_HAS_ACTIVE_DEVICES", "Prvo deaktivirajte aktivne ENU uređaje poslovne jedinice.");
        record.IsActive = active; record.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(record);
    }

    public async Task<FiscalDeviceSummary> AddDeviceAsync(Guid companyId, FiscalDeviceCommand command, CancellationToken cancellationToken)
    {
        var unit = await dbContext.BusinessUnits.SingleOrDefaultAsync(x => x.Id == command.BusinessUnitId && x.CompanyId == companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("BUSINESS_UNIT_NOT_FOUND", "Poslovna jedinica ne postoji ili ne pripada firmi.");
        var record = new FiscalDeviceRecord { BusinessUnitId = unit.Id, TcrCode = command.TcrCode.Trim(), InternalCode = command.InternalCode.Trim(), RegistrationStatus = "Registered", RegisteredAt = DateTimeOffset.UtcNow };
        dbContext.FiscalDevices.Add(record);
        await SaveAsync("FISCAL_DEVICE_CODE_CONFLICT", "ENU uređaj sa ovim kodom već postoji.", cancellationToken);
        return Map(record, companyId);
    }

    public async Task<IReadOnlyCollection<FiscalDeviceSummary>> ListDevicesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireCompanyAsync(companyId, cancellationToken);
        var environment = await ActiveEnvironmentAsync(companyId, cancellationToken);
        var records = await dbContext.FiscalDevices.AsNoTracking()
            .Where(x => x.BusinessUnit.CompanyId == companyId && x.BusinessUnit.Environment == environment)
            .OrderBy(x => x.InternalCode).ToArrayAsync(cancellationToken);
        return records.Select(x => Map(x, companyId)).ToArray();
    }

    public async Task<FiscalDeviceSummary?> GetDeviceAsync(Guid companyId, Guid deviceId, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalDevices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == deviceId && x.BusinessUnit.CompanyId == companyId, cancellationToken);
        return record is null ? null : Map(record, companyId);
    }

    public async Task<FiscalDeviceSummary> UpdateDeviceAsync(Guid companyId, Guid deviceId, FiscalDeviceCommand command, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalDevices.Include(x => x.BusinessUnit).SingleOrDefaultAsync(x => x.Id == deviceId && x.BusinessUnit.CompanyId == companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("FISCAL_DEVICE_NOT_FOUND", "ENU uređaj ne postoji ili ne pripada firmi.");
        var targetUnit = await dbContext.BusinessUnits.SingleOrDefaultAsync(x => x.Id == command.BusinessUnitId && x.CompanyId == companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("BUSINESS_UNIT_NOT_FOUND", "Poslovna jedinica ne postoji ili ne pripada firmi.");
        record.BusinessUnitId = targetUnit.Id; record.TcrCode = command.TcrCode.Trim(); record.InternalCode = command.InternalCode.Trim(); record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync("FISCAL_DEVICE_CODE_CONFLICT", "ENU uređaj sa ovim kodom već postoji.", cancellationToken);
        return Map(record, companyId);
    }

    public async Task<FiscalDeviceSummary> SetDeviceActiveAsync(Guid companyId, Guid deviceId, bool active, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalDevices.Include(x => x.BusinessUnit).SingleOrDefaultAsync(x => x.Id == deviceId && x.BusinessUnit.CompanyId == companyId, cancellationToken)
            ?? throw new FiscalOnboardingException("FISCAL_DEVICE_NOT_FOUND", "ENU uređaj ne postoji ili ne pripada firmi.");
        if (active && !record.BusinessUnit.IsActive) throw new FiscalOnboardingException("BUSINESS_UNIT_INACTIVE", "ENU se ne može aktivirati u neaktivnoj poslovnoj jedinici.");
        record.IsActive = active; record.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(record, companyId);
    }

    public async Task<FiscalOperatorSummary> AddOperatorAsync(Guid companyId, FiscalOperatorCommand command, CancellationToken cancellationToken)
    {
        await RequireCompanyAsync(companyId, cancellationToken);
        var environment = await ActiveEnvironmentAsync(companyId, cancellationToken);
        var record = new FiscalOperatorRecord { CompanyId = companyId, Environment = environment, OperatorCode = command.OperatorCode.Trim(), FirstName = NullIfWhiteSpace(command.FirstName), LastName = NullIfWhiteSpace(command.LastName) };
        dbContext.FiscalOperators.Add(record);
        await SaveAsync("FISCAL_OPERATOR_CODE_CONFLICT", "Operater sa ovim kodom već postoji u firmi.", cancellationToken);
        return Map(record);
    }

    public async Task<IReadOnlyCollection<FiscalOperatorSummary>> ListOperatorsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireCompanyAsync(companyId, cancellationToken);
        var environment = await ActiveEnvironmentAsync(companyId, cancellationToken);
        var records = await dbContext.FiscalOperators.AsNoTracking().Where(x => x.CompanyId == companyId && x.Environment == environment).OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToArrayAsync(cancellationToken);
        return records.Select(Map).ToArray();
    }

    public async Task<FiscalOperatorSummary?> GetOperatorAsync(Guid companyId, Guid operatorId, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalOperators.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == operatorId, cancellationToken);
        return record is null ? null : Map(record);
    }

    public async Task<FiscalOperatorSummary> UpdateOperatorAsync(Guid companyId, Guid operatorId, FiscalOperatorCommand command, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalOperators.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == operatorId, cancellationToken)
            ?? throw new FiscalOnboardingException("FISCAL_OPERATOR_NOT_FOUND", "Operater ne postoji ili ne pripada firmi.");
        record.OperatorCode = command.OperatorCode.Trim(); record.FirstName = NullIfWhiteSpace(command.FirstName); record.LastName = NullIfWhiteSpace(command.LastName); record.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync("FISCAL_OPERATOR_CODE_CONFLICT", "Operater sa ovim kodom već postoji u firmi.", cancellationToken);
        return Map(record);
    }

    public async Task<FiscalOperatorSummary> SetOperatorActiveAsync(Guid companyId, Guid operatorId, bool active, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalOperators.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == operatorId, cancellationToken)
            ?? throw new FiscalOnboardingException("FISCAL_OPERATOR_NOT_FOUND", "Operater ne postoji ili ne pripada firmi.");
        record.IsActive = active; record.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(record);
    }

    public async Task<FiscalCertificateSummary> AddCertificateAsync(Guid companyId, string fileName, string storageKey, CertificateInspection inspection, CancellationToken cancellationToken)
    {
        await RequireCompanyAsync(companyId, cancellationToken);
        var record = new FiscalCertificateRecord
        {
            CompanyId = companyId, StorageKey = storageKey, FileName = Path.GetFileName(fileName),
            Thumbprint = inspection.Thumbprint, SerialNumber = inspection.SerialNumber,
            Subject = inspection.Subject, Issuer = inspection.Issuer,
            ValidFrom = inspection.ValidFrom, ValidTo = inspection.ValidTo
        };
        dbContext.FiscalCertificates.Add(record);
        await SaveAsync("CERT_UPLOAD_DUPLICATE_THUMBPRINT", "Sertifikat sa ovim thumbprintom već postoji za firmu.", cancellationToken);
        return Map(record);
    }

    public async Task<IReadOnlyCollection<FiscalCertificateSummary>> ListCertificatesAsync(Guid companyId, CancellationToken cancellationToken) =>
        await dbContext.FiscalCertificates.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt).Select(x => Map(x)).ToArrayAsync(cancellationToken);

    public async Task<FiscalCertificateSummary?> GetCertificateAsync(Guid companyId, Guid certificateId, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalCertificates.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == certificateId, cancellationToken);
        return record is null ? null : Map(record);
    }

    public Task<string?> GetCertificateStorageKeyAsync(Guid companyId, Guid certificateId, CancellationToken cancellationToken) =>
        dbContext.FiscalCertificates.AsNoTracking().Where(x => x.CompanyId == companyId && x.Id == certificateId).Select(x => x.StorageKey).SingleOrDefaultAsync(cancellationToken);

    public async Task<FiscalCertificateSummary> SetCertificateActiveAsync(Guid companyId, Guid certificateId, bool active, CancellationToken cancellationToken)
    {
        var record = await dbContext.FiscalCertificates.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == certificateId, cancellationToken)
            ?? throw new FiscalOnboardingException("CERT_ACTIVATE_NOT_FOUND", "Sertifikat ne postoji.");
        var now = DateTimeOffset.UtcNow;
        if (active)
        {
            var currentlyActive = await dbContext.FiscalCertificates.Where(x => x.CompanyId == companyId && x.IsActive && x.Id != certificateId).ToArrayAsync(cancellationToken);
            foreach (var old in currentlyActive) { old.IsActive = false; old.DeactivatedAt = now; old.UpdatedAt = now; }
            record.IsActive = true; record.ActivatedAt = now; record.DeactivatedAt = null;
        }
        else { record.IsActive = false; record.DeactivatedAt = now; }
        record.UpdatedAt = now;
        await SaveAsync("ACTIVE_CERTIFICATE_CONFLICT", "Firma već ima drugi aktivni sertifikat. Osvježite podatke i pokušajte ponovo.", cancellationToken);
        return Map(record);
    }

    public async Task<(IReadOnlyCollection<BusinessUnitSummary> Units, IReadOnlyCollection<FiscalDeviceSummary> Devices, IReadOnlyCollection<FiscalOperatorSummary> Operators)> GetConfigurationAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var environment = await ActiveEnvironmentAsync(companyId, cancellationToken);
        var units = await dbContext.BusinessUnits.AsNoTracking().Where(x => x.CompanyId == companyId && x.Environment == environment).ToArrayAsync(cancellationToken);
        var unitIds = units.Select(x => x.Id).ToArray();
        var devices = await dbContext.FiscalDevices.AsNoTracking().Where(x => unitIds.Contains(x.BusinessUnitId)).ToArrayAsync(cancellationToken);
        var operators = await dbContext.FiscalOperators.AsNoTracking().Where(x => x.CompanyId == companyId && x.Environment == environment).ToArrayAsync(cancellationToken);
        return (units.Select(Map).ToArray(), devices.Select(x => Map(x, companyId)).ToArray(), operators.Select(Map).ToArray());
    }

    public async Task AddAuditAsync(Guid? companyId, string action, string correlationId, string actor, string dataJson, CancellationToken cancellationToken)
    {
        dbContext.FiscalAudits.Add(new FiscalAuditRecord { CompanyId = companyId, Action = action, CorrelationId = correlationId, Actor = actor, DataJson = dataJson });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<FiscalAuditSummary>> ListAuditAsync(Guid companyId, int page, int pageSize, string? action, string? actor, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        await RequireCompanyAsync(companyId, cancellationToken);
        var query = dbContext.FiscalAudits.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action == action);
        if (!string.IsNullOrWhiteSpace(actor)) query = query.Where(x => x.Actor == actor);
        if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(x => x.CreatedAt <= to.Value);
        var total = await query.CountAsync(cancellationToken);
        var records = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new(records.Select(x => new FiscalAuditSummary(x.Id, x.CompanyId, x.Action, x.CorrelationId, x.Actor, x.DataJson, x.CreatedAt)).ToArray(), page, pageSize, total);
    }

    private async Task RequireCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Companies.AnyAsync(x => x.Id == companyId, cancellationToken))
            throw new FiscalOnboardingException("COMPANY_NOT_FOUND", "Firma ne postoji.");
    }

    private async Task SaveAsync(string conflictCode, string conflictMessage, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new FiscalOnboardingException(conflictCode, conflictMessage);
        }
    }

    private async Task<string> ActiveEnvironmentAsync(Guid companyId, CancellationToken cancellationToken) =>
        await dbContext.Companies.Where(x => x.Id == companyId).Select(x => x.ActiveEnvironment).SingleAsync(cancellationToken);

    private static CompanySummary Map(CompanyRecord x)
    {
        var profile = x.FiscalProfiles.SingleOrDefault(p => p.Environment == x.ActiveEnvironment);
        return new(x.Id, x.Tin, x.LegalName, x.ShortName, x.Address, x.Town, x.Country, x.IsVatPayer, x.IsActive, x.ActiveEnvironment, profile?.Endpoint ?? "", profile?.SoftwareCode ?? "", profile?.MaintainerCode ?? "", profile?.PaymentPolicy ?? "Any");
    }
    private static BusinessUnitSummary Map(BusinessUnitRecord x) => new(x.Id, x.CompanyId, x.Environment, x.Code, x.Name, x.Address, x.Town, x.IsActive);
    private static FiscalDeviceSummary Map(FiscalDeviceRecord x, Guid companyId) => new(x.Id, companyId, x.BusinessUnitId, x.TcrCode, x.InternalCode, x.RegistrationStatus, x.RegisteredAt, x.IsActive);
    private static FiscalOperatorSummary Map(FiscalOperatorRecord x) => new(x.Id, x.CompanyId, x.Environment, x.OperatorCode, x.FirstName, x.LastName, x.IsActive);
    private static FiscalCertificateSummary Map(FiscalCertificateRecord x) => new(x.Id, x.CompanyId, x.FileName, x.Thumbprint, x.SerialNumber, x.Subject, x.Issuer, x.ValidFrom, x.ValidTo, x.IsActive, x.ActivatedAt, x.DeactivatedAt);
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
