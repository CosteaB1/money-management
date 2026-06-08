using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace MoneyManagement.Infrastructure.Imports;

internal sealed partial class MaibStatementParser(ILogger<MaibStatementParser> logger) : IBankStatementParser
{
    private const string BankCodeMarker = "AGRNMD2X";
    private const string PendingSectionMarker = "Tranzacții în procesare";
    private const string EndOfStatementMarker = "Sfârșitul extrasului de cont";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // PdfPig concatenates the statement into one blob; the period appears as
    // `EXTRAS DE CONT01.05.2026-16.05.2026` directly after the section header.
    [GeneratedRegex(@"EXTRAS\s*DE\s*CONT\s*(\d{2}\.\d{2}\.\d{4})\s*-\s*(\d{2}\.\d{2}\.\d{4})")]
    private static partial Regex PeriodRegex();

    [GeneratedRegex(@"Sold inițial:\s*([\d., ]+?)Total", RegexOptions.Singleline)]
    private static partial Regex OpeningBalanceRegex();

    [GeneratedRegex(@"Total ieșiri:\s*([\d., ]+?)Total", RegexOptions.Singleline)]
    private static partial Regex TotalOutRegex();

    [GeneratedRegex(@"Total intrări:\s*([\d., ]+?)Total", RegexOptions.Singleline)]
    private static partial Regex TotalInRegex();

    // maib reports commission as a SEPARATE summary total (label is singular
    // "comision"), NOT folded into `Total ieșiri`. Value looks like `125.00`.
    [GeneratedRegex(@"Total comision:\s*([\d., ]+?)(?:Sold|Total|maib|$)", RegexOptions.Singleline)]
    private static partial Regex TotalFeesRegex();

    // Each row in the statement is anchored by a (transaction-date, processing-date)
    // pair. We split on that pair but only retain the transaction date — processing
    // date is irrelevant for a card account where the money moves on the spot.
    [GeneratedRegex(@"(\d{4}-\d{2}-\d{2})\s+\d{4}-\d{2}-\d{2}")]
    private static partial Regex DatePairRegex();

    // Body of a single row after the date pair is consumed:
    //   1: description  2: signed source-amount  3: currency (any ISO-style 3-letter
    //   uppercase code — MDL/USD/EUR/TRY/RON/GBP/...)  4: tail (numbers + trailing junk)
    // The currency group is intentionally broad: the bank can charge in any FX currency,
    // and silently dropping non-MDL/USD rows hides real transactions. The non-greedy
    // description defers to the FIRST `<number>\s*[A-Z]{3}` match, so 3-letter all-caps
    // tokens that live inside descriptions (SRL, ATM, MIA) — which always appear BEFORE
    // the amount, never directly after a number — won't false-match.
    // The tail is intentionally permissive: between the last row of a section and the
    // next anchor, PdfPig emits the card/account header markers ("999999******0000 #Cardul")
    // which contain asterisks and letters. TokenizeTailNumbers ignores everything that
    // is not a decimal-formatted number, so the markers are harmless.
    [GeneratedRegex(
        @"^(.*?)(-?\d{1,3}(?:[\s ]\d{3})*(?:\.\d{1,2})?)\s*([A-Z]{3})\b(.*)$",
        RegexOptions.Singleline)]
    private static partial Regex RowBodyRegex();

    // Section markers that PdfPig emits between table groups; stripped before parsing
    // so they don't contaminate a row body. The card-number sequence uses 4+ asterisks
    // (e.g. `999999******0000`), distinguishing it from descriptions like
    // `A2A de intrare pe cardul 999999***0000` which only use 3.
    [GeneratedRegex(@"\d+\*{4,}\d+\s*#Cardul")]
    private static partial Regex CardSectionMarkerRegex();

    [GeneratedRegex(@"\d+\s+[A-ZĂÂÎȘȚ][A-ZĂÂÎȘȚ.\s]+#Cont")]
    private static partial Regex AccountSectionMarkerRegex();

    public BankSource Source => BankSource.Maib;

    public Result<ParsedStatement> Parse(Stream pdfStream)
    {
        string allText;
        try
        {
            using var document = PdfDocument.Open(pdfStream);
            var sb = new StringBuilder();
            // page.Text concatenates glyph runs without preserving inter-word gaps, which
            // mangles descriptions like "Retragere Cashback" into "RetrageeCashback".
            // NearestNeighbourWordExtractor uses glyph positions to recover word boundaries.
            foreach (Page page in document.GetPages())
            {
                foreach (Word word in NearestNeighbourWordExtractor.Instance.GetWords(page.Letters))
                {
                    sb.Append(word.Text);
                    sb.Append(' ');
                }
            }

            allText = WhitespaceRegex().Replace(sb.ToString().Trim(), " ");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to open or read PDF as maib statement.");
            return Result.Failure<ParsedStatement>(ImportBatchErrors.UnsupportedFormat);
        }

        return ParseText(allText);
    }

