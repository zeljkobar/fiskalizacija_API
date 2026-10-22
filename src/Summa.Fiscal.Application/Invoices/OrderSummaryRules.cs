using Summa.Fiscal.Domain.Invoices;

namespace Summa.Fiscal.Application.Invoices;

public static class OrderSummaryRules
{
    public static bool IsSuccessful(FiscalInvoice x) => x.Status is FiscalStatus.Fiscalized or FiscalStatus.StornoCreated && !string.IsNullOrWhiteSpace(x.Jikr);
    public static void EnsureSources(IReadOnlyCollection<FiscalInvoice> orders, IReadOnlyCollection<FiscalInvoice> corrections, DateTimeOffset issuedAt)
    {
        if (orders.Count is < 1 or > 1000 || orders.Select(x => x.Id).Distinct().Count() != orders.Count)
            Fail("SUMMARY_SOURCES_INVALID", "Navesti 1–1000 različitih ORDER računa.");
        var first = orders.First();
        foreach (var order in orders)
        {
            if (!order.IsOrder || !IsSuccessful(order) || string.IsNullOrWhiteSpace(order.Iic)) Fail("SUMMARY_ORDER_REQUIRED", "Izvor mora biti uspješno fiskalizovan isključivo ORDER račun.");
            if (order.CompanyId != first.CompanyId || order.BusinessUnitId != first.BusinessUnitId || order.Currency != first.Currency)
                Fail("SUMMARY_CONTEXT_MISMATCH", "ORDER računi moraju pripadati istoj firmi, poslovnoj jedinici i valuti.");
            if (!SameBuyer(order.Buyer, first.Buyer)) Fail("SUMMARY_BUYER_MISMATCH", "Svi računi moraju imati istog kupca ili svi biti bez identifikovanog kupca.");
        }
        if (corrections.Any(x => x.Status != FiscalStatus.Cancelled && !IsSuccessful(x)))
            Fail("ORDER_CORRECTION_UNRESOLVED", "Prvo dovršiti ili otkazati neposlate korekcije izvornog ORDER računa.");
        var successful = corrections.Where(IsSuccessful).ToArray();
        if (successful.Any(x => !x.IsOrderCorrection || string.IsNullOrWhiteSpace(x.Iic) || !orders.Any(o => o.Id == x.OriginalInvoiceId)))
            Fail("SUMMARY_CORRECTION_SOURCE_INVALID", "Samo kontrolisane fiskalizovane ORDER korekcije mogu pratiti izvorni ORDER promet.");
        if (orders.Concat(successful).Any(x => x.IssueDateTime > issuedAt)) Fail("SUMMARY_DATE_INVALID", "Sumarni račun ne može prethoditi svojim izvorima.");
        if (orders.Count + successful.Length > 1000) Fail("SUMMARY_REFERENCE_LIMIT", "PU dozvoljava najviše 1000 referenci po računu.");
        if (orders.Sum(x => x.TotalGrossAmount) + successful.Sum(x => x.TotalGrossAmount) <= 0)
            Fail("SUMMARY_TOTAL_INVALID", "Sumarni račun mora imati pozitivan preostali iznos.");
    }

