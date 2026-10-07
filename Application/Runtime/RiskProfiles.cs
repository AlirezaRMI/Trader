namespace Application.Runtime;

/// <summary>A centrally configured preset for NEW orders; never changes existing broker protection.</summary>
public sealed record RiskProfile
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Description { get; init; } = "";
    public string FrequencyHint { get; init; } = "";
    public string ExposureHint { get; init; } = "";
    public bool ObservationOnly { get; init; }
    public double RiskPercent { get; init; }
    public double MaximumRiskAmount { get; init; }
    public int DailyTradeLimit { get; init; }
    public double MinimumEntryAdx { get; init; }
    public double AutoSwitchAdx { get; init; } = 45;
    public double WeakTrendAdx { get; init; } = 20;
    public double MomentumAdx { get; init; } = 45;
    public int MinimumZoneStrength { get; init; } = 2;
    public double StopAtrMultiplier { get; init; } = 1.5;

    public RuntimeSettings ApplyTo(RuntimeSettings current) => current with
    {
        RiskProfileId = Id, ObservationOnly = ObservationOnly, RiskPercent = RiskPercent,
        MaximumRiskAmount = MaximumRiskAmount, DailyTradeLimit = DailyTradeLimit,
        Strategy = current.Strategy with
        {
            Mode = "Auto", MinimumEntryAdx = MinimumEntryAdx, StrongTrendAdx = MinimumEntryAdx,
            AutoSwitchAdx = AutoSwitchAdx, WeakTrendAdx = WeakTrendAdx, MomentumAdx = MomentumAdx,
            MinimumZoneStrength = MinimumZoneStrength, StopAtrMultiplier = StopAtrMultiplier,
            RequireMacroTrend = true, RequireBreakoutRetest = true
        }
    };

    public bool Matches(RuntimeSettings settings) => ApplyTo(settings) == settings;
}

/// <summary>Preset definitions are read from configuration, not duplicated as frontend constants.</summary>
public sealed class RiskProfileCatalog
{
    public IReadOnlyList<RiskProfile> Profiles { get; }

    public RiskProfileCatalog(IReadOnlyList<RiskProfile> profiles)
    {
        if (profiles.Count is < 2 or > 10 || profiles.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) ||
            p.Id == "Custom" || string.IsNullOrWhiteSpace(p.Label) || string.IsNullOrWhiteSpace(p.Description)) ||
            profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != profiles.Count ||
            profiles.Count(p => p.ObservationOnly) != 1)
            throw new InvalidOperationException("Risk profile catalog requires unique named presets and one observation-only mode");
        foreach (var profile in profiles)
        {
            if (SettingsValidator.Validate(profile.ApplyTo(new())) is { } error)
                throw new InvalidOperationException($"Invalid risk profile {profile.Id}: {error}");
        }
        Profiles = Array.AsReadOnly(profiles.ToArray());
    }

    public RiskProfile? Find(string id) => Profiles.FirstOrDefault(p => p.Id == id);

    public RuntimeSettings Normalize(RuntimeSettings settings)
    {
        if (SettingsValidator.Validate(settings) is { } error) throw new ArgumentException(error);
        if (settings.RiskProfileId == "Custom") return settings;
        var profile = Find(settings.RiskProfileId) ?? throw new ArgumentException("Unknown risk profile");
        // Advanced edits are still allowed while paused, but must never keep a misleading preset label.
        return profile.Matches(settings) ? settings : settings with {RiskProfileId = "Custom"};
    }
}
