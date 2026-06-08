using MoneyManagement.Application.Abstractions.FxRates;
using NSubstitute;

namespace MoneyManagement.Application.Tests.TestSupport;

/// <summary>
/// Test doubles for <see cref="IFxConverter"/>. Use <see cref="Identity"/> for
/// the common case (input and output currencies coincide, or callers don't
/// care about the MDL-equivalent path); use <see cref="WithTable"/> for the
/// multi-currency aggregate tests.
/// </summary>
internal static class FakeFxConverter
{
    /// <summary>
    /// Returns the input amount when from==to; null otherwise. Mirrors
    /// <see cref="IFxConverter"/>'s contract for the identity case.
    /// </summary>
    public static IFxConverter Identity()
    {
        IFxConverter fx = Substitute.For<IFxConverter>();
        fx.ConvertAsync(
                Arg.Any<decimal>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                decimal amount = call.ArgAt<decimal>(0);
                string from = call.ArgAt<string>(1);
                string to = call.ArgAt<string>(2);
                return Task.FromResult<decimal?>(
                    string.Equals(from, to, StringComparison.Ordinal) ? amount : null);
            });
        return fx;
    }

    /// <summary>Converter that resolves to-MDL conversions from a fixed table.</summary>
    public static IFxConverter WithTable(Dictionary<string, decimal> ratesToMdl)
    {
        IFxConverter fx = Substitute.For<IFxConverter>();
        fx.ConvertAsync(
                Arg.Any<decimal>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                decimal amount = call.ArgAt<decimal>(0);
                string from = call.ArgAt<string>(1);
                string to = call.ArgAt<string>(2);

                if (string.Equals(from, to, StringComparison.Ordinal))
                {
                    return Task.FromResult<decimal?>(amount);
                }

                if (to == "MDL" && ratesToMdl.TryGetValue(from, out decimal rate))
                {
                    return Task.FromResult<decimal?>(amount * rate);
                }

                return Task.FromResult<decimal?>(null);
            });
        return fx;
    }
}
