namespace MoneyManagement.Domain.Loans;

/// <summary>
/// Whose money left whose pocket at loan inception.
/// <see cref="Given"/> — the user lent money to someone (they owe the user);
/// <see cref="Received"/> — the user borrowed money (the user owes them).
/// </summary>
public enum LoanDirection
{
    Given = 0,
    Received = 1,
}
