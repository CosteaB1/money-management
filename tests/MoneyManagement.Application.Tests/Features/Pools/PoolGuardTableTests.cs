using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Accounts.ArchiveAccount;
using MoneyManagement.Application.Features.Accounts.DeleteAccount;
using MoneyManagement.Application.Features.Imports;
using MoneyManagement.Application.Features.Imports.CommitImport;
using MoneyManagement.Application.Features.Loans.CreateLoan;
using MoneyManagement.Application.Features.Loans.DeleteLoanPayment;
using MoneyManagement.Application.Features.Loans.RecordLoanPayment;
using MoneyManagement.Application.Features.Pools.CloseDistribution;
using MoneyManagement.Application.Features.Pools.CreatePool;
using MoneyManagement.Application.Features.Pools.RecordRedemption;
using MoneyManagement.Application.Features.Pools.RecordSubscription;
using MoneyManagement.Application.Features.SavingsGoals.CreateGoal;
using MoneyManagement.Application.Features.SavingsGoals.UpdateGoal;
using MoneyManagement.Application.Features.Transactions.AdjustBalance;
using MoneyManagement.Application.Features.Transactions.CreateTransaction;
using MoneyManagement.Application.Features.Transactions.CreateTransfer;
using MoneyManagement.Application.Features.Transactions.DeleteTransaction;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// THE GUARD TABLE, one test per row, each asserting its own error code.
/// <para>
/// Net worth reads a pooled account as <c>value × ownerFraction</c>, so any cash
/// landing on it that does not mint or burn units is silently shared pro-rata
/// with the friends. These are not defensive niceties — every one of them closes
/// a path that moves somebody else's money.
/// </para>
/// </summary>
public class PoolGuardTableTests
{
    private static Account PlainAccount(string name = "Cash", AccountType type = AccountType.Cash) =>
        Account.Create(name, type, new Money(500m, PoolHarness.Currency), new DateOnly(2026, 1, 1), notes: null).Value;

    private static async Task<PoolHarness> PooledAsync(params Account[] extraAccounts)
    {
        var harness = PoolHarness.Create(openingBalance: 1_200m, extraAccounts: extraAccounts);
        await harness.SeedPoolAsync(poolValueAtInception: 1_200m);
        return harness;
    }

