using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.SavingsGoals;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class SavingsGoalConfiguration : IEntityTypeConfiguration<SavingsGoal>
{
    public void Configure(EntityTypeBuilder<SavingsGoal> builder)
    {
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name)
            .HasMaxLength(SavingsGoal.NameMaxLength)
            .IsRequired();

        builder.Property(g => g.TargetDate);

        builder.Property(g => g.LinkedAccountId);

        builder.Property(g => g.IsArchived).HasDefaultValue(false).IsRequired();
        builder.Property(g => g.CreatedAt);
        builder.Property(g => g.UpdatedAt);

        builder.ComplexProperty(g => g.TargetAmount, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("target_amount_value")
                .HasColumnType("numeric(18,2)")
                .IsRequired();

            money.Property(m => m.Currency)
                .HasColumnName("target_amount_currency")
                .HasMaxLength(3)
                .IsRequired();
        });

        // EF Core 10's ComplexProperty doesn't model a nullable Money? value
        // object cleanly, so we persist the manual-saved amount as two paired
        // scalar columns and reconstruct the Money? on read inside the domain
        // entity. The pair is set together: both NULL in linked mode, both
        // populated in manual mode (currency is always MDL today).
        builder.Property<decimal?>("ManualSavedAmountValue")
            .HasColumnName("manual_saved_amount_value")
            .HasColumnType("numeric(18,2)");

        builder.Property<string?>("ManualSavedAmountCurrency")
            .HasColumnName("manual_saved_amount_currency")
            .HasMaxLength(3);

        builder.Ignore(g => g.ManualSavedAmount);

        // Restrict so deleting an account that a goal points at is a hard
        // error - the user must unlink the goal (or archive it) first.
        // SET NULL would silently de-link the goal, which is worse for a
        // self-hosted single-user app where the user is also the operator.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(g => g.LinkedAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(g => g.LinkedAccountId)
            .HasDatabaseName("ix_savings_goals_linked_account_id");

        // Hide archived goals from default queries. The archive handler uses
        // IgnoreQueryFilters() so the same command can re-archive idempotently.
        builder.HasQueryFilter(g => !g.IsArchived);
    }
}
