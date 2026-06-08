using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Common;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Transactions.GetTransactions;

public sealed record GetTransactionsQuery(
    Guid? AccountId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? CategoryId = null,
    TransactionDirection? Direction = null,
    bool? IsTransfer = null,
    bool? IsAdjustment = null,
    int PageNumber = 1,
    int PageSize = 25) : IQuery<PagedResult<TransactionDto>>;
