using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Direction)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(l => l.Counterparty)
            .HasMaxLength(Loan.CounterpartyMaxLength)
            .IsRequired();

        builder.Property(l => l.LoanDate).IsRequired();

        builder.Property(l => l.Notes).HasMaxLength(Loan.NotesMaxLength);

        builder.Property(l => l.DisbursementTransactionId);

        builder.Property(l => l.IsArchived).HasDefaultValue(false).IsRequired();
        builder.Property(l => l.CreatedAt);
        builder.Property(l => l.UpdatedAt);

        builder.ComplexProperty(l => l.Principal, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("principal_value")
                .HasColumnType("numeric(18,2)")
                .IsRequired();

            money.Property(m => m.Currency)
                .HasColumnName("principal_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        // The disbursement link is bookkeeping, not ownership: if the
        // transaction row is ever hard-deleted (e.g. a backup-restore wipe),
        // the loan survives with the link nulled. Soft deletes never trigger
        // this — the domain event handler clears the link instead.
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(l => l.DisbursementTransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        // Hide archived loans from default queries. The archive handler uses
        // IgnoreQueryFilters() so the same command can re-archive idempotently.
        builder.HasQueryFilter(l => !l.IsArchived);
    }
}
