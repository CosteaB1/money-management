using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Transactions.UpdateTransactionNotes;

public sealed record UpdateTransactionNotesCommand(Guid Id, string? Notes) : ICommand;
