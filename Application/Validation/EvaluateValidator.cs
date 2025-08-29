using Application.Command;
using FluentValidation;

namespace Application.Validation;

public sealed class EvaluateValidator : AbstractValidator<EvaluateCommand>
{
    public EvaluateValidator()
    {
        RuleFor(x => x.Symbol).NotEmpty();
        RuleFor(x => x.Timeframe).NotEmpty();
        RuleFor(x => x.Atr).GreaterThan(0);
        RuleFor(x => x.Ask).GreaterThan(x => x.Bid);
        RuleFor(x => x.High).GreaterThanOrEqualTo(x => x.Open);
        RuleFor(x => x.High).GreaterThanOrEqualTo(x => x.Close);
        RuleFor(x => x.Low).LessThanOrEqualTo(x => x.Open);
        RuleFor(x => x.Low).LessThanOrEqualTo(x => x.Close);
        RuleFor(x => x.Equity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OpenTrades).GreaterThanOrEqualTo(0);
    }
}