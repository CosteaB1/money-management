using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Pools;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class PoolConfiguration : IEntityTypeConfiguration<Pool>
{
    public void Configure(EntityTypeBuilder<Pool> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.AccountId).IsRequired();

        builder.Property(p => p.Name)
            .HasMaxLength(Pool.NameMaxLength)
            .IsRequired();

        builder.Property(p => p.Currency)
            .HasMaxLength(CurrencyCodes.Length)
            .IsRequired();

        builder.Property(p => p.InceptionDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.Notes).HasMaxLength(Pool.NotesMaxLength);

        builder.Property(p => p.IsArchived).HasDefaultValue(false).IsRequired();
        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.UpdatedAt);

        // RESTRICT: a pooled account cannot be deleted out from under its pool.
        // SET NULL / CASCADE would both be worse - the first orphans a unit
        // ledger whose value is defined by that account's balance, the second
        // destroys a third party's stake record along with the account row.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(p => p.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // One pool per account, forever - deliberately NOT filtered on
        // is_archived. An archived pool keeps its account: its unit history
        // still shapes the owner fraction for every past date, so a second pool
        // on the same account would make "the owner's share of this account"
        // ambiguous for the overlapping period.
        builder.HasIndex(p => p.AccountId)
            .IsUnique()
            .HasDatabaseName("ix_pools_account_id");

        // Hide archived pools from default queries, matching Loan/SavingsGoal.
        // IMPORTANT for the read slice: the ownership source MUST call
        // IgnoreQueryFilters(), exactly as LoanExternalClaimSource does. An
        // archived pool's history still applies to the dates it covered, and a
        // filter that silently drops it would rewrite past net-worth points.
        builder.HasQueryFilter(p => !p.IsArchived);
    }
}
