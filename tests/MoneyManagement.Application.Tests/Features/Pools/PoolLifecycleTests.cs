using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Features.Accounts.DeleteAccount;
using MoneyManagement.Application.Features.Transactions.CreateTransaction;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Pools;

/// <summary>
/// Undoing the POOL itself - the level above <see cref="PoolEventLifecycleTests"/>.
/// <para>
/// Pools used to be the only slice with archive and no way back: no delete, no
/// unarchive. That reads fine for winding down a real pool (archiving already
/// demands zero outside units, so the account is wholly the owner's again) and
/// fails completely for a pool typed in BY MISTAKE - which is what happened. The
/// mistaken pool could not be deleted, its seed could not be deleted
/// (<c>pools.seed_cannot_be_deleted</c>), and because <c>pools.account_id</c> is
/// <c>ON DELETE RESTRICT</c> the hidden row made its account permanently
/// undeletable too.
/// </para>
/// <para>
/// The refusals live next door in <c>PoolGuardTableTests</c>, with the rest of
/// the guard table. What is pinned here is what the two new commands DO.
/// </para>
/// </summary>
public class PoolLifecycleTests
{
    /// <summary>
    /// The reported shape, exactly: one Seed, one (owner) participant, no cash,
    /// no movement transaction, and - because the inception value matches the
    /// derived balance - no catch-up mark either.
    /// </summary>
    private static async Task<PoolHarness> MistakenPoolAsync()
    {
        var harness = PoolHarness.Create(openingBalance: 3_000m);
        await harness.SeedPoolAsync(poolValueAtInception: 3_000m);
        return harness;
    }

