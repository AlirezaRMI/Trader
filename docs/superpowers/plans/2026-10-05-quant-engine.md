# Quant engine completion implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline; obtain one fresh-context final review.

**Goal:** Complete interrupted platform fixes, deterministic offline/live strategy analysis, bounded research tools and the Modam glass dashboard.

**Architecture:** Domain functions and external parameters feed one Application strategy pipeline. Offline backtest/optimization and real-time broker deployment use that pipeline; Infrastructure owns durable broker execution and serialized asynchronous commands.

**Tech Stack:** C#/.NET, SQLite, Channels, xUnit, native ES modules/CSS, MQL4.

**Spec:** docs/superpowers/specs/2026-10-05-quant-engine-design.md

## Global constraints

- Preserve the existing dirty feature branch and user files; no external publish or real trading/notifications.
- Default runtime startup is paused. Research never activates or changes live trading.
- Same causal C# indicators/strategy/risk logic in replay and live; simulation assumptions are visible.
- Use supplied Modam fonts and responsive animated glass panels with reduced-motion support.

## Review focus

- Account, server or terminal replacement during any request must fail closed.
- Failed poll, mid-cycle pause or stale market must prevent a new order.
- Multiple restarts/polls and immediately closed orders produce one durable closure event.
- Missing warmup, nonfinite data and out-of-order candles must not create signals.
- Research work is bounded/cancellable and does not fit parameters on validation data.

## Task 1: Complete interrupted broker/runtime fixes

Files: Application/Broker/BrokerContext.cs; Infrastructure/Runtime/*; Infrastructure/Persistence/SqliteTradeStore.cs; Application/Runtime/ITradeStore.cs; Api/ClientInformation/TraderBridgeEA.mq4; Trader.Tests/{Execution,Runtime,Persistence,BridgeIntegration}Tests.cs.
Interfaces: BrokerContext.Bind(AccountInfo,string)->string and Unwrap(AccountInfo,JsonElement,bool)->JsonElement; contextual reconciliation and atomic once-only closure registration.
- [x] Observe existing context regressions RED; add poll readiness, closure and immediately closed-order regressions.
- [x] Implement bound requests/replies, revoke readiness before polls, migrate durable server/closure data, bound EA history lookup, initialize tick deadlines.
- [x] Run the full .NET suite; require zero failures.

## Task 2: Shared deterministic strategy and offline research

Files: Domain/Parameters/*, Domain/Functions/*, existing engines; Application/Research/* and Application/Strategy/*; Trader.Tests/QuantEngineTests.cs.
Interfaces: immutable StrategyParameters; StrategyPipeline.Evaluate(StrategyInput)->Analysis; BacktestEngine.Run(BacktestRequest,CancellationToken)->BacktestResult; StrategyOptimizer.Run(OptimizationRequest,CancellationToken)->OptimizationResult.
- [x] Add RED tests for causal indicators, deterministic replay/costs, chronological validation and strategy regressions; verify configured higher risk budgets and event-time expiry separately. Those last two checks were added after implementation, not claimed as RED-to-GREEN.
- [x] Implement validated parameters, shared causal indicators and pipeline, replay and bounded parameter search.
- [x] Integrate live analysis using raw closed histories and those same functions; run complete tests.

## Task 3: Channel orchestration and research API

Files: Infrastructure/Runtime/TradingRuntime.cs; Api/TraderApplication.cs; Api/Endpoints/*; Application/Runtime/RuntimeModels.cs; API/runtime tests.
Interfaces: bounded serialized runtime control channel; authenticated bounded research endpoints; settings persist strategy parameters.
- [x] Exercise concurrent refresh/cancellation, research lane contention/cancellation and invalid/oversized research/parameter requests. Lifecycle and size-limit checks were added after implementation; malformed research input was observed RED before its fix.
- [x] Implement channel ownership/shutdown and API routes; verify full .NET suite.

## Task 4: Modam glass dashboard and browser reliability

Files: Api/wwwroot/{css,js}/*, index.html; Trader.Tests/ui.test.mjs.
- [x] Observe retry/timeframe race failures against existing browser behavior.
- [x] Load supplied font weights, integrate research/forms/results and parameters, improve glass animation/layout and preserve user edits during live updates.
- [x] Run JavaScript behavior and module syntax checks; verify authenticated font/static asset delivery.
- [ ] Inspect desktop/mobile browser rendering. Blocked external verification gate: the browser inventory contains no enabled browser or app. No visual QA claim.

## Task 5: Delivery verification

Files: project/runtime configuration, docs, startup scripts, README.
- [x] Resolve project-local supported SDK, build/test/release publish and run a synthetic API smoke check.
- [x] Obtain the single final read-only review; fix important findings with regression tests.
- [x] Record actual verification and native MT4/container limits; deliver source without unrequested push/deployment.

Evidence and rulings: [quant engine ledger](../progress-quant-engine.md). The remaining browser/native/container gates are not represented as passing code checks.
