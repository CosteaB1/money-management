using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Loans.ArchiveLoan;

public sealed record ArchiveLoanCommand(Guid Id) : ICommand;
