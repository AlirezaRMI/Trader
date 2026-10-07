using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Application.Broker;
using Application.Runtime;
using Application.Strategy;
using Domain.Enum;
using Domain.Services;
using Infrastructure.Telegram;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trader.Protocol;

namespace Infrastructure.Runtime;

public sealed class TradingRuntime : BackgroundService
{
    private readonly IBrokerGateway _broker;
    private readonly ITradeStore _store;
    private StrategyPipeline _pipeline = new();
    private readonly TradeExecutor _executor;
    private readonly ILogger<TradingRuntime> _logger;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _cycleGate = new(1, 1);
    private readonly Channel<CycleCommand> _commands = Channel.CreateBounded<CycleCommand>(new BoundedChannelOptions(32)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _commandPump;
    private int _historyBefore = -1;
    private int _historyTotal = -1;
    private DateTimeOffset _nextHistorySync = DateTimeOffset.MinValue;
    private DateTimeOffset _historyRetryAt = DateTimeOffset.MinValue;
    private sealed record CycleCommand(bool Force, CancellationToken Cancellation, TaskCompletionSource Completion);
    private CancellationTokenSource _entryStop = new();
    private RuntimeSettings _settings;
    private RuntimeSnapshot _snapshot;
    private bool _entriesEnabled;
    private bool _brokerTradingReady;
    private DateTimeOffset _nextManagement;
    private DateTimeOffset _nextAnalysis;
    private string? _historySymbol;
    private string? _activeChartSymbol;
    private long _historyCandle;
    private List<MarketData> _history = [];
    private RiskCheckpoint? _risk;

    public TradingRuntime(IBrokerGateway broker, ITradeStore store, MarketSupervisor supervisor,
        PriceActionAnalyzer analyzer, TradeExecutor executor, ILogger<TradingRuntime> logger)
    {
        _broker = broker;
        _store = store;
        _executor = executor;
        _logger = logger;
        _settings = store.LoadSettings();
        if (SettingsValidator.Validate(_settings) is { } error) throw new InvalidOperationException(error);
        _snapshot = new() {StartedAt = DateTimeOffset.UtcNow, Settings = _settings, Journal = store.ReadJournal()};
        _commandPump = ProcessCommandsAsync();
    }

    public RuntimeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                var connection = _broker.Connection;
                var visibleError = _snapshot.LastError ?? _snapshot.ExecutionPolicyError;
                var state = visibleError is not null ? "Degraded" :
                    !_entriesEnabled ? "Paused" :
                    !connection.TerminalConnected ? "Waiting" : "Running";
                return _snapshot with
                {
                    Broker = connection, EntriesEnabled = _entriesEnabled, State = state, Settings = _settings, LastError = visibleError
                };
            }
        }
    }

    public Task SetEntriesEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (enabled && _settings.ObservationOnly)
                throw new InvalidOperationException("حالت پایش فقط؛ برای ورود جدید یک حالت معاملاتی را انتخاب و اعمال کن.");
            if (enabled && (!_broker.Connection.TerminalConnected || !_brokerTradingReady ||
                            !_snapshot.OrdersEnabledAtTerminal ||
                            _snapshot.Account is null || _snapshot.LastCycleAt is null ||
                            DateTimeOffset.UtcNow - _snapshot.LastCycleAt > TimeSpan.FromSeconds(20)))
                throw new InvalidOperationException("Fresh broker state and enabled EA execution are required");
            if (enabled && _snapshot.ExecutionPolicyError is { } policyError)
                throw new InvalidOperationException(policyError);
            if (enabled && _snapshot.Account is {IsDemo: false} && !_settings.AllowLiveAccount)
                throw new InvalidOperationException("Live account execution is disabled");
            if (enabled && (_risk is null || _risk.AccountId != _snapshot.Account?.AccountId || _risk.BrokerServer != _snapshot.Account?.Server ||
                            _risk.BlocksEntries(DateTimeOffset.UtcNow)))
                throw new InvalidOperationException("Equity drawdown circuit breaker blocks new entries");
            if (_entriesEnabled == enabled) return Task.CompletedTask;
            _entriesEnabled = enabled;
            if (!enabled) _entryStop.Cancel();
            else _entryStop = new CancellationTokenSource();
            _snapshot = _snapshot with {Revision = _snapshot.Revision + 1};
        }

        _store.AppendJournal("Information", enabled ? "RuntimeResumed" : "RuntimePaused",
            enabled ? "New entries enabled" : "New entries paused; existing broker protection remains active");
        if (_settings.TelegramEnabled)
            _store.EnqueueNotification(NotificationFormatter.Event(
                enabled ? "ورود خودکار فعال شد" : "ورود خودکار متوقف شد",
                enabled
                    ? "ورود جدید فقط پس از تأیید سیگنال و محدودیت‌های ریسک انجام می‌شود. فعال‌بودن، به معنی بازشدن فوری معامله نیست."
                    : "ورود جدید متوقف شد؛ معاملات باز بسته نشدند. حد ضرر ثبت‌شده باقی است؛ تریلینگ به روشن‌بودن MT4 و فعال‌بودن EA وابسته است."));
        return Task.CompletedTask;
    }

    public void UpdateSettings(RuntimeSettings settings, RuntimeSettings expectedSettings) => ChangeSettings(settings, expectedSettings);

    public RuntimeSettings ApplyRiskProfile(RiskProfile profile, RuntimeSettings expectedSettings)
    {
        if (SettingsValidator.Validate(expectedSettings) is { } error) throw new ArgumentException(error);
        return ChangeSettings(profile.ApplyTo(expectedSettings), expectedSettings, riskProfileChange: true);
    }

    private RuntimeSettings ChangeSettings(RuntimeSettings settings, RuntimeSettings expectedSettings, bool riskProfileChange = false)
    {
        if (SettingsValidator.Validate(settings) is { } error) throw new ArgumentException(error);
        _cycleGate.Wait();
        try
        {
            lock (_sync)
            {
                if (_entriesEnabled) throw new InvalidOperationException("Pause new entries before changing settings");
                if (_settings != expectedSettings)
                    throw new InvalidOperationException("تنظیمات هم‌زمان تغییر کرده‌اند؛ پروفایل را با وضعیت تازه دوباره انتخاب کن.");
                _store.SaveSettings(settings);
                _settings = settings;
                _brokerTradingReady = false; // Recheck the EA policy against the newly saved settings before resuming.
                _nextAnalysis = DateTimeOffset.MinValue;
                _historySymbol = null;
                _pipeline = new();
                _historyBefore = -1;
                _historyTotal = -1;
                _nextHistorySync = DateTimeOffset.MinValue;
                _historyRetryAt = DateTimeOffset.MinValue;
                _snapshot = _snapshot with {Revision = _snapshot.Revision + 1, Analysis = null, LastCycleAt = null};
            }
            _store.AppendJournal("Information", riskProfileChange ? "RiskProfileChanged" : "SettingsChanged",
                riskProfileChange ? "پروفایل ریسک اعمال شد؛ ورود خودکار همچنان متوقف است." : "Runtime settings updated",
                detail: JsonSerializer.Serialize(new {settings.RiskProfileId, settings.ObservationOnly, settings.RiskPercent,
                    settings.MaximumRiskAmount, settings.DailyTradeLimit, settings.Strategy}, WebSocketFrames.JsonOptions));
            return settings;
        }
        finally { _cycleGate.Release(); }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => QueueCycleAsync(true, cancellationToken);

    public async Task ResetRiskAsync(CancellationToken cancellationToken = default)
    {
        await _cycleGate.WaitAsync(cancellationToken);
        try
        {
            lock (_sync)
            {
                if (_entriesEnabled || !_broker.Connection.TerminalConnected || !_brokerTradingReady || _snapshot.Account is null || _snapshot.LastCycleAt is null ||
                    DateTimeOffset.UtcNow - _snapshot.LastCycleAt > TimeSpan.FromSeconds(20))
                    throw new InvalidOperationException("Pause entries and obtain a fresh account snapshot before explicitly resetting risk");
                var account = _snapshot.Account;
                if (account.Equity <= 0) throw new InvalidOperationException("Positive equity is required for a risk reset");
                var checkpoint = new RiskCheckpoint(account.AccountId, account.Server,
                    new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero), account.Equity, account.Equity, null, false);
                _store.AppendJournal("Warning", "RiskReset", "ریست دستی مرجع افت سرمایه؛ ورود همچنان متوقف است.");
                SaveRisk(checkpoint); // Keep the same exclusion boundary as SetEntriesEnabled through persistence.
            }
        }
        finally { _cycleGate.Release(); }
    }

    private void SaveRisk(RiskCheckpoint checkpoint)
    {
        _store.AppendJournal("Information", "RiskCheckpoint", "مرجع و محدودیت افت سرمایه ذخیره شد.",
            detail: JsonSerializer.Serialize(checkpoint, WebSocketFrames.JsonOptions));
        lock (_sync) { _risk = checkpoint; _snapshot = _snapshot with {Risk = checkpoint}; }
    }

    private void ObserveRisk(AccountInfo account)
    {
        var now = DateTimeOffset.UtcNow;
        var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var saved = _store.ReadLatestJournal("RiskCheckpoint", account.AccountId, account.Server);
        var old = saved?.Detail is { } detail ? Decode<RiskCheckpoint>(JsonSerializer.Deserialize<JsonElement>(detail)) : null;
        if (old is null)
        {
            old = new(account.AccountId, account.Server, day, account.Equity, account.Equity, null, false);
        }
        if (old.TotalPeak <= 0) throw new InvalidOperationException("A positive account equity reference is required");
        var checkpoint = old.Observe(account.Equity, now, _settings.DailyDrawdownLimitPercent,
            _settings.TotalDrawdownLimitPercent, _settings.DrawdownCooldownHours);
        var halted = checkpoint.Halted; var until = checkpoint.BlockedUntil;
        if (_risk != checkpoint) SaveRisk(checkpoint);
        if (halted || until > now)
        {
            var newlyBlocked = !old.Halted && (halted || !(old.BlockedUntil > now));
            lock (_sync) { _entriesEnabled = false; _entryStop.Cancel(); }
            if (newlyBlocked)
            {
                var reason = halted ? "سقف افت کل سرمایه فعال شد؛ ورود تا ریست دستی متوقف است." : "سقف افت روزانه فعال شد؛ ورود تا پایان دورهٔ توقف مسدود است.";
                _store.AppendJournal("Warning", "RiskCircuitBreaker", reason);
                if (_settings.TelegramEnabled) _store.EnqueueNotification(NotificationFormatter.Event("محافظت سرمایه", reason));
            }
        }
    }

    private async Task QueueCycleAsync(bool force, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _commands.Writer.WriteAsync(new(force, cancellationToken, completion), stop.Token);
        await completion.Task.WaitAsync(stop.Token);
    }

    private async Task ProcessCommandsAsync()
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(_lifetime.Token))
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(command.Cancellation, _lifetime.Token);
                try { await CycleAsync(command.Force, stop.Token); command.Completion.TrySetResult(); }
                catch (OperationCanceledException) { command.Completion.TrySetCanceled(stop.Token); }
                catch (Exception error) { command.Completion.TrySetException(error); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally
        {
            while (_commands.Reader.TryRead(out var pending)) pending.Completion.TrySetCanceled(_lifetime.Token);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _store.AppendJournal("Information", "RuntimeStarted", "Runtime started paused; awaiting MT4 agent");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                await QueueCycleAsync(false, stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _lifetime.Cancel();
        _commands.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
        await _commandPump.WaitAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _lifetime.Cancel();
        _commands.Writer.TryComplete();
        _entryStop.Cancel();
        base.Dispose();
    }

    private async Task CycleAsync(bool force, CancellationToken cancellationToken)
    {
        await _cycleGate.WaitAsync(cancellationToken);
        var start = Stopwatch.GetTimestamp();
        try
        {
            var now = DateTimeOffset.UtcNow;
            RuntimeSettings settings;
            lock (_sync) settings = _settings;
            if (force || now >= _nextManagement)
            {
                _nextManagement = now.AddSeconds(settings.PositionIntervalSeconds);
                await RefreshBrokerAsync(cancellationToken);
            }

            if (force || now >= _nextAnalysis)
            {
                _nextAnalysis = now.AddSeconds(settings.AnalysisIntervalSeconds);
                if (_brokerTradingReady) await AnalyzeAsync(settings, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is BrokerException or IOException or JsonException or KeyNotFoundException
                                          or InvalidOperationException or ArgumentException)
        {
            lock (_sync)
            {
                _brokerTradingReady = false;
                if (_snapshot.LastError != error.Message)
                {
                    _store.AppendJournal("Error", "RuntimeError", error.Message);
                    _logger.LogWarning("Trading cycle could not complete: {Reason}", error.Message);
                }

                _snapshot = _snapshot with {LastError = error.Message, Revision = _snapshot.Revision + 1};
            }
        }
        finally
        {
            lock (_sync)
                _snapshot = _snapshot with
                {
                    Journal = _store.ReadJournal(),
                    LastCycleDurationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds
                };
            _cycleGate.Release();
        }
    }

    private async Task RefreshBrokerAsync(CancellationToken cancellationToken)
    {
        lock (_sync) _brokerTradingReady = false;
        if (!_broker.Connection.AgentConnected) throw new BrokerException("MT4 agent is not connected");
        var ping = await _broker.InvokeAsync("PING", "", cancellationToken);
        var ready = ping.TryGetProperty("connected", out var connected) && connected.GetBoolean();
        var policyError = ExecutionPolicyError(ping, _settings);
        var account = Decode<AccountInfo>(await _broker.InvokeAsync("GET_ACCOUNT", "", cancellationToken));
        _ = BrokerContext.Bind(account, "");
        if (account.AccountId <= 0 || !double.IsFinite(account.Equity) || !double.IsFinite(account.Balance) ||
            !double.IsFinite(account.FreeMargin))
            throw new BrokerException("Invalid broker account state");
        IReadOnlyList<BrokerPosition> previous;
        lock (_sync)
        {
            var switched = _snapshot.Account is { } old &&
                (old.AccountId != account.AccountId || old.Server != account.Server || old.TerminalSession != account.TerminalSession);
            if (switched)
            {
                _entriesEnabled = false;
                _entryStop.Cancel();
                _historySymbol = null;
                _pipeline = new();
            }

            previous = _store.ReadPositions(account.AccountId, account.Server)
                .Concat(_snapshot.Account?.AccountId == account.AccountId && _snapshot.Account.Server == account.Server
                    ? _snapshot.Positions : []).DistinctBy(p => p.Ticket).ToArray();
            if (switched) _snapshot = _snapshot with { Analysis = null, Account = null, Positions = [], LastCycleAt = null };
        }

        var positions = Decode<List<BrokerPosition>>(await BrokerContext.InvokeAsync(_broker, account, "GET_POSITIONS", "", cancellationToken));
        if (positions.Any(p => !BrokerPositionValidation.IsValid(p, closed: false)) ||
            positions.Select(p => p.Ticket).Distinct().Count() != positions.Count)
            throw new BrokerException("Invalid broker position snapshot");
        await _executor.ReconcileAsync(account, cancellationToken);
        foreach (var position in positions)
            _store.RecordTradeContext(new(account.AccountId, account.Server, position.Ticket, account.Currency, account.IsDemo));
        var tracked = previous.Select(p => (p.Ticket, p.Symbol))
            .Concat(_store.OutstandingTickets(account.AccountId, account.Server)).DistinctBy(p => p.Ticket);
        string? closureError = null;
        foreach (var missing in tracked.Where(old => positions.All(p => p.Ticket != old.Ticket)))
        {
            var closed = await BrokerContext.InvokeAsync(_broker, account, "GET_CLOSED",
                missing.Ticket.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
            if (!closed.TryGetProperty("found", out var found) || !found.GetBoolean())
            {
                closureError ??= $"Closure of ticket {missing.Ticket} is not confirmed in broker history";
                continue; // Retain the old persisted watchlist; absence is never fabricated as a closure.
            }
            var order = Decode<BrokerPosition>(closed.GetProperty("order"));
            if (order.Ticket != missing.Ticket || order.CloseTime <= 0 || order.Symbol != missing.Symbol)
                throw new BrokerException("Closed position ownership mismatch");
            _store.RecordTradeContext(new(account.AccountId, account.Server, order.Ticket, account.Currency, account.IsDemo));
            _store.RecordClosure(account.AccountId, account.Server, order, account.Currency,
                _settings.TelegramEnabled ? NotificationFormatter.Position("معامله بسته شد", order, account.Currency) : null);
        }

        await SynchronizeHistoryAsync(account, ping, cancellationToken);
        if (closureError is null) _store.SavePositions(account.AccountId, positions, account.Server);
        else if (Snapshot.LastError != closureError) _store.AppendJournal("Warning", "ClosureUnconfirmed", closureError);
        lock (_sync)
        {
            _brokerTradingReady = ready && closureError is null; // Market reads remain available during deployment-policy mismatches.
            if (policyError is not null) { _entriesEnabled = false; _entryStop.Cancel(); }
            _snapshot = _snapshot with
            {
                Revision = _snapshot.Revision + 1, Account = account, Positions = positions.ToArray(),
                UnresolvedIntents = _store.Unresolved(account.AccountId, account.Server), LastCycleAt = DateTimeOffset.UtcNow,
                LastError = closureError ?? (ready ? null : "MT4 is disconnected from its broker"), ExecutionPolicyError = policyError,
                OrdersEnabledAtTerminal = ping.TryGetProperty("ordersEnabled", out var orders) && orders.GetBoolean(),
                TrailingEnabledAtTerminal =
                ping.TryGetProperty("trailingEnabled", out var trailing) && trailing.GetBoolean(),
                ConfirmedTradesToday = _store.ConfirmedToday(account.AccountId, DateTimeOffset.UtcNow, account.Server)
            };
            if (ping.TryGetProperty("symbol", out var symbol))
                _activeChartSymbol = symbol.GetString();
        }
        ObserveRisk(account);
    }

    private static string? ExecutionPolicyError(JsonElement ping, RuntimeSettings settings)
    {
        if (!ping.TryGetProperty("tradingLogicVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var number) || number < 1)
            return "برای منطق جدید، EA نسخهٔ 3.10 را کامپایل و دوباره بارگذاری کن؛ ورود جدید مسدود است.";
        var p = settings.Strategy;
        bool Matches(string key, double expected) => ping.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var actual) && double.IsFinite(actual) && Math.Abs(actual - expected) <= 1e-7 * Math.Max(1, Math.Abs(expected));
        if (!ping.TryGetProperty("trailingEnabled", out var trailing) || trailing.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            trailing.GetBoolean() != p.TrailingEnabled || p.TrailingEnabled &&
            (!Matches("trailingAtrPeriod", p.AtrPeriod) || !Matches("trailingHistoryBars", p.HistoryBars) || !Matches("trailingTimeframe", p.EntryTimeframeMinutes) ||
             !Matches("trailingActivationAtr", p.TrailingActivationAtr) || !Matches("trailingDistanceAtr", p.TrailingDistanceAtr) ||
             !Matches("trailingStepPoints", p.TrailingStepPoints)))
            return "تنظیمات تریلینگ EA و داشبورد یکسان نیست؛ هر دو را هماهنگ کن. ورود جدید مسدود است.";
        if (!Matches("commissionPerLot", p.CommissionPerLot) || !Matches("slippagePoints", p.SlippageBufferPoints))
            return "کارمزد یا لغزش EA و داشبورد یکسان نیست؛ CommissionPerLot و SlippagePoints را هماهنگ کن. ورود جدید مسدود است.";
        if (!Matches("dailyDrawdownLimitPercent", settings.DailyDrawdownLimitPercent) ||
            !Matches("totalDrawdownLimitPercent", settings.TotalDrawdownLimitPercent) || !Matches("drawdownCooldownHours", settings.DrawdownCooldownHours))
            return "حدهای افت سرمایه EA و داشبورد یکسان نیست؛ هر دو را هماهنگ کن. ورود جدید مسدود است.";
        return null;
    }

    private sealed record BrokerHistoryPage(List<BrokerPosition> Orders, int NextIndex, int Total);

    private async Task SynchronizeHistoryAsync(AccountInfo account, JsonElement ping, CancellationToken cancellationToken)
    {
        var supported = ping.TryGetProperty("tradeHistoryVersion", out var version) &&
                        version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var value) && value >= 1;
        if (!supported)
        {
            lock (_sync) _snapshot = _snapshot with {HistorySync = new(false, false, 0, 0, null,
                "برای بازیابی کامل سابقه، نسخهٔ جدید EA را کامپایل و روی نمودار بارگذاری کن.")};
            return;
        }

        if (ping.TryGetProperty("loadedHistoryRows", out var count) && count.ValueKind == JsonValueKind.Number &&
            count.TryGetInt32(out var loaded) && loaded != _historyTotal)
        { _historyBefore = -1; _nextHistorySync = DateTimeOffset.MinValue; }
        if (DateTimeOffset.UtcNow < _historyRetryAt || _historyBefore == 0 && DateTimeOffset.UtcNow < _nextHistorySync) return;
        try
        {
            var page = Decode<BrokerHistoryPage>(await BrokerContext.InvokeAsync(_broker, account, "GET_CLOSED_HISTORY",
                $"{(_historyBefore == 0 ? -1 : _historyBefore)},50", cancellationToken));
            if (page.Orders is null || page.Orders.Count > 50 || page.Total < 0 || page.NextIndex < 0 ||
                page.NextIndex > Math.Max(0, Math.Min(_historyBefore <= 0 ? page.Total : _historyBefore, page.Total) - 1) ||
                page.Orders.Any(order => !BrokerPositionValidation.IsValid(order, closed: true)) ||
                page.Orders.Select(order => order.Ticket).Distinct().Count() != page.Orders.Count)
                throw new BrokerException("Invalid broker trade history page");
            foreach (var order in page.Orders)
            {
                _store.RecordTradeContext(new(account.AccountId, account.Server, order.Ticket, account.Currency, account.IsDemo)
                    {Source = "BrokerHistory"});
                _store.RecordClosure(account.AccountId, account.Server, order, account.Currency, notification: null);
            }

            _historyBefore = page.NextIndex;
            _historyTotal = page.Total;
            _historyRetryAt = DateTimeOffset.MinValue;
            _nextHistorySync = DateTimeOffset.UtcNow.AddSeconds(60);
            lock (_sync) _snapshot = _snapshot with {HistorySync = new(true, page.NextIndex > 0, page.Total, page.NextIndex,
                DateTimeOffset.UtcNow, null)};
        }
        catch (Exception error) when (error is BrokerException or JsonException or ArgumentException)
        {
            // History retrieval is read-only; its failure must not invent closures or replay orders.
            var message = "بازیابی سابقه کامل نشد؛ اتصال و بازهٔ Account History متاتریدر را بررسی کن.";
            if (Snapshot.HistorySync.Error != message) _store.AppendJournal("Warning", "HistorySyncFailed", message);
            _historyRetryAt = DateTimeOffset.UtcNow.AddSeconds(60);
            lock (_sync) _snapshot = _snapshot with {HistorySync = _snapshot.HistorySync with {Supported = true, Error = message}};
        }
    }

    private async Task AnalyzeAsync(RuntimeSettings settings, CancellationToken cancellationToken)
    {
        var current = Snapshot;
        var account = current.Account ?? throw new BrokerException("No current account");
        var symbolName = string.IsNullOrEmpty(settings.Symbol) ? _activeChartSymbol : settings.Symbol;
        if (string.IsNullOrWhiteSpace(symbolName)) throw new BrokerException("No active trading symbol");
        var spec = Decode<SymbolDetails>(await BrokerContext.InvokeAsync(_broker, account, "GET_SPEC", symbolName, cancellationToken));
        if (spec.SymbolName != symbolName || spec.Point <= 0 || spec.TickSize <= 0 || spec.TickValue <= 0 ||
            spec.StepVolume <= 0)
            throw new BrokerException("Broker tick and volume metadata is invalid");
        var parameters = settings.Strategy;
        var quote = Decode<MarketData>(await BrokerContext.InvokeAsync(_broker, account, "GET_MARKET",
            symbolName + "," + parameters.EntryTimeframeMinutes, cancellationToken));
        if (!ValidMarket(quote)) throw new BrokerException("Incomplete or invalid closed-candle market data");
        var phaseHistory = Decode<List<MarketData>>(await BrokerContext.InvokeAsync(_broker, account, "GET_HISTORY",
            $"{symbolName},{parameters.PhaseTimeframeMinutes},{parameters.PhaseHistoryBars}", cancellationToken));
        var patternHistory = Decode<List<MarketData>>(await BrokerContext.InvokeAsync(_broker, account, "GET_HISTORY",
            $"{symbolName},{parameters.PatternTimeframeMinutes},{parameters.HistoryBars}", cancellationToken));
        if (_historyCandle != quote.OpenTime || _historySymbol != symbolName || _history.Count < parameters.WarmupBars)
        {
            _history = Decode<List<MarketData>>(await BrokerContext.InvokeAsync(_broker, account, "GET_HISTORY",
                $"{symbolName},{parameters.EntryTimeframeMinutes},{parameters.HistoryBars}", cancellationToken));
            _historyCandle = quote.OpenTime;
            _historySymbol = symbolName;
        }

        if (_history.Count < parameters.WarmupBars || _history[0].OpenTime != quote.OpenTime)
            throw new BrokerException("History and entry candle are not aligned");
        // Account for history latency and reject any candle boundary crossed during the read.
        var quoteReadStarted = Stopwatch.GetTimestamp();
        var freshQuote = Decode<MarketData>(await BrokerContext.InvokeAsync(_broker, account, "GET_MARKET",
            symbolName + "," + parameters.EntryTimeframeMinutes, cancellationToken));
        if (!ValidMarket(freshQuote) || freshQuote.OpenTime != quote.OpenTime)
            throw new BrokerException("Entry candle changed while reading history; retry with a consistent snapshot");
        quote = freshQuote;
        var effectiveQuoteAge = (int)Math.Min(int.MaxValue,
            quote.QuoteAgeSeconds + Math.Ceiling(Stopwatch.GetElapsedTime(quoteReadStarted).TotalSeconds));
        var evaluated = _pipeline.Evaluate(new StrategyInput(account, spec, phaseHistory.AsEnumerable().Reverse().ToArray(),
            patternHistory.AsEnumerable().Reverse().ToArray(), _history.AsEnumerable().Reverse().ToArray(),
            quote.Bid, quote.Ask, effectiveQuoteAge, _store.LastConfirmedSignal(account.AccountId, symbolName, account.Server),
            parameters, settings.RiskPercent, settings.MaximumRiskAmount) {MaximumSpreadPoints = settings.MaximumSpreadPoints});
        var phase = evaluated.Phase; var pattern = evaluated.Pattern; var entry = evaluated.Entry;
        var zones = evaluated.Zones;
        var decision = evaluated.Decision;
        var spread = (entry.Ask - entry.Bid) / spec.Point;
        string? gate = current.ExecutionPolicyError ?? (settings.ObservationOnly ? "Observation mode blocks all new entries" :
            !current.EntriesEnabled ? "New entries are paused" :
            _risk?.Halted == true || _risk?.BlockedUntil > DateTimeOffset.UtcNow ? "Equity drawdown circuit breaker blocks new entries" :
            !account.IsDemo && !settings.AllowLiveAccount ? "Live account execution is disabled" :
            !current.OrdersEnabledAtTerminal ? "EA execution is disabled" :
            entry.QuoteAgeSeconds > parameters.MaximumQuoteAgeSeconds ? "Market quote is stale" :
            spread > settings.MaximumSpreadPoints ? "Spread exceeds the configured limit" :
            current.Positions.Any(p => p.Symbol == symbolName) ? "An owned position already exists on this symbol" :
            current.UnresolvedIntents.Any(i => i.Symbol == symbolName) ? "An uncertain order blocks new entries" :
            current.ConfirmedTradesToday >= settings.DailyTradeLimit ? "Daily trade limit reached" : null);
        if (decision.Action != ActionKind.Hold)
        {
            var lots = PositionSizer.CalculateLots(account.Equity, spec, decision.EntryPrice, decision.StopLossPrice,
                settings.RiskPercent / 100, settings.MaximumRiskAmount, parameters.CommissionPerLot, parameters.SlippageBufferPoints);
            lots = Math.Min(decision.PositionSizeLots, lots); // Execution guards may reduce, never replace or enlarge an allocation.
            if (lots <= 0) gate = "Broker minimum volume exceeds the configured risk budget";
            decision = decision with {PositionSizeLots = lots};
        }

        var reason = gate ?? decision.Note;
        var risk = decision.Action == ActionKind.Hold
            ? 0
            : PositionSizer.RiskPerLot(spec, decision.EntryPrice, decision.StopLossPrice,
                parameters.CommissionPerLot, parameters.SlippageBufferPoints) *
              decision.PositionSizeLots;
        var analysis = new AnalysisSnapshot(symbolName, evaluated.Engine,
            decision.Action.ToString(), reason, DateTimeOffset.UtcNow, phase, pattern, entry, spec,
            zones.ToArray(), _history.ToArray(), decision, risk, spread);
        lock (_sync)
            _snapshot = _snapshot with
            {
                Analysis = analysis, LastAnalysisAt = DateTimeOffset.UtcNow, Revision = _snapshot.Revision + 1
            };
        _store.AppendJournal("Information", "Analysis", reason, symbolName,
            JsonSerializer.Serialize(new {analysis.Engine, analysis.Action, risk, spread}));
        if (gate is not null || decision.Action == ActionKind.Hold) return;
        CancellationToken entryToken;
        lock (_sync)
        {
            if (!_entriesEnabled || _settings != settings) return;
            entryToken = _entryStop.Token;
        }

        using var executionStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entryToken);
        var remainingQuoteLife = parameters.MaximumQuoteAgeSeconds - quote.QuoteAgeSeconds -
            Stopwatch.GetElapsedTime(quoteReadStarted).TotalSeconds;
        if (remainingQuoteLife <= 0)
        {
            lock (_sync) _snapshot = _snapshot with { Analysis = analysis with { Reason = "Market quote is stale" }, Revision = _snapshot.Revision + 1 };
            return;
        }
        executionStop.CancelAfter(TimeSpan.FromSeconds(remainingQuoteLife));
        var result = await _executor.ExecuteAsync(account, symbolName, decision, entry.OpenTime,
            settings.DailyTradeLimit, settings.RiskPercent, settings.MaximumRiskAmount, executionStop.Token,
            parameters.MaximumQuoteAgeSeconds);
        lock (_sync)
            _snapshot = _snapshot with
            {
                UnresolvedIntents = _store.Unresolved(account.AccountId, account.Server),
                ConfirmedTradesToday = _store.ConfirmedToday(account.AccountId, DateTimeOffset.UtcNow, account.Server),
                Analysis = analysis with {Reason = result.Note}, Revision = _snapshot.Revision + 1
            };
        _nextManagement = DateTimeOffset.MinValue;
    }

    private static T Decode<T>(JsonElement value) => value.Deserialize<T>(WebSocketFrames.JsonOptions)
                                                     ?? throw new BrokerException("Empty broker response");

    private static bool ValidMarket(MarketData candle) => candle.OpenTime > 0 &&
                                                          double.IsFinite(candle.Open) &&
                                                          double.IsFinite(candle.Close) &&
                                                          double.IsFinite(candle.High) && double.IsFinite(candle.Low) &&
                                                          candle.Low > 0 &&
                                                          candle.High >= Math.Max(candle.Open, candle.Close) &&
                                                          candle.Low <= Math.Min(candle.Open, candle.Close) &&
                                                          double.IsFinite(candle.Bid) && double.IsFinite(candle.Ask) &&
                                                          candle.Bid > 0 && candle.Ask >= candle.Bid && candle.QuoteAgeSeconds >= 0;
}
