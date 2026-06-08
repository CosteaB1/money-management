using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Common;

namespace MoneyManagement.Application.Features.FxRates.GetFxRates;

public sealed record GetFxRatesQuery(int PageNumber = 1, int PageSize = 25) : IQuery<PagedResult<FxRateDto>>;
