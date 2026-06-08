using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.Categories;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .HasMaxLength(Category.NameMaxLength)
            .IsRequired();

        builder.Property(c => c.Color).HasMaxLength(Category.ColorMaxLength);
        builder.Property(c => c.Icon).HasMaxLength(Category.IconMaxLength);

        builder.Property(c => c.Flow)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(c => c.IsArchived);
        builder.Property(c => c.CreatedAt);
        builder.Property(c => c.UpdatedAt);

        builder
            .HasOne<Category>()
            .WithMany()
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.Name);
    }
}
