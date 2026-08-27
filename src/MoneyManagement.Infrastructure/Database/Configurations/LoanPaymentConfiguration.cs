using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class LoanPaymentConfiguration : IEntityTypeConfiguration<LoanPayment>
{
    public void Configure(EntityTypeBuilder<LoanPayment> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.LoanId).IsRequired();

        builder.Property(p => p.OccurredOn).IsRequired();

        builder.Property(p => p.TransactionId);

        builder.Property(p => p.Notes)
            .HasMaxLength(LoanPayment.NotesMaxLength);

        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.UpdatedAt);

        builder.ComplexProperty(p => p.Amount, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("amount_value")
                .HasColumnType("numeric(18,2)")
                .IsRequired();

            money.Property(m => m.Currency)
                .HasColumnName("amount_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        // Payments belong to their loan; loans are only ever archived by the
        // v1 endpoints, so CASCADE fires only if a row is purged out-of-band
        // (e.g. the backup-restore wipe). Mirrors SavingsGoalContribution.
        builder.HasOne<Loan>()
            .WithMany()
            .HasForeignKey(p => p.LoanId)
            .OnDelete(DeleteBehavior.Cascade);

        // Same bookkeeping-link semantics as the loan's disbursement FK: a
        // hard-deleted transaction nulls the link, the payment row survives.
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(p => p.TransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        // Read-path index: GetLoanDetail pulls payments for one loan in
        // descending date order, so a composite index covers the common query
        // (and doubles as the loan_id FK index).
        builder.HasIndex(p => new { p.LoanId, p.OccurredOn })
            .IsDescending(false, true)
            .HasDatabaseName("ix_loan_payments_loan_id_occurred_on");

        // The delete-side lookup (payment by linked transaction) used by the
        // TransactionDeleted event handler.
        builder.HasIndex(p => p.TransactionId)
            .HasDatabaseName("ix_loan_payments_transaction_id");
    }
}
