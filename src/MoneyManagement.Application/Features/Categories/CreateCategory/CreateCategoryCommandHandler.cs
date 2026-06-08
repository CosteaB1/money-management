using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Categories.CreateCategory;

internal sealed class CreateCategoryCommandHandler(IApplicationDbContext db)
    : ICommandHandler<CreateCategoryCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        if (command.ParentId is { } parentId)
        {
            bool parentExists = await db.Categories
                .AnyAsync(c => c.Id == parentId, cancellationToken);

            if (!parentExists)
            {
                return Result.Failure<Guid>(CategoryErrors.ParentNotFound(parentId));
            }
        }

        Result<Category> categoryResult = Category.Create(
            command.Name,
            command.Flow,
            command.ParentId,
            command.Color,
            command.Icon);

        if (categoryResult.IsFailure)
        {
            return Result.Failure<Guid>(categoryResult.Error);
        }

        Category category = categoryResult.Value;
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return category.Id;
    }
}
