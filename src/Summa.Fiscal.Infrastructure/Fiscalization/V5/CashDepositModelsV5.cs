namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public enum PuCashDepositOperationV5
{
    Initial,
    Withdraw
}

internal static class PuCashDepositLexicalValuesV5
{
    public static string ToXmlValue(this PuCashDepositOperationV5 value) => value switch
    {
        PuCashDepositOperationV5.Initial => "INITIAL",
        PuCashDepositOperationV5.Withdraw => "WITHDRAW",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
}

public sealed record RegisterCashDepositRequestV5(
    RegisterCashDepositHeaderV5 Header,
    PuCashDepositV5 CashDeposit);

public sealed record RegisterCashDepositHeaderV5(
    Guid Uuid,
    DateTimeOffset SendDateTime,
    PuSubsequentDeliveryTypeV5? SubsequentDeliveryType = null);

public sealed record PuCashDepositV5(
    DateTimeOffset ChangeDateTime,
    PuCashDepositOperationV5 Operation,
    decimal CashAmount,
    string TcrCode,
    string IssuerTin);
