using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Accounts;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .HasMaxLength(Account.NameMaxLength)
            .IsRequired();

        // Store enum as its name rather than its integer value.
        builder.Property(a => a.Type)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(a => a.OpeningDate);
        builder.Property(a => a.IsArchived);
        builder.Property(a => a.Notes).HasMaxLength(1_000);
        builder.Property(a => a.CreatedAt);
        builder.Property(a => a.UpdatedAt);

        // Money is a value object (record struct); ComplexProperty stores it
        // as inline columns without giving it an identity of its own.
        builder.ComplexProperty(a => a.Balance, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("balance_amount")
                .HasColumnType("numeric(18,2)")
                .IsRequired();

            money.Property(m => m.Currency)
                .HasColumnName("balance_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        builder.HasIndex(a => a.Name);
    }
}