    /// <summary>A loan through the real handler, so every guard sees production state.</summary>
    private static async Task<Guid> NewLoanAsync(PoolHarness harness, Guid? accountId)
    {
        var handler = new CreateLoanCommandHandler(harness.Db, harness.Fx, harness.Clock);

        Result<CreateLoanResponse> result = await handler.Handle(
            new CreateLoanCommand(
                LoanDirection.Received,
                "Parents",
                500m,
                PoolHarness.Currency,
                harness.Today,
                accountId,
                Notes: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        return result.Value.Id;
    }

    // ---- AdjustBalance ---------------------------------------------------

    [Theory]
    [InlineData(BalanceChangeKind.Investment)]
    [InlineData(BalanceChangeKind.Withdrawal)]
    public async Task AdjustBalance_NonAdjustmentKindOnAPooledAccount_IsBlocked(BalanceChangeKind kind)
    {
        PoolHarness harness = await PooledAsync();

        var handler = new AdjustBalanceCommandHandler(harness.Db, harness.Fx, harness.Clock);

        Result<AdjustBalanceResult> result = await handler.Handle(
            new AdjustBalanceCommand(harness.Account.Id, kind, 100m, harness.Today, Notes: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.kind_not_allowed");
    }

    [Fact]
    public async Task AdjustBalance_BackDatedMarkOnAPooledAccount_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();

        var handler = new AdjustBalanceCommandHandler(harness.Db, harness.Fx, harness.Clock);

        Result<AdjustBalanceResult> result = await handler.Handle(
            new AdjustBalanceCommand(
                harness.Account.Id,
                BalanceChangeKind.Adjustment,
                1_500m,
                harness.Today.AddDays(-1),
                Notes: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.mark_must_be_today");
    }

    [Fact]
    public async Task AdjustBalance_TodaysMarkOnAPooledAccount_IsStillAllowed()
    {
        PoolHarness harness = await PooledAsync();

        var handler = new AdjustBalanceCommandHandler(harness.Db, harness.Fx, harness.Clock);

        Result<AdjustBalanceResult> result = await handler.Handle(
            new AdjustBalanceCommand(
                harness.Account.Id,
                BalanceChangeKind.Adjustment,
                1_500m,
                harness.Today,
                Notes: null),
            CancellationToken.None);

        // Re-pricing is the one thing a pooled account MUST still accept.
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.Delta.Should().Be(300m);
    }

    [Fact]
    public async Task AdjustBalance_OnANonPooledAccount_IsUntouched()
    {
        Account plain = PlainAccount("XTB", AccountType.Brokerage);
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [plain]);
        var clock = new MutableClock(new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc));

        var handler = new AdjustBalanceCommandHandler(db, FakeFxConverter.Identity(), clock);

        Result<AdjustBalanceResult> result = await handler.Handle(
            new AdjustBalanceCommand(
                plain.Id,
                BalanceChangeKind.Investment,
                100m,
                clock.Today.AddDays(-5),
                Notes: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
    }

    // ---- CreateTransaction -----------------------------------------------

    [Fact]
    public async Task CreateTransaction_OnAPooledAccount_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();

        var handler = new CreateTransactionCommandHandler(harness.Db, harness.Fx);

        Result<Guid> result = await handler.Handle(
            new CreateTransactionCommand(
                harness.Account.Id,
                harness.Today,
                TransactionDirection.Income,
                250m,
                "Manual top-up",
                CategoryId: null,
                OriginalAmount: null,
                OriginalCurrency: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.manual_movement_blocked");
    }

    // ---- CreateTransfer ---------------------------------------------------

    [Fact]
    public async Task CreateTransfer_WithThePooledAccountAsSource_IsBlocked()
    {
        Account plain = PlainAccount();
        PoolHarness harness = await PooledAsync(plain);

        var handler = new CreateTransferCommandHandler(harness.Db, harness.Fx);

        Result<TransferResult> result = await handler.Handle(
            new CreateTransferCommand(harness.Account.Id, plain.Id, 100m, harness.Today, "Move", CategoryId: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.transfer_blocked");
    }

    [Fact]
    public async Task CreateTransfer_WithThePooledAccountAsDestination_IsBlocked()
    {
        Account plain = PlainAccount();
        PoolHarness harness = await PooledAsync(plain);

        var handler = new CreateTransferCommandHandler(harness.Db, harness.Fx);

        Result<TransferResult> result = await handler.Handle(
            new CreateTransferCommand(plain.Id, harness.Account.Id, 100m, harness.Today, "Move", CategoryId: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.transfer_blocked");
    }

    // ---- CommitImport -----------------------------------------------------

    [Fact]
    public async Task CommitImport_TargetingAPooledAccount_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();

        var handler = new CommitImportCommandHandler(harness.Db, harness.Clock, harness.Fx);

        Result<CommitResultDto> result = await handler.Handle(
            new CommitImportCommand(
                harness.Account.Id,
                "statement.pdf",
                "hash",
                BankSource.Maib,
                [
                    new TransactionToImport(
                        harness.Today,
                        TransactionDirection.Expense,
                        300m,
                        "P2P out",
                        CategoryId: null,
                        OriginalAmount: null,
                        OriginalCurrency: null),
                ]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.import_blocked");
    }

    [Fact]
    public async Task CommitImport_WithAPooledCounterAccount_IsBlocked()
    {
        Account plain = PlainAccount("maib", AccountType.BankCurrent);
        PoolHarness harness = await PooledAsync(plain);

        var handler = new CommitImportCommandHandler(harness.Db, harness.Clock, harness.Fx);

        Result<CommitResultDto> result = await handler.Handle(
            new CommitImportCommand(
                plain.Id,
                "statement.pdf",
                "hash",
                BankSource.Maib,
                [
                    new TransactionToImport(
                        harness.Today,
                        TransactionDirection.Expense,
                        300m,
                        "To Binance",
                        CategoryId: null,
                        OriginalAmount: null,
                        OriginalCurrency: null,
                        IsTransfer: true,
                        CounterAccountId: harness.Account.Id),
                ]),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.import_blocked");
    }

    // ---- DeleteTransaction ------------------------------------------------

    [Fact]
    public async Task DeleteTransaction_ThatAUnitEventReferences_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();
        Guid andrei = await harness.AddParticipantAsync("Andrei");

        Result<RecordSubscriptionResponse> subscription = await harness.SubscribeAsync(andrei, 1_200m, 800m);
        subscription.IsSuccess.Should().BeTrue(subscription.IsFailure ? subscription.Error.Code : null);

        var handler = new DeleteTransactionCommandHandler(harness.Db, harness.Fx);

        Result result = await handler.Handle(
            new DeleteTransactionCommand(subscription.Value.MovementTransactionId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.delete_reprices_units");
    }

    [Fact]
    public async Task DeleteTransaction_DatedOnOrBeforeTheLatestUnitEvent_IsBlocked()
    {
        // Inception value above the derived balance, so a catch-up mark exists.
        var harness = PoolHarness.Create(openingBalance: 1_200m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_500m);

        // The re-pricing mark itself: not linked to any unit event, but dated on
        // the same day as the seed, so deleting it would restate the value every
        // NAV since was struck from.
        Transaction mark = (await harness.Db.Transactions.ToListAsync()).Single();

        var handler = new DeleteTransactionCommandHandler(harness.Db, harness.Fx);

        Result result = await handler.Handle(new DeleteTransactionCommand(mark.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.delete_reprices_units");
    }

    [Fact]
    public async Task DeleteTransaction_OnANonPooledAccount_IsUntouched()
    {
        Account plain = PlainAccount();

        Transaction transaction = Transaction.Create(
            plain.Id,
            new DateOnly(2026, 6, 1),
            TransactionDirection.Expense,
            new Money(20m, PoolHarness.Currency),
            "Coffee",
            TransactionSource.Manual).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [plain],
            transactions: [transaction]);

        var handler = new DeleteTransactionCommandHandler(db, FakeFxConverter.Identity());

        Result result = await handler.Handle(new DeleteTransactionCommand(transaction.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        transaction.IsDeleted.Should().BeTrue();
    }

    // ---- ArchiveAccount ---------------------------------------------------

    [Fact]
    public async Task ArchiveAccount_WithOutsideUnitsOutstanding_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        (await harness.SubscribeAsync(andrei, 1_200m, 800m)).IsSuccess.Should().BeTrue();

        var handler = new ArchiveAccountCommandHandler(harness.Db);

        Result result = await handler.Handle(
            new ArchiveAccountCommand(harness.Account.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.account_archive_blocked");
        harness.Account.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task ArchiveAccount_WhenOnlyTheOwnerHoldsUnits_IsAllowed()
    {
        PoolHarness harness = await PooledAsync();

        var handler = new ArchiveAccountCommandHandler(harness.Db);

        Result result = await handler.Handle(
            new ArchiveAccountCommand(harness.Account.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        harness.Account.IsArchived.Should().BeTrue();
    }

    // ---- Loans ------------------------------------------------------------

    [Fact]
    public async Task CreateLoan_DisbursingIntoAPooledAccount_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();

        var handler = new CreateLoanCommandHandler(harness.Db, harness.Fx, harness.Clock);

        // Borrowed money landing on the pool account mints no units, so the
        // friends' fraction owns a slice of it the moment it arrives - and net
        // worth moves twice, because LoanExternalClaimSource books the liability
        // on top of the raised balance.
        Result<CreateLoanResponse> result = await handler.Handle(
            new CreateLoanCommand(
                LoanDirection.Received,
                "Parents",
                500m,
                PoolHarness.Currency,
                harness.Today,
                harness.Account.Id,
                Notes: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.loan_movement_blocked");

        // The loan is rejected whole - no half-written row, no leg.
        harness.Db.Loans.Should().BeEmpty();
        harness.Db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordLoanPayment_PaidOutOfAPooledAccount_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();

        // The loan itself is fine; it is the account the money leaves from that
        // is not.
        Guid loanId = await NewLoanAsync(harness, accountId: null);

        var handler = new RecordLoanPaymentCommandHandler(harness.Db, harness.Fx, harness.Clock);

        Result<RecordLoanPaymentResponse> result = await handler.Handle(
            new RecordLoanPaymentCommand(loanId, 100m, harness.Today, harness.Account.Id, Notes: null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.loan_movement_blocked");

        // Cash leaving with no unit event makes the owner fund part of the
        // friends' stake, so neither the payment nor its leg may land.
        harness.Db.LoanPayments.Should().BeEmpty();
        harness.Db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteLoanPayment_SoftDeletingAPooledAccountRow_IsBlocked()
    {
        // The reachable order of events: the payment is recorded while the
        // account is still ordinary, and the pool is created over it afterwards.
        // The guards above cannot prevent that history - only refuse to unwind it.
        var harness = PoolHarness.Create(openingBalance: 1_200m);

        Guid loanId = await NewLoanAsync(harness, accountId: null);

        var record = new RecordLoanPaymentCommandHandler(harness.Db, harness.Fx, harness.Clock);

        Result<RecordLoanPaymentResponse> payment = await record.Handle(
            new RecordLoanPaymentCommand(loanId, 100m, harness.Today, harness.Account.Id, Notes: null),
            CancellationToken.None);

        payment.IsSuccess.Should().BeTrue(payment.IsFailure ? payment.Error.Code : null);

        // The balance is now 1,100; pooling at exactly that keeps the mark delta
        // at zero, so the payment leg is the only transaction on the account.
        await harness.SeedPoolAsync(poolValueAtInception: 1_100m);

        var handler = new DeleteLoanPaymentCommandHandler(harness.Db, harness.Fx);

        Result result = await handler.Handle(
            new DeleteLoanPaymentCommand(loanId, payment.Value.Id),
            CancellationToken.None);

        // This handler soft-deletes the transaction INLINE, so DeleteTransaction's
        // guard never runs on it. The row is dated on the seed's date, and
        // removing it would restate the value those units were struck from.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.delete_reprices_units");

        harness.Db.LoanPayments.Should().ContainSingle();
        Transaction leg = (await harness.Db.Transactions.ToListAsync()).Single();
        leg.IsDeleted.Should().BeFalse();
    }

    // ---- The cash-looks-like-a-balance validator ---------------------------

    [Fact]
    public async Task Subscription_WhoseCashEqualsTheAccountBalance_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();
        Guid andrei = await harness.AddParticipantAsync("Andrei");

        // 1,200 is the account's derived balance — the demonstrated slip is a
        // BALANCE typed into an AMOUNT field.
        Result<RecordSubscriptionResponse> result = await harness.SubscribeAsync(andrei, 1_500m, 1_200m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.cash_looks_like_a_balance");
    }

    [Fact]
    public async Task Subscription_WhoseCashEqualsTheValueJustTyped_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();
        Guid andrei = await harness.AddParticipantAsync("Andrei");

        Result<RecordSubscriptionResponse> result = await harness.SubscribeAsync(andrei, 1_500m, 1_500m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.cash_looks_like_a_balance");
    }

    [Fact]
    public async Task Redemption_WhoseCashEqualsTheAccountBalance_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();

        Result<RecordRedemptionResponse> result = await harness.RedeemAsync(harness.OwnerId, 1_500m, 1_200m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.cash_looks_like_a_balance");
    }

    [Fact]
    public async Task Distribution_WhoseCashEqualsTheAccountBalance_IsBlocked()
    {
        var harness = PoolHarness.Create(openingBalance: 600m);
        await harness.SeedPoolAsync(poolValueAtInception: 600m);

        Guid andrei = await harness.AddParticipantAsync("Andrei");
        (await harness.SubscribeAsync(andrei, 600m, 400m)).IsSuccess.Should().BeTrue();

        // Balance is 1,000 and total units are 1,000. Marking to 4,000 puts NAV
        // at 4.0, so Andrei's 400 units are worth 1,600 against a 400 base — a
        // distributable of 1,200. Asking for exactly 1,000 is therefore WITHIN
        // his entitlement, which is what makes this a clean test of the
        // looks-like-a-balance guard rather than of the entitlement cap.
        Result<CloseDistributionResponse> result = await harness.CloseAsync(
            4_000m,
            [new DistributionPayout(andrei, 1_000m)]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.cash_looks_like_a_balance");
    }

    // ---- A non-owner payout cannot name a tracked destination -------------

    [Fact]
    public async Task Redemption_ByANonOwner_NamingADestinationAccount_IsBlocked()
    {
        Account bybit = PlainAccount("Bybit", AccountType.CryptoExchange);
        PoolHarness harness = await PooledAsync(bybit);

        Guid andrei = await harness.AddParticipantAsync("Andrei");
        (await harness.SubscribeAsync(andrei, 1_200m, 500m)).IsSuccess.Should().BeTrue();

        // The redemption's counter leg lands on a WHOLLY-OWNED account, where the
        // money reads as the user's own contribution and counts in full towards
        // net worth. For the owner moving value to Bybit that is the intended
        // path; for a friend's payout it converts their capital into the user's.
        Result<RecordRedemptionResponse> result =
            await harness.RedeemAsync(andrei, 1_700m, 400m, destinationAccountId: bybit.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.destination_requires_owner");
    }

    [Fact]
    public async Task Redemption_ByTheOwner_NamingADestinationAccount_IsStillAllowed()
    {
        Account bybit = PlainAccount("Bybit", AccountType.CryptoExchange);
        PoolHarness harness = await PooledAsync(bybit);

        // POOLED-CAPITAL.md §6, Sep 18: the owner parks 400 on Bybit. Two
        // reciprocal legs, and net worth is conserved.
        Result<RecordRedemptionResponse> result =
            await harness.RedeemAsync(harness.OwnerId, 1_200m, 400m, destinationAccountId: bybit.Id);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.CounterTransactionId.Should().NotBeNull();
    }

    // ---- Savings goals ----------------------------------------------------

    [Fact]
    public async Task CreateGoal_LinkedToAPooledAccount_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();

        var handler = new CreateGoalCommandHandler(harness.Db, harness.Clock);

        Result<CreateGoalResponse> result = await handler.Handle(
            new CreateGoalCommand("Emergency fund", 10_000m, TargetDate: null, harness.Account.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.goal_link_blocked");
    }

    [Fact]
    public async Task UpdateGoal_RelinkedToAPooledAccount_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();

        SavingsGoal goal = SavingsGoal.Create(
            "Emergency fund",
            new Money(10_000m, ReportingCurrencies.Mdl),
            targetDate: null,
            linkedAccountId: null,
            harness.Clock).Value;

        harness.Db.SavingsGoals.Add(goal);

        var handler = new UpdateGoalCommandHandler(harness.Db, harness.Clock);

        Result result = await handler.Handle(
            new UpdateGoalCommand(goal.Id, "Emergency fund", 10_000m, TargetDate: null, harness.Account.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.goal_link_blocked");
    }

    [Fact]
    public async Task CreatePool_OnAnAccountASavingsGoalAlreadyPointsAt_IsBlocked()
    {
        // The mirror of the two rows above. CreateGoal/UpdateGoal only ever
        // asked the question in one direction, so goal-first-then-pool walked
        // straight through: from then on SavingsGoal.Saved - which IS this
        // account's balance, with no ownership fraction anywhere in GetGoals or
        // GetGoalDetail - would count the friends' capital as the user's
        // progress, and the goal could report itself complete on their money.
        var harness = PoolHarness.Create(openingBalance: 1_200m);

        SavingsGoal goal = SavingsGoal.Create(
            "Emergency fund",
            new Money(10_000m, ReportingCurrencies.Mdl),
            targetDate: null,
            linkedAccountId: harness.Account.Id,
            harness.Clock).Value;

        harness.Db.SavingsGoals.Add(goal);

        Result<CreatePoolResponse> result = await harness.CreatePoolAsync(poolValueAtInception: 1_200m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.goal_link_blocked");

        // Rejected before Pool.Create, so nothing is left half-built.
        harness.Db.Pools.Should().BeEmpty();
        harness.Db.PoolParticipants.Should().BeEmpty();
        harness.Db.PoolUnitEvents.Should().BeEmpty();
    }

    // ---- DeletePool -------------------------------------------------------
    //
    // DELETE /pools/{id} is the escape hatch for a pool created BY MISTAKE, and
    // it is the narrowest door in the slice: allowed only when the pool cannot
    // possibly hold anyone else's money (no non-owner units) and provably never
    // moved any (no event with cash or a linked transaction). Everything else is
    // a ledger of real money, wound down by redeeming and archiving. One error
    // code covers both refusals - the remedy is the same either way.

    [Fact]
    public async Task DeletePool_WhileANonOwnerHoldsUnits_IsBlocked()
    {
        PoolHarness harness = await PooledAsync();
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        (await harness.SubscribeAsync(andrei, 1_200m, 800m)).IsSuccess.Should().BeTrue();

        Result result = await harness.DeletePoolAsync();

        // Same count Pool.Archive is judged on, so "deletable" can never be
        // laxer than "archivable" on the only question that matters: is any of
        // this somebody else's?
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.delete_has_movements");
        harness.Db.Pools.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DeletePool_WhenAUnitEventCarriesCash_IsBlocked()
    {
        // A CLOSED but unpaid distribution: cash on the event, no transaction
        // yet. The units-side guard is silent here (the owner is the only
        // participant), so this isolates the cash test.
        PoolHarness harness = await PooledAsync();

        Result<CloseDistributionResponse> close =
            await harness.CloseAsync(1_400m, [new DistributionPayout(harness.OwnerId, 100m)]);

        close.IsSuccess.Should().BeTrue(close.IsFailure ? close.Error.Code : null);

        PoolUnitEvent payout = (await harness.Db.PoolUnitEvents.ToListAsync())
            .Single(e => e.Kind == PoolUnitEventKind.Distribution);

        payout.Cash.Should().NotBeNull();
        payout.MovementTransactionId.Should().BeNull("an unpaid distribution has no money row yet");

        Result result = await harness.DeletePoolAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.delete_has_movements");
    }

    [Fact]
    public async Task DeletePool_WhenAUnitEventLinksAMovementTransaction_IsBlocked()
    {
        // The owner's own subscription: outside units are still zero, but the
        // event is the bookkeeping half of a real transaction on the account.
        // Deleting the pool would strand that row's explanation.
        PoolHarness harness = await PooledAsync();

        Result<RecordSubscriptionResponse> subscription =
            await harness.SubscribeAsync(harness.OwnerId, 1_200m, 300m);

        subscription.IsSuccess.Should().BeTrue(subscription.IsFailure ? subscription.Error.Code : null);
        subscription.Value.MovementTransactionId.Should().NotBeEmpty();

        Result result = await harness.DeletePoolAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.delete_has_movements");
    }

    // ---- DeleteAccount ----------------------------------------------------

    [Fact]
    public async Task DeleteAccount_HoldingAPool_IsAConflictNotAForeignKeyCrash()
    {
        PoolHarness harness = await PooledAsync();

        // Exactly the reachable shape: a zero-delta inception mark writes no
        // transaction and the seed carries no cash leg by design, so this
        // account has no transactions, imports or goals to stop the delete -
        // only the pool itself, whose FK is ON DELETE RESTRICT.
        harness.Db.Transactions.Should().BeEmpty();

        var handler = new DeleteAccountCommandHandler(harness.Db);

        Result result = await handler.Handle(
            new DeleteAccountCommand(harness.Account.Id),
            CancellationToken.None);

        // Without the pre-check this reached db.Accounts.Remove and died on
        // fk_pools_accounts_account_id as an unhandled 500 with no errorCode.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.has_linked_records");
        harness.Db.Accounts.Should().Contain(harness.Account);
    }

    [Fact]
    public async Task DeleteAccount_HoldingAnARCHIVEDPool_IsStillBlocked()
    {
        PoolHarness harness = await PooledAsync();
        (await harness.ArchivePoolAsync()).IsSuccess.Should().BeTrue();

        var handler = new DeleteAccountCommandHandler(harness.Db);

        Result result = await handler.Handle(
            new DeleteAccountCommand(harness.Account.Id),
            CancellationToken.None);

        // DECIDED, not an oversight. Archiving releases the account from every
        // OTHER guard - but pools.account_id is ON DELETE RESTRICT and the row
        // is still there, so dropping archived pools from this check would not
        // make the account deletable, it would just swap this 409 for the
        // unhandled 500 the pre-check exists to prevent. The way out is to
        // delete the pool (below), not to stop counting it.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.has_linked_records");
    }

    [Fact]
    public async Task DeleteAccount_AfterTheMistakenPoolIsDeleted_Succeeds()
    {
        PoolHarness harness = await PooledAsync();
        (await harness.ArchivePoolAsync()).IsSuccess.Should().BeTrue();
        (await harness.DeletePoolAsync()).IsSuccess.Should().BeTrue();

        var handler = new DeleteAccountCommandHandler(harness.Db);

        Result result = await handler.Handle(
            new DeleteAccountCommand(harness.Account.Id),
            CancellationToken.None);

        // The guarantee behind the decision above: a pool is never a permanent
        // life sentence on its account, because there is always a route to a
        // state where the account is deletable.
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
    }

    // ---- Archived pools release the account -------------------------------

    [Fact]
    public async Task ArchivingThePool_ReleasesTheAccountFromEveryGuard()
    {
        PoolHarness harness = await PooledAsync();

        Result archive = await harness.ArchivePoolAsync();
        archive.IsSuccess.Should().BeTrue(archive.IsFailure ? archive.Error.Code : null);

        // Archiving already requires zero outside units, so the account is a
        // normal account again and the guards must let go of it.
        var handler = new CreateTransactionCommandHandler(harness.Db, harness.Fx);

        Result<Guid> result = await handler.Handle(
            new CreateTransactionCommand(
                harness.Account.Id,
                harness.Today,
                TransactionDirection.Income,
                250m,
                "Manual top-up",
                CategoryId: null,
                OriginalAmount: null,
                OriginalCurrency: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
    }

    [Fact]
    public async Task ArchivingThePool_WithOutsideUnitsOutstanding_IsRejected()
    {
        PoolHarness harness = await PooledAsync();
        Guid andrei = await harness.AddParticipantAsync("Andrei");
        (await harness.SubscribeAsync(andrei, 1_200m, 800m)).IsSuccess.Should().BeTrue();

        Result result = await harness.ArchivePoolAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.pool_has_outside_units");
    }

    [Fact]
    public async Task PoolCommands_BypassTheGuards_AndStillWriteTheirOwnMarkAndLegs()
    {
        // The sanctioned path is structural: the pool's handlers build their
        // rows inline rather than delegating to the guarded handlers, so nothing
        // has to remember to set a bypass flag.
        PoolHarness harness = await PooledAsync();
        Guid andrei = await harness.AddParticipantAsync("Andrei");

        Result<RecordSubscriptionResponse> result = await harness.SubscribeAsync(andrei, 1_500m, 800m);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.MarkTransactionId.Should().NotBeNull();
        result.Value.MovementTransactionId.Should().NotBeEmpty();
    }

    [Fact]
    public void EveryGuardErrorCode_IsNamespacedAndDistinct()
    {
        // The codes surface verbatim as the API's errorCode, so they are part of
        // the contract: add, never rename.
        Error[] guards =
        [
            PoolErrors.KindNotAllowed,
            PoolErrors.MarkMustBeToday,
            PoolErrors.ManualMovementBlocked,
            PoolErrors.TransferBlocked,
            PoolErrors.LoanMovementBlocked,
            PoolErrors.ImportBlocked,
            PoolErrors.DeleteRepricesUnits,
            PoolErrors.AccountArchiveBlocked,
            PoolErrors.CashLooksLikeABalance,
            PoolErrors.DestinationRequiresOwner,
            PoolErrors.GoalLinkBlocked,
            PoolErrors.DeleteHasMovements,
        ];

        guards.Select(e => e.Code).Should().OnlyHaveUniqueItems();
        guards.Should().AllSatisfy(e => e.Code.Should().StartWith("pools."));
    }
}
