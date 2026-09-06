using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.NetWorth;
using MoneyManagement.Domain.Loans;

namespace MoneyManagement.Application.Features.Loans;

/// <summary>
/// Projects personal loans into <see cref="ExternalClaim"/>s so net worth can
/// subtract what the user still owes (and add what is still owed to them).
/// </summary>
internal sealed class LoanExternalClaimSource(IApplicationDbContext db) : IExternalClaimSource
{
    public async Task<IReadOnlyList<ExternalClaim>> GetHistoryAsync(CancellationToken cancellationToken)
    {
        // Two deliberate divergences from the /loans list:
        //
        // 1. IgnoreQueryFilters() keeps ARCHIVED loans in. Archiving is
        //    bookkeeping-only — it never moves money, and the app happily
        //    archives a loan with outstanding > 0. A debt hidden from the UI is
        //    still a debt, so the /loans summary tiles (which exclude archived)
        //    and net worth are correctly allowed to disagree here.
        //
        // 2. Only ACCOUNT-LINKED loans qualify. An unlinked loan never touched
        //    a tracked balance, so its cash isn't in gross assets; folding in
        //    the obligation anyway would push net worth wrong in the opposite
        //    direction. DisbursementTransactionId is nulled when the
        //    disbursement transaction is soft-deleted, which correctly retires
        //    the claim along with the balance it created.
        var loans = await db.Loans
            .IgnoreQueryFilters()
            .Where(l => l.DisbursementTransactionId != null)
            .Select(l => new
            {
                l.Id,
                l.Direction,
                PrincipalValue = l.Principal.Amount,
                PrincipalCurrency = l.Principal.Currency,
                l.LoanDate,
            })
            .ToListAsync(cancellationToken);

        if (loans.Count == 0)
        {
            return [];
        }

        // Single grouped fetch for the whole payment history — no N+1, and the
        // per-date slicing happens in memory (see IExternalClaimSource).
        Guid[] loanIds = [.. loans.Select(l => l.Id)];
        var payments = await db.LoanPayments
            .Where(p => loanIds.Contains(p.LoanId))
            .Select(p => new
            {
                p.LoanId,
                AmountValue = p.Amount.Amount,
                p.OccurredOn,
            })
            .ToListAsync(cancellationToken);

        var settlementsByLoan = payments
            .GroupBy(p => p.LoanId)
            .ToDictionary(
                g => g.Key,
                IReadOnlyList<ExternalClaimSettlement> (g) =>
                    [.. g.Select(p => new ExternalClaimSettlement(p.AmountValue, p.OccurredOn))]);

        var claims = new List<ExternalClaim>(loans.Count);
        foreach (var loan in loans)
        {
            claims.Add(new ExternalClaim(
                loan.PrincipalValue,
                loan.PrincipalCurrency,
                loan.LoanDate,
                // Received = the user borrowed = the user owes it back.
                // Given = the user lent = the counterparty owes the user.
                loan.Direction == LoanDirection.Received
                    ? ExternalClaimSide.ReducesNetWorth
                    : ExternalClaimSide.IncreasesNetWorth,
                settlementsByLoan.GetValueOrDefault(loan.Id, [])));
        }

        return claims;
    }
}
