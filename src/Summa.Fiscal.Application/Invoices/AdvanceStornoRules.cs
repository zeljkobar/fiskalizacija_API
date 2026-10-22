using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Invoices;

public static class AdvanceStornoRules
{
    public static void EnsureSettlementResolved(FiscalInvoice final, FiscalInvoice? settlement)
    {
        var payment = final.Payments.Single(x => x.PaymentType == PaymentType.Advance);
        if (settlement is null || settlement.Status == FiscalStatus.Cancelled) return;
        if (settlement.AdvanceSettlementFinalInvoiceId != final.Id ||
            settlement.OriginalInvoiceId != payment.AdvanceInvoiceId ||
            settlement.CompanyId != final.CompanyId || settlement.TotalGrossAmount != -payment.Amount)
            throw new FiscalInvoiceOperationException("ADVANCE_STORNO_CONTEXT_MISMATCH", "Umanjenje ne odgovara konačnom računu i avansu.");
        if (settlement.Status == FiscalStatus.ReadyForFiscalization && string.IsNullOrWhiteSpace(settlement.Iic)) return;
        if (settlement.Status == FiscalStatus.Fiscalized && !string.IsNullOrWhiteSpace(settlement.Jikr)) return;
        throw new FiscalInvoiceOperationException("ADVANCE_SETTLEMENT_OUTCOME_UNRESOLVED",
            "Prvo dovršiti ili oporaviti prethodno slanje umanjenja avansa; njegov ishod nije razriješen.");
    }

    public static void EnsureFinalStornoReference(FiscalInvoice storno, FiscalInvoice final)
    {
        if (storno.OriginalInvoiceId != final.Id || storno.CompanyId != final.CompanyId ||
            storno.TotalGrossAmount != -final.TotalGrossAmount || storno.InvoiceType != InvoiceType.Corrective ||
            storno.IsAdvanceRestoration || storno.AdvanceSettlementFinalInvoiceId.HasValue)
            throw new FiscalInvoiceOperationException("ADVANCE_STORNO_CONTEXT_MISMATCH", "Storno nije potpuni korektivni dokument konačnog računa.");
        var source = final.Payments.Single(x => x.PaymentType == PaymentType.Advance);
        var correction = storno.Payments.Single(x => x.PaymentType == PaymentType.Advance);
        if (correction.AdvanceInvoiceId != source.AdvanceInvoiceId || correction.AdvanceIic != source.AdvanceIic || correction.Amount != -source.Amount)
            throw new FiscalInvoiceOperationException("ADVANCE_STORNO_CONTEXT_MISMATCH", "Storno mora tačno poništiti izvorno avansno plaćanje.");
    }
}
