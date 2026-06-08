using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.Infrastructure.Imports;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Imports;

public class MaibStatementParserTests
{
    private static Stream LoadFixture(string fileName = "maib_sample.pdf")
    {
        Assembly assembly = typeof(MaibStatementParserTests).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(fileName, StringComparison.Ordinal));
        return assembly.GetManifestResourceStream(resourceName)!;
    }

    private static ParsedStatement Parse(string fileName = "maib_sample.pdf")
    {
        var parser = new MaibStatementParser(NullLogger<MaibStatementParser>.Instance);
        using Stream stream = LoadFixture(fileName);
        Result<ParsedStatement> result = parser.Parse(stream);
        result.IsSuccess.Should().BeTrue(because: result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

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
        // This fixture's box reads `Sold final: 12 549.66 Sold Disponibil: 11 409.66`
        // (Disponibil nets out a 1 140 pending hold); the booked balance is
        // 1 500.00 + 14 111.00 − 3 061.34 = 12 549.66.
        statement.Summary.ClosingBalance.Should().Be(12549.66m);
        statement.Summary.TotalIn.Should().Be(14111.00m);
        statement.Summary.TotalOut.Should().Be(3061.34m);

        // The base sample charges no commission, so the `Total comision` summary
        // total is absent and TotalFees falls back to 0.
        statement.Summary.TotalFees.Should().Be(0m);
    }

    [Fact]
    public void Parse_SalaryFixture_Summary_ParsesTotalComision()
    {
        // maib reports commission as a SEPARATE summary total `Total comision:`
        // (singular label), NOT folded into `Total ieșiri`, so the preview summary
        // must surface it to reconcile. This fixture's summary box reads (one line):
        //   Sold inițial: 40 000.00 Total ieșiri: 17 663.00 Total intrări: 30 000.00
        //   Total comision: 10.00 Sold final: 52 327.00 Sold Disponibil: 52 337.00
        // The single MAIB P2P fee (10.00 MDL) is the only commission in the period,
        // so the standalone `Total comision` equals 10.00.
        ParsedStatement statement = Parse("maib_salary_with_commission.pdf");

        statement.Summary.TotalFees.Should().Be(10.00m);

        // ClosingBalance is COMPUTED as opening + intrări − ieșiri =
        // 40 000.00 + 30 000.00 − 17 663.00 = 52 337.00 (here that equals
        // `Sold Disponibil`; `Sold final` 52 327.00 double-subtracts the 10.00 fee).
        statement.Summary.ClosingBalance.Should().Be(52337.00m);
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
    public void Parse_ContainsClaudeAiUsdExpense()
    {
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

        statement.Rows.Should().AllSatisfy(r => r.Description.Should().NotBeNullOrWhiteSpace());
        statement.Rows.Should().NotBeEmpty();

        // The pending row `ASP IALOVENI SIVDCA HI -1 140.00` is the only one for that
        // amount in the document and lives below the `Tranzacții în procesare` header.
        statement.Rows.Should().NotContain(r => r.AmountMdl == 1140.00m);
    }

    [Fact]
    public void Parse_ContainsRowImmediatelyBeforePendingSection()
    {
        // This is the very last completed row (`999999******0000` card-section marker sits
        // between it and the pending header), historically dropped because the trailing
        // marker contaminated the row body.
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
        // Last row of the `#Cont` section, just before the `#Cardul` header. Same class
        // of bug — historically dropped because the trailing marker contaminated the row.
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
        // Last row of page 1 in the card section — its tail is followed by the page
        // footer ("1 2 din") and page 2's column header. Same dropout class.
        ParsedStatement statement = Parse();

        statement.Rows.Where(r =>
                r.TransactionDate == new DateOnly(2026, 5, 6)
                && r.AmountMdl == 68.00m
                && r.Description.Contains("LINELLA", StringComparison.OrdinalIgnoreCase))
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_RowCount_MatchesStatementSummary()
    {
        // The fixture has 3 income rows (2 A2A transfers + 1 cashback) and 7 expense
        // rows = 10 completed rows. The pending section (1 row, 1 140.00 MDL) is excluded.
        ParsedStatement statement = Parse();

        statement.Rows.Should().HaveCount(10);
        statement.Rows.Count(r => r.Direction == TransactionDirection.Income).Should().Be(3);
        statement.Rows.Count(r => r.Direction == TransactionDirection.Expense).Should().Be(7);
    }

    [Fact]
    public void Parse_SumOfMdlAmounts_MatchesStatementTotals()
    {
        // Each row's MDL amount is the account-currency value (positive). Summing them
        // by direction must equal the statement's Total intrări / Total ieșiri.
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
    public void Parse_SampleFixture_EmitsNoCommissionRows_RegressionGuard()
    {
        // The base sample has no commission column populated on any row, so we must
        // never emit a "Comision: ..." companion row. Tail layout for every row in
        // this fixture is the normal <amount> <balance> pair.
        ParsedStatement statement = Parse();

        statement.Rows.Should().NotContain(r =>
            r.Description.StartsWith("Comision:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_SalaryFixture_RowWithCommission_EmitsSplitPrincipalAndFeeRows()
    {
        // The Salary statement contains one MAIB P2P row whose `ieșiri` column
        // shows 17,163.00 MDL and whose `comision` column shows 10.00 MDL.
        // The 10 is the FEE PORTION of the 17,163 (not an extra debit) — the
        // running balance subtracts 17,163 only. So the parser splits:
        //   primary  = 17,163 − 10 = 17,153 (the user's actual transfer)
        //   fee row  = 10                   (the bank's fee)
        // The two together reconstruct the bank's 17,163 deduction.
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

        // Contract: principal + fee == the original `ieșiri` column the bank
        // actually deducted. If this ever drifts, the running-balance
        // reconciliation downstream will silently break.
        (primary.AmountMdl + fee.AmountMdl).Should().Be(17163.00m);
    }

    [Fact]
    public void Parse_SalaryFixture_BalanceReconciles_AgainstSoldDisponibil()
    {
        // maib's `Sold final` in the summary section is a quirky artifact:
        //   Sold final = opening + intrări − ieșiri − comision
        // which double-subtracts the fee (since `ieșiri` already includes it).
        // The truthful current balance is the per-row `Sold Disponibil` after
        // the last transaction. Because the parser splits ieșiri into
        // (principal, fee) whose sum equals ieșiri, the reconciliation is:
        //   opening + Σ(income) − Σ(expense incl. fees) == Sold Disponibil.
        //
        // For this salary statement the expected `Sold Disponibil` is 52,337.00 MDL
        // (NOT the summary's 52,327.00 `Sold final`, which is the same number minus
        // the 10 MDL total commission).
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

    [Fact]
    public void Parse_RowWhereCommissionConsumesEntireIesiri_EmitsOnlyFeeRow()
    {
        // Synthetic degenerate case: a row whose `ieșiri` equals its
        // `comision` (the full debit is a bank fee, no principal transfer).
        // The parser must skip the primary (which would otherwise be 0 or
        // negative) and emit only the fee row.
        var parser = new MaibStatementParser(NullLogger<MaibStatementParser>.Instance);

        MethodInfo extractRows = typeof(MaibStatementParser).GetMethod(
            "ExtractRows",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        // The same shape PdfPig produces for a real row, but the tail carries
        // <ieșiri> <commission> <running-balance> with ieșiri == commission.
        const string synthetic =
            "AGRNMD2X "
            + "2026-05-19 2026-05-19 Comision deservire card -10.00 MDL 10.00 10.00 1234.56 "
            + "Sfârșitul extrasului de cont";

        var rows = (List<ParsedStatementRow>)extractRows.Invoke(null, [synthetic])!;

        rows.Should().ContainSingle();
        ParsedStatementRow only = rows.Single();
        only.Description.Should().StartWith("Comision:");
        only.Direction.Should().Be(TransactionDirection.Expense);
        only.AmountMdl.Should().Be(10.00m);
    }

    // ---------------------------------------------------------------------
    // FX-currency regression tests. Before the fix, RowBodyRegex only
    // matched `MDL|USD`, silently dropping every EUR / TRY / RON / GBP row.
    // These tests invoke ExtractRows via reflection with synthetic row bodies
    // in the exact shape PdfPig emits.
    // ---------------------------------------------------------------------

    private static List<ParsedStatementRow> ExtractRowsFromSyntheticBody(string syntheticInner)
    {
        // Wrap the row body in the minimum context ExtractRows needs:
        //   - the AGRNMD2X bank marker (currently checked by Parse, harmless here)
        //   - a date-pair anchor in front of the body
        //   - the EndOfStatementMarker behind it so the scan window is bounded
        MethodInfo extractRows = typeof(MaibStatementParser).GetMethod(
            "ExtractRows",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        string synthetic =
            "AGRNMD2X "
            + "2026-05-19 2026-05-19 " + syntheticInner + " "
            + "Sfârșitul extrasului de cont";

        return (List<ParsedStatementRow>)extractRows.Invoke(null, [synthetic])!;
    }

    [Fact]
    public void Parse_EurFxRow_ParsesWithOriginalAmountAndCurrency()
    {
        // Synthetic EUR card payment abroad.
        // Tail layout: <MDL ieșiri> <running-balance>.
        List<ParsedStatementRow> rows = ExtractRowsFromSyntheticBody(
            "OSTERIA DA FORTUNATA -145.00 EUR 2 883.91 4 952.29");

        rows.Should().ContainSingle();
        ParsedStatementRow only = rows.Single();
        only.Description.Should().Be("OSTERIA DA FORTUNATA");
        only.Direction.Should().Be(TransactionDirection.Expense);
        only.AmountMdl.Should().Be(2883.91m);
        only.OriginalCurrency.Should().Be("EUR");
        only.OriginalAmount.Should().Be(145.00m);
    }

    [Fact]
    public void Parse_TryFxRow_ParsesWithOriginalAmountAndCurrency()
    {
        // Synthetic TRY card payment abroad.
        List<ParsedStatementRow> rows = ExtractRowsFromSyntheticBody(
            "MUSTANG MARKET -425.00 TRY 183.63 1 237.97");

        rows.Should().ContainSingle();
        ParsedStatementRow only = rows.Single();
        only.Description.Should().Be("MUSTANG MARKET");
        only.Direction.Should().Be(TransactionDirection.Expense);
        only.AmountMdl.Should().Be(183.63m);
        only.OriginalCurrency.Should().Be("TRY");
        only.OriginalAmount.Should().Be(425.00m);
    }

    [Fact]
    public void Parse_RonFxRow_ParsesWithOriginalAmountAndCurrency()
    {
        // Synthetic RON card payment abroad.
        List<ParsedStatementRow> rows = ExtractRowsFromSyntheticBody(
            "PENNY 4620 BICAZ C4 -17.70 RON 70.06 8 759.29");

        rows.Should().ContainSingle();
        ParsedStatementRow only = rows.Single();
        only.Description.Should().Be("PENNY 4620 BICAZ C4");
        only.Direction.Should().Be(TransactionDirection.Expense);
        only.AmountMdl.Should().Be(70.06m);
        only.OriginalCurrency.Should().Be("RON");
        only.OriginalAmount.Should().Be(17.70m);
    }

    [Fact]
    public void Parse_PositiveFxRow_ParsesAsIncomeRefund()
    {
        // Hotel refund — positive source amount means money came back into the
        // account, so Direction must be Income. The MDL value still comes from
        // the bank's pre-converted column (no FxConverter at parse time).
        List<ParsedStatementRow> rows = ExtractRowsFromSyntheticBody(
            "ONKEL RESORT HOTEL 1 175.00 TRY 507.64 1 929.24");

        rows.Should().ContainSingle();
        ParsedStatementRow only = rows.Single();
        only.Description.Should().Be("ONKEL RESORT HOTEL");
        only.Direction.Should().Be(TransactionDirection.Income);
        only.AmountMdl.Should().Be(507.64m);
        only.OriginalCurrency.Should().Be("TRY");
        only.OriginalAmount.Should().Be(1175.00m);
    }

    [Fact]
    public void Parse_DescriptionWithThreeLetterToken_DoesNotFalseMatchCurrency()
    {
        // Defensive: tokens like "SRL", "ATM", "MIA" are common in maib
        // descriptions, but always live BEFORE the amount (never directly
        // after a number), so the non-greedy `.*?` cannot bind them as a
        // currency. The real currency `RON` after the amount wins.
        List<ParsedStatementRow> rows = ExtractRowsFromSyntheticBody(
            "MEPIFOOD SRL -10.00 RON 40.58 3 784.78");

        rows.Should().ContainSingle();
        ParsedStatementRow only = rows.Single();
        only.Description.Should().Be("MEPIFOOD SRL");
        only.OriginalCurrency.Should().Be("RON");
        only.OriginalAmount.Should().Be(10.00m);
        only.AmountMdl.Should().Be(40.58m);
    }

    [Fact]
    public void Parse_SalaryFixture_FeeRow_IsExpenseInAccountCurrency()
    {
        // Defensive: a commission charged by the bank is in the account currency,
        // never an FX-converted amount. OriginalAmount / OriginalCurrency must be
        // null even if the parent transaction was an FX charge.
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
}