    // Parses the already-extracted statement text. Split out from Parse(Stream)
    // so the format-sniffing / period / amount / row logic can be exercised with
    // crafted text without minting PDFs. Pure: no PDF/IO involvement.
    internal Result<ParsedStatement> ParseText(string allText)
    {
        if (!allText.Contains(BankCodeMarker, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ParsedStatement>(ImportBatchErrors.UnsupportedFormat);
        }

        Match periodMatch = PeriodRegex().Match(allText);
        if (!periodMatch.Success)
        {
            logger.LogWarning("maib statement: could not extract period.");
            return Result.Failure<ParsedStatement>(ImportBatchErrors.ParseFailed);
        }

        var period = new ParsedStatementPeriod(
            DateOnly.ParseExact(periodMatch.Groups[1].Value, "dd.MM.yyyy", Inv),
            DateOnly.ParseExact(periodMatch.Groups[2].Value, "dd.MM.yyyy", Inv));

        decimal opening = ExtractAmount(OpeningBalanceRegex(), allText);
        decimal totalIn = ExtractAmount(TotalInRegex(), allText);
        decimal totalOut = ExtractAmount(TotalOutRegex(), allText);

        // Closing = opening + intrări − ieșiri — the booked balance, which is
        // exactly what the imported account reconciles to (Σ income − Σ expense
        // rows, fees included). We deliberately do NOT read maib's printed
        // end-balances: `Sold final` double-subtracts the commission (already
        // inside `ieșiri`), and `Sold Disponibil` nets out pending authorization
        // holds — neither equals the booked balance across all statements, but
        // this identity always does.
        var summary = new ParsedStatementSummary(
            OpeningBalance: opening,
            ClosingBalance: opening + totalIn - totalOut,
            TotalIn: totalIn,
            TotalOut: totalOut,
            TotalFees: ExtractAmount(TotalFeesRegex(), allText));

        List<ParsedStatementRow> rows = ExtractRows(allText);

        return new ParsedStatement(period, summary, rows);
    }

    private static decimal ExtractAmount(Regex regex, string text)
    {
        Match m = regex.Match(text);
        if (!m.Success)
        {
            return 0m;
        }

        return TryParseAmount(m.Groups[1].Value, out decimal value) ? Math.Abs(value) : 0m;
    }

    private static List<ParsedStatementRow> ExtractRows(string text)
    {
        var rows = new List<ParsedStatementRow>();

        // Cut off the pending section so in-progress rows are never emitted.
        int pendingIdx = text.IndexOf(PendingSectionMarker, StringComparison.Ordinal);
        int endIdx = text.IndexOf(EndOfStatementMarker, StringComparison.Ordinal);
        int relevantEnd = pendingIdx >= 0 ? pendingIdx : (endIdx >= 0 ? endIdx : text.Length);
        string scan = text[..relevantEnd];

        // Strip section headers (`999999000000001 ION POPESCU #Cont`, `999999******0000 #Cardul`)
        // so the row that immediately precedes one of them ends in a clean tail. Replacing
        // with a space preserves byte structure for the date anchors that follow.
        scan = CardSectionMarkerRegex().Replace(scan, " ");
        scan = AccountSectionMarkerRegex().Replace(scan, " ");

        // Each row's body is the slice from one anchor's end to the next anchor's start.
        MatchCollection anchors = DatePairRegex().Matches(scan);
        for (int i = 0; i < anchors.Count; i++)
        {
            Match anchor = anchors[i];
            int bodyStart = anchor.Index + anchor.Length;
            int bodyEnd = i + 1 < anchors.Count ? anchors[i + 1].Index : scan.Length;
            string body = scan[bodyStart..bodyEnd];

            if (!DateOnly.TryParseExact(anchor.Groups[1].Value, "yyyy-MM-dd", Inv, DateTimeStyles.None, out DateOnly txDate))
            {
                continue;
            }

            Match bodyMatch = RowBodyRegex().Match(body);
            if (!bodyMatch.Success)
            {
                continue;
            }

            string description = NormalizeWhitespace(bodyMatch.Groups[1].Value).TrimEnd();
            string sourceAmountToken = bodyMatch.Groups[2].Value;
            string currency = bodyMatch.Groups[3].Value;
            string tail = bodyMatch.Groups[4].Value;

            if (!TryParseAmount(sourceAmountToken, out decimal sourceAmount))
            {
                continue;
            }

            TransactionDirection direction = sourceAmount < 0 ? TransactionDirection.Expense : TransactionDirection.Income;

            decimal? originalAmount = null;
            string? originalCurrency = null;
            if (currency != "MDL")
            {
                originalCurrency = currency;
                originalAmount = Math.Abs(sourceAmount);
            }

            List<decimal> amounts = TokenizeTailNumbers(tail);
            if (amounts.Count == 0)
            {
                continue;
            }

            // Tail layout in PdfPig output: <abs-amount> <running-balance>. The
            // ieșiri/intrări/comision columns appear empty in extracted text, so only
            // the populated MDL amount and the post-tx balance are emitted. Anything
            // that's not a decimal-formatted number (section markers, page footer
            // tokens) is ignored by TokenizeTailNumbers.
            //
            // When the bank charges a commission, the tail expands to three numbers:
            //   <ieșiri-total> <commission> <running-balance>
            // (e.g. `MAIB P2P -17,163.00 ... 10.00 ... 71,283.07`).
            //
            // Critical: maib's `ieșiri` column is principal + fee combined, NOT
            // principal alone. The `comision` column is the fee portion (an
            // informational breakdown of the same money), not an extra debit.
            // The running balance subtracts only the combined `ieșiri` amount.
            // So we split the single statement row into two ParsedStatementRow:
            //   primary  = ieșiri − comision   (the user's real transfer)
            //   fee row  = comision            (Bank Fees, for audit)
            // Sum of the two equals `ieșiri`, matching the bank's actual deduction.
            decimal mdl = amounts[0];
            if (mdl <= 0)
            {
                continue;
            }

            bool hasCommission = amounts.Count >= 3 && amounts[1] > 0m;
            decimal primaryAmount = mdl;
            decimal commission = 0m;

            if (hasCommission)
            {
                commission = amounts[1];
                primaryAmount = mdl - commission;
            }

            // Guard: when commission consumes the entire `ieșiri` (degenerate
            // case — pure-fee row with no principal transfer), skip the primary
            // and emit only the fee row so we never produce a zero or negative
            // primary amount. Negative would be a programmer error; zero would
            // be a meaningless row.
            if (primaryAmount > 0m)
            {
                rows.Add(new ParsedStatementRow(
                    txDate,
                    direction,
                    primaryAmount,
                    description,
                    originalAmount,
                    originalCurrency));
            }

            // Emit a paired fee row when the tail carries a commission column.
            // The fee row is always Expense in the account currency (MDL) — bank
            // fees aren't FX-converted. We do NOT set any transfer flag here; the
            // parser deals only in ParsedStatementRow, and the downstream
            // SubstringTransferDetector won't match "Comision: ..." because none of
            // its inclusion tokens (A2A / TRANSFER / RETRAGERE / ATM) appear in the
            // prefix. If a future edit adds "COMISION" to the inclusion list in
            // SubstringTransferDetector this invariant breaks — keep them in sync.
            if (hasCommission)
            {
                rows.Add(new ParsedStatementRow(
                    txDate,
                    TransactionDirection.Expense,
                    commission,
                    $"Comision: {description}",
                    OriginalAmount: null,
                    OriginalCurrency: null));
            }
        }

        return rows;
    }

    private static List<decimal> TokenizeTailNumbers(string tail)
    {
        // The tail looks like "136.002 232.13" — two adjacent decimals where the first
        // is the MDL column amount (`136.00`) and the second is the running balance
        // (`2 232.13`). The regex matches numbers with optional thousands-space
        // separator and a mandatory decimal portion.
        var result = new List<decimal>();
        if (string.IsNullOrWhiteSpace(tail))
        {
            return result;
        }

        MatchCollection matches = Regex.Matches(tail, @"\d{1,3}(?:[\s ]\d{3})*\.\d{1,2}");
        foreach (Match m in matches)
        {
            if (TryParseAmount(m.Value, out decimal v))
            {
                result.Add(v);
            }
        }

        if (result.Count == 0)
        {
            // Fallback for integer-only amounts.
            foreach (Match m in Regex.Matches(tail, @"\d+"))
            {
                if (TryParseAmount(m.Value, out decimal v))
                {
                    result.Add(v);
                }
            }
        }

        return result;
    }

    private static bool TryParseAmount(string token, out decimal value)
    {
        // maib uses period as decimal separator and space as thousands separator.
        string cleaned = token.Replace(" ", string.Empty);
        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, Inv, out value);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private static string NormalizeWhitespace(string text) =>
        WhitespaceRegex().Replace(text.Trim(), " ");
}
