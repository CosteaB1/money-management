using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MoneyManagement.Domain.FxRates;

namespace MoneyManagement.Infrastructure.Database.Configurations;

internal sealed class FxRateConfiguration : IEntityTypeConfiguration<FxRate>
{
    public void Configure(EntityTypeBuilder<FxRate> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.FromCurrency)
            .HasMaxLength(FxRate.CurrencyLength)
            .IsRequired();

        builder.Property(r => r.ToCurrency)
            .HasMaxLength(FxRate.CurrencyLength)
            .IsRequired();

        // Rates can need more precision than amounts (e.g. 0.054321 BTC->USD).
        builder.Property(r => r.Rate)
            .HasColumnType("numeric(18,6)")
            .IsRequired();

        builder.Property(r => r.AsOf)
            .HasColumnType("date");

        // Enum-as-string so source ("Manual" / "BnmAuto") is human-readable
        // in the table. snake_case naming convention maps property to column.
        // Default = "Manual" so the migration backfills any existing rows with
        // a parseable enum value (an empty-string default would break the
        // enum-as-string read path).
        builder.Property(r => r.Source)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired()
            .HasDefaultValue(FxRateSource.Manual);

        builder.Property(r => r.CreatedAt);
        builder.Property(r => r.UpdatedAt);

        // One rate per (from, to, asOf, source) tuple. Two physical rows for
        // the same (from, to, asOf) triple are allowed when the sources
        // differ — a Manual row can sit alongside a BnmAuto row, and the
        // converter prefers the Manual one. Multiple as-of rows for the
        // same pair+source are intentional - the converter picks the latest <= asOf.
        builder.HasIndex(r => new { r.FromCurrency, r.ToCurrency, r.AsOf, r.Source })
            .IsUnique();
    }
}
