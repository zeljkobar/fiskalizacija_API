namespace Summa.Fiscal.Persistence.Entities;

public abstract class FiscalRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CompanyRecord : FiscalRecord
{
    public string Tin { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? Address { get; set; }
    public string? Town { get; set; }
    public string Country { get; set; } = "MNE";
    public bool IsVatPayer { get; set; }
    public bool IsActive { get; set; } = true;
    public string ActiveEnvironment { get; set; } = "Test";
    public ICollection<FiscalProfileRecord> FiscalProfiles { get; set; } = [];
    public ICollection<BusinessUnitRecord> BusinessUnits { get; set; } = [];
    public ICollection<FiscalOperatorRecord> Operators { get; set; } = [];
    public ICollection<ApiClientCompanyAccessRecord> ApiClientAccesses { get; set; } = [];
    public ICollection<FiscalCertificateRecord> Certificates { get; set; } = [];
    public FiscalActivationRecord? FiscalActivation { get; set; }
}

public sealed class FiscalActivationRecord : FiscalRecord
{
    public Guid CompanyId { get; set; }
    public CompanyRecord Company { get; set; } = null!;
    public string Status { get; set; } = "NotTested";
    public Guid? TestInvoiceId { get; set; }
    public string? TestJikr { get; set; }
    public string? TestConfigurationHash { get; set; }
    public DateTimeOffset? TestPassedAt { get; set; }
    public string? TestPassedBy { get; set; }
    public DateTimeOffset? ProductionActivatedAt { get; set; }
    public string? ProductionActivatedBy { get; set; }
}

public sealed class ApiClientRecord : FiscalRecord
{
    public string ClientId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ApiKeyHash { get; set; } = string.Empty;
    public string ApiKeyPrefix { get; set; } = string.Empty;
    public string Permissions { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public ICollection<ApiClientCompanyAccessRecord> CompanyAccesses { get; set; } = [];
}

public sealed class ApiClientCompanyAccessRecord : FiscalRecord
{
    public Guid ApiClientId { get; set; }
    public ApiClientRecord ApiClient { get; set; } = null!;
    public Guid CompanyId { get; set; }
    public CompanyRecord Company { get; set; } = null!;
}

public sealed class FiscalProfileRecord : FiscalRecord
{
    public Guid CompanyId { get; set; }
    public CompanyRecord Company { get; set; } = null!;
    public string Environment { get; set; } = "Test";
    public string Endpoint { get; set; } = string.Empty;
    public string? ProducerCode { get; set; }
    public string? SoftwareName { get; set; }
    public string? SoftwareVersion { get; set; }
    public string SoftwareCode { get; set; } = string.Empty;
    public string MaintainerCode { get; set; } = string.Empty;
    public bool IsSoftwareCertified { get; set; }
    public string PaymentPolicy { get; set; } = "Any";
    public bool IsActive { get; set; } = true;
}

public sealed class BusinessUnitRecord : FiscalRecord
{
    public Guid CompanyId { get; set; }
    public CompanyRecord Company { get; set; } = null!;
    public string Environment { get; set; } = "Test";
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Town { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<FiscalDeviceRecord> Devices { get; set; } = [];
}

public sealed class FiscalDeviceRecord : FiscalRecord
{
    public Guid BusinessUnitId { get; set; }
    public BusinessUnitRecord BusinessUnit { get; set; } = null!;
    public string? TcrCode { get; set; }
    public string InternalCode { get; set; } = string.Empty;
    public string RegistrationStatus { get; set; } = "Registered";
    public DateTimeOffset? RegisteredAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class FiscalOperatorRecord : FiscalRecord
{
    public Guid CompanyId { get; set; }
    public CompanyRecord Company { get; set; } = null!;
    public string Environment { get; set; } = "Test";
    public string OperatorCode { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class FiscalCertificateRecord : FiscalRecord
{
    public Guid CompanyId { get; set; }
    public CompanyRecord Company { get; set; } = null!;
    public string StorageKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public ICollection<FiscalCertificateAlertRecord> ExpiryAlerts { get; set; } = [];
}

public sealed class FiscalCertificateAlertRecord : FiscalRecord
{
    public Guid CertificateId { get; set; }
    public FiscalCertificateRecord Certificate { get; set; } = null!;
    public Guid CompanyId { get; set; }
    public CompanyRecord Company { get; set; } = null!;
    public int ThresholdDays { get; set; }
    public DateTimeOffset CertificateValidTo { get; set; }
    public bool IsAcknowledged { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
}

public sealed class FiscalAuditRecord : FiscalRecord
{
    public Guid? CompanyId { get; set; }
    public CompanyRecord? Company { get; set; }
    public string Action { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
}

public sealed class FiscalInvoiceRecord : FiscalRecord
{
    public Guid CompanyId { get; set; }
    public CompanyRecord Company { get; set; } = null!;
    public Guid BusinessUnitId { get; set; }
    public BusinessUnitRecord BusinessUnit { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public FiscalDeviceRecord Device { get; set; } = null!;
    public Guid OperatorId { get; set; }
    public FiscalOperatorRecord Operator { get; set; } = null!;
    public string InvoiceType { get; set; } = string.Empty;
    public int InvoiceOrdinalNumber { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTimeOffset IssueDateTime { get; set; }
    public string Currency { get; set; } = "EUR";
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Iic { get; set; }
    public string? IicSignature { get; set; }
    public string? Jikr { get; set; }
    public string? QrCodeData { get; set; }
    public string Status { get; set; } = "Pending";
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid RequestUuid { get; set; }
    public DateTimeOffset? FiscalizedAt { get; set; }
    public ICollection<FiscalInvoiceItemRecord> Items { get; set; } = [];
    public ICollection<FiscalPaymentRecord> Payments { get; set; } = [];
}

public sealed class FiscalInvoiceItemRecord : FiscalRecord
{
    public Guid InvoiceId { get; set; }
    public FiscalInvoiceRecord Invoice { get; set; } = null!;
    public int LineNumber { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPriceBeforeVat { get; set; }
    public decimal UnitPriceAfterVat { get; set; }
    public decimal RebateRate { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

public sealed class FiscalPaymentRecord : FiscalRecord
{
    public Guid InvoiceId { get; set; }
    public FiscalInvoiceRecord Invoice { get; set; } = null!;
    public string PaymentType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
}

public sealed class CashDepositRecord : FiscalRecord
{
    public Guid CompanyId { get; set; }
    public CompanyRecord Company { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public FiscalDeviceRecord Device { get; set; } = null!;
    public string Operation { get; set; } = string.Empty;
    public decimal CashAmount { get; set; }
    public DateTimeOffset ChangeDateTime { get; set; }
    public Guid RequestUuid { get; set; }
    public string? Fcdc { get; set; }
    public string Status { get; set; } = "Pending";
}

public sealed class FiscalExchangeRecord : FiscalRecord
{
    public Guid? CompanyId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? CashDepositId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string SoapAction { get; set; } = string.Empty;
    public int? HttpStatusCode { get; set; }
    public string RequestSha256 { get; set; } = string.Empty;
    public string? ResponseSha256 { get; set; }
    public string RequestStoragePath { get; set; } = string.Empty;
    public string? ResponseStoragePath { get; set; }
    public string? FaultCode { get; set; }
    public string? FaultMessage { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class InvoiceSequenceRecord : FiscalRecord
{
    public Guid DeviceId { get; set; }
    public FiscalDeviceRecord Device { get; set; } = null!;
    public int Year { get; set; }
    public int LastNumber { get; set; }
}
