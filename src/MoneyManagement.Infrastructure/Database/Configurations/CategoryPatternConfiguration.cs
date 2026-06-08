using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Categories;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class CategoryPatternConfiguration : IEntityTypeConfiguration<CategoryPattern>
{
    public void Configure(EntityTypeBuilder<CategoryPattern> builder)
    {
        builder.ToTable("category_patterns");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Keyword)
            .HasMaxLength(CategoryPattern.KeywordMaxLength)
            .IsRequired();

        builder.HasIndex(p => p.Keyword)
            .IsUnique()
            .HasDatabaseName("ix_category_patterns_keyword");

        builder.Property(p => p.Source)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.UpdatedAt);

        builder
            .HasOne<Category>()
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
