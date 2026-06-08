using FluentAssertions;
using MoneyManagement.Application.Abstractions.Imports;
using MoneyManagement.Infrastructure.Imports;

namespace MoneyManagement.Application.Tests.Imports;

public class SubstringTransferDetectorTests
{
    private readonly ITransferDetector _detector = new SubstringTransferDetector();

    [Theory]
    [InlineData("A2A de intrare")]
    [InlineData("A2A DE IESIRE")]
    [InlineData("Retragere numerar bancomat")]
    [InlineData("RETRAGERE de pe card")]
    [InlineData("A2A de iesire pe cardul 435696***5875")]
    [InlineData("ATM MAIB REC IALOVENI")]
    public void IsLikelyTransfer_WithInclusionPattern_ReturnsTrue(string description)
    {
        _detector.IsLikelyTransfer(description).Should().BeTrue();
    }

    [Theory]
    [InlineData("Achitare marfa")]
    [InlineData("ACHITARE LINELLA")]
    [InlineData("Plata pentru servicii")]
    [InlineData("Plată comunale")]
    [InlineData("Transfer Plara salariu aprilie 2026")]
    [InlineData("Transfer MIA de iesire de pe contul 22594164829")]
    [InlineData("Transfer Retragere Cashback")]
    // Bank-fee companion rows emitted by MaibStatementParser carry a "Comision: "
    // prefix. They are real spending in the account currency — never a net-zero
    // transfer. The detector keys off whole-token equality, so the "COMISION"
    // token does not match any inclusion. Note: if the parent description itself
    // contains a transfer token (e.g. "Comision: A2A ..."), the fee row WILL be
    // flagged — that's a known edge case tracked separately and acceptable for v1
    // since real fee rows in maib statements come from "MAIB P2P" / merchant
    // descriptions, not A2A/TRANSFER lines.
    [InlineData("Comision: MAIB P2P")]
    public void IsLikelyTransfer_WithExcludedPattern_ReturnsFalse(string description)
    {
        _detector.IsLikelyTransfer(description).Should().BeFalse();
    }

    [Fact]
    public void IsLikelyTransfer_WhenInclusionAndExclusionPresent_ExclusionWins()
    {
        // E.g. "Achitare A2A" would still be a normal payment, not an internal
        // card-to-card transfer.
        _detector.IsLikelyTransfer("Achitare A2A merchant").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Coffee at Linella")]
    [InlineData("MCDONALD CHISINAU")]
    public void IsLikelyTransfer_WithUnrelatedDescription_ReturnsFalse(string description)
    {
        _detector.IsLikelyTransfer(description).Should().BeFalse();
    }

    [Theory]
    [InlineData("Transfer intern")]
    [InlineData("TRANSFER intre carduri")]
    [InlineData("Transfer Salariul pentru iunie")]
    [InlineData("Transfer Plata pentru mai")]
    public void IsLikelyTransfer_BareTransferPrefix_ReturnsFalse(string description)
    {
        // maib stamps the generic word "Transfer" on salary, ordinary payments,
        // and real A2A moves alike, so it is no longer an inclusion signal —
        // only A2A / RETRAGERE / ATM are.
        _detector.IsLikelyTransfer(description).Should().BeFalse();
    }

    [Fact]
    public void IsLikelyTransfer_InflectedExclusion_BeatsInclusion()
    {
        // The definite-article form "Salariul" must still be excluded even when a
        // real inclusion token (A2A) is present — exclusions are prefix-matched.
        _detector.IsLikelyTransfer("A2A Salariul pentru iunie").Should().BeFalse();
    }

    [Fact]
    public void IsLikelyTransfer_DoesNotMatchSubstringInsideWord()
    {
        // Inclusion is whole-token equality, so a longer word that merely
        // contains an inclusion token (e.g. "ATMOSFERA" ⊃ "ATM") must not match.
        _detector.IsLikelyTransfer("ATMOSFERA placuta").Should().BeFalse();
    }
}
