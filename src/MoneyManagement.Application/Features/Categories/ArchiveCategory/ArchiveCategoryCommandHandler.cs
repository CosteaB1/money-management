using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Categories.ArchiveCategory;

internal sealed class ArchiveCategoryCommandHandler(IApplicationDbContext db)
    : ICommandHandler<ArchiveCategoryCommand>
{
    public async Task<Result> Handle(ArchiveCategoryCommand command, CancellationToken cancellationToken)
    {
        Category? category = await db.Categories
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);

        if (category is null)
        {
            return Result.Failure(CategoryErrors.NotFound(command.Id));
        }

        category.Archive();
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
