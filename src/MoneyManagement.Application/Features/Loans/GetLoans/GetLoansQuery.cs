using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Loans.GetLoans;

public sealed record GetLoansQuery(bool IncludeArchived = false)
    : IQuery<IReadOnlyList<LoanDto>>;
