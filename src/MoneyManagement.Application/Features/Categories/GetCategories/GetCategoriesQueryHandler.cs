using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Categories.GetCategories;

internal sealed class GetCategoriesQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(
        GetCategoriesQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Category> categoriesQuery = db.Categories.AsQueryable();

        if (!query.IncludeArchived)
        {
            categoriesQuery = categoriesQuery.Where(c => !c.IsArchived);
        }

        List<CategoryDto> categories = await categoriesQuery
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id,
                c.Name,
                c.ParentId,
                c.Color,
                c.Icon,
                c.Flow,
                c.IsArchived))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<CategoryDto>>(categories);
    }
}
