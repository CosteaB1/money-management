using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Imports;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.Infrastructure.Imports;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Imports;

public class CategorySuggesterTests
{
    private static Category NewCategory(Guid id, string name, CategoryFlow flow)
    {
        Result<Category> result = Category.Create(name, flow);
        result.IsSuccess.Should().BeTrue();
        SetEntityId(result.Value, id);
        return result.Value;
    }

    private static CategoryPattern NewPattern(string keyword, Guid categoryId)
    {
        Result<CategoryPattern> result =
            CategoryPattern.Create(keyword, categoryId, CategoryPatternSource.Seeded);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task SuggestAsync_KeywordContainedInDescription_ReturnsMatchingCategory()
    {
        Guid groceriesId = new("00000000-0000-0000-0000-000000000001");
        Category groceries = NewCategory(groceriesId, "Groceries", CategoryFlow.Expense);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [groceries],
            categoryPatterns: [NewPattern("LINELLA", groceriesId)]);

        var suggester = new CategorySuggester(db);

        CategorySuggestion? suggestion = await suggester.SuggestAsync(
            "Plata cu cardul Linella mun. Chisinau",
            TransactionDirection.Expense,
            CancellationToken.None);

        suggestion.Should().NotBeNull();
        suggestion!.Id.Should().Be(groceriesId);
        suggestion.Name.Should().Be("Groceries");
    }

    [Fact]
    public async Task SuggestAsync_NoKeywordMatches_ReturnsNull()
    {
        Guid groceriesId = new("00000000-0000-0000-0000-000000000001");
        Category groceries = NewCategory(groceriesId, "Groceries", CategoryFlow.Expense);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [groceries],
            categoryPatterns: [NewPattern("LINELLA", groceriesId)]);

        var suggester = new CategorySuggester(db);

        CategorySuggestion? suggestion = await suggester.SuggestAsync(
            "Some unrelated merchant",
            TransactionDirection.Expense,
            CancellationToken.None);

        suggestion.Should().BeNull();
    }

    [Fact]
    public async Task SuggestAsync_MultipleKeywordsMatch_LongestKeywordWins()
    {
        Guid transfersId = new("00000000-0000-0000-0000-000000000008");
        Guid otherId = new("00000000-0000-0000-0000-000000000009");
        Category transfers = NewCategory(transfersId, "Transfers", CategoryFlow.Both);
        Category other = NewCategory(otherId, "Other expenses", CategoryFlow.Expense);

        // A short keyword on "Other" and a longer, more specific keyword on
        // "Transfers". The description contains both; the longer one must win.
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [transfers, other],
            categoryPatterns:
            [
                NewPattern("A2A", otherId),
                NewPattern("A2A DE INTRARE", transfersId),
            ]);

        var suggester = new CategorySuggester(db);

        CategorySuggestion? suggestion = await suggester.SuggestAsync(
            "A2A de intrare pe cardul 999999***0000",
            TransactionDirection.Income,
            CancellationToken.None);

        suggestion.Should().NotBeNull();
        suggestion!.Id.Should().Be(transfersId);
        suggestion.Name.Should().Be("Transfers");
    }

    [Fact]
    public async Task SuggestAsync_KeywordsAtDifferentPositions_EarliestMatchWins()
    {
        Guid withdrawalId = new("00000000-0000-0000-0000-000000000010");
        Guid groceriesId = new("00000000-0000-0000-0000-000000000001");
        Category withdrawal = NewCategory(withdrawalId, "Withdrawal", CategoryFlow.Both);
        Category groceries = NewCategory(groceriesId, "Groceries", CategoryFlow.Expense);

        // "ATM" appears at index 0 (the leading transaction-type token);
        // "LINELLA" is a longer keyword but appears later. The earlier keyword
        // must win even though it is shorter.
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [withdrawal, groceries],
            categoryPatterns:
            [
                NewPattern("ATM", withdrawalId),
                NewPattern("LINELLA", groceriesId),
            ]);

        var suggester = new CategorySuggester(db);

        CategorySuggestion? suggestion = await suggester.SuggestAsync(
            "ATM MAIB LINELLA MOSILOR",
            TransactionDirection.Expense,
            CancellationToken.None);

        suggestion.Should().NotBeNull();
        suggestion!.Id.Should().Be(withdrawalId);
        suggestion.Name.Should().Be("Withdrawal");
    }

    [Fact]
    public async Task SuggestAsync_MatchIsCaseInsensitive()
    {
        Guid groceriesId = new("00000000-0000-0000-0000-000000000001");
        Category groceries = NewCategory(groceriesId, "Groceries", CategoryFlow.Expense);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [groceries],
            // Keyword stored upper-cased by Create; description is lower-case.
            categoryPatterns: [NewPattern("felicia", groceriesId)]);

        var suggester = new CategorySuggester(db);

        CategorySuggestion? suggestion = await suggester.SuggestAsync(
            "plata felicia farm",
            TransactionDirection.Expense,
            CancellationToken.None);

        suggestion.Should().NotBeNull();
        suggestion!.Id.Should().Be(groceriesId);
    }

    [Fact]
    public async Task SuggestAsync_PatternForArchivedCategory_IsIgnored()
    {
        Guid groceriesId = new("00000000-0000-0000-0000-000000000001");
        Category groceries = NewCategory(groceriesId, "Groceries", CategoryFlow.Expense);
        groceries.Archive();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [groceries],
            categoryPatterns: [NewPattern("LINELLA", groceriesId)]);

        var suggester = new CategorySuggester(db);

        CategorySuggestion? suggestion = await suggester.SuggestAsync(
            "Plata cu cardul Linella",
            TransactionDirection.Expense,
            CancellationToken.None);

        suggestion.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SuggestAsync_BlankDescription_ReturnsNull(string description)
    {
        Guid groceriesId = new("00000000-0000-0000-0000-000000000001");
        Category groceries = NewCategory(groceriesId, "Groceries", CategoryFlow.Expense);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [groceries],
            categoryPatterns: [NewPattern("LINELLA", groceriesId)]);

        var suggester = new CategorySuggester(db);

        CategorySuggestion? suggestion = await suggester.SuggestAsync(
            description,
            TransactionDirection.Expense,
            CancellationToken.None);

        suggestion.Should().BeNull();
    }

    [Fact]
    public async Task SuggestAsync_SecondCall_ReusesCachedPatterns()
    {
        Guid groceriesId = new("00000000-0000-0000-0000-000000000001");
        Category groceries = NewCategory(groceriesId, "Groceries", CategoryFlow.Expense);

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            categories: [groceries],
            categoryPatterns: [NewPattern("LINELLA", groceriesId)]);

        var suggester = new CategorySuggester(db);

        // First call loads + caches the patterns; the second call must take the
        // cache-hit early return (`_cache is not null`) and still match.
        CategorySuggestion? first = await suggester.SuggestAsync(
            "Linella shop", TransactionDirection.Expense, CancellationToken.None);
        CategorySuggestion? second = await suggester.SuggestAsync(
            "Another Linella visit", TransactionDirection.Expense, CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second!.Id.Should().Be(groceriesId);
    }

    private static void SetEntityId(Category category, Guid id) =>
        typeof(Entity)
            .GetProperty(nameof(Entity.Id))!
            .SetValue(category, id);
}
