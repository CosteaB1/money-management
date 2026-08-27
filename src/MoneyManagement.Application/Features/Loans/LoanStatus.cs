namespace MoneyManagement.Application.Features.Loans;

/// <summary>
/// Computed lifecycle bucket for a loan projection — never persisted.
/// <see cref="Active"/> while any principal remains outstanding;
/// <see cref="Settled"/> once payments have fully covered the principal.
/// Serialized by name via the API's <c>JsonStringEnumConverter</c>.
/// </summary>
public enum LoanStatus
{
    Active = 0,
    Settled = 1,
}
