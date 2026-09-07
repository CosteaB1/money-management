using FluentAssertions;
using MoneyManagement.Domain.Pools;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Pools;

public class PoolTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);
    private static readonly DateOnly Inception = new(2026, 4, 1);
    private static readonly Guid AccountId = Guid.CreateVersion7();

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private static IDateTimeProvider Clock() => new FixedClock(FixedNow);

    private static Result<Pool> Create(
        Guid? accountId = null,
        string name = "Binance pool",
        string currency = "USD",
        string accountCurrency = "USD",
        DateOnly? inceptionDate = null,
        string? notes = null) =>
        Pool.Create(
            accountId ?? AccountId,
            name,
            currency,
            accountCurrency,
            inceptionDate ?? Inception,
            notes,
            Clock());

    [Fact]
    public void Create_ValidPool_Succeeds()
    {
        Result<Pool> result = Create(notes: "Two friends, USD 1,000 each");

        result.IsSuccess.Should().BeTrue();
        Pool pool = result.Value;
        pool.Id.Should().NotBe(Guid.Empty);
        pool.AccountId.Should().Be(AccountId);
        pool.Name.Should().Be("Binance pool");
        pool.Currency.Should().Be("USD");
        pool.InceptionDate.Should().Be(Inception);
        pool.Notes.Should().Be("Two friends, USD 1,000 each");
        pool.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Create_EmptyAccountId_ReturnsAccountRequired()
    {
        // The account is explicit and immutable - never inferred from the
        // account's type, because Bybit is also CryptoExchange/USD.
        Result<Pool> result = Create(accountId: Guid.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.AccountRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_ReturnsNameRequired(string name)
    {
        Result<Pool> result = Create(name: name);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.NameRequired);
    }

    [Fact]
    public void Create_NameOver100Chars_ReturnsNameTooLong()
    {
        Result<Pool> result = Create(name: new string('P', 101));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.NameTooLong);
    }

    [Fact]
    public void Create_TrimsName()
    {
        Result<Pool> result = Create(name: "  Binance pool  ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Binance pool");
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDT")]
    [InlineData("")]
    public void Create_InvalidCurrencyCode_ReturnsInvalidCurrency(string currency)
    {
        Result<Pool> result = Create(currency: currency, accountCurrency: currency);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.InvalidCurrency);
    }

    [Fact]
    public void Create_CurrencyDifferentFromAccount_ReturnsCurrencyMismatch()
    {
        // The pool never does FX - NAV is pure native, so a EUR pool inside a
        // USD account would need a rate on every single NAV.
        Result<Pool> result = Create(currency: "EUR", accountCurrency: "USD");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.CurrencyMismatch);
    }

    [Fact]
    public void Create_InceptionDateInFuture_ReturnsInceptionDateInFuture()
    {
        Result<Pool> result = Create(inceptionDate: Today.AddDays(1));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.InceptionDateInFuture);
    }

    [Fact]
    public void Create_InceptionDateToday_Succeeds()
    {
        Result<Pool> result = Create(inceptionDate: Today);

        result.IsSuccess.Should().BeTrue();
        result.Value.InceptionDate.Should().Be(Today);
    }

    [Fact]
    public void Create_NotesOver500Chars_ReturnsNotesTooLong()
    {
        Result<Pool> result = Create(notes: new string('n', 501));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.NotesTooLong);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_BlankNotes_NormalizesToNull(string? notes)
    {
        Result<Pool> result = Create(notes: notes);

        result.IsSuccess.Should().BeTrue();
        result.Value.Notes.Should().BeNull();
    }

    [Fact]
    public void Update_ChangesNameAndNotesOnly()
    {
        Pool pool = Create().Value;

        Result result = pool.Update("  Renamed pool  ", "  new note  ");

        result.IsSuccess.Should().BeTrue();
        pool.Name.Should().Be("Renamed pool");
        pool.Notes.Should().Be("new note");

        // Everything the unit history is denominated against stays put.
        pool.AccountId.Should().Be(AccountId);
        pool.Currency.Should().Be("USD");
        pool.InceptionDate.Should().Be(Inception);
    }

    [Fact]
    public void Update_BlankName_FailsAndLeavesPoolUnchanged()
    {
        Pool pool = Create(name: "Binance pool", notes: "note").Value;

        Result result = pool.Update("   ", "changed");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.NameRequired);
        pool.Name.Should().Be("Binance pool");
        pool.Notes.Should().Be("note");
    }

    [Fact]
    public void Update_NotesTooLong_FailsAndLeavesPoolUnchanged()
    {
        Pool pool = Create(notes: "note").Value;

        Result result = pool.Update("Renamed", new string('n', 501));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.NotesTooLong);
        pool.Name.Should().Be("Binance pool");
        pool.Notes.Should().Be("note");
    }

    [Fact]
    public void Archive_WithNoOutsideUnits_Succeeds()
    {
        Pool pool = Create().Value;

        Result result = pool.Archive(outsideUnitsOutstanding: 0m);

        result.IsSuccess.Should().BeTrue();
        pool.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Archive_WithOutsideUnitsOutstanding_ReturnsPoolHasOutsideUnits()
    {
        // Archiving reverts the account's owner fraction to 1.0. With a friend
        // still in, that silently reabsorbs their stake into the user's net worth.
        Pool pool = Create().Value;

        Result result = pool.Archive(outsideUnitsOutstanding: 1_000m);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(PoolErrors.PoolHasOutsideUnits);
        pool.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Archive_WithSubQuantumUnitDust_Succeeds()
    {
        // numeric(28,12) cannot even store this, so it is rounding noise from a
        // final redemption, not a stake.
        Pool pool = Create().Value;

        Result result = pool.Archive(outsideUnitsOutstanding: 0.0000000000001m);

        result.IsSuccess.Should().BeTrue();
        pool.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Archive_IsIdempotent()
    {
        Pool pool = Create().Value;
        pool.Archive(0m);

        Result result = pool.Archive(0m);

        result.IsSuccess.Should().BeTrue();
        pool.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Unarchive_IsIdempotent()
    {
        Pool pool = Create().Value;
        pool.Archive(0m);

        pool.Unarchive().IsSuccess.Should().BeTrue();
        pool.IsArchived.Should().BeFalse();

        pool.Unarchive().IsSuccess.Should().BeTrue();
        pool.IsArchived.Should().BeFalse();
    }
}