    public static void EnsureOrderCorrection(FiscalInvoice order, FiscalInvoice correction, IReadOnlyCollection<FiscalInvoice> previous)
    {
        if (!order.IsOrder || order.Status != FiscalStatus.Fiscalized || !IsSuccessful(order) ||
            !correction.IsOrderCorrection || correction.OriginalInvoiceId != order.Id)
            Fail("ORDER_CORRECTION_INVALID", "Korekcija zahtijeva raspoloživ fiskalizovani ORDER račun.");
        var allocations = correction.OrderSummary!.OrderAllocations;
        if (allocations.Count == 0 || allocations.Select(x => x.OrderItemId).Distinct().Count() != allocations.Count ||
            allocations.Any(x => x.Quantity <= 0 || decimal.Round(x.Quantity, 4) != x.Quantity || !order.Items.Any(i => i.Id == x.OrderItemId)))
            Fail("ORDER_ALLOCATION_INVALID", "Navesti različite izvorne stavke i pozitivne količine, do četiri decimale.");
        var active = previous.Where(x => x.Status != FiscalStatus.Cancelled && x.Id != correction.Id).ToArray();
        foreach (var allocation in allocations)
        {
            var item = order.Items.Single(x => x.Id == allocation.OrderItemId);
            var used = active.SelectMany(x => x.OrderSummary?.OrderAllocations ?? []).Where(x => x.OrderItemId == item.Id).Sum(x => x.Quantity);
            if (used + allocation.Quantity > item.Quantity) Fail("ORDER_CORRECTION_EXCEEDED", "Količina korekcija prelazi izvorni ORDER račun, uključujući rezervisane korekcije.");
            var priorItems = active.SelectMany(x => (x.OrderSummary?.OrderAllocations ?? []).Zip(x.Items))
                .Where(x => x.First.OrderItemId == item.Id).Select(x => x.Second).ToArray();
            var correctionItem = correction.Items.ElementAt(allocations.ToList().IndexOf(allocation));
            if (-priorItems.Sum(x => x.NetAmount) - correctionItem.NetAmount > item.NetAmount ||
                -priorItems.Sum(x => x.VatAmount) - correctionItem.VatAmount > item.VatAmount ||
                (used + allocation.Quantity == item.Quantity &&
                 (priorItems.Sum(x => x.GrossAmount) + correctionItem.GrossAmount != -item.GrossAmount ||
                  priorItems.Sum(x => x.NetAmount) + correctionItem.NetAmount != -item.NetAmount ||
                  priorItems.Sum(x => x.VatAmount) + correctionItem.VatAmount != -item.VatAmount)))
                Fail("ORDER_TAX_ALLOCATION_INVALID", "Raspodjela ne može tačno vratiti izvorne iznose/PDV; korigovati raspodjelu količina.");
            var remainingQuantity = item.Quantity - used - allocation.Quantity;
            if (remainingQuantity > 0)
            {
                var remainingDiscount = decimal.Round(item.DiscountAmount * remainingQuantity / item.Quantity, 2, MidpointRounding.AwayFromZero);
                var remainingGross = decimal.Round(remainingQuantity * item.UnitPrice - remainingDiscount, 2, MidpointRounding.AwayFromZero);
                var remainingNet = decimal.Round(remainingGross / (1 + item.VatRate / 100), 2, MidpointRounding.AwayFromZero);
                if (remainingGross != item.GrossAmount + priorItems.Sum(x => x.GrossAmount) + correctionItem.GrossAmount ||
                    remainingNet != item.NetAmount + priorItems.Sum(x => x.NetAmount) + correctionItem.NetAmount)
                    Fail("ORDER_TAX_ALLOCATION_INVALID", "Ova podjela ne ostavlja tačno povrativ preostali iznos/PDV; koristiti drugu raspodjelu ili puni storno.");
            }
        }
        if (correction.IssueDateTime < order.IssueDateTime) Fail("CORRECTIVE_DATE_INVALID", "Korekcija ne može prethoditi ORDER računu.");
    }

    public static IReadOnlyCollection<FiscalInvoice> PendingSummaryCorrections(FiscalInvoice summary,
        IReadOnlyCollection<FiscalInvoice> orderCorrections, IReadOnlyCollection<FiscalInvoice> adjustments)
    {
        var accounted = summary.OrderSummary!.AccountedCorrectionIds
            .Concat(adjustments.Where(x => x.Status != FiscalStatus.Cancelled).SelectMany(x => x.OrderSummary!.AccountedCorrectionIds)).ToHashSet();
        return orderCorrections.Where(x => IsSuccessful(x) && !accounted.Contains(x.Id)).OrderBy(x => x.Id).ToArray();
    }

