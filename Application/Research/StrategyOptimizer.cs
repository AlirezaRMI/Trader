namespace Application.Research;

public sealed class StrategyOptimizer
{
    public OptimizationResult Run(OptimizationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Backtest is null) throw new ArgumentException("Backtest input is required");
        BacktestEngine.Validate(request.Backtest);
        if (!double.IsFinite(request.TrainingFraction) || request.TrainingFraction is < .5 or > .85 ||
            !double.IsFinite(request.MaximumDrawdownPercent) || request.MaximumDrawdownPercent is <= 0 or > 100 ||
            request.MinimumTrainingTrades is < 1 or > 1000 || request.StopAtrMultipliers is null || request.RewardRiskRatios is null ||
            request.StopAtrMultipliers.Count is < 1 or > 4 || request.RewardRiskRatios.Count is < 1 or > 4)
            throw new ArgumentException("Invalid bounded optimization constraints");
        var split = (int)(request.Backtest.Candles.Count * request.TrainingFraction);
        var engine = new BacktestEngine(); var candidates = new List<OptimizationCandidate>(); var evaluated = 0;
        foreach (var stop in request.StopAtrMultipliers.Distinct())
        foreach (var reward in request.RewardRiskRatios.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameters = request.Backtest.Parameters with { StopAtrMultiplier = stop, RewardRiskRatio = reward };
            if (parameters.Validate() is { } error) throw new ArgumentException(error);
            var training = engine.RunWindow(request.Backtest with { Parameters = parameters }, 0, split, cancellationToken);
            evaluated++;
            if (training.Trades.Count < request.MinimumTrainingTrades || training.MaximumDrawdownPercent > request.MaximumDrawdownPercent) continue;
            candidates.Add(new(parameters, training, training.ReturnPercent - training.MaximumDrawdownPercent));
        }
        var ranked = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Parameters.StopAtrMultiplier)
            .ThenBy(c => c.Parameters.RewardRiskRatio).ToArray();
        var selected = ranked.FirstOrDefault();
        var validation = selected is null ? null : engine.RunWindow(request.Backtest with { Parameters = selected.Parameters },
            split, request.Backtest.Candles.Count, cancellationToken);
        return new(request.Backtest.Candles[split].OpenTime, evaluated, selected, validation, ranked);
    }
}
