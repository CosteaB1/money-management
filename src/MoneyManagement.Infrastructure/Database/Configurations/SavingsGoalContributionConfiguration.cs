using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.SavingsGoals;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class SavingsGoalContributionConfiguration : IEntityTypeConfiguration<SavingsGoalContribution>
{
    public void Configure(EntityTypeBuilder<SavingsGoalContribution> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.GoalId).IsRequired();

        builder.Property(c => c.OccurredOn).IsRequired();

        builder.Property(c => c.Notes)
            .HasMaxLength(SavingsGoalContribution.NotesMaxLength);

        builder.Property(c => c.CreatedAt);
        builder.Property(c => c.UpdatedAt);

        builder.ComplexProperty(c => c.Amount, money =>
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

        // Hard-delete the contribution rows when the parent goal is removed.
        // Archiving sets is_archived = true and leaves the rows alone (the
        // goal entity itself is never hard-deleted by the v1 endpoints), so
        // CASCADE only fires if the user goes around the app to purge a row.
        builder.HasOne<SavingsGoal>()
            .WithMany()
            .HasForeignKey(c => c.GoalId)
            .OnDelete(DeleteBehavior.Cascade);

        // Read-path index: GetGoalDetail pulls contributions for one goal in
        // descending date order, so a composite index covers the common query.
        builder.HasIndex(c => new { c.GoalId, c.OccurredOn })
            .IsDescending(false, true)
            .HasDatabaseName("ix_savings_goal_contributions_goal_id_occurred_on");
    }
}
