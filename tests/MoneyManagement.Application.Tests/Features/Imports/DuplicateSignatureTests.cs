using FluentAssertions;
using MoneyManagement.Application.Features.Imports;

namespace MoneyManagement.Application.Tests.Features.Imports;

/// <summary>
/// <see cref="DuplicateSignature"/> builds a stable hash over
/// (account, date, amount, normalized-description) so re-imported rows are
/// recognised as duplicates. Whitespace/casing in the description must not change
/// the signature.
/// </summary>
public class DuplicateSignatureTests
{
    private static readonly Guid Account = new("00000000-0000-0000-0000-000000000001");
    private static readonly DateOnly Date = new(2026, 5, 1);

    [Fact]
    public void Compute_IsDeterministic_ForSameInputs()
    {
        string a = DuplicateSignature.Compute(Account, Date, 100.50m, "Linella");
        string b = DuplicateSignature.Compute(Account, Date, 100.50m, "Linella");

        a.Should().Be(b);
        a.Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void Compute_IgnoresDescriptionCaseAndWhitespace()
    {
        string canonical = DuplicateSignature.Compute(Account, Date, 100m, "Linella Shop");
        string noisy = DuplicateSignature.Compute(Account, Date, 100m, "  LINELLA   shop  ");

        noisy.Should().Be(canonical);
    }

    [Fact]
    public void Compute_DiffersWhenAmountDiffers()
    {
        string a = DuplicateSignature.Compute(Account, Date, 100m, "Linella");
        string b = DuplicateSignature.Compute(Account, Date, 101m, "Linella");

        a.Should().NotBe(b);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void NormalizeDescription_EmptyOrNull_ReturnsEmptyString(string? description)
    {
        DuplicateSignature.NormalizeDescription(description!).Should().Be(string.Empty);
    }

    [Fact]
    public void NormalizeDescription_TrimsCollapsesAndLowercases()
    {
        DuplicateSignature.NormalizeDescription("  Two   Words  ").Should().Be("two words");
    }
}
