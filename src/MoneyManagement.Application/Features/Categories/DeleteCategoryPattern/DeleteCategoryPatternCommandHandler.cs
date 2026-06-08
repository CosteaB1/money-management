using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Categories.DeleteCategoryPattern;

/// <summary>
/// Hard-deletes a category pattern row. Patterns are not soft-deleted — they are
/// disposable matching rules, so removal is permanent.
/// </summary>
internal sealed class DeleteCategoryPatternCommandHandler(IApplicationDbContext db)
    : ICommandHandler<DeleteCategoryPatternCommand>
{
    public async Task<Result> Handle(DeleteCategoryPatternCommand command, CancellationToken cancellationToken)
    {
        CategoryPattern? pattern = await db.CategoryPatterns
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);

        if (pattern is null)
        {
            return Result.Failure(CategoryErrors.PatternNotFound(command.Id));
        }

        db.CategoryPatterns.Remove(pattern);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
