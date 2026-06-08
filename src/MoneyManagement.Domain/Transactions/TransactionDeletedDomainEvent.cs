using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Transactions;

/// <summary>
/// Raised whenever a <see cref="Transaction"/> is soft-deleted via
/// <see cref="Transaction.MarkDeleted"/>. Mirrors
/// <see cref="TransactionCreatedDomainEvent"/> so budget-period maintenance
/// can apply the inverse update symmetrically. As with the create event,
/// <see cref="AmountMdl"/> is nullable — consumers MUST handle the no-FX-rate
/// case (no implicit 1:1 fallback).
/// </summary>
public sealed record TransactionDeletedDomainEvent(
    Guid TransactionId,
    Guid? CategoryId,
    DateOnly TransactionDate,
    decimal? AmountMdl,
    TransactionDirection Direction,
    bool IsTransfer,
    bool IsAdjustment) : IDomainEvent;
