using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

// ---------------------------------------------------------------------------
// MaibFixtureGenerator
//
// Mints two SYNTHETIC maib statement PDFs that PdfPig re-extracts into the
// exact whitespace-collapsed glyph stream MaibStatementParser expects. All
// account-holder identity, card number, IBAN, merchants and amounts are
// fabricated; the summary box reconciles (opening + Σintrări − Σieșiri ==
// closing) and the commission split (primary + fee == ieșiri) balances.
//
// The tool writes each PDF into BOTH test projects' Fixtures folders and then
// re-parses them with the REAL parser, asserting the values the unit tests
// pin. If any check fails the tool exits non-zero so the layout can be fixed
// before the PDFs are committed.
// ---------------------------------------------------------------------------

QuestPDF.Settings.License = LicenseType.Community;

// Repo root = three levels up from the tool's bin folder is fragile; instead
// walk up from the executable until we find the .slnx.
string repoRoot = FindRepoRoot();

string[] fixtureDirs =
[
    Path.Combine(repoRoot, "tests", "MoneyManagement.Application.Tests", "Imports", "Fixtures"),
    Path.Combine(repoRoot, "tests", "MoneyManagement.Infrastructure.Tests", "Imports", "Fixtures"),
];

// ---- Fixture 1: maib_sample.pdf (no commission) ---------------------------
// Synthetic account holder ION POPESCU, card 999999******0000.
// Income: 111.00 (cashback) + 5 000.00 + 9 000.00 = 14 111.00
// Expense: 136.00 + 2 091.20 (FX 120 USD) + 68.00 + 250.00 + 200.00 + 71.14
//          + 245.00 = 3 061.34
// Opening 1 500.00, Closing 1 500.00 + 14 111.00 − 3 061.34 = 12 549.66.
var sample = new Statement(
    PeriodFrom: "01.05.2026",
    PeriodTo: "16.05.2026",
    Opening: "1 500.00",
    TotalOut: "3 061.34",
    TotalIn: "14 111.00",
    TotalFees: null,
    SoldFinal: "12 549.66",
    SoldDisponibil: "11 409.66",
    Pages:
    [
        // Page 1 — bank-account (#Cont) section.
        new Page(
            AccountMarker: "999999000000001 ION POPESCU #Cont",
            CardMarker: null,
            Rows:
            [
                Row("2026-05-01", "2026-05-01", "LINELLA SRL", "-136.00", "MDL", "136.00", "2 364.00"),
                Row("2026-05-02", "2026-05-02", "Transfer Retragere Cashback", "111.00", "MDL", "111.00", "2 475.00"),
                Row("2026-05-03", "2026-05-03", "A2A de intrare pe cardul 999999***0000", "5 000.00", "MDL", "5 000.00", "7 475.00"),
                Row("2026-05-04", "2026-05-04", "CLAUDE.AI SUBSCRIPTION", "-120.00", "USD", "2 091.20", "5 383.80"),
                // Last row of page 1 in the #Cont section (page boundary case).
                Row("2026-05-06", "2026-05-06", "LINELLA SRL", "-68.00", "MDL", "68.00", "5 315.80"),
            ]),
        // Page 2 — still #Cont, ends just before the #Cardul section header.
        new Page(
            AccountMarker: null,
            CardMarker: "999999******0000 #Cardul",
            Rows:
            [
                Row("2026-05-08", "2026-05-08", "ANDYS PIZZA", "-250.00", "MDL", "250.00", "5 065.80"),
                Row("2026-05-10", "2026-05-10", "A2A de intrare pe cardul 999999***0000", "9 000.00", "MDL", "9 000.00", "14 065.80"),
                Row("2026-05-12", "2026-05-12", "ORANGE MOLDOVA", "-200.00", "MDL", "200.00", "13 865.80"),
                // Last row of the #Cont section, immediately before #Cardul marker.
                Row("2026-05-15", "2026-05-15", "Achitare prin QR", "-245.00", "MDL", "245.00", "13 620.80"),
            ]),
        // Page 3 — card (#Cardul) section; last completed row sits right before
        // the pending header.
        new Page(
            AccountMarker: null,
            CardMarker: null,
            Rows:
            [
                Row("2026-05-14", "2026-05-14", "LINELLA SRL", "-71.14", "MDL", "71.14", "12 549.66"),
            ]),
    ],
    // Pending section — excluded from parsing.
    PendingRows:
    [
        Row("2026-05-16", "2026-05-16", "ASP IALOVENI SIVDCA HI", "-1 140.00", "MDL", "1 140.00", "11 409.66"),
    ]);

