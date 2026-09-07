using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class PoolUnitEventConfiguration : IEntityTypeConfiguration<PoolUnitEvent>
{
    /// <summary>
    /// Units and NAV are stored at this precision, NOT the repo-wide money
    /// <c>numeric(18,2)</c>. See <see cref="PoolUnitEvent"/>: at 2dp a NAV near
    /// 1.0 quantizes about 0.1% per event per investor and the pool's
    /// invariance identity stops holding. This follows
    /// <c>FxRateConfiguration</c>'s reasoning (rates need more precision than
    /// amounts), taken further because units compound across events.
    /// </summary>
    private const string UnitPrecision = "numeric(28,12)";

    private const string MoneyPrecision = "numeric(18,2)";

    public void Configure(EntityTypeBuilder<PoolUnitEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PoolId).IsRequired();
        builder.Property(e => e.ParticipantId).IsRequired();

        // Enum-as-string so the ledger is readable in psql. New table, so no
        // HasDefaultValue backfill is needed (and none is wanted - an empty
        // string would break the enum read path).
        builder.Property(e => e.Kind)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(e => e.OccurredOn)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(e => e.Units)
            .HasColumnType(UnitPrecision)
            .IsRequired();

        builder.Property(e => e.NavPerUnit)
            .HasColumnType(UnitPrecision)
            .IsRequired();

        // Money-shaped: a value a market actually printed, not a compounding
        // ratio.
        builder.Property(e => e.PoolValuePreMoney)
            .HasColumnType(MoneyPrecision)
            .IsRequired();

        // Nullable Money persisted as two paired scalar columns - EF Core does
        // not map a nullable ComplexProperty cleanly, so this mirrors
        // SavingsGoal.ManualSavedAmount. Both NULL for Seed / CostShare /
        // CostRecovery, both populated for the cash kinds.
        builder.Property<decimal?>("CashValue")
            .HasColumnName("cash_value")
            .HasColumnType(MoneyPrecision);

        builder.Property<string?>("CashCurrency")
            .HasColumnName("cash_currency")
            .HasMaxLength(CurrencyCodes.Length);

        builder.Ignore(e => e.Cash);

        // Computed from Kind + Units; nothing to store.
        builder.Ignore(e => e.UnitsDelta);

        // NULL on an unpaid Distribution (and only there) - the close/pay split.
        builder.Property(e => e.SettledOn).HasColumnType("date");

        builder.Property(e => e.MovementTransactionId);

        builder.Property(e => e.Notes).HasMaxLength(PoolUnitEvent.NotesMaxLength);

        builder.Property(e => e.CreatedAt);
        builder.Property(e => e.UpdatedAt);

        builder.HasOne<Pool>()
            .WithMany()
            .HasForeignKey(e => e.PoolId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PoolParticipant>()
            .WithMany()
            .HasForeignKey(e => e.ParticipantId)
            .OnDelete(DeleteBehavior.Cascade);

        // Same bookkeeping-link semantics as Loan.DisbursementTransactionId: a
        // hard-deleted transaction nulls the link and the ledger row survives,
        // because the units really did move even if the money row is gone. Soft
        // deletes never reach here - the domain event handler calls
        // ClearMovementTransaction() instead (which also un-settles a
        // distribution, putting the cash back in play).
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(e => e.MovementTransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        // Read-path index: the register replays one pool's ledger in date
        // order. Doubles as the pool_id FK index.
        builder.HasIndex(e => new { e.PoolId, e.OccurredOn })
            .HasDatabaseName("ix_pool_unit_events_pool_id_occurred_on");

        // Per-participant unit balances (the sum behind the owner fraction).
        builder.HasIndex(e => e.ParticipantId)
            .HasDatabaseName("ix_pool_unit_events_participant_id");

        // The delete-side lookup (event by linked transaction), used by the
        // TransactionDeleted event handler and by the guard that refuses to
        // delete a transaction a unit event depends on.
        builder.HasIndex(e => e.MovementTransactionId)
            .HasDatabaseName("ix_pool_unit_events_movement_transaction_id");
    }
}
