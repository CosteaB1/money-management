using FluentAssertions;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Accounts;

public class AccountTests
{
    private static Money ValidBalance(decimal amount = 100m) => new(amount, "MDL");
    private static readonly DateOnly OpeningDate = new(2026, 1, 1);

    [Fact]
    public void Create_WithValidInput_ReturnsSuccessAndRaisesEvent()
    {
        Result<Account> result = Account.Create(
            "Cash Wallet",
            AccountType.Cash,
            ValidBalance(500m),
            OpeningDate,
            notes: "main wallet");

        result.IsSuccess.Should().BeTrue();
        Account account = result.Value;
        account.Name.Should().Be("Cash Wallet");
        account.Type.Should().Be(AccountType.Cash);
        account.Balance.Should().Be(ValidBalance(500m));
        account.IsArchived.Should().BeFalse();

        account.GetDomainEvents()
            .Should().ContainSingle()
            .Which.Should().BeOfType<AccountCreatedDomainEvent>()
            .Which.AccountId.Should().Be(account.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyName_ReturnsNameRequired(string name)
    {
        Result<Account> result = Account.Create(name, AccountType.Cash, ValidBalance(), OpeningDate, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NameRequired);
    }

    [Fact]
    public void Create_WithNameTooLong_ReturnsNameTooLong()
    {
        string name = new string('a', Account.NameMaxLength + 1);

        Result<Account> result = Account.Create(name, AccountType.Cash, ValidBalance(), OpeningDate, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NameTooLong);
    }

    [Theory]
    [InlineData(AccountType.Cash)]
    [InlineData(AccountType.BankDeposit)]
    public void Create_WithNegativeBalance_OnNonCreditCard_Fails(AccountType type)
    {
        Result<Account> result = Account.Create("Acc", type, ValidBalance(-1m), OpeningDate, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NegativeBalanceForNonCreditCard);
    }

    [Fact]
    public void Create_WithNegativeBalance_OnCreditCard_Succeeds()
    {
        Result<Account> result = Account.Create("Visa", AccountType.CreditCard, ValidBalance(-250m), OpeningDate, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Balance.Amount.Should().Be(-250m);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("RON")]
    public void Create_WithValidNonMdlCurrency_Succeeds(string currency)
    {
        Result<Account> result = Account.Create(
            "Acc",
            AccountType.Cash,
            new Money(100m, currency),
            OpeningDate,
            null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Balance.Currency.Should().Be(currency);
    }

    [Theory]
    [InlineData("eur")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("")]
    public void Create_WithInvalidCurrencyFormat_Fails(string currency)
    {
        Result<Account> result = Account.Create(
            "Acc",
            AccountType.Cash,
            new Money(100m, currency),
            OpeningDate,
            null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.InvalidCurrency);
    }

    [Theory]
    [InlineData(AccountType.Brokerage)]
    [InlineData(AccountType.CryptoExchange)]
    [InlineData(AccountType.P2PLending)]
    [InlineData(AccountType.BankCurrent)]
    public void Create_WithNewAccountType_Succeeds(AccountType type)
    {
        Result<Account> result = Account.Create("Acc", type, ValidBalance(), OpeningDate, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Type.Should().Be(type);
    }

    [Fact]
    public void Update_WithValidInput_UpdatesNameAndNotesAndTrimsName()
    {
        Account account = Account.Create("Old Name", AccountType.Cash, ValidBalance(), OpeningDate, "old notes").Value;

        Result result = account.Update("  New Name  ", "new notes");

        result.IsSuccess.Should().BeTrue();
        account.Name.Should().Be("New Name");
        account.Notes.Should().Be("new notes");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_WithBlankName_ReturnsNameRequired(string name)
    {
        Account account = Account.Create("Old Name", AccountType.Cash, ValidBalance(), OpeningDate, null).Value;

        Result result = account.Update(name, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NameRequired);
        account.Name.Should().Be("Old Name");
    }

    [Fact]
    public void Update_WithNameTooLong_ReturnsNameTooLong()
    {
        Account account = Account.Create("Old Name", AccountType.Cash, ValidBalance(), OpeningDate, null).Value;
        string name = new string('a', Account.NameMaxLength + 1);

        Result result = account.Update(name, null);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NameTooLong);
        account.Name.Should().Be("Old Name");
    }

    [Fact]
    public void Archive_SetsIsArchivedToTrue()
    {
        Account account = Account.Create("Acc", AccountType.Cash, ValidBalance(), OpeningDate, null).Value;

        account.Archive();

        account.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Unarchive_SetsIsArchivedToFalse()
    {
        Account account = Account.Create("Acc", AccountType.Cash, ValidBalance(), OpeningDate, null).Value;
        account.Archive();

        account.Unarchive();

        account.IsArchived.Should().BeFalse();
    }
}
