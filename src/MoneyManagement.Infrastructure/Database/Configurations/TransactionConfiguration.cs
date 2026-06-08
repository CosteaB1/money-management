using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.AccountId).IsRequired();
        builder.Property(t => t.CategoryId);
        builder.Property(t => t.TransactionDate).IsRequired();

        builder.Property(t => t.Direction)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(t => t.Description)
            .HasMaxLength(Transaction.DescriptionMaxLength)
            .IsRequired();

        builder.Property(t => t.Notes)
            .HasMaxLength(Transaction.NotesMaxLength);

        builder.Property(t => t.OriginalAmount).HasColumnType("numeric(18,2)");
        builder.Property(t => t.OriginalCurrency).HasMaxLength(3);

        builder.Property(t => t.Source)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(t => t.ImportBatchId);

        builder.Property(t => t.IsTransfer)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(t => t.IsAdjustment)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(t => t.CounterAccountId);

        builder.HasOne<Domain.Accounts.Account>()
            .WithMany()
            .HasForeignKey(t => t.CounterAccountId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(t => t.IsDeleted);
        builder.Property(t => t.CreatedAt);
        builder.Property(t => t.UpdatedAt);

        builder.ComplexProperty(t => t.Amount, money =>
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

        builder.HasIndex(t => new { t.AccountId, t.TransactionDate });

        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}
