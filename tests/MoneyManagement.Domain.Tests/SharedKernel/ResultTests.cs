using FluentAssertions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.SharedKernel;

/// <summary>
/// Covers the <see cref="Result"/> / <see cref="Result{T}"/> invariants and
/// conversion operators directly — the guard throws and the failure-value
/// throw are otherwise unreachable through the happy-path handlers.
/// </summary>
public class ResultTests
{
    private static readonly Error SampleError = Error.Validation("test.code", "boom");

    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_CarriesError()
    {
        var result = Result.Failure(SampleError);

        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(SampleError);
    }

    [Fact]
    public void Failure_WithNoneError_Throws()
    {
        // A failure result must carry a real error.
        Action act = () => Result.Failure(Error.None);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("A failure result must carry an error.");
    }

    [Fact]
    public void Construct_SuccessWithError_Throws()
    {
        // The success+error combination is rejected by the protected constructor.
        // The static factories never produce it, so it's reached via a subclass.
        Action act = () => _ = new SuccessWithErrorResult(SampleError);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("A successful result cannot carry an error.");
    }

    private sealed class SuccessWithErrorResult(Error error) : Result(isSuccess: true, error);

    [Fact]
    public void SuccessOfT_HasValueAndNoError()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void FailureOfT_AccessingValue_Throws()
    {
        var result = Result.Failure<int>(SampleError);

        Action act = () => _ = result.Value;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot access value of a failed result.");
    }

    [Fact]
    public void FailureOfT_WithNoneError_Throws()
    {
        Action act = () => Result.Failure<int>(Error.None);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("A failure result must carry an error.");
    }

    [Fact]
    public void ImplicitOperator_FromValue_ProducesSuccess()
    {
        Result<string> result = "hello";

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void ImplicitOperator_FromError_ProducesFailure()
    {
        Result<string> result = SampleError;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SampleError);
    }
}
