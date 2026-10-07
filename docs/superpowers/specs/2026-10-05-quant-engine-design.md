# Quantitative engine completion

The user explicitly requests immediate continuation through completion of the existing platform, repair of the interrupted review fixes, TiMi-style research/optimization/deployment separation, deterministic shared backtest/live strategy logic, external parameters, async channels, and a glass dashboard using the supplied Modam assets. Continue inline in the existing dirty feature branch, preserving earlier work. This authorization supersedes extra design-approval pauses. Do not enable live trading, send real notifications, or deploy externally.

## Architecture and contracts

- Domain remains independent of broker, persistence, hosting and research. Immutable validated strategy parameters describe indicators, timeframes, ATR stops, reward/risk, retest expiry, zones and risk allocation. Strategy state uses market timestamps rather than wall-clock time.
- Application owns a deterministic strategy pipeline and offline research. Both replay and live execution consume closed OHLC candles through the same C# indicators, zone detector, strategy selection and risk sizing. Live quote/fill timing, slippage and broker execution remain explicitly different from simulation.
- Research replays chronological closed candles, enters at the next available quote, charges configurable commission/slippage, marks equity, applies stop/target conservatively when both are touched, and exposes return, drawdown, profit factor, expectancy and trade records. Parameter search is bounded and ranks training candidates before separate chronological validation; it never automatically changes live settings.
- Infrastructure coordinates broker sessions, SQLite persistence and a bounded Channel for serialized runtime commands. Every account-scoped broker command carries account/server/session and verifies the acknowledgement. A failed management cycle revokes execution readiness. Closures and their journal/outbox records are committed atomically and once, including orders closed between polls.
- API provides authenticated research and parameter endpoints with bounded input/work. Existing start/pause semantics remain. Runtime starts paused. Settings changes require paused entries.
- Dashboard exposes research, configuration, live account and journal states in Persian RTL, with responsive glass panels and reduced-motion support. Supplied regular/medium/semibold/bold TTF assets are served locally.
- Use .NET 10 if a project-local SDK can be installed; record any actual environment limitation. Broker runtime compilation/demo checks are separate from .NET tests.

## Acceptance

All existing tests pass. New regressions prove context isolation, readiness revocation, once-only closure persistence, parameter effects, causal indicators, deterministic replay, fees/ambiguous intrabar fills, train/validation separation, bounded channel lifecycle and browser request ordering/retry behavior. Build, release publish, API and browser inspection must be reported with actual evidence. No claim of guaranteed profit or native MT4 verification without those checks.
