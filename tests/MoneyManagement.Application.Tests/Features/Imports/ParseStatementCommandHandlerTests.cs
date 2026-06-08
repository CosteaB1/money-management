using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Imports;
using MoneyManagement.Application.Features.Imports;
using MoneyManagement.Application.Features.Imports.ParseStatement;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Imports;

public class ParseStatementCommandHandlerTests
{
    private static readonly DateOnly TxDate = new(2026, 4, 10);
    private static readonly DateOnly PeriodFrom = new(2026, 4, 1);
    private static readonly DateOnly PeriodTo = new(2026, 4, 30);

    private static Account NewAccount(string name = "Checking", string currency = "MDL")
    {
        Result<Account> result = Account.Create(
            name,
            AccountType.Cash,
            new Money(0m, currency),
            new DateOnly(2026, 1, 1),
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static Transaction NewTransferLeg(
        Guid accountId,
        TransactionDirection direction,
        decimal amount,
        string description,
        DateOnly? date = null,
        string currency = "MDL")
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            date ?? TxDate,
            direction,
            new Money(amount, currency),
            description,
            TransactionSource.Imported,
            categoryId: null,
            importBatchId: null,
            originalAmount: null,
            originalCurrency: null,
            isTransfer: true,
            counterAccountId: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static IBankStatementParser StubParser(params ParsedStatementRow[] rows)
    {
        var parsed = new ParsedStatement(
            new ParsedStatementPeriod(PeriodFrom, PeriodTo),
            new ParsedStatementSummary(0m, 0m, 0m, 0m, 0m),
            rows);

        IBankStatementParser parser = Substitute.For<IBankStatementParser>();
        parser.Source.Returns(BankSource.Maib);
        parser.Parse(Arg.Any<Stream>()).Returns(parsed);
        return parser;
    }

    private static ITransferDetector TransferDetectorFor(params string[] transferDescriptions)
    {
        var lookup = new HashSet<string>(transferDescriptions, StringComparer.Ordinal);
        ITransferDetector detector = Substitute.For<ITransferDetector>();
        detector.IsLikelyTransfer(Arg.Any<string>())
            .Returns(call => lookup.Contains((string)call.Args()[0]!));
        return detector;
    }

    private static ICategorySuggester NoSuggester()
    {
        ICategorySuggester suggester = Substitute.For<ICategorySuggester>();
        suggester.SuggestAsync(Arg.Any<string>(), Arg.Any<TransactionDirection>(), Arg.Any<CancellationToken>())
            .Returns((CategorySuggestion?)null);
        return suggester;
    }

    [Fact]
    public async Task Handle_TransferRowMatchesExistingTransferLeg_FlagsAsDuplicate()
    {
        Account account = NewAccount();
        Transaction existingLeg = NewTransferLeg(
            account.Id,
            TransactionDirection.Income,
            500m,
            "A2A de iesire pe cardul 435696***5875");

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [existingLeg]);

        const string incomingDescription = "A2A de intrare pe cardul 999999***0000";
        IBankStatementParser parser = StubParser(new ParsedStatementRow(
            TxDate,
            TransactionDirection.Income,
            500m,
            incomingDescription,
            OriginalAmount: null,
            OriginalCurrency: null));

        var handler = new ParseStatementCommandHandler(
            db,
            [parser],
            NoSuggester(),
            TransferDetectorFor(incomingDescription));

        var command = new ParseStatementCommand(
            FileBytes: [0x25, 0x50, 0x44, 0x46], // "%PDF" -- the stub parser ignores content
            FileName: "gama.pdf",
            AccountId: account.Id);

        Result<StatementPreviewDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Transactions.Should().ContainSingle()
            .Which.IsDuplicate.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NonTransferRowSameDateAndAmount_NotDeduped()
    {
        // Existing non-transfer row with matching (date, amount, direction). The
        // transfer-aware fallback only fires when the existing row is a transfer,
        // so the incoming row should come back as not-a-duplicate.
        Account account = NewAccount();
        Result<Transaction> existingResult = Transaction.Create(
            account.Id,
            TxDate,
            TransactionDirection.Income,
            new Money(500m, "MDL"),
            "Salary advance",
            TransactionSource.Imported,
            categoryId: null,
            importBatchId: null,
            originalAmount: null,
            originalCurrency: null,
            isTransfer: false,
            counterAccountId: null);

        existingResult.IsSuccess.Should().BeTrue();

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [existingResult.Value]);

        const string incomingDescription = "A2A de intrare pe cardul 999999***0000";
        IBankStatementParser parser = StubParser(new ParsedStatementRow(
            TxDate,
            TransactionDirection.Income,
            500m,
            incomingDescription,
            OriginalAmount: null,
            OriginalCurrency: null));

        var handler = new ParseStatementCommandHandler(
            db,
            [parser],
            NoSuggester(),
            TransferDetectorFor(incomingDescription));

        var command = new ParseStatementCommand(
            FileBytes: [0x25, 0x50, 0x44, 0x46],
            FileName: "gama.pdf",
            AccountId: account.Id);

        Result<StatementPreviewDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Transactions.Should().ContainSingle()
            .Which.IsDuplicate.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_RowsGroupedNotChronological_ReturnsTransactionsSortedByDateAscending()
    {
        // maib groups rows by card/section, so the parser emits them out of date order.
        // The handler must sort the preview chronologically. The two rows sharing
        // 2026-04-12 verify the stable tiebreak keeps their original input order.
        Account account = NewAccount();

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var apr20 = new ParsedStatementRow(
            new DateOnly(2026, 4, 20),
            TransactionDirection.Expense,
            120m,
            "Apr 20 row",
            OriginalAmount: null,
            OriginalCurrency: null);
        var apr05 = new ParsedStatementRow(
            new DateOnly(2026, 4, 5),
            TransactionDirection.Expense,
            50m,
            "Apr 05 row",
            OriginalAmount: null,
            OriginalCurrency: null);
        var apr12First = new ParsedStatementRow(
            new DateOnly(2026, 4, 12),
            TransactionDirection.Expense,
            12m,
            "Apr 12 first",
            OriginalAmount: null,
            OriginalCurrency: null);
        var apr12Second = new ParsedStatementRow(
            new DateOnly(2026, 4, 12),
            TransactionDirection.Income,
            99m,
            "Apr 12 second",
            OriginalAmount: null,
            OriginalCurrency: null);

        IBankStatementParser parser = StubParser(apr20, apr05, apr12First, apr12Second);

        var handler = new ParseStatementCommandHandler(
            db,
            [parser],
            NoSuggester(),
            TransferDetectorFor());

        var command = new ParseStatementCommand(
            FileBytes: [0x25, 0x50, 0x44, 0x46],
            FileName: "gama.pdf",
            AccountId: account.Id);

        Result<StatementPreviewDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        IReadOnlyList<ParsedTransactionPreviewDto> transactions = result.Value.Transactions;
        transactions.Select(t => t.TransactionDate).Should().BeInAscendingOrder();

        // Stable sort: the two 2026-04-12 rows must keep their input order.
        ParsedTransactionPreviewDto[] sameDate =
            [.. transactions.Where(t => t.TransactionDate == new DateOnly(2026, 4, 12))];
        sameDate.Should().HaveCount(2);
        sameDate[0].Description.Should().Be("Apr 12 first");
        sameDate[0].Amount.Should().Be(12m);
        sameDate[1].Description.Should().Be("Apr 12 second");
        sameDate[1].Amount.Should().Be(99m);
    }

    [Fact]
    public async Task Handle_NonTransferIncomingRow_DoesNotUseTransferFallback()
    {
        // The fallback is gated on the parser's transfer flag for the incoming row.
        // A row the detector did not flag must not pick up the existing transfer leg.
        Account account = NewAccount();
        Transaction existingLeg = NewTransferLeg(
            account.Id,
            TransactionDirection.Income,
            500m,
            "A2A de iesire pe cardul 435696***5875");

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [existingLeg]);

        const string incomingDescription = "Random refund";
        IBankStatementParser parser = StubParser(new ParsedStatementRow(
            TxDate,
            TransactionDirection.Income,
            500m,
            incomingDescription,
            OriginalAmount: null,
            OriginalCurrency: null));

        var handler = new ParseStatementCommandHandler(
            db,
            [parser],
            NoSuggester(),
            TransferDetectorFor(/* no descriptions are flagged */));

        var command = new ParseStatementCommand(
            FileBytes: [0x25, 0x50, 0x44, 0x46],
            FileName: "gama.pdf",
            AccountId: account.Id);

        Result<StatementPreviewDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ParsedTransactionPreviewDto preview = result.Value.Transactions.Single();
        preview.IsDuplicate.Should().BeFalse();
        preview.IsTransfer.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MissingAccount_ReturnsAccountNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();

        var handler = new ParseStatementCommandHandler(
            db,
            [StubParser()],
            NoSuggester(),
            TransferDetectorFor());

        var missingAccount = Guid.CreateVersion7();
        var command = new ParseStatementCommand(
            FileBytes: [0x25, 0x50, 0x44, 0x46],
            FileName: "gama.pdf",
            AccountId: missingAccount);

        Result<StatementPreviewDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(missingAccount));
    }

