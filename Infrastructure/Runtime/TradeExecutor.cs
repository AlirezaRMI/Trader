using Application.Broker;
using Application.Runtime;
using Domain.Enum;
using Domain.Services;
using Infrastructure.Telegram;
using System.Globalization;
using System.Text.Json;
using Trader.Protocol;

namespace Infrastructure.Runtime;

public sealed class TradeExecutor(IBrokerGateway broker, ITradeStore store)
{
    public async Task<ExecutionResult> ExecuteAsync(AccountInfo account, string symbol, TradeDecision decision,
        long candleTime,
        int dailyLimit, double riskPercent, double maximumRiskAmount, CancellationToken cancellationToken = default,
        int maximumQuoteAgeSeconds = 120)
    {
        if (decision.Action == ActionKind.Hold) return new("Skipped", null, decision.Note);
        if (maximumQuoteAgeSeconds is < 1 or > 3600) return new("Failed", null, "Invalid quote freshness limit");
        if (!double.IsFinite(decision.PositionSizeLots) || decision.PositionSizeLots <= 0 ||
            !double.IsFinite(decision.StopLossPrice) || decision.StopLossPrice <= 0 ||
            !double.IsFinite(decision.EntryPrice) || decision.EntryPrice <= 0 || !double.IsFinite(decision.TakeProfitPrice) || decision.TakeProfitPrice <= 0 ||
            (decision.Action == ActionKind.Buy
                ? decision.StopLossPrice >= decision.EntryPrice
                : decision.StopLossPrice <= decision.EntryPrice))
            return new("Failed", null, "Invalid execution prices or volume");
        var settings = store.LoadSettings();
        if (settings.ObservationOnly) return new("Blocked", null, "Observation mode blocks all new entries");
        var freshAccountJson = await broker.InvokeAsync("GET_ACCOUNT", "", cancellationToken);
        var freshAccount = freshAccountJson.Deserialize<AccountInfo>(WebSocketFrames.JsonOptions) ?? throw new BrokerException("Invalid pre-order account");
        if (freshAccount.AccountId != account.AccountId || freshAccount.Server != account.Server || freshAccount.TerminalSession != account.TerminalSession)
            return new("Blocked", null, "Broker account/server/session changed");
        if (!freshAccount.IsDemo && !settings.AllowLiveAccount) return new("Blocked", null, "Live account execution is disabled");
        var now = DateTimeOffset.UtcNow;
        var savedRisk = store.ReadLatestJournal("RiskCheckpoint", account.AccountId, account.Server);
        if (savedRisk?.Detail is not { } riskJson) return new("Blocked", null, "A persisted equity reference is required before execution");
        var risk = JsonSerializer.Deserialize<RiskCheckpoint>(riskJson, WebSocketFrames.JsonOptions) ?? throw new BrokerException("Invalid persisted equity reference");
        var observedRisk = risk.Observe(freshAccount.Equity, now, settings.DailyDrawdownLimitPercent,
            settings.TotalDrawdownLimitPercent, settings.DrawdownCooldownHours);
        if (observedRisk != risk) store.AppendJournal("Information", "RiskCheckpoint", "مرجع سرمایه پیش از سفارش بازبینی شد.",
            detail: JsonSerializer.Serialize(observedRisk, WebSocketFrames.JsonOptions));
        if (observedRisk.BlocksEntries(now)) return new("Blocked", null, "Equity drawdown circuit breaker blocks new entries");
        var specificationJson = await BrokerContext.InvokeAsync(broker, account, "GET_SPEC", symbol, cancellationToken);
        var specification = specificationJson.Deserialize<SymbolDetails>(WebSocketFrames.JsonOptions) ?? throw new BrokerException("Invalid pre-order specification");
        var quoteJson = await BrokerContext.InvokeAsync(broker, account, "GET_MARKET", symbol + "," + settings.Strategy.EntryTimeframeMinutes, cancellationToken);
        var quote = quoteJson.Deserialize<MarketData>(WebSocketFrames.JsonOptions) ?? throw new BrokerException("Invalid pre-order quote");
        if (specification.SymbolName != symbol || quote.OpenTime != candleTime || quote.QuoteAgeSeconds < 0 ||
            quote.QuoteAgeSeconds > maximumQuoteAgeSeconds || !double.IsFinite(quote.Bid) || !double.IsFinite(quote.Ask) || quote.Bid <= 0 || quote.Ask < quote.Bid)
            return new("Blocked", null, "Entry candle or broker quote changed before execution");
        if (specification.Point <= 0 || (quote.Ask - quote.Bid) / specification.Point > settings.MaximumSpreadPoints)
            return new("Blocked", null, "Spread exceeds the configured limit");
        var price = decision.Action == ActionKind.Buy ? quote.Ask : quote.Bid;
        if (decision.Action == ActionKind.Buy ? decision.StopLossPrice >= price || decision.TakeProfitPrice <= price :
            decision.StopLossPrice <= price || decision.TakeProfitPrice >= price)
            return new("Blocked", null, "Protection direction changed before execution");
        var cap = PositionSizer.CalculateLots(freshAccount.Equity, specification, price, decision.StopLossPrice,
            riskPercent / 100, maximumRiskAmount, settings.Strategy.CommissionPerLot, settings.Strategy.SlippageBufferPoints);
        var riskPerLot = PositionSizer.RiskPerLot(specification, price, decision.StopLossPrice,
            settings.Strategy.CommissionPerLot, settings.Strategy.SlippageBufferPoints);
        var rewardPerLot = Math.Abs(decision.TakeProfitPrice - price) / specification.TickSize * specification.TickValue -
            settings.Strategy.CommissionPerLot - 2 * settings.Strategy.SlippageBufferPoints * specification.Point / specification.TickSize * specification.TickValue;
        if (cap <= 0 || !double.IsFinite(rewardPerLot) || rewardPerLot + .00000001 < settings.Strategy.RewardRiskRatio * riskPerLot)
            return new("Blocked", null, "Fresh quote violates the risk budget or net reward/risk floor");
        decision = decision with {PositionSizeLots = Math.Min(decision.PositionSizeLots, cap)};
        if (store.Unresolved(account.AccountId, account.Server).Any(intent => intent.Symbol == symbol))
            return new("Blocked", null, "An unresolved order must be reconciled first");
        cancellationToken.ThrowIfCancellationRequested();
        var intent = new TradeIntent(Guid.NewGuid(), account.AccountId, symbol, candleTime, decision.Action.ToString(),
            DateTimeOffset.UtcNow, "Pending", null, null) {BrokerServer = account.Server};
        if (!store.TryCreateIntent(intent, dailyLimit))
            return new("Blocked", null, "Signal already reserved or daily capacity reached");
        var arguments = string.Create(CultureInfo.InvariantCulture,
            $"{symbol},{(decision.Action == ActionKind.Buy ? "BUY" : "SELL")},{decision.PositionSizeLots:G17},{decision.StopLossPrice:G17},{decision.TakeProfitPrice:G17},{riskPercent / 100:G17},{maximumRiskAmount:G17},{maximumQuoteAgeSeconds}");
        try
        {
            var reply = BrokerContext.Unwrap(account, await broker.ExecuteAsync(intent.Id, "OPEN_ORDER",
                BrokerContext.Bind(account, arguments), cancellationToken), mutation: true);
            if (!reply.TryGetProperty("ticket", out var value) || !value.TryGetInt64(out var ticket) || ticket <= 0)
                throw new BrokerException("Invalid acknowledgement after order dispatch", true);
            store.SetOutcome(intent.Id, "Confirmed", ticket, "Broker acknowledged execution");
            var context = new TradeContext(account.AccountId, account.Server, ticket, account.Currency, account.IsDemo)
            {
                IntentId = intent.Id, SignalCandleTime = candleTime, RequestedAt = intent.CreatedAt,
                Decision = decision, Settings = store.LoadSettings(), Source = "OrderAcknowledgement"
            };
            store.RecordTradeContext(context);
            store.AppendJournal("Information", "TradeOpened",
                $"معامله #{ticket} تأیید شد؛ علت سیگنال در جزئیات ثبت شد.", symbol,
                JsonSerializer.Serialize(context, WebSocketFrames.JsonOptions));
            if (store.LoadSettings().TelegramEnabled)
                store.EnqueueNotification(NotificationFormatter.Position("معامله تأیید شد", new(ticket, symbol,
                    decision.Action.ToString(),
                    decision.PositionSizeLots, decision.EntryPrice, decision.StopLossPrice, decision.TakeProfitPrice, 0,
                    candleTime, 0, ""), account.Currency));
            return new("Confirmed", ticket, "Broker acknowledged execution");
        }
        catch (BrokerException error)
        {
            var status = error.MayHaveExecuted ? "Unknown" : "Failed";
            store.SetOutcome(intent.Id, status, null, error.Message);
            store.AppendJournal("Error", "Execution" + status, error.Message, symbol, intent.Id.ToString("N"));
            if (store.LoadSettings().TelegramEnabled)
                store.EnqueueNotification(NotificationFormatter.Event(
                    status == "Unknown" ? "نتیجه سفارش نامشخص" : "سفارش رد شد",
                    (status == "Unknown"
                        ? "تأیید قطعی سفارش دریافت نشد؛ سفارش کورکورانه تکرار نمی‌شود. تطبیق با بروکر ادامه دارد."
                        : "ارسال سفارش ناموفق بود و معامله‌ای تأیید نشد؛ پیش از تلاش بعدی علت را بررسی کن.") +
                    "\n\nجزئیات فنی: " + error.Message, symbol));
            return new(status, null, error.Message);
        }
        catch (OperationCanceledException)
        {
            // Gateway reports cancellation after dispatch as BrokerException(MayHaveExecuted=true).
            store.SetOutcome(intent.Id, "Failed", null, "Cancelled before dispatch");
            return new("Failed", null, "Cancelled before dispatch");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or IOException)
        {
            store.SetOutcome(intent.Id, "Unknown", null, "Execution acknowledgement could not be persisted or parsed");
            return new("Unknown", null, "Execution outcome requires reconciliation");
        }
    }

