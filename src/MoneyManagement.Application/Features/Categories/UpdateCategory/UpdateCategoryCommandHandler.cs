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

        Result updateResult = category.Update(command.Name, command.Flow, command.Color);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