    [Fact]
    public async Task DeletePool_OfASeedOnlyPool_RemovesThePoolItsParticipantsAndItsEvents()
    {
        PoolHarness harness = await MistakenPoolAsync();

        // The shape is what makes the delete safe, so assert it rather than
        // assuming it: one seed, no cash, no linked transaction.
        PoolUnitEvent seed = (await harness.Db.PoolUnitEvents.ToListAsync()).Should().ContainSingle().Which;
        seed.Kind.Should().Be(PoolUnitEventKind.Seed);
        seed.Cash.Should().BeNull();
        seed.MovementTransactionId.Should().BeNull();

        Result result = await harness.DeletePoolAsync();

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

        // All three tables, not just the parent. The FKs cascade in the real
        // database (pinned in PoolPersistenceTests), but the handler removes the
        // children explicitly so the intent is readable and testable here.
        (await harness.Db.Pools.ToListAsync()).Should().BeEmpty();
        (await harness.Db.PoolParticipants.ToListAsync()).Should().BeEmpty();
        (await harness.Db.PoolUnitEvents.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DeletePool_LeavesTheAccountsCatchUpMarkAndBalanceAlone()
    {
        // Inception value 1,500 against a derived 1,200 - so create-pool writes a
        // real +300 IsAdjustment row, the catch-up mark.
        var harness = PoolHarness.Create(openingBalance: 1_200m);
        await harness.SeedPoolAsync(poolValueAtInception: 1_500m);

        Transaction mark = (await harness.Db.Transactions.ToListAsync()).Should().ContainSingle().Which;
        mark.IsAdjustment.Should().BeTrue();
        (await harness.DerivedBalanceAsync()).Should().Be(1_500m);

        Result result = await harness.DeletePoolAsync();
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);

        // DELIBERATE. The mark moved the balance to what the exchange really
        // showed and the user may have reconciled a statement against it;
        // silently reversing it here would rewrite balance history to undo a
        // correction that was TRUE. Deleting a pool removes the pool's own rows
        // and nothing else.
        Transaction survivor = (await harness.Db.Transactions.ToListAsync()).Should().ContainSingle().Which;
        survivor.Id.Should().Be(mark.Id);
        survivor.IsDeleted.Should().BeFalse();
        (await harness.DerivedBalanceAsync()).Should().Be(1_500m);
    }

    [Fact]
    public async Task DeletePool_UnblocksThePermanentAccountDelete()
    {
        // The user's actual dead end: an archived pool with nothing in it, and an
        // account that could never be permanently deleted because of it.
        PoolHarness harness = await MistakenPoolAsync();
        (await harness.ArchivePoolAsync()).IsSuccess.Should().BeTrue();

        var accounts = new DeleteAccountCommandHandler(harness.Db);

        Result blocked = await accounts.Handle(
            new DeleteAccountCommand(harness.Account.Id),
            CancellationToken.None);

        blocked.IsFailure.Should().BeTrue();
        blocked.Error.Code.Should().Be("account.has_linked_records");

        (await harness.DeletePoolAsync()).IsSuccess.Should().BeTrue();

        // The escape hatch, closed loop: with the pool gone the FK is gone, and
        // this account never had a transaction to block it on its own merits.
        (await harness.Db.Transactions.ToListAsync()).Should().BeEmpty();

        Result deleted = await accounts.Handle(
            new DeleteAccountCommand(harness.Account.Id),
            CancellationToken.None);

        deleted.IsSuccess.Should().BeTrue(deleted.IsFailure ? deleted.Error.Code : null);
        (await harness.Db.Accounts.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DeletePool_OfAnArchivedPool_IsAllowed()
    {
        PoolHarness harness = await MistakenPoolAsync();
        (await harness.ArchivePoolAsync()).IsSuccess.Should().BeTrue();

        Result result = await harness.DeletePoolAsync();

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        (await harness.Db.Pools.ToListAsync()).Should().BeEmpty();

        // NOTE: the handler's IgnoreQueryFilters() cannot be PROVEN here - the
        // fake context has no query filter and its provider is not an
        // EntityQueryProvider, so IgnoreQueryFilters() silently returns the
        // source and this would pass with the call deleted. The real filter is
        // exercised over HTTP in PoolEndpointTests, on the same reasoning as
        // PoolPersistenceTests' ownership-source test.
    }

    [Fact]
    public async Task DeletePool_ForAnUnknownId_IsNotFound()
    {
        PoolHarness harness = await MistakenPoolAsync();

        Result result = await harness.DeletePoolAsync(Guid.CreateVersion7());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.not_found");

        // A miss changes nothing.
        (await harness.Db.Pools.ToListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task UnarchivePool_PutsThePoolBackInService_AndReArmsTheGuards()
    {
        PoolHarness harness = await MistakenPoolAsync();
        (await harness.ArchivePoolAsync()).IsSuccess.Should().BeTrue();

        // Archived: the account is a normal account again and manual rows land.
        Result<Guid> whileArchived = await ManualRowAsync(harness);
        whileArchived.IsSuccess.Should().BeTrue(whileArchived.IsFailure ? whileArchived.Error.Code : null);

        Result unarchive = await harness.UnarchivePoolAsync();
        unarchive.IsSuccess.Should().BeTrue(unarchive.IsFailure ? unarchive.Error.Code : null);

        Pool pool = (await harness.Db.Pools.ToListAsync()).Should().ContainSingle().Which;
        pool.IsArchived.Should().BeFalse();

        // The direction of travel is towards MORE restriction, never less: an
        // active pool re-arms every row of the guard table on its account. That
        // is why unarchive needs no outside-units check, while archive does.
        Result<Guid> whileActive = await ManualRowAsync(harness);
        whileActive.IsFailure.Should().BeTrue();
        whileActive.Error.Code.Should().Be("pools.manual_movement_blocked");
    }

    [Fact]
    public async Task UnarchivePool_OfAnAlreadyActivePool_IsAnIdempotentNoOp()
    {
        PoolHarness harness = await MistakenPoolAsync();

        Result result = await harness.UnarchivePoolAsync();

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : null);
        (await harness.Db.Pools.ToListAsync()).Should().ContainSingle().Which.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task UnarchivePool_ForAnUnknownId_IsNotFound()
    {
        PoolHarness harness = await MistakenPoolAsync();

        Result result = await harness.UnarchivePoolAsync(Guid.CreateVersion7());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("pools.not_found");
    }

    /// <summary>A plain manual row on the pool's account - the simplest guard probe.</summary>
    private static Task<Result<Guid>> ManualRowAsync(PoolHarness harness) =>
        new CreateTransactionCommandHandler(harness.Db, harness.Fx).Handle(
            new CreateTransactionCommand(
                harness.Account.Id,
                harness.Today,
                TransactionDirection.Income,
                50m,
                "Manual top-up",
                CategoryId: null,
                OriginalAmount: null,
                OriginalCurrency: null),
            CancellationToken.None);
}
