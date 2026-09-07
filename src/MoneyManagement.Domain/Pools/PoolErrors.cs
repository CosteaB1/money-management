using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Pools;

/// <summary>
/// Every validation / lookup failure the pool aggregate can produce. Codes are
/// namespaced <c>pools.*</c> and surface verbatim as the API's
/// <c>errorCode</c>, so they are part of the contract — add, never rename.
/// </summary>
public static class PoolErrors
{
    // ---- Pool ------------------------------------------------------------

    public static readonly Error AccountRequired =
        Error.Validation("pools.account_required", "A pool must be attached to an account.");

    public static readonly Error NameRequired =
        Error.Validation("pools.name_required", "Pool name is required.");

    public static readonly Error NameTooLong =
        Error.Validation("pools.name_too_long", "Pool name must be 100 characters or fewer.");

    public static readonly Error InvalidCurrency =
        Error.Validation(
            "pools.invalid_currency",
            "Currency must be a 3-letter uppercase ISO code (e.g. MDL, USD, EUR, RON).");

    public static readonly Error CurrencyMismatch =
        Error.Validation(
            "pools.currency_mismatch",
            "A pool's currency must match its account's currency - the pool never does FX, NAV is pure native.");

    public static readonly Error InceptionDateInFuture =
        Error.Validation("pools.inception_date_in_future", "Pool inception date cannot be in the future.");

    public static readonly Error NotesTooLong =
        Error.Validation("pools.notes_too_long", "Notes must be 500 characters or fewer.");

    public static readonly Error AccountAlreadyPooled =
        Error.Conflict("pools.account_already_pooled", "That account already has a pool.");

