using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Categories.UpdateCategory;

internal sealed class UpdateCategoryCommandHandler(IApplicationDbContext db)
    : ICommandHandler<UpdateCategoryCommand>
{
    public async Task<Result> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        Category? category = await db.Categories
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);

        if (category is null)
        {
            return Result.Failure(CategoryErrors.NotFound(command.Id));
        }

        // Names are unique per flow among active categories; archived ones free the
        // name. The category itself is excluded so a self-match (no rename) passes.
        string normalized = command.Name.Trim().ToUpperInvariant();
        bool duplicate = await db.Categories
            .AnyAsync(
                c => c.Id != command.Id && !c.IsArchived && c.Flow == command.Flow && c.Name.ToUpper() == normalized,
                cancellationToken);

        if (duplicate)
        {
            return Result.Failure(CategoryErrors.DuplicateName(command.Name.Trim(), command.Flow));
        }

        Result updateResult = category.Update(command.Name, command.Flow, command.Color);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
