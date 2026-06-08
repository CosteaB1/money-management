using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Imports;

public interface ICategorySuggester
{
    Task<CategorySuggestion?> SuggestAsync(
        string description,
        TransactionDirection direction,
        CancellationToken cancellationToken);
}

public sealed record CategorySuggestion(Guid Id, string Name);
