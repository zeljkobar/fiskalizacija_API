using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Invoices;

public static class AdvanceInvoiceRules
{
    public static void EnsureAvailable(FiscalInvoice advance, FiscalInvoice final, decimal requested, decimal reserved)
    {
        if (advance.InvoiceType != InvoiceType.Advance || advance.Status != FiscalStatus.Fiscalized ||
            string.IsNullOrWhiteSpace(advance.Iic) || string.IsNullOrWhiteSpace(advance.Jikr))
            throw new FiscalInvoiceOperationException("ADVANCE_NOT_AVAILABLE", "Avans mora biti uspješno fiskalizovan i raspoloživ.");
        if (advance.CompanyId != final.CompanyId || advance.Currency != final.Currency)
            throw new FiscalInvoiceOperationException("ADVANCE_CONTEXT_MISMATCH", "Avans i konačni račun moraju pripadati istoj firmi i valuti.");
        if (advance.Buyer is null || final.Buyer is null ||
            advance.Buyer.IdentificationType != final.Buyer.IdentificationType ||
            advance.Buyer.IdentificationNumber != final.Buyer.IdentificationNumber ||
            advance.Buyer.Country != final.Buyer.Country)
            throw new FiscalInvoiceOperationException("ADVANCE_BUYER_MISMATCH", "Avans i konačni račun moraju imati istog identifikovanog kupca.");
        if (final.IssueDateTime < advance.IssueDateTime)
            throw new FiscalInvoiceOperationException("ADVANCE_DATE_INVALID", "Konačni račun ne može prethoditi avansu.");
        if (requested <= 0 || requested + reserved > advance.TotalGrossAmount)
            throw new FiscalInvoiceOperationException("ADVANCE_AMOUNT_EXCEEDED", "Iznos prelazi raspoloživi avans.");
    }

    public static void EnsureSettlementAllocationAvailable(
        FiscalInvoice advance,
        FiscalInvoice settlement,
        IEnumerable<FiscalInvoice> existingSettlements)
    {
        var existing = existingSettlements.Where(x => x.Status is not (FiscalStatus.Cancelled or FiscalStatus.StornoCreated)).ToArray();
        foreach (var allocation in settlement.AdvanceSettlementAllocations)
        {
            var source = advance.Items.Single(x => x.Id == allocation.AdvanceItemId);
            var previous = existing.SelectMany(x => x.AdvanceSettlementAllocations)
                .Where(x => x.AdvanceItemId == source.Id).Sum(x => x.GrossAmount);
            if (previous + allocation.GrossAmount > source.GrossAmount)
                throw new FiscalInvoiceOperationException("ADVANCE_ITEM_AMOUNT_EXCEEDED", "Iznos umanjenja prelazi preostali iznos stavke avansa.");
        }
        // Preserve the original tax totals exactly; do not silently create a rounding difference.
        foreach (var rate in advance.Items.Select(x => x.VatRate).Distinct())
        {
            var sourceNet = advance.Items.Where(x => x.VatRate == rate).Sum(x => x.NetAmount);
            var sourceVat = advance.Items.Where(x => x.VatRate == rate).Sum(x => x.VatAmount);
            var reductions = existing.Append(settlement).SelectMany(x => x.Items).Where(x => x.VatRate == rate).ToArray();
            if (-reductions.Sum(x => x.NetAmount) > sourceNet || -reductions.Sum(x => x.VatAmount) > sourceVat)
                throw new FiscalInvoiceOperationException("ADVANCE_TAX_ALLOCATION_INVALID", "Raspodjela umanjenja prelazi izvornu osnovicu ili PDV. Potrebna je druga raspodjela iznosa po stavkama.");
        }
    }
}
