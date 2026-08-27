using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Loans.GetLoans;

internal sealed class GetLoansQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : IQueryHandler<GetLoansQuery, IReadOnlyList<LoanDto>>
{
    public async Task<Result<IReadOnlyList<LoanDto>>> Handle(
        GetLoansQuery query,
        CancellationToken cancellationToken)
    {
        // The is_archived = false global query filter excludes archived loans
        // under EF Core; the explicit predicate is defense-in-depth so unit
        // tests (which bypass model configuration) exercise the same rule.
        // When the caller opts into archived rows, IgnoreQueryFilters drops
        // the global filter and the explicit predicate is skipped — mirrors
        // GetAccountsQueryHandler's IncludeArchived switch (Accounts has no
        // query filter, so only Loans needs the IgnoreQueryFilters half).
        IQueryable<Loan> loansQuery = query.IncludeArchived
            ? db.Loans.IgnoreQueryFilters()
            : db.Loans.Where(l => !l.IsArchived);

        List<Loan> loans = await loansQuery
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

        if (loans.Count == 0)
        {
            return Result.Success<IReadOnlyList<LoanDto>>([]);
        }

        // Single grouped query for per-loan repaid totals and payment counts —
        // no N+1, mirrors GetAccountsQueryHandler's grouped-totals approach.
        Guid[] loanIds = loans.Select(l => l.Id).ToArray();
        var paymentAggregates = await db.LoanPayments
            .Where(p => loanIds.Contains(p.LoanId))
            .GroupBy(p => p.LoanId)
            .Select(g => new
            {
                LoanId = g.Key,
                Total = g.Sum(p => p.Amount.Amount),
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        var repaidByLoan = paymentAggregates.ToDictionary(x => x.LoanId, x => x.Total);
        var countByLoan = paymentAggregates.ToDictionary(x => x.LoanId, x => x.Count);

        // Outstanding is "now-valued" — the latest rate up to today best
        // approximates the reader's perspective; same convention as
        // AccountDto.BalanceMdl.
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var dtos = new List<LoanDto>(loans.Count);

        foreach (Loan loan in loans)
        {
            decimal totalRepaid = repaidByLoan.GetValueOrDefault(loan.Id, 0m);
            int paymentCount = countByLoan.GetValueOrDefault(loan.Id, 0);
            decimal outstanding = loan.Principal.Amount - totalRepaid;

            decimal? outstandingMdl = await fxConverter.ConvertAsync(
                outstanding,
                loan.Principal.Currency,
                ReportingCurrencies.Mdl,
                today,
                cancellationToken);

            dtos.Add(new LoanDto(
                loan.Id,
                loan.Direction,
                loan.Counterparty,
                loan.Principal.Amount,
                loan.Principal.Currency,
                loan.LoanDate,
                totalRepaid,
                outstanding,
                outstandingMdl,
                MissingFxRate: outstandingMdl is null,
                Status: outstanding > 0m ? LoanStatus.Active : LoanStatus.Settled,
                paymentCount,
                loan.Notes,
                loan.IsArchived));
        }

        return Result.Success<IReadOnlyList<LoanDto>>(dtos);
    }
}