    [Fact]
    public async Task Handle_ParserRecognizesFileButFailsToParse_PropagatesParseFailure()
    {
        // A parser that recognizes the bank but can't read the content returns
        // ParseFailed (not UnsupportedFormat) — the loop stops and surfaces it.
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        IBankStatementParser failingParser = Substitute.For<IBankStatementParser>();
        failingParser.Source.Returns(BankSource.Maib);
        failingParser.Parse(Arg.Any<Stream>())
            .Returns(Result.Failure<ParsedStatement>(ImportBatchErrors.ParseFailed));

        var handler = new ParseStatementCommandHandler(
            db,
            [failingParser],
            NoSuggester(),
            TransferDetectorFor());

        var command = new ParseStatementCommand(
            FileBytes: [0x25, 0x50, 0x44, 0x46],
            FileName: "gama.pdf",
            AccountId: account.Id);

        Result<StatementPreviewDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.ParseFailed);
    }

    [Fact]
    public async Task Handle_NoParserRecognizesFile_ReturnsUnsupportedFormat()
    {
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        IBankStatementParser decliningParser = Substitute.For<IBankStatementParser>();
        decliningParser.Source.Returns(BankSource.Maib);
        decliningParser.Parse(Arg.Any<Stream>())
            .Returns(Result.Failure<ParsedStatement>(ImportBatchErrors.UnsupportedFormat));

        var handler = new ParseStatementCommandHandler(
            db,
            [decliningParser],
            NoSuggester(),
            TransferDetectorFor());

        var command = new ParseStatementCommand(
            FileBytes: [0x25, 0x50, 0x44, 0x46],
            FileName: "gama.pdf",
            AccountId: account.Id);

        Result<StatementPreviewDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ImportBatchErrors.UnsupportedFormat);
    }

    [Fact]
    public async Task Handle_PopulatesEverySummaryAndPreviewField()
    {
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var parsed = new ParsedStatement(
            new ParsedStatementPeriod(PeriodFrom, PeriodTo),
            new ParsedStatementSummary(
                OpeningBalance: 1_000m,
                ClosingBalance: 1_234m,
                TotalIn: 500m,
                TotalOut: 266m,
                TotalFees: 12m),
            [
                new ParsedStatementRow(
                    TxDate,
                    TransactionDirection.Expense,
                    73.20m,
                    "APPLE.COM subscription",
                    OriginalAmount: 4.99m,
                    OriginalCurrency: "USD"),
            ]);

        IBankStatementParser parser = Substitute.For<IBankStatementParser>();
        parser.Source.Returns(BankSource.Maib);
        parser.Parse(Arg.Any<Stream>()).Returns(parsed);

        var suggestedCategoryId = Guid.CreateVersion7();
        ICategorySuggester suggester = Substitute.For<ICategorySuggester>();
        suggester.SuggestAsync(Arg.Any<string>(), Arg.Any<TransactionDirection>(), Arg.Any<CancellationToken>())
            .Returns(new CategorySuggestion(suggestedCategoryId, "Subscriptions"));

        var handler = new ParseStatementCommandHandler(db, [parser], suggester, TransferDetectorFor());

        var command = new ParseStatementCommand(
            FileBytes: [0x25, 0x50, 0x44, 0x46],
            FileName: "gama.pdf",
            AccountId: account.Id);

        // The command carries the original file name for downstream auditing even
        // though the parse handler keys off FileBytes/AccountId; pin it here.
        command.FileName.Should().Be("gama.pdf");

        Result<StatementPreviewDto> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        StatementPreviewDto preview = result.Value;

        preview.FileHash.Should().NotBeNullOrEmpty();
        preview.BankSource.Should().Be(BankSource.Maib);
        preview.StatementPeriod.From.Should().Be("2026-04-01");
        preview.StatementPeriod.To.Should().Be("2026-04-30");
        preview.Summary.OpeningBalance.Should().Be(1_000m);
        preview.Summary.ClosingBalance.Should().Be(1_234m);
        preview.Summary.TotalIn.Should().Be(500m);
        preview.Summary.TotalOut.Should().Be(266m);
        preview.Summary.TotalFees.Should().Be(12m);

        ParsedTransactionPreviewDto row = preview.Transactions.Single();
        row.Direction.Should().Be(TransactionDirection.Expense);
        row.Amount.Should().Be(73.20m);
        row.SuggestedCategoryId.Should().Be(suggestedCategoryId);
        row.SuggestedCategoryName.Should().Be("Subscriptions");
        row.OriginalAmount.Should().Be(4.99m);
        row.OriginalCurrency.Should().Be("USD");
        row.IsDuplicate.Should().BeFalse();
        row.IsTransfer.Should().BeFalse();
    }
}
