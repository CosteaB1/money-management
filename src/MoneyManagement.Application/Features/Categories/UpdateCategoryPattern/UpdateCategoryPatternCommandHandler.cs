using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Categories.UpdateCategoryPattern;

internal sealed class UpdateCategoryPatternCommandHandler(IApplicationDbContext db)
    : ICommandHandler<UpdateCategoryPatternCommand>
{
    public async Task<Result> Handle(UpdateCategoryPatternCommand command, CancellationToken cancellationToken)
    {
        CategoryPattern? pattern = await db.CategoryPatterns
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);

        if (pattern is null)
        {
            return Result.Failure(CategoryErrors.PatternNotFound(command.Id));
        }

        bool categoryExists = await db.Categories
            .AnyAsync(c => c.Id == command.CategoryId, cancellationToken);

        if (!categoryExists)
        {
            return Result.Failure(CategoryErrors.NotFound(command.CategoryId));
        }

        // Uniqueness pre-check excludes self so a no-op keyword edit still succeeds.
        string normalized = command.Keyword.Trim().ToUpperInvariant();
        bool keywordExists = await db.CategoryPatterns
            .AnyAsync(p => p.Id != command.Id && p.Keyword == normalized, cancellationToken);

        if (keywordExists)
        {
            return Result.Failure(CategoryErrors.PatternKeywordExists(normalized));
        }

        Result updateResult = pattern.Update(command.Keyword, command.CategoryId);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
