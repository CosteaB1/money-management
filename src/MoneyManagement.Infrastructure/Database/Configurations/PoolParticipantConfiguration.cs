using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Pools;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class PoolParticipantConfiguration : IEntityTypeConfiguration<PoolParticipant>
{
    public void Configure(EntityTypeBuilder<PoolParticipant> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PoolId).IsRequired();

        builder.Property(p => p.Name)
            .HasMaxLength(PoolParticipant.NameMaxLength)
            .IsRequired();

        builder.Property(p => p.IsOwner).IsRequired();

        builder.Property(p => p.JoinedOn)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.IsArchived).HasDefaultValue(false).IsRequired();
        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.UpdatedAt);

        // Participants belong to their pool. CASCADE mirrors LoanPayment ->
        // Loan: pools are archived, never deleted, by the endpoints, so this
        // only fires when a row is purged out-of-band (the backup-restore wipe).
        builder.HasOne<Pool>()
            .WithMany()
            .HasForeignKey(p => p.PoolId)
            .OnDelete(DeleteBehavior.Cascade);

        // EXACTLY ONE OWNER PER POOL, enforced in the database rather than only
        // in a handler - a second owner row would double-count the user's units
        // and silently inflate their share of the account.
        //
        // The filter deliberately does NOT exclude archived rows: PoolParticipant
        // .Archive() already refuses to archive the owner, and if archiving were
        // ever allowed, a filter on is_archived would let a replacement owner be
        // inserted, quietly reassigning the stake.
        builder.HasIndex(p => p.PoolId)
            .IsUnique()
            .HasFilter("\"is_owner\"")
            .HasDatabaseName("ux_pool_participants_pool_id_owner");

        // Plain lookup index for "all participants of this pool" (the unique
        // index above is partial, so it cannot serve the FK).
        builder.HasIndex(p => new { p.PoolId, p.IsArchived })
            .HasDatabaseName("ix_pool_participants_pool_id_is_archived");

        // NO global query filter here, deliberately. The model rests on the
        // identity sum(participantUnits) == totalUnits, and a filter on
        // is_archived would silently drop rows from that sum. Read handlers
        // exclude archived participants explicitly instead.
    }
}
