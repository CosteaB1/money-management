using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Budgets;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class BudgetPeriodConfiguration : IEntityTypeConfiguration<BudgetPeriod>
{
    public void Configure(EntityTypeBuilder<BudgetPeriod> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.BudgetId).IsRequired();
        builder.Property(p => p.Year).IsRequired();
        builder.Property(p => p.Month).IsRequired();
        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.UpdatedAt);

        builder.ComplexProperty(p => p.Spent, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("spent_amount")
                .HasColumnType("numeric(18,2)")
                .IsRequired();

            money.Property(m => m.Currency)
                .HasColumnName("spent_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        // Composite uniqueness: one period row per (budget, year, month). The
        // domain-event handler does a find-or-create on this triple per
        // transaction, so the DB constraint catches the rare race where two
        // events for the same month land before either commits.
        builder.HasIndex(p => new { p.BudgetId, p.Year, p.Month })
            .IsUnique()
            .HasDatabaseName("ix_budget_periods_budget_id_year_month");
    }
}