    public async Task ReconcileAsync(AccountInfo account, CancellationToken cancellationToken = default)
    {
        foreach (var intent in store.Unresolved(account.AccountId, account.Server))
        {
            if (intent.BrokerServer != account.Server)
            {
                if (intent.Status != "Confirmed")
                {
                    if (intent.Status == "Pending")
                        store.SetOutcome(intent.Id, "Unknown", null,
                            "Legacy execution has no broker server identity; manual terminal review required");
                    continue;
                }

                var proof = await BrokerContext.InvokeAsync(broker, account, "FIND_ORDER", intent.Id.ToString("N"),
                    cancellationToken);
                if (!proof.TryGetProperty("found", out var proven) || proven.ValueKind != JsonValueKind.True) continue;
                var legacyOrder = RecoveredIdentity(proof);
                if (legacyOrder.Ticket != intent.Ticket || legacyOrder.Symbol != intent.Symbol ||
                    legacyOrder.Side != intent.Side)
                    throw new BrokerException("Legacy order ownership mismatch");
                if (!store.TryBindLegacyConfirmed(intent.Id, account.Server, intent.Ticket!.Value))
                    throw new BrokerException("Legacy order identity conflicts with a current reservation");
                store.AppendJournal("Information", "TradeRecovered",
                    $"Legacy order {intent.Ticket} broker identity verified", intent.Symbol, intent.Id.ToString("N"));
                continue; // Runtime closure tracking now sees this ticket under its proven server.
            }

            var result = await BrokerContext.InvokeAsync(broker, account, "FIND_ORDER", intent.Id.ToString("N"),
                cancellationToken);
            if (!result.TryGetProperty("found", out var found) || found.ValueKind != JsonValueKind.True)
            {
                if (intent.Status == "Pending")
                    store.SetOutcome(intent.Id, "Unknown", null, "Restarted with an unacknowledged order");
                continue; // Absence in currently loaded history is not proof that execution failed.
            }

            var order = RecoveredIdentity(result);
            if (order.Symbol != intent.Symbol || order.Side != intent.Side)
                throw new BrokerException("Recovered order ownership mismatch");
            var ticket = order.Ticket;
            store.SetOutcome(intent.Id, "Confirmed", ticket, "Recovered through broker order identity");
            store.AppendJournal("Information", "TradeRecovered", $"Order {ticket} reconciled", intent.Symbol,
                intent.Id.ToString("N"));
            if (store.LoadSettings().TelegramEnabled)
                store.EnqueueNotification(NotificationFormatter.Event("سفارش بازیابی شد",
                    $"تیکت: {ticket}؛ ارسال مجدد انجام نشد.", intent.Symbol));
        }
    }

    private static (long Ticket, string Symbol, string Side) RecoveredIdentity(JsonElement result)
    {
        if (!result.TryGetProperty("order", out var order) || order.ValueKind != JsonValueKind.Object ||
            !order.TryGetProperty("ticket", out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var ticket) || ticket <= 0 ||
            !order.TryGetProperty("symbol", out var symbol) || symbol.ValueKind != JsonValueKind.String ||
            !order.TryGetProperty("side", out var side) || side.ValueKind != JsonValueKind.String)
            throw new BrokerException("Invalid recovered order identity");
        return (ticket, symbol.GetString()!, side.GetString()!);
    }
}
