using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Categories;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Categories.GetCategoryPatterns;

internal sealed class GetCategoryPatternsQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetCategoryPatternsQuery, IReadOnlyList<CategoryPatternDto>>
{
    public async Task<Result<IReadOnlyList<CategoryPatternDto>>> Handle(
        GetCategoryPatternsQuery query,
        CancellationToken cancellationToken)
    {
        // Inner join to Categories. Categories have no global query filter, so
        // archived categories are included — an orphaned-looking pattern still
        // surfaces its category name rather than dropping out of the list.
        // Order by the underlying keyword column BEFORE projecting — ordering by
        // a property of the constructed DTO (`OrderBy(dto => dto.Keyword)`) can't
        // be translated by the relational provider (it works under the in-memory
        // test fake, which is why the unit test didn't catch it).
        List<CategoryPatternDto> patterns = await db.CategoryPatterns
            .Join(
                db.Categories,
                p => p.CategoryId,
                c => c.Id,
                (p, c) => new { Pattern = p, CategoryName = c.Name })
            .OrderBy(x => x.Pattern.Keyword)
            .Select(x => new CategoryPatternDto(
                x.Pattern.Id,
                x.Pattern.Keyword,
                x.Pattern.CategoryId,
                x.CategoryName,
                x.Pattern.Source))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<CategoryPatternDto>>(patterns);
    }
}
