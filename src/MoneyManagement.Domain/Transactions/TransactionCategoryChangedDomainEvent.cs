using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Transactions;

/// <summary>
/// Raised whenever a <see cref="Transaction"/>'s category changes via
/// <see cref="Transaction.SetCategory"/>. Carries both the previous and the
/// new <see cref="Guid"/>? so the budget handler can subtract from the old
/// category's period and add to the new one in a single pass. Idempotent
/// no-op calls (old == new) do NOT raise this event — see
/// <see cref="Transaction.SetCategory"/>. <see cref="AmountMdl"/> is nullable
/// for the same reason as <see cref="TransactionCreatedDomainEvent"/>.
/// </summary>
public sealed record TransactionCategoryChangedDomainEvent(
    Guid TransactionId,
    Guid? OldCategoryId,
    Guid? NewCategoryId,
    DateOnly TransactionDate,
    decimal? AmountMdl,
    TransactionDirection Direction,
    bool IsTransfer,
    bool IsAdjustment) : IDomainEvent;