// ---- Fixture 2: maib_salary_with_commission.pdf ---------------------------
// Income: 30 000.00 (salary)
// Expense (ieșiri, fee-inclusive): 17 163.00 (MAIB P2P, fee 10.00) + 500.00 = 17 663.00
// Total comision: 10.00
// Opening 40 000.00, Closing 40 000.00 + 30 000.00 − 17 663.00 = 52 337.00.
var salary = new Statement(
    PeriodFrom: "01.05.2026",
    PeriodTo: "31.05.2026",
    Opening: "40 000.00",
    TotalOut: "17 663.00",
    TotalIn: "30 000.00",
    TotalFees: "10.00",
    // Sold final double-subtracts the 10.00 fee (already inside ieșiri).
    SoldFinal: "52 327.00",
    SoldDisponibil: "52 337.00",
    Pages:
    [
        new Page(
            AccountMarker: "999999000000001 ION POPESCU #Cont",
            CardMarker: null,
            Rows:
            [
                Row("2026-05-05", "2026-05-05", "Transfer Salariul pentru luna mai", "30 000.00", "MDL", "30 000.00", "70 000.00"),
                Row("2026-05-12", "2026-05-12", "LINELLA SRL", "-500.00", "MDL", "500.00", "69 500.00"),
                // Commission row: ieșiri 17 163.00, comision 10.00 -> the tail
                // carries three numbers <ieșiri> <comision> <balance>.
                CommissionRow("2026-05-20", "2026-05-20", "MAIB P2P", "-17 163.00", "MDL", "17 163.00", "10.00", "52 337.00"),
            ]),
    ],
    PendingRows: []);

GeneratePdf(sample, "maib_sample.pdf", fixtureDirs);
GeneratePdf(salary, "maib_salary_with_commission.pdf", fixtureDirs);

Console.WriteLine();
Console.WriteLine("Self-verifying with the real MaibStatementParser...");

int failures = 0;
failures += VerifySample(Path.Combine(fixtureDirs[0], "maib_sample.pdf"));
failures += VerifySalary(Path.Combine(fixtureDirs[0], "maib_salary_with_commission.pdf"));

if (failures > 0)
{
    Console.Error.WriteLine($"\n{failures} verification check(s) FAILED. Fix the layout before committing.");
    return 1;
}

Console.WriteLine("\nAll verification checks passed. Fixtures written to both test projects.");
return 0;

// ---------------------------------------------------------------------------

void GeneratePdf(Statement statement, string fileName, string[] dirs)
{
    byte[] bytes = Render(statement);
    foreach (string dir in dirs)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"Wrote {path} ({bytes.Length} bytes)");
    }
}

static byte[] Render(Statement s) =>
    Document.Create(container =>
    {
        bool firstPage = true;
        foreach (Page page in s.Pages)
        {
            container.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(28);
                p.DefaultTextStyle(t => t.FontSize(8).FontFamily(Fonts.Calibri));
                p.Content().Column(col =>
                {
                    col.Spacing(4);

                    if (firstPage)
                    {
                        // Header: bank marker + period + summary box, all on
                        // their own lines so PdfPig keeps them word-separated.
                        col.Item().Text("maib AGRNMD2X");
                        col.Item().Text($"EXTRAS DE CONT {s.PeriodFrom} - {s.PeriodTo}");
                        col.Item().Text(BuildSummaryLine(s));
                        firstPage = false;
                    }

                    if (page.AccountMarker is not null)
                    {
                        col.Item().Text(page.AccountMarker);
                    }

                    col.Item().Text("Data Data procesarii Descriere Suma Sold");

                    foreach (RowLine row in page.Rows)
                    {
                        col.Item().Text(row.Text);
                    }

                    if (page.CardMarker is not null)
                    {
                        col.Item().Text(page.CardMarker);
                    }
                });
            });
        }

        // Pending + end markers on a final page.
        container.Page(p =>
        {
            p.Size(PageSizes.A4);
            p.Margin(28);
            p.DefaultTextStyle(t => t.FontSize(8).FontFamily(Fonts.Calibri));
            p.Content().Column(col =>
            {
                col.Spacing(4);
                if (s.PendingRows.Count > 0)
                {
                    col.Item().Text("Tranzacții în procesare");
                    foreach (RowLine row in s.PendingRows)
                    {
                        col.Item().Text(row.Text);
                    }
                }

                col.Item().Text("Sfârșitul extrasului de cont");
            });
        });
    }).GeneratePdf();

