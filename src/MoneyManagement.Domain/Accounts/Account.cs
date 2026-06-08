using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Accounts;

public sealed class Account : Entity
{
    public const int NameMaxLength = 100;
    public const int CurrencyLength = CurrencyCodes.Length;

    // EF Core
    private Account() { }

    private Account(
        Guid id,
        string name,
        AccountType type,
        Money balance,
        DateOnly openingDate,
        string? notes) : base(id)
    {
        Name = name;
        Type = type;
        Balance = balance;
        OpeningDate = openingDate;
        Notes = notes;
        IsArchived = false;
    }

    public string Name { get; private set; } = string.Empty;
    public AccountType Type { get; private set; }
    public Money Balance { get; private set; }
    public DateOnly OpeningDate { get; private set; }
    public bool IsArchived { get; private set; }
    public string? Notes { get; private set; }

    public static Result<Account> Create(
        string name,
        AccountType type,
        Money balance,
        DateOnly openingDate,
        string? notes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Account>(AccountErrors.NameRequired);
        }

        if (name.Length > NameMaxLength)
        {
            return Result.Failure<Account>(AccountErrors.NameTooLong);
        }

        if (!CurrencyCodes.IsValidIso(balance.Currency))
        {
            return Result.Failure<Account>(AccountErrors.InvalidCurrency);
        }

        if (balance.Amount < 0 && type != AccountType.CreditCard)
        {
            return Result.Failure<Account>(AccountErrors.NegativeBalanceForNonCreditCard);
        }

        var account = new Account(Guid.CreateVersion7(), name.Trim(), type, balance, openingDate, notes);
        account.Raise(new AccountCreatedDomainEvent(account.Id));
        return account;
    }

    public Result Update(string name, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(AccountErrors.NameRequired);
        }

        if (name.Length > NameMaxLength)
        {
            return Result.Failure(AccountErrors.NameTooLong);
        }

        Name = name.Trim();
        Notes = notes;
        return Result.Success();
    }

    public void Archive() => IsArchived = true;

    public void Unarchive() => IsArchived = false;
}