    /// <summary>
    /// Archiving a pool that still holds outside capital would revert the owner
    /// fraction to 1.0 and silently reabsorb a friend's stake.
    /// </summary>
    public static readonly Error PoolHasOutsideUnits =
        Error.Validation(
            "pools.pool_has_outside_units",
            "A pool cannot be archived while non-owner participants still hold units.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("pools.not_found", $"Pool with id '{id}' was not found.");

    // ---- Participant -----------------------------------------------------

    public static readonly Error ParticipantNameRequired =
        Error.Validation("pools.participant_name_required", "Participant name is required.");

    public static readonly Error ParticipantNameTooLong =
        Error.Validation("pools.participant_name_too_long", "Participant name must be 100 characters or fewer.");

    public static readonly Error ParticipantJoinedOnInFuture =
        Error.Validation("pools.participant_joined_on_in_future", "Participant join date cannot be in the future.");

    public static readonly Error ParticipantJoinedBeforeInception =
        Error.Validation(
            "pools.participant_joined_before_inception",
            "Participant join date cannot be earlier than the pool's inception date.");

    /// <summary>
    /// The whole model rests on the identity <c>sum(participantUnits) == totalUnits</c>.
    /// Archiving someone who still holds units drops their stake out of that sum
    /// and silently absorbs their money into the owner's share.
    /// </summary>
    public static readonly Error ParticipantHoldsUnits =
        Error.Validation(
            "pools.participant_holds_units",
            "A participant who still holds units cannot be archived - redeem their units first.");

    /// <summary>
    /// The owner row is the pool's anchor (one per pool, enforced by a filtered
    /// unique index). Archiving it would leave the pool ownerless with no way to
    /// appoint a replacement; archive the pool instead.
    /// </summary>
    public static readonly Error OwnerCannotBeArchived =
        Error.Validation(
            "pools.owner_cannot_be_archived",
            "The owner participant cannot be archived - archive the pool instead.");

    public static readonly Error OwnerAlreadyExists =
        Error.Conflict("pools.owner_already_exists", "A pool can have only one owner participant.");

    public static readonly Error OwnerRequired =
        Error.Validation("pools.owner_required", "A pool must have exactly one owner participant.");

    public static Error ParticipantNotFound(Guid id) =>
        Error.NotFound("pools.participant_not_found", $"Pool participant with id '{id}' was not found.");

    // ---- Unit event ------------------------------------------------------

    public static readonly Error UnitEventKindInvalid =
        Error.Validation("pools.unit_event_kind_invalid", "Unit event kind is not a recognised value.");

    public static readonly Error UnitsMustBePositive =
        Error.Validation(
            "pools.units_must_be_positive",
            "Units must be greater than zero - direction comes from the event kind, not the sign.");

    public static readonly Error NavPerUnitMustBePositive =
        Error.Validation("pools.nav_per_unit_must_be_positive", "NAV per unit must be greater than zero.");

    public static readonly Error PoolValueCannotBeNegative =
        Error.Validation("pools.pool_value_cannot_be_negative", "Pre-money pool value cannot be negative.");

    public static readonly Error UnitEventDateInFuture =
        Error.Validation("pools.unit_event_date_in_future", "Unit event date cannot be in the future.");

    public static readonly Error UnitEventBeforeInception =
        Error.Validation(
            "pools.unit_event_before_inception",
            "Unit event date cannot be earlier than the pool's inception date.");

    public static readonly Error CashRequired =
        Error.Validation(
            "pools.cash_required",
            "Subscriptions, redemptions and distributions must carry a cash amount.");

    public static readonly Error CashMustBePositive =
        Error.Validation(
            "pools.cash_must_be_positive",
            "Cash must be greater than zero - direction comes from the event kind, not the sign.");

    public static readonly Error CashCurrencyMismatch =
        Error.Validation("pools.cash_currency_mismatch", "Cash currency must match the pool's currency.");

    public static readonly Error CashDoesNotMatchUnits =
        Error.Validation(
            "pools.cash_does_not_match_units",
            "Cash must equal units x NAV per unit (to within half a cent).");

    /// <summary>
    /// Seeds and the two cost kinds move no money, so cash on them is always a
    /// modelling mistake - on a seed it doubles the account, on a cost transfer
    /// it mints value out of nothing.
    /// </summary>
    public static readonly Error CashNotAllowed =
        Error.Validation(
            "pools.cash_not_allowed",
            "Seed, cost-share and cost-recovery events move no money and cannot carry cash.");

    public static readonly Error SettledOnRequired =
        Error.Validation(
            "pools.settled_on_required",
            "Subscriptions and redemptions settle immediately and must carry a settlement date.");

    public static readonly Error SettledOnNotAllowed =
        Error.Validation(
            "pools.settled_on_not_allowed",
            "Events that move no cash cannot carry a settlement date.");

    public static readonly Error SettlementBeforeEvent =
        Error.Validation("pools.settlement_before_event", "Settlement date cannot be earlier than the event date.");

    public static readonly Error SettlementDateInFuture =
        Error.Validation("pools.settlement_date_in_future", "Settlement date cannot be in the future.");

    public static readonly Error SeedMustBeOwner =
        Error.Validation("pools.seed_must_be_owner", "Only the owner participant can be seeded.");

    public static readonly Error SeedNavMustBeOne =
        Error.Validation("pools.seed_nav_must_be_one", "A seed is priced at a NAV of exactly 1.");

    public static readonly Error SeedDateMustMatchInception =
        Error.Validation(
            "pools.seed_date_must_match_inception",
            "A seed must be dated on the pool's inception date.");

    public static readonly Error SeedPoolValueMustBeZero =
        Error.Validation(
            "pools.seed_pool_value_must_be_zero",
            "A seed's pre-money pool value is zero by definition - the pool holds nothing before it.");

    public static readonly Error SeedAlreadyExists =
        Error.Conflict("pools.seed_already_exists", "A pool can be seeded only once.");

    public static readonly Error MovementTransactionNotAllowed =
        Error.Validation(
            "pools.movement_transaction_not_allowed",
            "Seed, cost-share and cost-recovery events move no money and cannot link a transaction.");

    /// <summary>
    /// A distribution's transaction and its settlement date must be set
    /// together - see <see cref="PoolUnitEvent.Settle"/>. Setting one without
    /// the other is exactly what makes the friends get paid twice on the same
    /// money.
    /// </summary>
    public static readonly Error DistributionMustSettle =
        Error.Validation(
            "pools.distribution_must_settle",
            "A distribution's payment transaction must be linked through Settle, together with its settlement date.");

    public static readonly Error SettleNotApplicable =
        Error.Validation("pools.settle_not_applicable", "Only a distribution can be settled.");

    public static readonly Error DistributionAlreadySettled =
        Error.Validation("pools.distribution_already_settled", "That distribution has already been paid.");

    public static readonly Error MovementTransactionRequired =
        Error.Validation("pools.movement_transaction_required", "A movement transaction id is required.");

    public static Error UnitEventNotFound(Guid id) =>
        Error.NotFound("pools.unit_event_not_found", $"Pool unit event with id '{id}' was not found.");

    // ---- Register / pricing ----------------------------------------------

    /// <summary>
    /// <c>navPerUnit = poolValue / unitsOutstanding</c> has no answer. TWO
    /// situations resolve to this ONE code, because the hole is the same shape
    /// in both: no units outstanding (the divisor is zero), and a pool VALUE
    /// that leaves no price — non-positive, or small enough that it quantizes
    /// to zero at the 12dp unit scale, which divides exactly as badly as a
    /// literal zero. Callers must FAIL here rather than divide, substitute 1.0,
    /// or "just use the pool value" — each of those silently invents a price
    /// and then mints units against it.
    /// <para>
    /// Deliberately NOT split into a second code: the codes are part of the API
    /// contract (add, never rename), and the remedy the user is being pointed at
    /// — re-price the pool, or seed it — is the same either way.
    /// </para>
    /// </summary>
    public static readonly Error NavUndefined =
        Error.Validation(
            "pools.nav_undefined",
            "The pool has no units outstanding, or a value that leaves no price, so NAV per unit is undefined.");

    public static readonly Error AccountTypeNotEligible =
        Error.Validation(
            "pools.account_type_not_eligible",
            "A pool's account must be a type that accepts balance adjustments (Brokerage, CryptoExchange, "
            + "P2PLending or BankDeposit) - an account that can never be re-priced would freeze the pool's NAV at inception.");

    public static readonly Error PoolValueMustBePositive =
        Error.Validation("pools.pool_value_must_be_positive", "The pool's value must be greater than zero.");

    public static readonly Error ParticipantIsArchived =
        Error.Validation("pools.participant_is_archived", "That participant has been archived and cannot transact.");

    /// <summary>The pool-side equivalent of <c>LoanErrors.PaymentExceedsOutstanding</c>.</summary>
    public static readonly Error RedemptionExceedsStake =
        Error.Validation(
            "pools.redemption_exceeds_stake",
            "A redemption cannot burn more units than the participant holds.");

    public static readonly Error DistributionExceedsDistributable =
        Error.Validation(
            "pools.distribution_exceeds_distributable",
            "A distribution cannot exceed the participant's distributable profit (their stake above their capital base).");

    public static readonly Error DistributionNothingToPay =
        Error.Validation(
            "pools.distribution_nothing_to_pay",
            "No participant has distributable profit - every stake is at or below its capital base.");

    public static readonly Error DestinationCurrencyMismatch =
        Error.Validation(
            "pools.destination_currency_mismatch",
            "The destination account's currency must match the pool's - the pool never does FX.");

    /// <summary>
    /// A redemption's counter leg lands on a WHOLLY-OWNED account, where the
    /// money reads as the user's own and counts in full towards net worth. That
    /// is exactly right for the owner moving their own value to Bybit, and
    /// exactly wrong for anybody else: a friend's payout leaves the tracked
    /// world, so naming a destination for it would convert their capital into
    /// the user's on the way out. One leg, no counter account — see
    /// <c>RecordRedemptionCommand.DestinationAccountId</c>.
    /// </summary>
    public static readonly Error DestinationRequiresOwner =
        Error.Validation(
            "pools.destination_requires_owner",
            "Only the OWNER's redemption can land in another tracked account. A non-owner payout leaves the "
            + "tracked world entirely - record it without a destination account.");

    /// <summary>
    /// The escape hatch for <see cref="CashLooksLikeABalance"/> was claimed but
    /// the redemption does not, in fact, retire every unit in the pool. The flag
    /// is an assertion about the ledger, not a bypass switch: honouring it
    /// unchecked would re-open the very typo the guard exists to catch, one
    /// checkbox away.
    /// </summary>
    public static readonly Error NotAFullWindDown =
        Error.Validation(
            "pools.not_a_full_wind_down",
            "This redemption was flagged as winding the pool down in full, but units would remain "
            + "outstanding afterwards. Clear the flag, or redeem the pool's entire value.");

    public static readonly Error CostLegsUnbalanced =
        Error.Failure(
            "pools.cost_legs_unbalanced",
            "A cost reimbursement must move exactly as many units to the owner as it takes from the participants.");

    public static readonly Error CostExceedsParticipantUnits =
        Error.Validation(
            "pools.cost_exceeds_participant_units",
            "A cost reimbursement cannot take more units than a participant holds.");

    public static readonly Error CostNoOutsideUnits =
        Error.Validation(
            "pools.cost_no_outside_units",
            "There is no outside capital to reimburse the cost from - the owner bears the whole cost.");

    /// <summary>
    /// The seed is the pool's entire basis: every later NAV is denominated
    /// against it. Deleting it would leave a ledger of units struck at prices
    /// that reconcile to nothing. Archive the pool instead.
    /// </summary>
    public static readonly Error SeedCannotBeDeleted =
        Error.Validation(
            "pools.seed_cannot_be_deleted",
            "A pool's seed event cannot be deleted - archive the pool instead.");

    // ---- Guard table -----------------------------------------------------
    //
    // "Pooled" means a NON-ARCHIVED pool exists for the account (archiving
    // already requires zero outside units, so an archived pool's account is
    // safely normal again).
    //
    // Net worth reads pooled accounts as `value x ownerFraction`, which means
    // ANY cash landing on the account that does not mint or burn units is
    // silently shared pro-rata with the outside investors. Every path that can
    // move money on such an account is therefore closed here and re-opened only
    // through the pool's own commands.

    public static readonly Error KindNotAllowed =
        Error.Validation(
            "pools.kind_not_allowed",
            "Only an Adjustment (a re-pricing mark) is allowed on a pooled account. Investment and Withdrawal "
            + "move value with no matching unit event, which hands the difference to the other participants; "
            + "record a pool subscription or redemption instead.");

    public static readonly Error MarkMustBeToday =
        Error.Validation(
            "pools.mark_must_be_today",
            "A pooled account can only be marked as of today. The adjustment delta is computed against a "
            + "DATE-BLIND sum of every row on the account, so a back-dated mark corrupts both that NAV and today's balance.");

    public static readonly Error ManualMovementBlocked =
        Error.Validation(
            "pools.manual_movement_blocked",
            "Manual income/expense rows cannot be added to a pooled account - they would re-price the outside "
            + "investors with no unit event. Use a pool subscription, redemption or distribution.");

    public static readonly Error TransferBlocked =
        Error.Validation(
            "pools.transfer_blocked",
            "Transfers cannot touch a pooled account - the cash has to move together with units. Use a pool "
            + "subscription (money in) or a pool redemption with a destination account (money out).");

    /// <summary>
    /// A loan's disbursement and payment legs are transfer-flagged rows written
    /// inline by the loans slice, so <see cref="TransferBlocked"/> never sees
    /// them. They are the worst case of all: the cash moves with no unit event
    /// AND <c>LoanExternalClaimSource</c> books the matching claim, so net worth
    /// moves twice while the friends' fraction quietly takes a slice of borrowed
    /// money.
    /// </summary>
    public static readonly Error LoanMovementBlocked =
        Error.Validation(
            "pools.loan_movement_blocked",
            "A loan cannot be disbursed into or repaid from a pooled account - the cash would move with no "
            + "unit event, handing the outside investors a share of borrowed money. Settle the loan through "
            + "a non-pooled account.");

    public static readonly Error ImportBlocked =
        Error.Validation(
            "pools.import_blocked",
            "Statement rows cannot be imported into a pooled account - each one would silently re-price the "
            + "outside investors. Record the movement through the pool instead.");

    public static readonly Error DeleteRepricesUnits =
        Error.Validation(
            "pools.delete_reprices_units",
            "That transaction underpins units already issued to a third party: it is either linked to a unit "
            + "event or dated on/before the pool's latest one. Delete the unit event instead.");

    public static readonly Error AccountArchiveBlocked =
        Error.Validation(
            "pools.account_archive_blocked",
            "This account backs a pool whose outside participants still hold units. Redeem them and archive "
            + "the pool first.");

    /// <summary>
    /// The demonstrated data-entry slip: two soft-deleted rows on the real
    /// account are each the then-current BALANCE typed into an AMOUNT field. A
    /// pool cash amount that happens to equal the account's own balance to the
    /// cent is almost always that mistake, not a coincidence.
    /// </summary>
    public static readonly Error CashLooksLikeABalance =
        Error.Validation(
            "pools.cash_looks_like_a_balance",
            "The cash amount equals the account's balance to the cent. That is almost always a balance typed "
            + "into an amount field - enter the amount that actually moved.");

    /// <summary>
    /// Both directions of the same collision: linking a goal to an account that
    /// is already pooled, and pooling an account a goal already points at.
    /// <c>SavingsGoal.Saved</c> IS the linked account's derived balance, with no
    /// ownership fraction anywhere in <c>GetGoals</c> / <c>GetGoalDetail</c>, and
    /// savings goals are deliberately NOT on the spec's read-side divergence
    /// list - so the two features must never share an account at all.
    /// </summary>
    public static readonly Error GoalLinkBlocked =
        Error.Validation(
            "pools.goal_link_blocked",
            "A savings goal and a pool cannot share an account - the goal's progress is that account's "
            + "balance, which includes the other participants' money. Unlink the goal first, or use a "
            + "different account.");
}
