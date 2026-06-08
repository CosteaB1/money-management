using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.Infrastructure.Imports;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Tests.Imports;

/// <summary>
/// The maib PDF parser is THE highest-value test target in Infrastructure — it
/// is where this project's worst bugs lived (dropped boundary rows, silently
/// dropped foreign-currency rows, commission split errors). These tests pin the
/// behaviors documented in BACKEND.md "PDF Statement Import (maib)" against the
/// real fixture PDFs, plus synthetic row bodies exercised through the private
/// ExtractRows for the FX / commission edge cases.
/// </summary>
public sealed class MaibStatementParserTests
{
    private static Stream LoadFixture(string fileName)
    {
        // Fixtures are copied to the output directory (CopyToOutputDirectory),
        // so they sit next to the test assembly at runtime.
        string path = Path.Combine(AppContext.BaseDirectory, "Imports", "Fixtures", fileName);
        return File.OpenRead(path);
    }

    private static ParsedStatement Parse(string fileName = "maib_sample.pdf")
    {
        var parser = new MaibStatementParser(NullLogger<MaibStatementParser>.Instance);
        using Stream stream = LoadFixture(fileName);
        Result<ParsedStatement> result = parser.Parse(stream);
        result.IsSuccess.Should().BeTrue(because: result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    // ------------------------------------------------------------------
    // Format sniffing / unsupported input
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_NonPdfStream_ReturnsUnsupportedFormat()
    {
        var parser = new MaibStatementParser(NullLogger<MaibStatementParser>.Instance);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("this is not a PDF at all"));

        Result<ParsedStatement> result = parser.Parse(stream);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.UnsupportedFormat);
    }

