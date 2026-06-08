using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.Infrastructure.Imports;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Tests.Imports;

/// <summary>
/// Adversarial overnight sweep (2026-06-02): synthetic-body probes of the maib
/// parser's commission-split, multi-currency, same-day-same-amount, and
/// section-boundary behaviors that the existing fixture-based suite doesn't
/// exercise directly. Driven through the private ExtractRows so we can craft
/// exact glyph-stream shapes without minting new PDFs.
/// </summary>
public sealed class MaibStatementParserEdgeCaseTests
{
    private static List<ParsedStatementRow> ExtractRows(string scanText)
    {
        MethodInfo extractRows = typeof(MoneyManagement.Infrastructure.Imports.MaibStatementParser).GetMethod(
            "ExtractRows",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (List<ParsedStatementRow>)extractRows.Invoke(null, [scanText])!;
    }

    private static string Doc(params string[] innerBodies)
    {
        // Each inner body is a full row "DESC -amt CCY mdl balance" with its own
        // date pair anchor, joined into one scan terminated by the end marker.
        var sb = new System.Text.StringBuilder("AGRNMD2X ");
        foreach (string body in innerBodies)
        {
            sb.Append("2026-05-19 2026-05-19 ").Append(body).Append(' ');
        }

        sb.Append("Sfârșitul extrasului de cont");
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Multiple same-day, same-amount rows must NOT collapse in the parser.
    // (The parser is order-faithful and has no dedup; dedup happens later,
    // and within-batch identical rows are intentionally kept.)
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_TwoIdenticalSameDaySameAmountRows_KeepsBoth()
    {
        List<ParsedStatementRow> rows = ExtractRows(Doc(
            "ATM MAIB MOSILOR -5 000.00 MDL 5 000.00 10 000.00",
            "ATM MAIB MOSILOR -5 000.00 MDL 5 000.00 5 000.00"));

        rows.Should().HaveCount(2);
        rows.Should().AllSatisfy(r =>
        {
            r.AmountMdl.Should().Be(5000.00m);
            r.Direction.Should().Be(TransactionDirection.Expense);
            r.Description.Should().Be("ATM MAIB MOSILOR");
        });
    }

    // ------------------------------------------------------------------
    // Commission split must NOT false-trigger on a foreign-currency row.
    // An FX row's tail is `<mdl-value> <running-balance>` — exactly two
    // numeric tokens. A 3-token tail is the commission signal, but an FX
    // row should never produce three numbers from a clean body.
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_FxRow_DoesNotProduceSpuriousCommissionRow()
    {
        List<ParsedStatementRow> rows = ExtractRows(Doc(
            "OSTERIA DA FORTUNATA -145.00 EUR 2 883.91 4 952.29"));

        // Exactly one row, no "Comision:" split.
        rows.Should().ContainSingle();
        rows[0].Description.Should().Be("OSTERIA DA FORTUNATA");
        rows.Should().NotContain(r => r.Description.StartsWith("Comision:", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------
    // Several foreign currencies in one statement all survive.
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_MixedForeignCurrencies_AllSurvive()
    {
        List<ParsedStatementRow> rows = ExtractRows(Doc(
            "SHOP A -10.00 USD 174.30 1 000.00",
            "SHOP B -20.00 EUR 388.40 980.00",
            "SHOP C -30.00 RON 117.50 950.00",
            "SHOP D -40.00 GBP 920.00 910.00",
            "SHOP E -50.00 TRY 21.60 900.00"));

        rows.Should().HaveCount(5);
        rows.Select(r => r.OriginalCurrency).Should()
            .BeEquivalentTo(["USD", "EUR", "RON", "GBP", "TRY"]);
        rows.Should().AllSatisfy(r => r.Direction.Should().Be(TransactionDirection.Expense));
    }

    // ------------------------------------------------------------------
    // Commission split: principal = ieșiri − comision, fee = comision.
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_CommissionRow_SplitsPrincipalAndFee_SummingToIesiri()
    {
        // ieșiri=100.00, comision=2.50, balance=897.50 → principal 97.50, fee 2.50
        List<ParsedStatementRow> rows = ExtractRows(Doc(
            "MAIB P2P -100.00 MDL 100.00 2.50 897.50"));

        rows.Should().HaveCount(2);
        ParsedStatementRow principal = rows.Single(r => !r.Description.StartsWith("Comision:", StringComparison.Ordinal));
        ParsedStatementRow fee = rows.Single(r => r.Description.StartsWith("Comision:", StringComparison.Ordinal));

        principal.AmountMdl.Should().Be(97.50m);
        fee.AmountMdl.Should().Be(2.50m);
        (principal.AmountMdl + fee.AmountMdl).Should().Be(100.00m);
    }

    // ------------------------------------------------------------------
    // A positive (income) row with a commission column. The fee row should
    // still be Expense (a fee is never income), and the principal stays
    // income. Probes the direction wiring of the split.
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_IncomeRowWithCommission_FeeIsExpense_PrincipalKeepsIncome()
    {
        List<ParsedStatementRow> rows = ExtractRows(Doc(
            "Transfer Retragere Cashback 100.00 MDL 100.00 1.00 5 000.00"));

        rows.Should().HaveCount(2);
        ParsedStatementRow principal = rows.Single(r => !r.Description.StartsWith("Comision:", StringComparison.Ordinal));
        ParsedStatementRow fee = rows.Single(r => r.Description.StartsWith("Comision:", StringComparison.Ordinal));

        // BUG-WATCH: documented intent is that fee rows are ALWAYS Expense.
        // The split copies the row's direction for the primary but hard-codes
        // Expense for the fee — verify the fee really is Expense even when the
        // source row is Income.
        principal.Direction.Should().Be(TransactionDirection.Income);
        principal.AmountMdl.Should().Be(99.00m);
        fee.Direction.Should().Be(TransactionDirection.Expense);
        fee.AmountMdl.Should().Be(1.00m);
    }

    // ------------------------------------------------------------------
    // Pure-fee row (comision == ieșiri): only the fee row is emitted.
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_CommissionEqualsIesiri_EmitsOnlyFeeRow()
    {
        List<ParsedStatementRow> rows = ExtractRows(Doc(
            "Comision deservire -10.00 MDL 10.00 10.00 1 234.56"));

        rows.Should().ContainSingle();
        rows[0].Description.Should().StartWith("Comision:");
        rows[0].AmountMdl.Should().Be(10.00m);
    }

    // ------------------------------------------------------------------
    // Section-boundary row immediately followed by a card marker: the marker
    // must be stripped and not contaminate the tail / drop the row.
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_RowFollowedByCardSectionMarker_IsNotDropped()
    {
        string scan =
            "AGRNMD2X 2026-05-19 2026-05-19 LINELLA SRL -71.14 MDL 71.14 2 000.00 "
            + "999999******0000 #Cardul "
            + "2026-05-20 2026-05-20 ANDYS PIZZA -55.00 MDL 55.00 1 945.00 "
            + "Sfârșitul extrasului de cont";

        List<ParsedStatementRow> rows = ExtractRows(scan);

        rows.Should().HaveCount(2);
        rows.Should().Contain(r => r.AmountMdl == 71.14m && r.Description.Contains("LINELLA", StringComparison.Ordinal));
        rows.Should().Contain(r => r.AmountMdl == 55.00m);
    }

    // ------------------------------------------------------------------
    // A "Transfer Plata salariu" style row: the description carries the word
    // "Transfer" but it is NOT an internal transfer (salary income). The
    // parser does not itself set IsTransfer (that is the detector's job), but
    // it must still emit the row correctly as Income.
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_TransferSalaryRow_EmittedAsIncomeRow()
    {
        List<ParsedStatementRow> rows = ExtractRows(Doc(
            "Transfer Salariul pentru luna mai 50 000.00 MDL 50 000.00 55 000.00"));

        rows.Should().ContainSingle();
        rows[0].Direction.Should().Be(TransactionDirection.Income);
        rows[0].AmountMdl.Should().Be(50000.00m);
        rows[0].Description.Should().Contain("Salariul");
    }

    // ------------------------------------------------------------------
    // Skip-branch coverage for ExtractRows: malformed anchors / bodies must be
    // dropped silently, leaving only the valid rows.
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_AnchorWithImpossibleCalendarDate_IsSkipped()
    {
        // The date-pair regex matches the SHAPE (yyyy-MM-dd) but 2026-13-45 is not
        // a real date, so DateOnly.TryParseExact fails and the row is skipped.
        string scan =
            "AGRNMD2X 2026-13-45 2026-13-45 BOGUS DATE ROW -10.00 MDL 10.00 990.00 "
            + "2026-05-19 2026-05-19 ANDYS PIZZA -55.00 MDL 55.00 935.00 "
            + "Sfârșitul extrasului de cont";

        List<ParsedStatementRow> rows = ExtractRows(scan);

        rows.Should().ContainSingle();
        rows[0].AmountMdl.Should().Be(55.00m);
    }

    [Fact]
    public void Parse_BodyWithoutAmountCurrencyPattern_IsSkipped()
    {
        // No "<number> <CCY>" pair -> RowBodyRegex doesn't match -> row skipped.
        List<ParsedStatementRow> rows = ExtractRows(Doc("DESCRIPTION WITH NO AMOUNT TOKEN"));

        rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_BodyWithNoTailNumbers_IsSkipped()
    {
        // Body matches "<amt> MDL" but the tail carries no parseable number, so
        // TokenizeTailNumbers returns empty and the row is skipped.
        List<ParsedStatementRow> rows = ExtractRows(Doc("PAYMENT -12.00 MDL"));

        rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TailMdlAmountZero_IsSkipped()
    {
        // First tail number (the MDL column amount) is 0 -> mdl <= 0 guard skips it.
        List<ParsedStatementRow> rows = ExtractRows(Doc("ZERO ROW -0.00 MDL 0.00 1 000.00"));

        rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_AmountTokenOverflowingDecimal_IsSkipped()
    {
        // The body regex matches the shape of the source-amount token, but a value
        // far beyond decimal's range fails TryParseAmount, so the row is skipped.
        string huge = "-999 999 999 999 999 999 999 999 999 999 999";
        List<ParsedStatementRow> rows = ExtractRows(Doc(
            $"OVERFLOW ROW {huge} MDL 100.00 200.00",
            "ANDYS PIZZA -55.00 MDL 55.00 145.00"));

        rows.Should().ContainSingle();
        rows[0].Description.Should().Contain("ANDYS");
    }

    [Fact]
    public void Parse_IntegerOnlyTail_UsesFallbackTokenizer()
    {
        // The amount token carries a decimal (so the body regex parses cleanly),
        // but the TAIL has only integers, forcing TokenizeTailNumbers' integer-only
        // fallback branch (the decimal-format pass finds nothing).
        List<ParsedStatementRow> rows = ExtractRows(Doc("CASH ROW -100.00 MDL 5000 12000"));

        rows.Should().ContainSingle();
        rows[0].AmountMdl.Should().Be(5000m);
        rows[0].Direction.Should().Be(TransactionDirection.Expense);
    }

    // ------------------------------------------------------------------
    // ParseText (the post-extraction stage) — exercised with crafted text so the
    // format-sniffing, missing-period, and missing-amount branches are covered
    // without minting PDFs.
    // ------------------------------------------------------------------

    private static MaibStatementParser NewParser() =>
        new(NullLogger<MaibStatementParser>.Instance);

    [Fact]
    public void ParseText_WithoutBankCodeMarker_ReturnsUnsupportedFormat()
    {
        Result<ParsedStatement> result = NewParser().ParseText("some random text without the marker");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.UnsupportedFormat);
    }

    [Fact]
    public void ParseText_WithMarkerButNoPeriod_ReturnsParseFailed()
    {
        Result<ParsedStatement> result = NewParser().ParseText("AGRNMD2X no period line here");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.ParseFailed);
    }

    [Fact]
    public void ParseText_WithPeriodButNoBalanceLines_DefaultsAmountsToZero()
    {
        // Marker + a valid period, but none of the OpeningBalance/TotalIn/TotalOut/
        // TotalFees regexes match -> ExtractAmount returns 0 for each (the no-match
        // branch). No rows either, so the summary is all zeros.
        const string text =
            "AGRNMD2X EXTRAS DE CONT 01.05.2026 - 31.05.2026 Sfârșitul extrasului de cont";

        Result<ParsedStatement> result = NewParser().ParseText(text);

        result.IsSuccess.Should().BeTrue(because: result.IsFailure ? result.Error.Message : null);
        ParsedStatement statement = result.Value;
        statement.Period.From.Should().Be(new DateOnly(2026, 5, 1));
        statement.Period.To.Should().Be(new DateOnly(2026, 5, 31));
        statement.Summary.OpeningBalance.Should().Be(0m);
        statement.Summary.TotalIn.Should().Be(0m);
        statement.Summary.TotalOut.Should().Be(0m);
        statement.Summary.TotalFees.Should().Be(0m);
        statement.Rows.Should().BeEmpty();
    }
}
