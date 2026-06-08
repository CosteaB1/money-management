using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.FxRates.GetFxRates;

internal sealed class GetFxRatesQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetFxRatesQuery, PagedResult<FxRateDto>>
{
    public async Task<Result<PagedResult<FxRateDto>>> Handle(
        GetFxRatesQuery query,
        CancellationToken cancellationToken)
    {
        int totalCount = await db.FxRates.CountAsync(cancellationToken);
        int pageSize = Math.Clamp(query.PageSize, 1, 100);
        int pageNumber = Math.Max(1, query.PageNumber);

        List<FxRateDto> rates = await db.FxRates
            .OrderByDescending(r => r.AsOf)
            .ThenBy(r => r.FromCurrency)
            .ThenBy(r => r.ToCurrency)
            .ThenBy(r => r.Source)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new FxRateDto(
                r.Id,
                r.FromCurrency,
                r.ToCurrency,
                r.Rate,
                r.AsOf,
                r.Source,
                r.CreatedAt,
                r.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<FxRateDto>(rates, totalCount, pageNumber, pageSize));
    }
}
