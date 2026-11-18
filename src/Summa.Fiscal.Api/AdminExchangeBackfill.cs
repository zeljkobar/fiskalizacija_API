using System.Text.Json;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Summa.Fiscal.Application.Admin;
using Summa.Fiscal.Infrastructure.Fiscalization.V5;
using Summa.Fiscal.Persistence;
using Summa.Fiscal.Persistence.Entities;

namespace Summa.Fiscal.Api;

public sealed class AdminExchangeBackfill(IDbContextFactory<SummaFiscalDbContext> factory)
{
    public sealed class Counts
    {
        public int Scanned { get; set; }
        public int Verified { get; set; }
        public int Linked { get; set; }
        public int UnknownEnvironment { get; set; }
        public int Incomplete { get; set; }
        public int Unlinked { get; set; }
        public int Corrupt { get; set; }
        public int Conflict { get; set; }
        public int Inserted { get; set; }
        public int Enriched { get; set; }
        public int Skipped { get; set; }
    }

    public async Task<Counts> RunAsync(string archive, bool apply, string? expectedDatabase, string? after, Action<string>? committed, CancellationToken ct)
    {
        var root = Path.GetFullPath(archive);
        if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Arhiva mora biti postojeći obični direktorijum.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var target = db.Database.GetDbConnection();
        var config = new NpgsqlConnectionStringBuilder(target.ConnectionString);
        if (apply && (expectedDatabase != config.Database || string.IsNullOrWhiteSpace(expectedDatabase)))
            throw new InvalidOperationException("--apply zahtijeva ime tačno odabrane baze kroz --expected-database.");
        if (after != null && !Guid.TryParseExact(after, "N", out _)) throw new InvalidOperationException("Neispravan checkpoint.");
        using var lockDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lockDeadline.CancelAfter(TimeSpan.FromSeconds(10));
        await db.Database.OpenConnectionAsync(lockDeadline.Token);
        if (apply)
        {
            await using var cmd = target.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(24336755)";
            if (await cmd.ExecuteScalarAsync(lockDeadline.Token) is not true) throw new InvalidOperationException("Drugi uvoz već radi.");
        }
        var totals = new Counts();
        var directories = Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>()
            .Where(n => Guid.TryParseExact(n, "N", out _) && (after is null || StringComparer.Ordinal.Compare(n, after) > 0))
            .Order(StringComparer.Ordinal);
        foreach (var batch in directories.Chunk(100))
        {
            await using var tx = apply ? await db.Database.BeginTransactionAsync(ct) : null;
            foreach (var name in batch)
            {
                ct.ThrowIfCancellationRequested();
                totals.Scanned++;
                var id = Guid.ParseExact(name, "N");
                var folder = Path.Combine(root, name);
                if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint)) { totals.Corrupt++; continue; }
                try { await ImportAsync(db, id, folder, apply, totals, ct); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException or JsonException or
                    FormatException or OverflowException or InvalidDataException or ArgumentException or InvalidOperationException)
                { totals.Corrupt++; }
            }
            if (apply)
            {
                await db.SaveChangesAsync(ct);
                await tx!.CommitAsync(ct);
                committed?.Invoke(batch[^1]);
            }
            db.ChangeTracker.Clear();
        }
        return totals;
    }

    private static async Task ImportAsync(SummaFiscalDbContext db, Guid id, string folder, bool apply, Counts totals, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<FiscalExchangeRequestV5>(await Read(folder, "request.metadata.json", 65536, ct))
            ?? throw new InvalidDataException("Nema metapodataka zahtjeva.");
        var xml = await Read(folder, "request.xml", ExchangeEvidenceReader.MaxEvidenceBytes, ct);
        if (request.ExchangeId != id || !SameHash(ExchangeEvidenceReader.Hash(xml), request.RequestSha256))
        { totals.Conflict++; return; }
        var op = ExchangeEvidenceReader.Operation(request.SoapAction);
        var uuid = ExchangeEvidenceReader.ValidateRequest(xml, op);
        if (request.RequestUuid != uuid) { totals.Conflict++; return; }
        var replyFile = File.Exists(Path.Combine(folder, "response.xml"));
        var replyMeta = File.Exists(Path.Combine(folder, "response.metadata.json"));
        ExchangeFinish? finish = null;
        if (replyFile && replyMeta)
        {
            var reply = JsonSerializer.Deserialize<FiscalExchangeResponseV5>(await Read(folder, "response.metadata.json", 65536, ct))
                ?? throw new InvalidDataException("Nema metapodataka odgovora.");
            var replyXml = await Read(folder, "response.xml", ExchangeEvidenceReader.MaxEvidenceBytes, ct);
            if (reply.ExchangeId != id || !SameHash(ExchangeEvidenceReader.Hash(replyXml), reply.ResponseSha256) ||
                reply.ReceivedAt < request.CreatedAt) { totals.Conflict++; return; }
            finish = ExchangeEvidenceReader.Response(id, op, uuid, replyXml, reply.HttpStatusCode, reply.ReceivedAt.ToUniversalTime());
        }
        var evidence = finish is { Outcome: ExchangeOutcome.Accepted or ExchangeOutcome.Rejected } ? "Complete" :
            replyFile || replyMeta ? "Incomplete" : "RequestOnly";
        totals.Verified++;
        if (evidence != "Complete") totals.Incomplete++;
        var current = await db.FiscalExchanges.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (current is not null && (!SameHash(current.RequestSha256, request.RequestSha256) ||
            current.ResponseSha256 != null && finish?.ResponseHash != null && !SameHash(current.ResponseSha256, finish.ResponseHash)))
        { totals.Conflict++; return; }

        FiscalInvoiceRecord? invoice = null;
        FiscalContextSnapshot? snapshot = null;
        var env = EnvironmentFor(request.Endpoint);
        if (op == ExchangeOperation.RegisterInvoice)
        {
            var e = ExchangeEvidenceReader.Invoice(xml);
            var matches = await db.FiscalInvoices.Include(x => x.Company)
                .Where(x => x.Iic != null && x.Iic.ToUpper() == e.Iic.ToUpper()).Take(2).ToArrayAsync(ct);
            if (matches.Length > 1) { totals.Conflict++; return; }
            invoice = matches.SingleOrDefault();
            if (invoice is not null && (invoice.Company.Tin != e.Tin || invoice.TotalAmount != e.Total ||
                Math.Abs((invoice.IssueDateTime - e.IssuedAt).TotalSeconds) >= 1 ||
                (invoice.OfficialInvoiceNumber ?? invoice.InvoiceNumber) != e.Number &&
                 (invoice.OfficialInvoiceNumber != null || !InvoiceNumberMatches(invoice.InvoiceOrdinalNumber, e))))
            { totals.Conflict++; return; }
            if (invoice is not null && env != "Unknown")
                snapshot = new(env, e.Tin, e.Unit, e.Tcr, e.Operator, e.Software);
        }
        if (invoice != null && (current?.InvoiceId is Guid boundInvoice && boundInvoice != invoice.Id ||
            current?.CompanyId is Guid boundCompany && boundCompany != invoice.CompanyId ||
            env != "Unknown" && current?.Environment is "Test" or "Production" && current.Environment != env))
        { totals.Conflict++; return; }
        var companyId = invoice?.CompanyId ?? (current?.Provenance == "Live" ? current.CompanyId : null);
        if (companyId == null && op == ExchangeOperation.RegisterTcr && finish?.Outcome == ExchangeOutcome.Accepted)
        {
            var proof = JsonSerializer.Serialize(new { ExchangeId = id, TcrCode = finish.FiscalIdentifier });
            var tcr = ExchangeEvidenceReader.Payload(xml).Element(PuFiscalContractV5.Schema + "TCR");
            var tin = (string?)tcr?.Attribute("IssuerTIN");
            var owners = await (from audit in db.FiscalAudits
                                join company in db.Companies on audit.CompanyId equals (Guid?)company.Id
                                where audit.Action == "PRODUCTION_TCR_REGISTERED" && EF.Functions.JsonContains(audit.DataJson, proof)
                                    && company.Tin == tin
                                select company.Id).Distinct().Take(2).ToArrayAsync(ct);
            if (owners.Length > 1) { totals.Conflict++; return; }
            companyId = owners.Length == 1 ? owners[0] : null;
            if (companyId.HasValue && env == "Test") { totals.Conflict++; return; }
            if (companyId.HasValue && env == "Production" && tcr != null)
                snapshot = new(env, tin!, (string?)tcr.Attribute("BusinUnitCode") ?? "", finish.FiscalIdentifier!, "",
                    (string?)tcr.Attribute("SoftCode"));
        }
        if (companyId == null) { totals.Unlinked++; return; }
        totals.Linked++;
        if (env == "Unknown") totals.UnknownEnvironment++;
        var existingOutcome = current?.Outcome;
        var add = current == null;
        var update = !add && ((finish != null && (existingOutcome is null or "Started" or "Unknown" || current!.ResponseSha256 == null)) ||
            current!.InvoiceId == null && invoice != null || current.Provenance == null ||
            finish is { Outcome: ExchangeOutcome.Accepted } && current.LocalCompletion != "Completed" &&
            invoice?.Jikr?.Equals(finish.FiscalIdentifier, StringComparison.OrdinalIgnoreCase) == true);
        var fillContext = invoice != null && snapshot != null && (invoice.FirstSendContextJson == null ||
            invoice.AdminEnvironmentSource == "VerifiedExchange" && invoice.AdminEnvironment == env &&
            await PrecedesKnownSendAsync(db, invoice.Id, env, request.CreatedAt, ct));
        var environmentConflict = invoice != null && snapshot != null && invoice.AdminEnvironmentSource is "SendSnapshot" or "VerifiedExchange" &&
            invoice.AdminEnvironment != env;
        if (environmentConflict) totals.Conflict++;
        if (add) totals.Inserted++; else if (update || fillContext) totals.Enriched++; else totals.Skipped++;
        if (!apply) return;
        var row = current ?? new FiscalExchangeRecord
        {
            Id = id, CreatedAt = request.CreatedAt.ToUniversalTime(), UpdatedAt = request.CreatedAt.ToUniversalTime(),
            CorrelationId = request.CorrelationId is { Length: <= 100 } && !request.CorrelationId.Any(char.IsControl) ? request.CorrelationId : "",
            SoapAction = request.SoapAction, Operation = op.ToString(), Endpoint = request.Endpoint.GetLeftPart(UriPartial.Path),
            RequestSha256 = request.RequestSha256, RequestStoragePath = id.ToString("N") + "/request.xml", RequestUuid = uuid
        };
        if (add) db.FiscalExchanges.Add(row);
        row.Provenance ??= "Backfill";
        row.CompanyId ??= companyId;
        row.InvoiceId ??= invoice?.Id;
        row.Environment ??= snapshot?.Environment;
        row.EnvironmentSource ??= snapshot == null ? "Unknown" : "VerifiedExchange";
        row.ContextJson ??= snapshot == null ? null : JsonSerializer.Serialize(snapshot);
        row.EvidenceState ??= evidence;
        row.Outcome ??= "Unknown";
        row.LocalCompletion ??= "Unknown";
        if (finish != null && (add || row.Outcome is "Started" or "Unknown" || row.ResponseSha256 == null))
        {
            row.Outcome = finish.Outcome.ToString(); row.FaultCode = finish.ErrorCode;
            row.CompletedAt = finish.CompletedAt; row.HttpStatusCode = finish.HttpStatus;
            row.ResponseSha256 = finish.ResponseHash; row.FiscalIdentifier = finish.FiscalIdentifier;
            row.ResponseStoragePath = id.ToString("N") + "/response.xml";
            row.EvidenceState = evidence;
        }
        if (row.Outcome == "Accepted" && (invoice?.Jikr != null && invoice.Jikr.Equals(row.FiscalIdentifier, StringComparison.OrdinalIgnoreCase) ||
            current is null && op == ExchangeOperation.RegisterTcr && companyId.HasValue))
        {
            row.LocalCompletion = "Completed";
            if (row.FaultCode == "LOCAL_COMPLETION_FAILED") row.FaultCode = null;
        }
        if (fillContext && !environmentConflict)
        {
            invoice!.FirstSendContextJson = JsonSerializer.Serialize(snapshot);
            invoice.AdminEnvironment = env; invoice.AdminEnvironmentSource = "VerifiedExchange";
        }
        else if (environmentConflict && invoice!.AdminEnvironmentSource == "VerifiedExchange")
        {
            invoice.AdminEnvironment = "Unknown"; invoice.AdminEnvironmentSource = "Unknown";
        }
    }

    private static async Task<bool> PrecedesKnownSendAsync(SummaFiscalDbContext db, Guid invoiceId, string environment,
        DateTimeOffset received, CancellationToken ct)
    {
        // UUID checkpoint order is not chronological; check already imported rows and the current batch.
        var when = received.ToUniversalTime();
        return !db.FiscalExchanges.Local.Any(x => x.InvoiceId == invoiceId && x.Environment == environment &&
                x.ContextJson != null && x.CreatedAt <= when) &&
            !await db.FiscalExchanges.AnyAsync(x => x.InvoiceId == invoiceId && x.Environment == environment &&
                x.ContextJson != null && x.CreatedAt <= when, ct);
    }

    private static bool InvoiceNumberMatches(int ordinal, ExchangeEvidenceReader.InvoiceEvidence e)
    {
        var parts = e.Number.Split('/');
        var localYear = TimeZoneInfo.ConvertTime(e.IssuedAt,
            TimeZoneInfo.FindSystemTimeZoneById(ReportPeriod.ZoneName)).Year;
        return parts.Length == 4 && parts[0] == e.Unit && int.TryParse(parts[1], out var n) && n == ordinal &&
            parts[3] == e.Tcr && int.TryParse(parts[2], out var year) && year == localYear;
    }
    private static bool SameHash(string? a, string? b) => a is { Length: 64 } && b is { Length: 64 } && a.Equals(b, StringComparison.OrdinalIgnoreCase);
    private static string EnvironmentFor(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "https" || !endpoint.IsDefaultPort || endpoint.UserInfo != "" ||
            endpoint.AbsolutePath != "/fs-v1" || endpoint.Query != "" || endpoint.Fragment != "") return "Unknown";
        return endpoint.Host switch { "efi.tax.gov.me" => "Production", "efitest.tax.gov.me" => "Test", _ => "Unknown" };
    }
    private static async Task<string> Read(string folder, string name, int limit, CancellationToken ct)
    {
        var file = new FileInfo(Path.Combine(folder, name));
        if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.Length > limit) throw new InvalidDataException("Fajl dokaza nije čitljiv u zadatom obimu.");
        return await File.ReadAllTextAsync(file.FullName, ct);
    }
}
