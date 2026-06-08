using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Imports;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Imports;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Imports.ParseStatement;

internal sealed class ParseStatementCommandHandler(
    IApplicationDbContext db,
    IEnumerable<IBankStatementParser> parsers,
    ICategorySuggester categorySuggester,
    ITransferDetector transferDetector) : ICommandHandler<ParseStatementCommand, StatementPreviewDto>
{
    public async Task<Result<StatementPreviewDto>> Handle(
        ParseStatementCommand command,
        CancellationToken cancellationToken)
    {
        bool accountExists = await db.Accounts
            .AnyAsync(a => a.Id == command.AccountId, cancellationToken);

        if (!accountExists)
        {
            return Result.Failure<StatementPreviewDto>(
                Domain.Accounts.AccountErrors.NotFound(command.AccountId));
        }

        string fileHash = Convert.ToHexString(SHA256.HashData(command.FileBytes));

        // PDF content is FlateDecode-compressed, so a raw-byte sniff can't see the bank
        // markers. Hand the bytes to each parser; the parser returns UnsupportedFormat
        // if it doesn't recognize the file, ParseFailed if it does but can't read it.
        IBankStatementParser? parser = null;
        var parseResult = Result.Failure<ParsedStatement>(ImportBatchErrors.UnsupportedFormat);
        foreach (IBankStatementParser candidate in parsers)
        {
            using var probeStream = new MemoryStream(command.FileBytes, writable: false);
            Result<ParsedStatement> attempt = candidate.Parse(probeStream);
            if (attempt.IsSuccess)
            {
                parser = candidate;
                parseResult = attempt;
                break;
            }

            // Stop on a real parse failure (recognized bank, malformed content); keep
            // trying only when the parser declined the file.
            if (attempt.Error != ImportBatchErrors.UnsupportedFormat)
            {
                return Result.Failure<StatementPreviewDto>(attempt.Error);
            }
        }

        if (parser is null)
        {
            return Result.Failure<StatementPreviewDto>(ImportBatchErrors.UnsupportedFormat);
        }

        ParsedStatement parsed = parseResult.Value;

        DateOnly fromDate = parsed.Period.From;
        DateOnly toDate = parsed.Period.To;

        var existing = await db.Transactions
            .Where(t => t.AccountId == command.AccountId
                && t.TransactionDate >= fromDate
                && t.TransactionDate <= toDate)
            .Select(t => new
            {
                t.TransactionDate,
                t.Amount,
                t.Description,
                t.IsTransfer,
                t.Direction,
            })
            .ToListAsync(cancellationToken);

        var existingSignatures = existing
            .Select(t => DuplicateSignature.Compute(command.AccountId, t.TransactionDate, t.Amount.Amount, t.Description))
            .ToHashSet(StringComparer.Ordinal);

        // Transfer-aware dedup: maib statements include both sides of an A2A
        // transfer with different descriptions ("A2A de iesire ..." vs.
        // "A2A de intrare ..."), so signature-based dedup misses the
        // counter-side statement on re-import. Match on (date, amount,
        // direction) when the existing row is flagged as a transfer.
        var existingTransferLegs = existing
            .Where(t => t.IsTransfer)
            .Select(t => (t.TransactionDate, t.Amount.Amount, t.Direction))
            .ToHashSet();

        var previews = new List<ParsedTransactionPreviewDto>(parsed.Rows.Count);
        foreach (ParsedStatementRow row in parsed.Rows)
        {
            string signature = DuplicateSignature.Compute(command.AccountId, row.TransactionDate, row.AmountMdl, row.Description);
            bool isDuplicate = existingSignatures.Contains(signature);

            CategorySuggestion? suggestion = await categorySuggester.SuggestAsync(row.Description, row.Direction, cancellationToken);
            bool isLikelyTransfer = transferDetector.IsLikelyTransfer(row.Description);

            if (!isDuplicate && isLikelyTransfer)
            {
                isDuplicate = existingTransferLegs.Contains((row.TransactionDate, row.AmountMdl, row.Direction));
            }

            previews.Add(new ParsedTransactionPreviewDto(
                row.TransactionDate,
                row.Direction,
                row.AmountMdl,
                row.Description,
                suggestion?.Id,
                suggestion?.Name,
                isDuplicate,
                row.OriginalAmount,
                row.OriginalCurrency,
                isLikelyTransfer));
        }

        // maib groups statement rows by card/section rather than chronologically,
        // and the parser faithfully preserves that PDF order. Sort by date ascending
        // for the preview (and the commit that flows from it). OrderBy is a stable
        // sort, so rows sharing a date keep their original PDF/section relative order.
        var orderedPreviews = previews.OrderBy(p => p.TransactionDate).ToList();

        var preview = new StatementPreviewDto(
            fileHash,
            new PeriodDto(parsed.Period.From.ToString("yyyy-MM-dd"), parsed.Period.To.ToString("yyyy-MM-dd")),
            parser.Source,
            new SummaryDto(
                parsed.Summary.OpeningBalance,
                parsed.Summary.ClosingBalance,
                parsed.Summary.TotalIn,
                parsed.Summary.TotalOut,
                parsed.Summary.TotalFees),
            orderedPreviews);

        return preview;
    }
}
