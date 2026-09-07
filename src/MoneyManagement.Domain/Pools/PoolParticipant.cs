using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Pools;

/// <summary>
/// One holder of units in a <see cref="Pool"/> — a friend, or the user.
/// <para>
/// <b>The owner holds REAL units, not a residual.</b> Modelling the user as
/// "whatever is left over" would make <c>Σ participantUnits == totalUnits</c>
/// true by construction and therefore worthless as a check; with a real owner
/// row it becomes an assertable identity, and an owner withdrawal is literally
/// the same operation as a friend payout (a <see cref="PoolUnitEventKind.Redemption"/>).
/// </para>
/// <para>
/// Exactly one owner per pool, enforced in the database by a filtered unique
/// index on <c>(pool_id) WHERE is_owner</c> — see
/// <c>PoolParticipantConfiguration</c>.
/// </para>
/// </summary>
public sealed class PoolParticipant : Entity
{
    public const int NameMaxLength = 100;

    // EF Core
    private PoolParticipant()
    {
        Name = string.Empty;
    }

    private PoolParticipant(
        Guid id,
        Guid poolId,
        string name,
        bool isOwner,
        DateOnly joinedOn) : base(id)
    {
        PoolId = poolId;
        Name = name;
        IsOwner = isOwner;
        JoinedOn = joinedOn;
        IsArchived = false;
    }

    public Guid PoolId { get; private set; }
    public string Name { get; private set; }

    /// <summary>
    /// <c>true</c> for the single participant that represents the user. Frozen
    /// at creation: flipping it would reassign a stake between the user and a
    /// friend with no unit event to account for the transfer.
    /// </summary>
    public bool IsOwner { get; private set; }

    public DateOnly JoinedOn { get; private set; }
    public bool IsArchived { get; private set; }

    /// <param name="poolId">The owning pool.</param>
    /// <param name="name">Display name.</param>
    /// <param name="isOwner">Whether this row is the user. Exactly one per pool.</param>
    /// <param name="joinedOn">The day they joined; cannot precede the pool's inception.</param>
    /// <param name="poolInceptionDate">
    /// The parent's invariant, supplied by the caller — the domain layer cannot
    /// load the pool (same shape as <c>LoanPayment.Create</c>'s <c>loanDate</c>).
    /// </param>
    /// <param name="clock">Injected so the "no future dates" guard is deterministic in tests.</param>
    public static Result<PoolParticipant> Create(
        Guid poolId,
        string name,
        bool isOwner,
        DateOnly joinedOn,
        DateOnly poolInceptionDate,
        IDateTimeProvider clock)
    {
        if (poolId == Guid.Empty)
        {
            return Result.Failure<PoolParticipant>(PoolErrors.NotFound(poolId));
        }

        Result<string> nameValidation = ValidateName(name);
        if (nameValidation.IsFailure)
        {
            return Result.Failure<PoolParticipant>(nameValidation.Error);
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (joinedOn > today)
        {
            return Result.Failure<PoolParticipant>(PoolErrors.ParticipantJoinedOnInFuture);
        }

        if (joinedOn < poolInceptionDate)
        {
            return Result.Failure<PoolParticipant>(PoolErrors.ParticipantJoinedBeforeInception);
        }

        return new PoolParticipant(
            Guid.CreateVersion7(),
            poolId,
            nameValidation.Value,
            isOwner,
            joinedOn);
    }

    /// <summary>Renames the participant. The only user-editable field.</summary>
    public Result Rename(string name)
    {
        Result<string> validation = ValidateName(name);
        if (validation.IsFailure)
        {
            return Result.Failure(validation.Error);
        }

        Name = validation.Value;
        return Result.Success();
    }

    /// <summary>
    /// Archives a participant who has fully exited.
    /// <para>
    /// <b>This guard is deliberately NOT the loans convention.</b> <c>Loan.Archive()</c>
    /// is unconditional because archiving a loan is pure bookkeeping — the debt
    /// still counts (<c>LoanExternalClaimSource</c> reads archived loans with
    /// <c>IgnoreQueryFilters()</c>). A participant is different: their units are
    /// what the owner fraction is computed FROM, so hiding a row that still
    /// holds units drops it out of <c>Σ participantUnits</c> and hands a
    /// friend's stake to the user silently, with no event and no audit trail.
    /// If you are here to "fix" this method to match <c>Loan.Archive()</c>:
    /// don't. Redeem the units first.
    /// </para>
    /// <para>
    /// The units total is supplied by the caller (the write slice sums the
    /// participant's <see cref="PoolUnitEvent.UnitsDelta"/>s) because the domain
    /// layer cannot query. Idempotent once the balance is zero.
    /// </para>
    /// </summary>
    /// <param name="unitsHeld">The participant's current unit balance.</param>
    public Result Archive(decimal unitsHeld)
    {
        // The owner row anchors the pool: it is the only row the filtered
        // unique index allows, so archiving it would leave the pool ownerless
        // with no way to appoint a replacement.
        if (IsOwner)
        {
            return Result.Failure(PoolErrors.OwnerCannotBeArchived);
        }

        if (Math.Abs(unitsHeld) > PoolUnitEvent.UnitsDustTolerance)
        {
            return Result.Failure(PoolErrors.ParticipantHoldsUnits);
        }

        IsArchived = true;
        return Result.Success();
    }

    /// <summary>Idempotent: unarchiving an already-active participant is a no-op.</summary>
    public Result Unarchive()
    {
        IsArchived = false;
        return Result.Success();
    }

    private static Result<string> ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<string>(PoolErrors.ParticipantNameRequired);
        }

        string trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
        {
            return Result.Failure<string>(PoolErrors.ParticipantNameTooLong);
        }

        return Result.Success(trimmed);
    }
}
