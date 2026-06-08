using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Common;
using MoneyManagement.Application.Features.Transactions;
using MoneyManagement.Application.Features.Transactions.GetTransactions;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Transactions;

/// <summary>
/// Adversarial overnight sweep (2026-06-02): the transactions-list
/// <c>AmountMdl</c> projection re-implements the FX lookup in-memory
/// (<c>GetTransactionsQueryHandler.ConvertInMemory</c>) and must apply the same
/// "Manual rate wins over BnmAuto on a same triple" tie-break as
/// <c>EfFxConverter</c> — otherwise a transaction's MDL value disagrees with the
/// dashboard/report figures for the same row.
/// </summary>
public sealed class TransactionsAmountMdlManualRatePreferenceTests
{
    private static FxRate Rate(decimal rate, FxRateSource source) =>
        FxRate.Create("USD", "MDL", rate, new DateOnly(2026, 1, 1), source).Value;

    [Fact]
    public async Task AmountMdl_PrefersManualRate_OverBnmAuto_OnSameTriple()
    {
        var accountId = Guid.CreateVersion7();
        Transaction usdTx = Transaction.Create(
            accountId,
            new DateOnly(2026, 1, 1),
            TransactionDirection.Expense,
            new Money(10m, "USD"),
            "USD purchase",
            TransactionSource.Manual).Value;

        // BnmAuto inserted first; a naive OrderByDescending(AsOf) + FirstOrDefault
        // would surface it. Manual (rate 20) must win.
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            transactions: [usdTx],
            fxRates: [Rate(17m, FxRateSource.BnmAuto), Rate(20m, FxRateSource.Manual)]);

        var handler = new GetTransactionsQueryHandler(db);
        Result<PagedResult<TransactionDto>> result = await handler.Handle(
            new GetTransactionsQuery(AccountId: accountId), CancellationToken.None);

        TransactionDto dto = result.Value.Items.Single();
        // 10 USD * 20 (Manual) = 200, NOT 10 * 17 (BnmAuto) = 170.
        dto.AmountMdl.Should().Be(200m);
    }
}
