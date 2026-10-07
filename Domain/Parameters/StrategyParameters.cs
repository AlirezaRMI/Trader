namespace Domain.Parameters;

public sealed record StrategyParameters
{
    public string Mode { get; init; } = "Auto";
    public int FastEmaPeriod { get; init; } = 15;
    public int SlowEmaPeriod { get; init; } = 30;
    public int AtrPeriod { get; init; } = 14;
    public int AtrAveragePeriod { get; init; } = 20;
    public int AdxPeriod { get; init; } = 14;
    public double StrongTrendAdx { get; init; } = 25;
    public double AutoSwitchAdx { get; init; } = 45; // Engine preference, independent of the minimum entry ADX.
    public double WeakTrendAdx { get; init; } = 20;
    public double MomentumAdx { get; init; } = 45;
    public double AtrSpikeMultiplier { get; init; } = 1.5;
    public double StopAtrMultiplier { get; init; } = 1.5;
    public double TargetAtrMultiplier { get; init; } = 3;
    public double RewardRiskRatio { get; init; } = 2;
    public int SwingLookbackBars { get; init; } = 10;
    public double StructuralStopBufferAtr { get; init; } = .1;
    public int MacroFastEmaPeriod { get; init; } = 50;
    public int MacroSlowEmaPeriod { get; init; } = 200;
    public bool RequireMacroTrend { get; init; } = true;
    public bool RequireBreakoutRetest { get; init; } = true;
    public double MinimumEntryAdx { get; init; } = 25;
    public double MinimumBodyRatio { get; init; } = 0; // No unsupported threshold is invented.
    public double RetestToleranceAtr { get; init; } = .1;
    public double BreakoutBufferAtr { get; init; } = .05;
    public double CommissionPerLot { get; init; } = 0; // Round-trip account-currency assumption; configure broker costs.
    public double SlippageBufferPoints { get; init; } = 10; // Reserve per fill, in points, not pips.
    public bool TrailingEnabled { get; init; } = true;
    public double TrailingActivationAtr { get; init; } = 1;
    public double TrailingDistanceAtr { get; init; } = 1.2;
    public double TrailingStepPoints { get; init; } = 2;
    public int SpreadLookbackSamples { get; init; } = 50;
    public double SpreadSpikeMultiplier { get; init; } = 2;
    public double ZoneMergeAtr { get; init; } = .2;
    public int MinimumZoneStrength { get; init; } = 2;
    public int RetestExpiryBars { get; init; } = 48;
    public int EntryTimeframeMinutes { get; init; } = 5;
    public int PatternTimeframeMinutes { get; init; } = 15;
    public int PhaseTimeframeMinutes { get; init; } = 240;
    public int HistoryBars { get; init; } = 500;
    public int MaximumQuoteAgeSeconds { get; init; } = 120;
    public int WarmupBars => Math.Max(SlowEmaPeriod * 3, Math.Max(AtrPeriod + AtrAveragePeriod, AdxPeriod * 3));
    public int PhaseWarmupBars => RequireMacroTrend ? Math.Max(WarmupBars, MacroSlowEmaPeriod * 3) : WarmupBars;
    public int PhaseHistoryBars => Math.Max(HistoryBars, PhaseWarmupBars);

    public string? Validate()
    {
        if (Mode is not ("Auto" or "Indicator" or "PriceAction")) return "Invalid strategy mode";
        if (FastEmaPeriod < 2 || SlowEmaPeriod <= FastEmaPeriod || SlowEmaPeriod > 200 ||
            AtrPeriod is < 2 or > 100 || AtrAveragePeriod is < 2 or > 100 || AdxPeriod is < 2 or > 100 ||
            MacroFastEmaPeriod < 2 || MacroSlowEmaPeriod <= MacroFastEmaPeriod || MacroSlowEmaPeriod > 200)
            return "Invalid indicator periods";
        if (!double.IsFinite(StrongTrendAdx) || !double.IsFinite(WeakTrendAdx) || !double.IsFinite(MomentumAdx) ||
            WeakTrendAdx < 0 || StrongTrendAdx <= WeakTrendAdx || StrongTrendAdx > 100 || MomentumAdx < StrongTrendAdx || MomentumAdx > 100)
            return "Invalid trend thresholds";
        if (!Positive(AutoSwitchAdx) || AutoSwitchAdx > 100 || Mode == "Auto" &&
            (AutoSwitchAdx < MinimumEntryAdx || AutoSwitchAdx == MinimumEntryAdx && MinimumEntryAdx < 100))
            return "Auto engine-switch ADX must exceed the minimum entry ADX";
        if (!Positive(AtrSpikeMultiplier) || AtrSpikeMultiplier > 10 || !Positive(StopAtrMultiplier) || StopAtrMultiplier > 10 ||
            !Positive(TargetAtrMultiplier) || TargetAtrMultiplier > 20 ||
            !Positive(RewardRiskRatio) || RewardRiskRatio > 10 || !Positive(ZoneMergeAtr) || ZoneMergeAtr > 5 ||
            MinimumZoneStrength is < 2 or > 20 || RetestExpiryBars is < 1 or > 1000)
            return "Invalid stop, target or zone parameters";
        if (SwingLookbackBars is < 2 or > 200 || !Nonnegative(StructuralStopBufferAtr) || StructuralStopBufferAtr > 5 ||
            !Nonnegative(MinimumEntryAdx) || MinimumEntryAdx > 100 || !Nonnegative(MinimumBodyRatio) || MinimumBodyRatio > 1 ||
            !Nonnegative(RetestToleranceAtr) || RetestToleranceAtr > 5 || !Nonnegative(BreakoutBufferAtr) || BreakoutBufferAtr > 5 ||
            !Nonnegative(CommissionPerLot) || CommissionPerLot > 10000 || !Nonnegative(SlippageBufferPoints) || SlippageBufferPoints > 10000 ||
            !Positive(TrailingActivationAtr) || TrailingActivationAtr > 20 || !Positive(TrailingDistanceAtr) || TrailingDistanceAtr > 20 ||
            !Nonnegative(TrailingStepPoints) || TrailingStepPoints > 10000 ||
            SpreadLookbackSamples is < 2 or > 1000 || !Positive(SpreadSpikeMultiplier) || SpreadSpikeMultiplier > 10)
            return "Invalid confirmation, cost or trailing parameters";
        if (!ValidTimeframe(EntryTimeframeMinutes) || !ValidTimeframe(PatternTimeframeMinutes) || !ValidTimeframe(PhaseTimeframeMinutes) ||
            PatternTimeframeMinutes % EntryTimeframeMinutes != 0 || PhaseTimeframeMinutes % PatternTimeframeMinutes != 0 ||
            HistoryBars < Math.Max(WarmupBars, SwingLookbackBars) || HistoryBars > 1000 || MaximumQuoteAgeSeconds is < 1 or > 3600)
            return "Invalid timeframes, history or quote age";
        return null;
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
    private static bool ValidTimeframe(int value) => value is 1 or 5 or 15 or 30 or 60 or 240 or 1440;
}
