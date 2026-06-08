using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Transactions;

/// <summary>
/// Raised whenever a <see cref="Transaction"/> is created (the
/// <see cref="Transaction.Create"/> factory raises it before returning).
/// Consumers MUST be ready for the no-FX-rate case where
/// <see cref="AmountMdl"/> is <c>null</c> — the calling handler converts via
/// <see cref="MoneyManagement.Application.Abstractions.FxRates.IFxConverter"/>
/// at the transaction's own date, and there's no implicit 1:1 fallback.
/// </summary>
public sealed record TransactionCreatedDomainEvent(
    Guid TransactionId,
    Guid? CategoryId,
    DateOnly TransactionDate,
    decimal? AmountMdl,
    TransactionDirection Direction,
    bool IsTransfer,
    bool IsAdjustment) : IDomainEvent;