    public static void EnsureSummaryCorrection(FiscalInvoice summary, FiscalInvoice adjustment,
        IReadOnlyCollection<FiscalInvoice> orderCorrections, IReadOnlyCollection<FiscalInvoice> previous)
    {
        if (summary.InvoiceType != InvoiceType.Summary || summary.Status != FiscalStatus.Fiscalized || !IsSuccessful(summary))
            Fail("SUMMARY_NOT_FISCALIZED", "Prvo fiskalizovati ili oporaviti sumarni račun.");
        if (previous.Any(x => x.Id != adjustment.Id && x.Status != FiscalStatus.Cancelled && !IsSuccessful(x)))
            Fail("SUMMARY_CORRECTION_UNRESOLVED", "Prvo dovršiti postojeću korekciju sumarnog računa.");
        if (adjustment.IsSummaryCancellation)
        {
            var successful = previous.Where(x => x.Id != adjustment.Id && IsSuccessful(x)).ToArray();
            if (successful.Any(x => x.IsSummaryCancellation) || adjustment.TotalGrossAmount != -summary.TotalGrossAmount - successful.Sum(x => x.TotalGrossAmount) ||
                adjustment.TotalNetAmount != -summary.TotalNetAmount - successful.Sum(x => x.TotalNetAmount) ||
                adjustment.TotalVatAmount != -summary.TotalVatAmount - successful.Sum(x => x.TotalVatAmount) ||
                successful.Any(x => x.IssueDateTime > adjustment.IssueDateTime))
                Fail("SUMMARY_CANCELLATION_INVALID", "Poništenje mora tačno vratiti preostalo informativno stanje sumarnog računa.");
            return;
        }
        var pending = PendingSummaryCorrections(summary, orderCorrections, previous.Where(x => x.Id != adjustment.Id).ToArray());
        if (pending.Count == 0 || !pending.Select(x => x.Id).Order().SequenceEqual(adjustment.OrderSummary!.AccountedCorrectionIds.Order()))
            Fail("SUMMARY_CORRECTION_CHANGED", "Izvorne korekcije su promijenjene ili već obuhvaćene; ponoviti pripremu.");
        if (adjustment.TotalGrossAmount != pending.Sum(x => x.TotalGrossAmount) ||
            adjustment.TotalNetAmount != pending.Sum(x => x.TotalNetAmount) || adjustment.TotalVatAmount != pending.Sum(x => x.TotalVatAmount))
            Fail("SUMMARY_CORRECTION_TOTAL_INVALID", "Korekcija sumarnog mora odgovarati korekcijama izvornog prometa.");
        if (pending.Any(x => x.IssueDateTime > adjustment.IssueDateTime)) Fail("CORRECTIVE_DATE_INVALID", "Korekcija sumarnog ne može prethoditi korekcijama ORDER računa.");
        if (-previous.Where(x => x.Id != adjustment.Id && x.Status != FiscalStatus.Cancelled).Sum(x => x.TotalGrossAmount) - adjustment.TotalGrossAmount > summary.TotalGrossAmount)
            Fail("SUMMARY_CORRECTION_EXCEEDED", "Korekcije prelaze iznos sumarnog računa.");
    }

    public static void EnsureSummaryResolved(FiscalInvoice? summary)
    {
        if (summary is null || summary.Status == FiscalStatus.Cancelled || IsSuccessful(summary) ||
            summary.Status == FiscalStatus.ReadyForFiscalization && string.IsNullOrWhiteSpace(summary.Iic)) return;
        Fail("SUMMARY_OUTCOME_UNRESOLVED", "Prvo razriješiti prethodno slanje sumarnog računa; korekcija ne smije promijeniti njegove izvore.");
    }

    private static bool SameBuyer(FiscalBuyer? a, FiscalBuyer? b) => a is null ? b is null : b is not null &&
        a.IdentificationType == b.IdentificationType && a.IdentificationNumber == b.IdentificationNumber && a.Country == b.Country;
    private static void Fail(string code, string message) => throw new FiscalInvoiceOperationException(code, message);
}
