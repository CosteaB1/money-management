using FluentValidation;
using MoneyManagement.Domain.FxRates;

namespace MoneyManagement.Application.Features.FxRates.CreateFxRate;

public sealed class CreateFxRateCommandValidator : AbstractValidator<CreateFxRateCommand>
{
    public CreateFxRateCommandValidator()
    {
        RuleFor(c => c.FromCurrency)
            .NotEmpty()
            .Length(FxRate.CurrencyLength)
            .Matches("^[A-Z]{3}$")
            .WithMessage("FromCurrency must be a 3-letter uppercase ISO code (e.g. MDL, USD, EUR, RON).");

        RuleFor(c => c.ToCurrency)
            .NotEmpty()
            .Length(FxRate.CurrencyLength)
            .Matches("^[A-Z]{3}$")
            .WithMessage("ToCurrency must be a 3-letter uppercase ISO code (e.g. MDL, USD, EUR, RON).");

        RuleFor(c => c)
            .Must(c => !string.Equals(c.FromCurrency, c.ToCurrency, StringComparison.Ordinal))
            .WithMessage("Source and target currencies must differ.")
            .WithName(nameof(CreateFxRateCommand.ToCurrency));

        RuleFor(c => c.Rate)
            .GreaterThan(0m);

        RuleFor(c => c.AsOf)
            .NotEqual(default(DateOnly));
    }
}