    [Fact]
    public void Parse_EmptyStream_ReturnsUnsupportedFormat()
    {
        var parser = new MaibStatementParser(NullLogger<MaibStatementParser>.Instance);
        using var stream = new MemoryStream([]);

        Result<ParsedStatement> result = parser.Parse(stream);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.UnsupportedFormat);
    }

    [Fact]
    public void Source_IsMaib()
    {
        var parser = new MaibStatementParser(NullLogger<MaibStatementParser>.Instance);
        parser.Source.Should().Be(BankSource.Maib);
    }

    // ------------------------------------------------------------------
    // Header / period / summary reconciliation
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_HeaderPeriod_IsExtracted()
    {
        ParsedStatement statement = Parse();

        statement.Period.From.Should().Be(new DateOnly(2026, 5, 1));
        statement.Period.To.Should().Be(new DateOnly(2026, 5, 16));
    }

    [Fact]
    public void Parse_Summary_MatchesStatement()
    {
        ParsedStatement statement = Parse();

        statement.Summary.OpeningBalance.Should().Be(1500.00m);

        // ClosingBalance is COMPUTED as opening + intrări − ieșiri (the booked
        // balance the import reconciles to), NOT read from a printed end-balance.
        statement.Summary.ClosingBalance.Should().Be(12549.66m);
        statement.Summary.TotalIn.Should().Be(14111.00m);
        statement.Summary.TotalOut.Should().Be(3061.34m);

        // The base sample charges no commission, so TotalFees falls back to 0.
        statement.Summary.TotalFees.Should().Be(0m);
    }

    [Fact]
    public void Parse_Summary_ReconciliationIdentityHolds()
    {
        // opening + in − out == closing — the booked-balance identity.
        ParsedStatement statement = Parse();

        decimal expectedClosing =
            statement.Summary.OpeningBalance + statement.Summary.TotalIn - statement.Summary.TotalOut;
        statement.Summary.ClosingBalance.Should().Be(expectedClosing);
    }

    [Fact]
    public void Parse_SalaryFixture_Summary_ParsesTotalComision()
    {
        // maib reports commission as a SEPARATE summary total `Total comision:`
        // (singular label), NOT folded into `Total ieșiri`.
        ParsedStatement statement = Parse("maib_salary_with_commission.pdf");

        statement.Summary.TotalFees.Should().Be(10.00m);
        statement.Summary.ClosingBalance.Should().Be(52337.00m);
    }

    // ------------------------------------------------------------------
    // Row extraction against the real fixture
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_RowCount_MatchesStatementSummary()
    {
        // 3 income rows (2 A2A transfers + 1 cashback) + 7 expense rows = 10
        // completed rows. The pending section (1 row) is excluded.
        ParsedStatement statement = Parse();

        statement.Rows.Should().HaveCount(10);
        statement.Rows.Count(r => r.Direction == TransactionDirection.Income).Should().Be(3);
        statement.Rows.Count(r => r.Direction == TransactionDirection.Expense).Should().Be(7);
    }

    [Fact]
    public void Parse_SumOfMdlAmounts_MatchesStatementTotals()
    {
        ParsedStatement statement = Parse();

        decimal totalIn = statement.Rows
            .Where(r => r.Direction == TransactionDirection.Income)
            .Sum(r => r.AmountMdl);
        decimal totalOut = statement.Rows
            .Where(r => r.Direction == TransactionDirection.Expense)
            .Sum(r => r.AmountMdl);

        totalIn.Should().Be(statement.Summary.TotalIn);
        totalOut.Should().Be(statement.Summary.TotalOut);
    }

    [Fact]
    public void Parse_ContainsLinella136Expense()
    {
        ParsedStatement statement = Parse();

        statement.Rows.Should().Contain(r =>
            r.TransactionDate == new DateOnly(2026, 5, 1)
            && r.Direction == TransactionDirection.Expense
            && r.AmountMdl == 136.00m
            && r.Description.Contains("LINELLA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_ContainsClaudeAiUsdExpense_KeepsMdlValueAndOriginal()
    {
        // FX row: MDL value stored in AmountMdl, source amount + currency retained.
        ParsedStatement statement = Parse();

        statement.Rows.Should().Contain(r =>
            r.Description.Contains("CLAUDE.AI", StringComparison.OrdinalIgnoreCase)
            && r.Direction == TransactionDirection.Expense
            && r.OriginalCurrency == "USD"
            && r.OriginalAmount == 120m
            && r.AmountMdl == 2091.20m);
    }

    [Fact]
    public void Parse_ContainsCashbackIncomeWithSpacedDescription()
    {
        // NearestNeighbourWordExtractor must recover word boundaries so the
        // description reads "Transfer Retragere Cashback", not "RetragereeCashback".
        ParsedStatement statement = Parse();

        statement.Rows.Should().Contain(r =>
            r.Direction == TransactionDirection.Income
            && r.AmountMdl == 111.00m
            && r.Description == "Transfer Retragere Cashback");
    }

    [Fact]
    public void Parse_DoesNotIncludePendingRows()
    {
        ParsedStatement statement = Parse();

        statement.Rows.Should().NotBeEmpty();
        statement.Rows.Should().AllSatisfy(r => r.Description.Should().NotBeNullOrWhiteSpace());

        // The pending `ASP IALOVENI SIVDCA HI -1 140.00` lives below the
        // `Tranzacții în procesare` header and must be excluded.
        statement.Rows.Should().NotContain(r => r.AmountMdl == 1140.00m);
    }

    [Fact]
    public void Parse_ContainsRowImmediatelyBeforePendingSection()
    {
        // Last completed row before the pending header — historically dropped
        // because the trailing section marker contaminated the row body.
        ParsedStatement statement = Parse();

        statement.Rows.Should().Contain(r =>
            r.TransactionDate == new DateOnly(2026, 5, 14)
            && r.Direction == TransactionDirection.Expense
            && r.AmountMdl == 71.14m
            && r.Description.Contains("LINELLA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_ContainsRowAtEndOfBankAccountSection()
    {
        // Last row of the `#Cont` section, just before the `#Cardul` header —
        // same boundary-dropout class.
        ParsedStatement statement = Parse();

        statement.Rows.Should().Contain(r =>
            r.TransactionDate == new DateOnly(2026, 5, 15)
            && r.Direction == TransactionDirection.Expense
            && r.AmountMdl == 245.00m
            && r.Description.Contains("Achitare", StringComparison.OrdinalIgnoreCase)
            && r.Description.Contains("QR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_ContainsRowAtPage1BoundaryInCardSection()
    {
        // Last row of page 1 in the card section — its tail is followed by the
        // page footer and page 2's column header. Same dropout class.
        ParsedStatement statement = Parse();

        statement.Rows.Where(r =>
                r.TransactionDate == new DateOnly(2026, 5, 6)
                && r.AmountMdl == 68.00m
                && r.Description.Contains("LINELLA", StringComparison.OrdinalIgnoreCase))
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_SampleFixture_EmitsNoCommissionRows()
    {
        ParsedStatement statement = Parse();

        statement.Rows.Should().NotContain(r =>
            r.Description.StartsWith("Comision:", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------
    // Commission split (Salary fixture)
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_SalaryFixture_RowWithCommission_EmitsSplitPrincipalAndFeeRows()
    {
        // The `ieșiri` column (17,163) is principal+fee combined; the `comision`
        // column (10) is the fee portion. Split: principal 17,153 + fee 10,
        // summing to the bank's actual 17,163 deduction.
        ParsedStatement statement = Parse("maib_salary_with_commission.pdf");

        ParsedStatementRow primary = statement.Rows.Should().ContainSingle(r =>
            r.AmountMdl == 17153.00m
            && r.Direction == TransactionDirection.Expense
            && r.Description.Contains("MAIB P2P", StringComparison.OrdinalIgnoreCase)
            && !r.Description.StartsWith("Comision:", StringComparison.OrdinalIgnoreCase))
            .Subject;

        ParsedStatementRow fee = statement.Rows.Should().ContainSingle(r =>
            r.Description.StartsWith("Comision:", StringComparison.OrdinalIgnoreCase))
            .Subject;

        fee.AmountMdl.Should().Be(10.00m);
        fee.Direction.Should().Be(TransactionDirection.Expense);
        fee.TransactionDate.Should().Be(primary.TransactionDate);
        fee.OriginalAmount.Should().BeNull();
        fee.OriginalCurrency.Should().BeNull();
        fee.Description.Should().Contain("MAIB P2P");

        (primary.AmountMdl + fee.AmountMdl).Should().Be(17163.00m);
    }

    [Fact]
    public void Parse_SalaryFixture_FeeRow_IsExpenseInAccountCurrency()
    {
        ParsedStatement statement = Parse("maib_salary_with_commission.pdf");

        IEnumerable<ParsedStatementRow> feeRows = statement.Rows
            .Where(r => r.Description.StartsWith("Comision:", StringComparison.OrdinalIgnoreCase));

        feeRows.Should().NotBeEmpty();
        feeRows.Should().AllSatisfy(r =>
        {
            r.Direction.Should().Be(TransactionDirection.Expense);
            r.OriginalAmount.Should().BeNull();
            r.OriginalCurrency.Should().BeNull();
            r.AmountMdl.Should().BeGreaterThan(0m);
        });
    }

    [Fact]
    public void Parse_SalaryFixture_BalanceReconciles()
    {
        ParsedStatement statement = Parse("maib_salary_with_commission.pdf");

        decimal totalIn = statement.Rows
            .Where(r => r.Direction == TransactionDirection.Income)
            .Sum(r => r.AmountMdl);
        decimal totalOut = statement.Rows
            .Where(r => r.Direction == TransactionDirection.Expense)
            .Sum(r => r.AmountMdl);

        decimal reconciled = statement.Summary.OpeningBalance + totalIn - totalOut;
        reconciled.Should().Be(52337.00m);
    }

    // ------------------------------------------------------------------
    // Synthetic-body edge cases via private ExtractRows
    // ------------------------------------------------------------------

    private static List<ParsedStatementRow> ExtractRows(string scanText)
    {
        MethodInfo extractRows = typeof(MaibStatementParser).GetMethod(
            "ExtractRows",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (List<ParsedStatementRow>)extractRows.Invoke(null, [scanText])!;
    }

    private static List<ParsedStatementRow> ExtractRowsFromBody(string syntheticInner) =>
        ExtractRows(
            "AGRNMD2X 2026-05-19 2026-05-19 " + syntheticInner + " Sfârșitul extrasului de cont");

    [Theory]
    [InlineData("OSTERIA DA FORTUNATA -145.00 EUR 2 883.91 4 952.29", "OSTERIA DA FORTUNATA", "EUR", 145.00, 2883.91)]
    [InlineData("MUSTANG MARKET -425.00 TRY 183.63 1 237.97", "MUSTANG MARKET", "TRY", 425.00, 183.63)]
    [InlineData("PENNY 4620 BICAZ C4 -17.70 RON 70.06 8 759.29", "PENNY 4620 BICAZ C4", "RON", 17.70, 70.06)]
    public void Parse_FxRow_KeepsMdlValueAndOriginalAmountCurrency(
        string body, string expectedDescription, string expectedCurrency, double expectedOriginal, double expectedMdl)
    {
        // Earlier versions only matched MDL|USD, silently dropping every other
        // FX row. Any 3-letter uppercase ISO code must now parse.
        List<ParsedStatementRow> rows = ExtractRowsFromBody(body);

        ParsedStatementRow only = rows.Should().ContainSingle().Subject;
        only.Description.Should().Be(expectedDescription);
        only.Direction.Should().Be(TransactionDirection.Expense);
        only.OriginalCurrency.Should().Be(expectedCurrency);
        only.OriginalAmount.Should().Be((decimal)expectedOriginal);
        only.AmountMdl.Should().Be((decimal)expectedMdl);
    }

    [Fact]
    public void Parse_PositiveFxRow_ParsesAsIncomeRefund()
    {
        List<ParsedStatementRow> rows = ExtractRowsFromBody(
            "ONKEL RESORT HOTEL 1 175.00 TRY 507.64 1 929.24");

        ParsedStatementRow only = rows.Should().ContainSingle().Subject;
        only.Direction.Should().Be(TransactionDirection.Income);
        only.AmountMdl.Should().Be(507.64m);
        only.OriginalCurrency.Should().Be("TRY");
        only.OriginalAmount.Should().Be(1175.00m);
    }

    [Fact]
    public void Parse_MdlRow_HasNoOriginalAmountOrCurrency()
    {
        List<ParsedStatementRow> rows = ExtractRowsFromBody(
            "LINELLA SRL -136.00 MDL 136.00 2 232.13");

        ParsedStatementRow only = rows.Should().ContainSingle().Subject;
        only.Direction.Should().Be(TransactionDirection.Expense);
        only.AmountMdl.Should().Be(136.00m);
        only.OriginalAmount.Should().BeNull();
        only.OriginalCurrency.Should().BeNull();
    }

    [Fact]
    public void Parse_DescriptionWithThreeLetterToken_DoesNotFalseMatchCurrency()
    {
        // "SRL" lives before the amount, so the non-greedy description can't
        // bind it as the currency — the real "RON" after the amount wins.
        List<ParsedStatementRow> rows = ExtractRowsFromBody(
            "MEPIFOOD SRL -10.00 RON 40.58 3 784.78");

        ParsedStatementRow only = rows.Should().ContainSingle().Subject;
        only.Description.Should().Be("MEPIFOOD SRL");
        only.OriginalCurrency.Should().Be("RON");
        only.OriginalAmount.Should().Be(10.00m);
        only.AmountMdl.Should().Be(40.58m);
    }

    [Fact]
    public void Parse_RowWhereCommissionConsumesEntireIesiri_EmitsOnlyFeeRow()
    {
        // Degenerate case: ieșiri == comision (pure-fee row). The parser must
        // skip the zero/negative primary and emit only the fee row.
        List<ParsedStatementRow> rows = ExtractRowsFromBody(
            "Comision deservire card -10.00 MDL 10.00 10.00 1234.56");

        ParsedStatementRow only = rows.Should().ContainSingle().Subject;
        only.Description.Should().StartWith("Comision:");
        only.Direction.Should().Be(TransactionDirection.Expense);
        only.AmountMdl.Should().Be(10.00m);
    }

    [Fact]
    public void Parse_RowWithThousandsSeparator_ParsesAmountCorrectly()
    {
        // maib uses a space thousands separator; the tail `17 163.00` must parse
        // to 17163.00, not 17 then 163.00.
        List<ParsedStatementRow> rows = ExtractRowsFromBody(
            "MAIB P2P -17 163.00 MDL 17 163.00 71 283.07");

        ParsedStatementRow only = rows.Should().ContainSingle().Subject;
        only.AmountMdl.Should().Be(17163.00m);
        only.Direction.Should().Be(TransactionDirection.Expense);
    }

    [Fact]
    public void Parse_PendingSectionMarker_TruncatesScan()
    {
        // A row after the pending marker must never be emitted, even mid-scan.
        List<ParsedStatementRow> rows = ExtractRows(
            "AGRNMD2X 2026-05-19 2026-05-19 REAL ROW -50.00 MDL 50.00 100.00 "
            + "Tranzacții în procesare "
            + "2026-05-20 2026-05-20 PENDING ROW -999.00 MDL 999.00 1.00");

        rows.Should().ContainSingle();
        rows.Single().Description.Should().Be("REAL ROW");
        rows.Should().NotContain(r => r.AmountMdl == 999.00m);
    }
}