static string BuildSummaryLine(Statement s)
{
    // Mirrors the single-line summary box PdfPig emits. `Total comision` is
    // present only for the salary fixture.
    string fees = s.TotalFees is null ? string.Empty : $"Total comision: {s.TotalFees} ";
    return
        $"Sold inițial: {s.Opening} Total Total ieșiri: {s.TotalOut} Total Total intrări: {s.TotalIn} Total "
        + fees
        + $"Sold final: {s.SoldFinal} Sold Disponibil: {s.SoldDisponibil}";
}

static RowLine Row(string txDate, string procDate, string desc, string signedAmount, string ccy, string mdl, string balance) =>
    new($"{txDate} {procDate} {desc} {signedAmount} {ccy} {mdl} {balance}");

static RowLine CommissionRow(string txDate, string procDate, string desc, string signedAmount, string ccy, string iesiri, string comision, string balance) =>
    new($"{txDate} {procDate} {desc} {signedAmount} {ccy} {iesiri} {comision} {balance}");

string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "MoneyManagement.slnx")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    throw new InvalidOperationException("Could not locate repo root (MoneyManagement.slnx).");
}

// --- Self-verification: drive the REAL parser over the generated PDFs. ------

static ParsedStatement ParseWithRealParser(string path)
{
    // The parser is internal; reach it via reflection so the tool needs no
    // InternalsVisibleTo grant in the shipped Infrastructure project.
    Type parserType = GetParserType();

    // The ctor wants ILogger<MaibStatementParser>; build the matching
    // NullLogger<T> instance via reflection.
    Type nullLoggerType = typeof(NullLogger<>).MakeGenericType(parserType);
    object logger = nullLoggerType.GetField("Instance")!.GetValue(null)!;
    object parser = Activator.CreateInstance(parserType, logger)!;

    using FileStream fs = File.OpenRead(path);
    MethodInfo parse = parserType.GetMethod("Parse", [typeof(Stream)])!;
    var result = (Result<ParsedStatement>)parse.Invoke(parser, [fs])!;
    if (result.IsFailure)
    {
        throw new InvalidOperationException($"Parser failed for {path}: {result.Error.Message}");
    }

    return result.Value;
}

static Type GetParserType()
{
    // typeof a public type forces the Infrastructure assembly to load, then
    // we reach the internal parser by name within it.
    Assembly infra = typeof(MoneyManagement.Infrastructure.DependencyInjection).Assembly;
    return infra.GetType("MoneyManagement.Infrastructure.Imports.MaibStatementParser")!;
}

