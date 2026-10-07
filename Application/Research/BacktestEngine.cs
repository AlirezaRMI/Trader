using Application.Strategy;
using Domain.Enum;
using Domain.Functions;
using Domain.Parameters;

namespace Application.Research;

public sealed class BacktestEngine
{
    public BacktestResult Run(BacktestRequest request, CancellationToken cancellationToken = default) =>
        RunWindow(request, 0, request.Candles?.Count ?? 0, cancellationToken);

    internal BacktestResult RunWindow(BacktestRequest request, int tradeStartIndex, int endIndex, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);
        var candles = request.Candles.Take(endIndex).ToArray();
        var p = request.Parameters with {CommissionPerLot = request.CommissionPerLot, SlippageBufferPoints = request.SlippagePoints};
        var spec = request.Symbol;
        IReadOnlyList<MarketData> patternBars = request.PatternCandles ?? CandleAggregator.Aggregate(candles, p.EntryTimeframeMinutes, p.PatternTimeframeMinutes);
        IReadOnlyList<MarketData> phaseBars = request.PhaseCandles ?? CandleAggregator.Aggregate(candles, p.EntryTimeframeMinutes, p.PhaseTimeframeMinutes);
        var pipeline = new StrategyPipeline();
        var account = new AccountInfo { AccountId = 1, IsDemo = true, Server = "Research", TerminalSession = "replay", Equity = request.InitialEquity };
        var equity = request.InitialEquity; var peak = equity; double drawdown = 0;
        var slippage = request.SlippagePoints * spec.Point;
        var trades = new List<BacktestTrade>(); var curve = new List<EquityPoint>();
        TradeDecision? position = null; long entryTime = 0; long lastSignal = 0; long day = -1; var dailyTrades = 0;
        var patternIndex = 0; var phaseIndex = 0;
        var dailyPeak = equity; long blockedUntil = 0; var halted = false;
        for (var i = 1; i < candles.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bar = candles[i]; var cutoff = candles[i - 1].OpenTime + p.EntryTimeframeMinutes * 60L;
            var spread = (request.SpreadPointsSeries?[i] ?? request.SpreadPoints) * spec.Point;
            var openingFloating = position is null ? 0 : ((bar.Open + (position.Action == ActionKind.Buy ? 0 : spread)) - position.EntryPrice) *
                (position.Action == ActionKind.Buy ? 1 : -1) / spec.TickSize * spec.TickValue * position.PositionSizeLots -
                request.CommissionPerLot * position.PositionSizeLots;
            var openingEquity = equity + openingFloating;
            if (day != bar.OpenTime / 86400) { day = bar.OpenTime / 86400; dailyTrades = 0; dailyPeak = Math.Max(openingEquity, .00000001); }
            peak = Math.Max(peak, openingEquity);
            dailyPeak = Math.Max(dailyPeak, openingEquity);
            if ((dailyPeak - openingEquity) / dailyPeak * 100 >= request.DailyDrawdownLimitPercent && bar.OpenTime >= blockedUntil)
                blockedUntil = checked(bar.OpenTime + request.DrawdownCooldownHours * 3600L);
            if ((peak - openingEquity) / peak * 100 >= request.TotalDrawdownLimitPercent) halted = true;
            while (patternIndex < patternBars.Count && patternBars[patternIndex].OpenTime + p.PatternTimeframeMinutes * 60L <= cutoff) patternIndex++;
            while (phaseIndex < phaseBars.Count && phaseBars[phaseIndex].OpenTime + p.PhaseTimeframeMinutes * 60L <= cutoff) phaseIndex++;
            if (i >= p.WarmupBars && patternIndex >= p.WarmupBars && phaseIndex >= p.PhaseWarmupBars)
            {
                var input = new StrategyInput(account with { Equity = equity }, spec,
                    Window(phaseBars, phaseIndex, p.PhaseHistoryBars), Window(patternBars, patternIndex, p.HistoryBars),
                    Window(candles, i, p.HistoryBars), bar.Open, bar.Open + spread,
                    0, lastSignal, p, request.RiskPercent, request.MaximumRiskAmount) {MaximumSpreadPoints = request.MaximumSpreadPoints};
                var signal = pipeline.Evaluate(input).Decision;
                if (i >= tradeStartIndex && position is null && equity > 0 && !halted && bar.OpenTime >= blockedUntil)
                {
                    if (signal.Action != ActionKind.Hold && dailyTrades < request.DailyTradeLimit)
                    {
                        position = signal with {EntryPrice = signal.EntryPrice + (signal.Action == ActionKind.Buy ? slippage : -slippage)};
                        entryTime = bar.OpenTime; lastSignal = candles[i - 1].OpenTime; dailyTrades++;
                    }
                }
            }
            if (position is not null)
            {
                var exit = SimulatedExecution.FindExit(position, bar, spread, slippage);
                if (exit is not null || i == candles.Length - 1)
                {
                    var price = exit?.Price ?? (position.Action == ActionKind.Buy ? bar.Close - slippage : bar.Close + spread + slippage);
                    var gross = (price - position.EntryPrice) * (position.Action == ActionKind.Buy ? 1 : -1) /
                        spec.TickSize * spec.TickValue * position.PositionSizeLots;
                    var costs = request.CommissionPerLot * position.PositionSizeLots;
                    var profit = gross - costs;
                    equity += profit;
                    trades.Add(new(entryTime, bar.OpenTime, position.Action.ToString(), position.PositionSizeLots,
                        position.EntryPrice, price, profit, costs, exit?.Reason ?? "EndOfData"));
                    position = null;
                }
                else if (p.TrailingEnabled)
                {
                    var closed = IndicatorCalculator.Calculate(Window(candles, i + 1, p.HistoryBars), p,
                        bar.Close, bar.Close + spread);
                    position = SimulatedExecution.TrailStop(position, closed, spec, spread, p);
                }
            }
            if (i < tradeStartIndex) continue;
            var floating = position is null ? 0 : ((position.Action == ActionKind.Buy ? bar.Close : bar.Close + spread) - position.EntryPrice) *
                (position.Action == ActionKind.Buy ? 1 : -1) / spec.TickSize * spec.TickValue * position.PositionSizeLots -
                request.CommissionPerLot * position.PositionSizeLots;
            var marked = equity + floating;
            peak = Math.Max(peak, marked);
            dailyPeak = Math.Max(dailyPeak, marked);
            if ((dailyPeak - marked) / dailyPeak * 100 >= request.DailyDrawdownLimitPercent && bar.OpenTime >= blockedUntil)
                blockedUntil = checked(bar.OpenTime + request.DrawdownCooldownHours * 3600L);
            if ((peak - marked) / peak * 100 >= request.TotalDrawdownLimitPercent) halted = true;
            drawdown = Math.Max(drawdown, 100 * (peak - marked) / peak);
            curve.Add(new(bar.OpenTime, marked));
        }
        var wins = trades.Where(t => t.Profit > 0).Sum(t => t.Profit);
        var losses = -trades.Where(t => t.Profit < 0).Sum(t => t.Profit);
        return new(request.InitialEquity, equity, equity - request.InitialEquity,
            100 * (equity - request.InitialEquity) / request.InitialEquity, drawdown,
            trades.Count == 0 ? 0 : 100.0 * trades.Count(t => t.Profit > 0) / trades.Count,
            losses > 0 ? wins / losses : null, trades.Count == 0 ? 0 : trades.Average(t => t.Profit), trades, curve,
            "Closed bid OHLC with optional native phase/pattern prehistory clipped causally; entry at next bar open with adverse slippage; round-trip commission and conservative per-fill slippage reserves in sizing; fixed spread or supplied spread series, fixed tick value, no swap or margin simulation; stop wins ambiguous touches. Trailing uses completed entry bars, is never loosened, and takes effect on the following bar; EA inputs must match. Daily and total equity drawdown halt new entries; existing stops remain. Daily boundaries follow supplied candle timestamps. Intrabar drawdown, actual broker fills and quote sampling can differ; final position closes at end of data. Kelly, MAE exits, independent mark-price and funding adjustments are not enabled.");
    }

    private static MarketData[] Window(IReadOnlyList<MarketData> source, int end, int count) =>
        Enumerable.Range(Math.Max(0, end - count), Math.Min(end, count)).Select(i => source[i]).ToArray();

    public static void Validate(BacktestRequest request)
    {
        if (request.Parameters is null || request.Symbol is null || request.Candles is null) throw new ArgumentException("Research fields are required");
        if (request.Parameters.Validate() is { } error) throw new ArgumentException(error);
        if (request.Candles.Count is < 2 or > 5000) throw new ArgumentException("Research requires 2 to 5000 chronological candles");
        IndicatorCalculator.ValidateCandles(request.Candles);
        var step = request.Parameters.EntryTimeframeMinutes * 60L;
        if (request.Candles.Any(c => c.OpenTime % step != 0)) throw new ArgumentException("Candle timestamps must align to the entry timeframe");
        if (!Positive(request.InitialEquity) || request.InitialEquity > 1e9 || !Positive(request.RiskPercent) || request.RiskPercent > 5 ||
            !Positive(request.MaximumRiskAmount) || request.MaximumRiskAmount > 10000 || request.DailyTradeLimit is < 1 or > 100 ||
            !Nonnegative(request.SpreadPoints) || request.SpreadPoints > 10000 || !Nonnegative(request.SlippagePoints) || request.SlippagePoints > 10000 ||
            !Nonnegative(request.CommissionPerLot) || request.CommissionPerLot > 10000 ||
            !Positive(request.MaximumSpreadPoints) || request.MaximumSpreadPoints > 10000 ||
            !Positive(request.DailyDrawdownLimitPercent) || request.DailyDrawdownLimitPercent >= 100 ||
            !Positive(request.TotalDrawdownLimitPercent) || request.TotalDrawdownLimitPercent >= 100 ||
            request.DrawdownCooldownHours is < 1 or > 720)
            throw new ArgumentException("Invalid research risk or cost assumptions");
        var s = request.Symbol;
        if (string.IsNullOrWhiteSpace(s.SymbolName) || !Positive(s.Point) || !Positive(s.TickSize) || !Positive(s.TickValue) ||
            !Positive(s.StepVolume) || !Positive(s.MinVolume) || !Positive(s.MaxVolume) || s.MinVolume > s.MaxVolume)
            throw new ArgumentException("Complete broker tick and volume specifications are required");
        if (request.SpreadPointsSeries is { } spreads && (spreads.Count != request.Candles.Count ||
            spreads.Any(value => !Nonnegative(value) || value > 10000)))
            throw new ArgumentException("Spread series must contain one finite nonnegative point value per entry candle");
        ValidateHigher(request.PhaseCandles, request.Candles, request.Parameters.EntryTimeframeMinutes,
            request.Parameters.PhaseTimeframeMinutes, request.Parameters.PhaseWarmupBars, s.TickSize);
        ValidateHigher(request.PatternCandles, request.Candles, request.Parameters.EntryTimeframeMinutes,
            request.Parameters.PatternTimeframeMinutes, request.Parameters.WarmupBars, s.TickSize);
    }

    private static void ValidateHigher(IReadOnlyList<MarketData>? supplied, IReadOnlyList<MarketData> entry,
        int entryMinutes, int higherMinutes, int required, double tickSize)
    {
        var aggregated = CandleAggregator.Aggregate(entry, entryMinutes, higherMinutes);
        var data = supplied ?? aggregated;
        if (data.Count < required || data.Count > 5000)
            throw new ArgumentException($"Supply native {higherMinutes}-minute closed candles with prehistory: at least {required}, at most 5000 bars");
        IndicatorCalculator.ValidateCandles(data);
        if (data.Any(c => c.OpenTime % (higherMinutes * 60L) != 0))
            throw new ArgumentException("Native higher-timeframe timestamps are not aligned");
        if (supplied is null) return;
        var observed = aggregated.ToDictionary(c => c.OpenTime);
        foreach (var bar in supplied)
            if (observed.TryGetValue(bar.OpenTime, out var same) &&
                (Math.Abs(bar.Open - same.Open) > tickSize / 2 || Math.Abs(bar.High - same.High) > tickSize / 2 ||
                 Math.Abs(bar.Low - same.Low) > tickSize / 2 || Math.Abs(bar.Close - same.Close) > tickSize / 2))
                throw new ArgumentException("Native higher-timeframe OHLC disagrees with complete entry observations");
    }
    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
}

