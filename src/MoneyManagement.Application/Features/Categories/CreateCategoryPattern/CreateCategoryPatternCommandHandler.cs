using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Categories.CreateCategoryPattern;

internal sealed class CreateCategoryPatternCommandHandler(IApplicationDbContext db)
    : ICommandHandler<CreateCategoryPatternCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCategoryPatternCommand command, CancellationToken cancellationToken)
    {
        bool categoryExists = await db.Categories
            .AnyAsync(c => c.Id == command.CategoryId, cancellationToken);

        if (!categoryExists)
        {
            return Result.Failure<Guid>(CategoryErrors.NotFound(command.CategoryId));
        }

        // Pre-check uniqueness against the normalized (upper-cased) keyword so the
        // collision message matches what would be persisted.
        string normalized = command.Keyword.Trim().ToUpperInvariant();
        bool keywordExists = await db.CategoryPatterns
            .AnyAsync(p => p.Keyword == normalized, cancellationToken);

        if (keywordExists)
        {
            return Result.Failure<Guid>(CategoryErrors.PatternKeywordExists(normalized));
        }

        Result<CategoryPattern> patternResult = CategoryPattern.Create(
            command.Keyword,
            command.CategoryId,
            CategoryPatternSource.Learned);

        if (patternResult.IsFailure)
        {
            return Result.Failure<Guid>(patternResult.Error);
        }

        CategoryPattern pattern = patternResult.Value;
        db.CategoryPatterns.Add(pattern);
        await db.SaveChangesAsync(cancellationToken);

        return pattern.Id;
    }
}
