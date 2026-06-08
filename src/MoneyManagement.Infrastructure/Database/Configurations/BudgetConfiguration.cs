using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Budgets;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.CategoryId).IsRequired();
        builder.Property(b => b.IsArchived).HasDefaultValue(false).IsRequired();
        builder.Property(b => b.CreatedAt);
        builder.Property(b => b.UpdatedAt);

        builder.ComplexProperty(b => b.MonthlyLimit, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("monthly_limit_amount")
                .HasColumnType("numeric(18,2)")
                .IsRequired();

            money.Property(m => m.Currency)
                .HasColumnName("monthly_limit_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        // Filtered unique index — one active budget per category. The DB backs
        // up the handler-side pre-check, closing the race window.
        builder.HasIndex(b => b.CategoryId)
            .IsUnique()
            .HasFilter("is_archived = false")
            .HasDatabaseName("ix_budgets_category_id_active");

        // Hide archived budgets from default queries; lookups in the
        // domain-event handler rely on this filter to find "the active budget
        // for this category" by category id alone.
        builder.HasQueryFilter(b => !b.IsArchived);
    }
}