public static class SimulatedExecution
{
    public sealed record Exit(double Price, string Reason);

    public static TradeDecision TrailStop(TradeDecision position, MarketData closed, SymbolDetails spec,
        double spread, StrategyParameters parameters)
    {
        if (!parameters.TrailingEnabled || !double.IsFinite(closed.Atr) || closed.Atr <= 0) return position;
        var buy = position.Action == ActionKind.Buy;
        var close = closed.Close + (buy ? 0 : spread);
        var profitDistance = (close - position.EntryPrice) * (buy ? 1 : -1);
        if (profitDistance < closed.Atr * parameters.TrailingActivationAtr) return position;
        var distance = Math.Max(closed.Atr * parameters.TrailingDistanceAtr,
            (Math.Max(spec.StopsLevel, spec.FreezeLevel) + 1) * spec.Point);
        var raw = close + (buy ? -distance : distance);
        var stop = (buy ? Math.Floor(raw / spec.TickSize) : Math.Ceiling(raw / spec.TickSize)) * spec.TickSize;
        var improvement = (stop - position.StopLossPrice) * (buy ? 1 : -1);
        return double.IsFinite(stop) && stop > 0 && improvement > parameters.TrailingStepPoints * spec.Point ?
            position with {StopLossPrice = stop} : position;
    }

    public static Exit? FindExit(TradeDecision position, MarketData bar, double spread, double slippage)
    {
        var buy = position.Action == ActionKind.Buy;
        var open = bar.Open + (buy ? 0 : spread);
        var low = bar.Low + (buy ? 0 : spread); var high = bar.High + (buy ? 0 : spread);
        if (buy ? open <= position.StopLossPrice : open >= position.StopLossPrice)
            return new(open + (buy ? -slippage : slippage), "StopLoss");
        if (position.TakeProfitPrice > 0 && (buy ? open >= position.TakeProfitPrice : open <= position.TakeProfitPrice))
            return new(position.TakeProfitPrice + (buy ? -slippage : slippage), "TakeProfit");
        if (buy ? low <= position.StopLossPrice : high >= position.StopLossPrice)
            return new((buy ? Math.Min(open, position.StopLossPrice) - slippage : Math.Max(open, position.StopLossPrice) + slippage), "StopLoss");
        if (position.TakeProfitPrice > 0 && (buy ? high >= position.TakeProfitPrice : low <= position.TakeProfitPrice))
            return new(position.TakeProfitPrice + (buy ? -slippage : slippage), "TakeProfit");
        return null;
    }
}
