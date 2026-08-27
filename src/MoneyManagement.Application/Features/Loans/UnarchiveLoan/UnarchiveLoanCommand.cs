using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Loans.UnarchiveLoan;

public sealed record UnarchiveLoanCommand(Guid Id) : ICommand;
