namespace MoneyManagement.Domain.Common;

/// <summary>Value object representing a monetary amount in a given currency.</summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Currency) && Currency.Length <= 3;
}
