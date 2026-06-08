using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Imports;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Infrastructure.Imports;

internal sealed class CategorySuggester(IApplicationDbContext db) : ICategorySuggester
{
    private IReadOnlyList<PatternMatch>? _cache;

    public async Task<CategorySuggestion?> SuggestAsync(
        string description,
        TransactionDirection direction,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        IReadOnlyList<PatternMatch> patterns = await LoadPatternsAsync(cancellationToken);
        if (patterns.Count == 0)
        {
            return null;
        }

        string normalized = description.ToUpperInvariant();

        // The keyword occurring EARLIEST in the description wins — the leading
        // token is typically the transaction type (e.g. "ATM ..." is a
        // withdrawal regardless of the merchant name that follows). Length only
        // breaks ties at the same index: the cache is longest-keyword-first, so
        // when two keywords share the earliest index the longer (more specific)
        // one is encountered first and kept (e.g. "A2A DE INTRARE" over "A2A").
        PatternMatch? best = null;
        int bestIndex = int.MaxValue;
        foreach (PatternMatch pattern in patterns) // longest-keyword-first
        {
            int idx = normalized.IndexOf(pattern.Keyword, StringComparison.Ordinal);
            if (idx < 0 || idx >= bestIndex)
            {
                // No match, or not strictly earlier than the best so far. An
                // equal index keeps the longer keyword already chosen, because
                // the cache is longest-first.
                continue;
            }

            best = pattern;
            bestIndex = idx;
        }

        return best is null ? null : new CategorySuggestion(best.CategoryId, best.CategoryName);
    }

    private async Task<IReadOnlyList<PatternMatch>> LoadPatternsAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        // Join non-archived patterns to their non-archived category. The
        // keyword is stored upper-cased, so a plain Ordinal Contains against an
        // upper-cased description is case-insensitive.
        //
        // Order longest-keyword-first IN MEMORY: ordering by a property of the
        // constructed PatternMatch (`OrderByDescending(m => m.Keyword.Length)`)
        // can't be translated by the relational provider — only the in-memory
        // test fake tolerates it. The pattern set is tiny and cached per
        // instance, so client-side ordering is cheap and translation-safe.
        List<PatternMatch> matches = await db.CategoryPatterns
            .Join(
                db.Categories.Where(c => !c.IsArchived),
                p => p.CategoryId,
                c => c.Id,
                (p, c) => new PatternMatch(p.Keyword, c.Id, c.Name))
            .ToListAsync(cancellationToken);

        _cache = [.. matches.OrderByDescending(m => m.Keyword.Length)];
        return _cache;
    }

    private sealed record PatternMatch(string Keyword, Guid CategoryId, string CategoryName);
}
