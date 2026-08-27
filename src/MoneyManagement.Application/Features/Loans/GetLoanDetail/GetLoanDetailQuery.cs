using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Loans.GetLoanDetail;

public sealed record GetLoanDetailQuery(Guid Id) : IQuery<LoanDetailDto>;
