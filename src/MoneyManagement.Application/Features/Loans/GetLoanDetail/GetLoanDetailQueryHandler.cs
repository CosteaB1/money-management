using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Loans.GetLoanDetail;

internal sealed class GetLoanDetailQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : IQueryHandler<GetLoanDetailQuery, LoanDetailDto>
{
    public async Task<Result<LoanDetailDto>> Handle(
        GetLoanDetailQuery query,
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters so archived loans stay reachable here — the loan
        // detail page is the user's drill-in for both active and archived
        // loans, same convention as the savings-goal detail.
        Loan? loan = await db.Loans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == query.Id, cancellationToken);

        if (loan is null)
        {
            return Result.Failure<LoanDetailDto>(LoanErrors.NotFound(query.Id));
        }

        List<LoanPayment> payments = await db.LoanPayments
            .Where(p => p.LoanId == loan.Id)
            .ToListAsync(cancellationToken);

        // Newest-first; CreatedAt breaks same-day ties so the most recently
        // recorded payment leads.
        payments = payments
            .OrderByDescending(p => p.OccurredOn)
            .ThenByDescending(p => p.CreatedAt)
            .ToList();

        // Resolve every referenced transaction (payment links + disbursement)
        // to its account in two batched lookups — no N+1. Null-safe end to
        // end: a TransactionId may be null, or dangle if the row was
        // soft-deleted out-of-band; unresolved links surface as null
        // account fields rather than failing the page.
        var transactionIds = payments
            .Where(p => p.TransactionId is not null)
            .Select(p => p.TransactionId!.Value)
            .ToList();

        if (loan.DisbursementTransactionId is Guid disbursementTxId)
        {
            transactionIds.Add(disbursementTxId);
        }

        Dictionary<Guid, Guid> accountIdByTransactionId = [];
        Dictionary<Guid, string> accountNameById = [];

        if (transactionIds.Count > 0)
        {
            var transactionRows = await db.Transactions
                .Where(t => !t.IsDeleted && transactionIds.Contains(t.Id))
                .Select(t => new { t.Id, t.AccountId })
                .ToListAsync(cancellationToken);

            accountIdByTransactionId = transactionRows.ToDictionary(t => t.Id, t => t.AccountId);

            Guid[] accountIds = transactionRows.Select(t => t.AccountId).Distinct().ToArray();
            if (accountIds.Length > 0)
            {
                // IgnoreQueryFilters (if any applies) + no archive predicate:
                // an archived account's name should still label historical
                // movements.
                var accountRows = await db.Accounts
                    .IgnoreQueryFilters()
                    .Where(a => accountIds.Contains(a.Id))
                    .Select(a => new { a.Id, a.Name })
                    .ToListAsync(cancellationToken);

                accountNameById = accountRows.ToDictionary(a => a.Id, a => a.Name);
            }
        }

        var paymentDtos = new List<LoanPaymentDto>(payments.Count);
        decimal totalRepaid = 0m;

        foreach (LoanPayment payment in payments)
        {
            totalRepaid += payment.Amount.Amount;

            Guid? accountId = null;
            string? accountName = null;
            if (payment.TransactionId is Guid txId
                && accountIdByTransactionId.TryGetValue(txId, out Guid resolvedAccountId))
            {
                accountId = resolvedAccountId;
                accountName = accountNameById.GetValueOrDefault(resolvedAccountId);
            }

            paymentDtos.Add(new LoanPaymentDto(
                payment.Id,
                payment.Amount.Amount,
                payment.Amount.Currency,
                payment.OccurredOn,
                payment.TransactionId,
                accountId,
                accountName,
                payment.Notes));
        }

        Guid? disbursementAccountId = null;
        string? disbursementAccountName = null;
        if (loan.DisbursementTransactionId is Guid dtxId
            && accountIdByTransactionId.TryGetValue(dtxId, out Guid resolvedDisbursementAccountId))
        {
            disbursementAccountId = resolvedDisbursementAccountId;
            disbursementAccountName = accountNameById.GetValueOrDefault(resolvedDisbursementAccountId);
        }

        decimal outstanding = loan.Principal.Amount - totalRepaid;

        var today = DateOnly.FromDateTime(clock.UtcNow);
        decimal? outstandingMdl = await fxConverter.ConvertAsync(
            outstanding,
            loan.Principal.Currency,
            ReportingCurrencies.Mdl,
            today,
            cancellationToken);

        var dto = new LoanDetailDto(
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
            PaymentCount: paymentDtos.Count,
            loan.Notes,
            loan.IsArchived,
            CreatedOn: loan.CreatedAt,
            loan.DisbursementTransactionId,
            disbursementAccountId,
            disbursementAccountName,
            paymentDtos);

        return Result.Success(dto);
    }
}
