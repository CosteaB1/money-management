using FluentAssertions;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.Transactions;

public class TransactionTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();

    // Transaction.Create judges "in the future" in UTC, so the test's notion
    // of "today" must match.
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static Money ValidAmount(decimal amount = 100m, string currency = ReportingCurrencies.Mdl) =>
        new(amount, currency);

    [Fact]
    public void Create_WithValidInput_ReturnsSuccess()
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(250m),
            "Coffee",
            TransactionSource.Manual);

        result.IsSuccess.Should().BeTrue();
        Transaction transaction = result.Value;
        transaction.AccountId.Should().Be(AccountId);
        transaction.Direction.Should().Be(TransactionDirection.Expense);
        transaction.Amount.Should().Be(ValidAmount(250m));
        transaction.Description.Should().Be("Coffee");
        transaction.IsDeleted.Should().BeFalse();
        transaction.Source.Should().Be(TransactionSource.Manual);
        transaction.IsAdjustment.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveAmount_Fails(decimal amount)
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(amount),
            "x",
            TransactionSource.Manual);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.AmountNotPositive);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("RON")]
    public void Create_WithNonMdlIsoCurrency_Succeeds(string currency)
    {
        // Phase 4 lifts the MDL-only restriction - any valid ISO code is accepted.
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            new Money(100m, currency),
            "x",
            TransactionSource.Manual);

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Currency.Should().Be(currency);
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("")]
    public void Create_WithInvalidIsoCurrency_Fails(string currency)
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            new Money(100m, currency),
            "x",
            TransactionSource.Manual);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.InvalidCurrency);
    }

    [Fact]
    public void Create_WithFutureDate_Fails()
    {
        DateOnly future = Today.AddDays(2);

        Result<Transaction> result = Transaction.Create(
            AccountId,
            future,
            TransactionDirection.Expense,
            ValidAmount(),
            "x",
            TransactionSource.Manual);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.DateInFuture);
    }

    [Fact]
    public void Create_WithTodayDate_Succeeds()
    {
        // Today's UTC calendar date must be accepted.
        Result<Transaction> result = Transaction.Create(
            AccountId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            TransactionDirection.Expense,
            ValidAmount(),
            "x",
            TransactionSource.Manual);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_WithPastDate_Succeeds()
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5),
            TransactionDirection.Expense,
            ValidAmount(),
            "x",
            TransactionSource.Manual);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyDescription_Fails(string description)
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            description,
            TransactionSource.Manual);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.DescriptionRequired);
    }

    [Fact]
    public void MarkDeleted_SetsIsDeletedTrue()
    {
        Transaction transaction = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "x",
            TransactionSource.Manual).Value;

        transaction.MarkDeleted();

        transaction.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void MarkDeleted_RaisesTransactionDeletedDomainEventWithRightFieldValues()
    {
        var categoryId = Guid.CreateVersion7();
        var txDate = new DateOnly(2026, 4, 15);

        Transaction transaction = Transaction.Create(
            AccountId,
            txDate,
            TransactionDirection.Expense,
            ValidAmount(75m),
            "Lunch",
            TransactionSource.Manual,
            categoryId: categoryId).Value;
        transaction.ClearDomainEvents();

        transaction.MarkDeleted(amountMdl: 75m);

        transaction.IsDeleted.Should().BeTrue();
        transaction.GetDomainEvents()
            .Should().ContainSingle()
            .Which.Should().BeOfType<TransactionDeletedDomainEvent>()
            .Which.Should().BeEquivalentTo(new
            {
                TransactionId = transaction.Id,
                CategoryId = (Guid?)categoryId,
                TransactionDate = txDate,
                AmountMdl = (decimal?)75m,
                Direction = TransactionDirection.Expense,
                IsTransfer = false,
                IsAdjustment = false,
            });
    }

    [Fact]
    public void MarkDeleted_WhenAlreadyDeleted_IsIdempotent_DoesNotRaiseSecondEvent()
    {
        Transaction transaction = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "x",
            TransactionSource.Manual).Value;
        transaction.ClearDomainEvents();

        transaction.MarkDeleted();
        transaction.MarkDeleted();

        transaction.IsDeleted.Should().BeTrue();
        transaction.GetDomainEvents()
            .OfType<TransactionDeletedDomainEvent>()
            .Should().ContainSingle();
    }

    [Fact]
    public void SetCategory_WhenValueUnchanged_DoesNotRaiseEvent()
    {
        var categoryId = Guid.CreateVersion7();
        Transaction transaction = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual,
            categoryId: categoryId).Value;
        transaction.ClearDomainEvents();

        transaction.SetCategory(categoryId);
        transaction.SetCategory(categoryId);

        transaction.GetDomainEvents()
            .OfType<TransactionCategoryChangedDomainEvent>()
            .Should().BeEmpty();
    }

    [Fact]
    public void SetCategory_WithValue_RaisesCategoryChangedEventWithOldAndNewIds()
    {
        var oldCategoryId = Guid.CreateVersion7();
        var newCategoryId = Guid.CreateVersion7();
        var txDate = new DateOnly(2026, 4, 15);

        Transaction transaction = Transaction.Create(
            AccountId,
            txDate,
            TransactionDirection.Expense,
            ValidAmount(50m),
            "Coffee",
            TransactionSource.Manual,
            categoryId: oldCategoryId).Value;
        transaction.ClearDomainEvents();

        transaction.SetCategory(newCategoryId, amountMdl: 50m);

        transaction.CategoryId.Should().Be(newCategoryId);
        transaction.GetDomainEvents()
            .Should().ContainSingle()
            .Which.Should().BeOfType<TransactionCategoryChangedDomainEvent>()
            .Which.Should().BeEquivalentTo(new
            {
                TransactionId = transaction.Id,
                OldCategoryId = (Guid?)oldCategoryId,
                NewCategoryId = (Guid?)newCategoryId,
                TransactionDate = txDate,
                AmountMdl = (decimal?)50m,
                Direction = TransactionDirection.Expense,
                IsTransfer = false,
                IsAdjustment = false,
            });
    }

    [Fact]
    public void SetCategory_WithNull_ClearsCategoryAndRaisesEventWithNullNewId()
    {
        var oldCategoryId = Guid.CreateVersion7();
        Transaction transaction = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual,
            categoryId: oldCategoryId).Value;
        transaction.ClearDomainEvents();

        transaction.SetCategory(null);

        transaction.CategoryId.Should().BeNull();
        TransactionCategoryChangedDomainEvent evt = transaction.GetDomainEvents()
            .OfType<TransactionCategoryChangedDomainEvent>()
            .Single();
        evt.OldCategoryId.Should().Be(oldCategoryId);
        evt.NewCategoryId.Should().BeNull();
    }

    [Fact]
    public void Create_WithoutTransferFlag_DefaultsTransferFieldsToFalseAndNull()
    {
        Transaction transaction = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual).Value;

        transaction.IsTransfer.Should().BeFalse();
        transaction.CounterAccountId.Should().BeNull();
    }

    [Fact]
    public void Create_WithTransferFlagOnly_Succeeds()
    {
        // The import auto-suggest path may flag a row as a transfer without yet
        // knowing the counterparty account — that's intentional.
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "A2A transfer",
            TransactionSource.Imported,
            isTransfer: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsTransfer.Should().BeTrue();
        result.Value.CounterAccountId.Should().BeNull();
    }

    [Fact]
    public void Create_WithTransferFlagAndCounterAccount_Succeeds()
    {
        var counter = Guid.CreateVersion7();

        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Card -> Card",
            TransactionSource.Manual,
            isTransfer: true,
            counterAccountId: counter);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsTransfer.Should().BeTrue();
        result.Value.CounterAccountId.Should().Be(counter);
    }

    [Fact]
    public void Create_WithCounterAccount_ButWithoutTransferFlag_Fails()
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "x",
            TransactionSource.Manual,
            counterAccountId: Guid.CreateVersion7());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.CounterAccountWithoutTransferFlag);
    }

    [Fact]
    public void Create_WithCounterAccountEqualToAccountId_Fails()
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "x",
            TransactionSource.Manual,
            isTransfer: true,
            counterAccountId: AccountId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.CounterAccountCannotBeSelf);
    }

    [Fact]
    public void Create_WithAdjustmentFlag_Succeeds()
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Income,
            ValidAmount(),
            "Balance adjustment",
            TransactionSource.Manual,
            isAdjustment: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAdjustment.Should().BeTrue();
        result.Value.IsTransfer.Should().BeFalse();
    }

    [Fact]
    public void Create_WithBothTransferAndAdjustmentFlags_Fails()
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Income,
            ValidAmount(),
            "ambiguous",
            TransactionSource.Manual,
            isTransfer: true,
            isAdjustment: true);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.TransferAndAdjustmentAreMutuallyExclusive);
    }

    [Fact]
    public void Create_RaisesTransactionCreatedDomainEventWithRightFieldValues()
    {
        var categoryId = Guid.CreateVersion7();
        var counterId = Guid.CreateVersion7();
        var txDate = new DateOnly(2026, 4, 15);

        Result<Transaction> result = Transaction.Create(
            AccountId,
            txDate,
            TransactionDirection.Expense,
            ValidAmount(150m),
            "Groceries",
            TransactionSource.Manual,
            categoryId: categoryId,
            counterAccountId: counterId,
            isTransfer: true,
            amountMdl: 150m);

        result.IsSuccess.Should().BeTrue();
        Transaction tx = result.Value;

        tx.GetDomainEvents()
            .Should().ContainSingle()
            .Which.Should().BeOfType<TransactionCreatedDomainEvent>()
            .Which.Should().BeEquivalentTo(new
            {
                TransactionId = tx.Id,
                CategoryId = (Guid?)categoryId,
                TransactionDate = txDate,
                AmountMdl = (decimal?)150m,
                Direction = TransactionDirection.Expense,
                IsTransfer = true,
                IsAdjustment = false,
            });
    }

    [Fact]
    public void SetCategory_WithValue_AssignsCategoryId()
    {
        var categoryId = Guid.CreateVersion7();
        Transaction transaction = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual).Value;

        transaction.SetCategory(categoryId);

        transaction.CategoryId.Should().Be(categoryId);
    }

    [Fact]
    public void SetCategory_WithNull_ClearsCategoryId()
    {
        Transaction transaction = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual,
            categoryId: Guid.CreateVersion7()).Value;

        transaction.SetCategory(null);

        transaction.CategoryId.Should().BeNull();
    }

    [Fact]
    public void Create_WithNotes_PersistsTrimmedValue()
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual,
            notes: "  Paid back Alex  ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Notes.Should().Be("Paid back Alex");
    }

    [Fact]
    public void Create_WithoutNotes_LeavesNotesNull()
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual);

        result.IsSuccess.Should().BeTrue();
        result.Value.Notes.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankNotes_NormalizesToNull(string notes)
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual,
            notes: notes);

        result.IsSuccess.Should().BeTrue();
        result.Value.Notes.Should().BeNull();
    }

    [Fact]
    public void Create_WithNotesExceedingMaxLength_Fails()
    {
        string tooLong = new('x', Transaction.NotesMaxLength + 1);

        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual,
            notes: tooLong);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.NotesTooLong);
    }

    [Fact]
    public void SetNotes_WithValue_AssignsTrimmedNotes()
    {
        Transaction transaction = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual).Value;

        transaction.SetNotes("  Reimbursed  ");

        transaction.Notes.Should().Be("Reimbursed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SetNotes_WithBlankOrNull_ClearsNotes(string? notes)
    {
        Transaction transaction = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual,
            notes: "Existing note").Value;

        transaction.SetNotes(notes);

        transaction.Notes.Should().BeNull();
    }

    [Fact]
    public void Create_WithEmptyAccountId_Fails()
    {
        Result<Transaction> result = Transaction.Create(
            Guid.Empty,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "x",
            TransactionSource.Manual);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.AccountRequired);
    }

    [Fact]
    public void Create_WithUndefinedDirection_Fails()
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            (TransactionDirection)999,
            ValidAmount(),
            "x",
            TransactionSource.Manual);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.InvalidDirection);
    }

    [Fact]
    public void Create_WithUndefinedSource_Fails()
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "x",
            (TransactionSource)999);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.InvalidSource);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDX")]
    public void Create_WithOriginalCurrencyNotThreeChars_Fails(string originalCurrency)
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "x",
            TransactionSource.Manual,
            originalAmount: 10m,
            originalCurrency: originalCurrency);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.InvalidOriginalCurrency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_WithNonPositiveOriginalAmount_Fails(decimal originalAmount)
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "x",
            TransactionSource.Manual,
            originalAmount: originalAmount,
            originalCurrency: "USD");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.OriginalAmountNotPositive);
    }

    [Fact]
    public void SetNotes_WhenNormalizedValueUnchanged_IsNoOp()
    {
        Transaction transaction = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(),
            "Coffee",
            TransactionSource.Manual,
            notes: "Reimbursed").Value;

        // Re-setting with surrounding whitespace normalizes to the same stored
        // value, so the early-return no-op branch is taken.
        transaction.SetNotes("  Reimbursed  ");

        transaction.Notes.Should().Be("Reimbursed");
    }

    [Fact]
    public void Create_WithoutAmountMdl_RaisesEventWithNullAmountMdl()
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            ValidAmount(100m, "USD"),
            "Coffee abroad",
            TransactionSource.Manual);

        result.IsSuccess.Should().BeTrue();
        TransactionCreatedDomainEvent evt = result.Value.GetDomainEvents()
            .OfType<TransactionCreatedDomainEvent>()
            .Single();

        evt.AmountMdl.Should().BeNull();
        evt.CategoryId.Should().BeNull();
        evt.IsTransfer.Should().BeFalse();
        evt.IsAdjustment.Should().BeFalse();
    }
}
