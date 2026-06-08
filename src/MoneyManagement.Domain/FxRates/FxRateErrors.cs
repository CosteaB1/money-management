using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.FxRates;

public static class FxRateErrors
{
    public static readonly Error InvalidCurrency =
        Error.Validation(
            "fx_rate.invalid_currency",
            "Currencies must be 3-letter uppercase ISO codes (e.g. MDL, USD, EUR, RON).");

    public static readonly Error SameSourceAndTargetCurrency =
        Error.Validation(
            "fx_rate.same_source_and_target_currency",
            "Source and target currencies must differ.");

    public static readonly Error RateMustBePositive =
        Error.Validation(
            "fx_rate.rate_must_be_positive",
            "Exchange rate must be greater than zero.");

    public static Error NotFound(Guid id) =>
        Error.NotFound("fx_rate.not_found", $"FX rate with id '{id}' was not found.");
}
