using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Imports;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.AccountId).IsRequired();

        builder.Property(b => b.FileName)
            .HasMaxLength(ImportBatch.FileNameMaxLength)
            .IsRequired();

        builder.Property(b => b.FileHash)
            .HasMaxLength(ImportBatch.FileHashMaxLength)
            .IsRequired();

        builder.Property(b => b.BankSource)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(b => b.ImportedAt).IsRequired();
        builder.Property(b => b.ImportedCount);
        builder.Property(b => b.SkippedDuplicateCount);
        builder.Property(b => b.CreatedAt);
        builder.Property(b => b.UpdatedAt);

        builder.HasIndex(b => new { b.AccountId, b.FileHash });
    }
}