int VerifySample(string path)
{
    ParsedStatement st = ParseWithRealParser(path);
    int f = 0;
    f += Check("sample period from", st.Period.From, new DateOnly(2026, 5, 1));
    f += Check("sample period to", st.Period.To, new DateOnly(2026, 5, 16));
    f += Check("sample opening", st.Summary.OpeningBalance, 1500.00m);
    f += Check("sample totalIn", st.Summary.TotalIn, 14111.00m);
    f += Check("sample totalOut", st.Summary.TotalOut, 3061.34m);
    f += Check("sample fees", st.Summary.TotalFees, 0m);
    f += Check("sample closing", st.Summary.ClosingBalance, 12549.66m);
    f += Check("sample row count", st.Rows.Count, 10);
    f += Check("sample income count", st.Rows.Count(r => r.Direction == TransactionDirection.Income), 3);
    f += Check("sample expense count", st.Rows.Count(r => r.Direction == TransactionDirection.Expense), 7);

    decimal inSum = st.Rows.Where(r => r.Direction == TransactionDirection.Income).Sum(r => r.AmountMdl);
    decimal outSum = st.Rows.Where(r => r.Direction == TransactionDirection.Expense).Sum(r => r.AmountMdl);
    f += Check("sample Σin == totalIn", inSum, st.Summary.TotalIn);
    f += Check("sample Σout == totalOut", outSum, st.Summary.TotalOut);

    f += CheckTrue("sample LINELLA 136", st.Rows.Any(r =>
        r.TransactionDate == new DateOnly(2026, 5, 1) && r.AmountMdl == 136.00m
        && r.Direction == TransactionDirection.Expense && r.Description.Contains("LINELLA")));
    f += CheckTrue("sample CLAUDE.AI USD", st.Rows.Any(r =>
        r.Description.Contains("CLAUDE.AI") && r.OriginalCurrency == "USD"
        && r.OriginalAmount == 120m && r.AmountMdl == 2091.20m));
    f += CheckTrue("sample cashback spaced desc", st.Rows.Any(r =>
        r.Direction == TransactionDirection.Income && r.AmountMdl == 111.00m
        && r.Description == "Transfer Retragere Cashback"));
    f += CheckTrue("sample LINELLA 71.14 (pre-pending)", st.Rows.Any(r =>
        r.TransactionDate == new DateOnly(2026, 5, 14) && r.AmountMdl == 71.14m
        && r.Description.Contains("LINELLA")));
    f += CheckTrue("sample QR 245 (end #Cont)", st.Rows.Any(r =>
        r.TransactionDate == new DateOnly(2026, 5, 15) && r.AmountMdl == 245.00m
        && r.Description.Contains("Achitare") && r.Description.Contains("QR")));
    f += CheckTrue("sample LINELLA 68 (page boundary)", st.Rows.Any(r =>
        r.TransactionDate == new DateOnly(2026, 5, 6) && r.AmountMdl == 68.00m
        && r.Description.Contains("LINELLA")));
    f += CheckTrue("sample no pending 1140", !st.Rows.Any(r => r.AmountMdl == 1140.00m));
    f += CheckTrue("sample no commission rows", !st.Rows.Any(r => r.Description.StartsWith("Comision:")));
    return f;
}

int VerifySalary(string path)
{
    ParsedStatement st = ParseWithRealParser(path);
    int f = 0;
    f += Check("salary fees", st.Summary.TotalFees, 10.00m);
    f += Check("salary closing", st.Summary.ClosingBalance, 52337.00m);

    ParsedStatementRow? primary = st.Rows.FirstOrDefault(r =>
        r.AmountMdl == 17153.00m && r.Direction == TransactionDirection.Expense
        && r.Description.Contains("MAIB P2P") && !r.Description.StartsWith("Comision:"));
    f += CheckTrue("salary primary 17153", primary is not null);

    ParsedStatementRow? fee = st.Rows.FirstOrDefault(r => r.Description.StartsWith("Comision:"));
    f += CheckTrue("salary fee row exists", fee is not null);
    if (fee is not null)
    {
        f += Check("salary fee amount", fee.AmountMdl, 10.00m);
        f += CheckTrue("salary fee desc has MAIB P2P", fee.Description.Contains("MAIB P2P"));
    }

    if (primary is not null && fee is not null)
    {
        f += Check("salary primary+fee == ieșiri", primary.AmountMdl + fee.AmountMdl, 17163.00m);
    }

    decimal inSum = st.Rows.Where(r => r.Direction == TransactionDirection.Income).Sum(r => r.AmountMdl);
    decimal outSum = st.Rows.Where(r => r.Direction == TransactionDirection.Expense).Sum(r => r.AmountMdl);
    f += Check("salary reconcile", st.Summary.OpeningBalance + inSum - outSum, 52337.00m);
    return f;
}

int Check<T>(string label, T actual, T expected)
{
    bool ok = EqualityComparer<T>.Default.Equals(actual, expected);
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}: actual={actual}, expected={expected}");
    return ok ? 0 : 1;
}

int CheckTrue(string label, bool ok)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}");
    return ok ? 0 : 1;
}

// --- DTOs ------------------------------------------------------------------

internal sealed record Statement(
    string PeriodFrom,
    string PeriodTo,
    string Opening,
    string TotalOut,
    string TotalIn,
    string? TotalFees,
    string SoldFinal,
    string SoldDisponibil,
    IReadOnlyList<Page> Pages,
    IReadOnlyList<RowLine> PendingRows);

internal sealed record Page(
    string? AccountMarker,
    string? CardMarker,
    IReadOnlyList<RowLine> Rows);

internal sealed record RowLine(string Text);
